using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// Emails the candidate the link to pick a new time for a booked interview, in the worker, after the team offered new times.
/// Idempotent like <see cref="SendInterviewInvitationHandler"/>: the send's id is the id of the email's timeline entry, and
/// <c>[Deduplicated]</c> drops a second envelope for the same send. It sends nothing once the interview is no longer Booked with
/// an offer pending (the candidate already picked, or it was cancelled), so a late redelivery cannot send a link that no longer
/// means anything.
/// </summary>
public static partial class SendInterviewNewTimesHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(SendInterviewNewTimes.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        SendInterviewNewTimes message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        InterviewLinks links,
        Branding.EmailHeaders headers,
        Applications.CandidateMailRouting routing,
        TimeProvider time,
        ILogger<SendInterviewNewTimes> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        if (await db.ApplicationEvents.AnyAsync(e => e.Id == message.SendId, cancellationToken))
        {
            return outgoing; // sent already
        }

        var now = time.GetUtcNow();
        var context = await InterviewContexts.LoadAsync(db, message.InterviewId, cancellationToken);
        var stage = context?.Job.Workflow.FindStage(context.Application.CurrentStageId);
        var reason = context is null ? "the interview or its application is gone"
            : context.Interview.Status != InterviewStatus.Booked || context.Interview.NewTimesOfferedAt is null ? "the offer of new times is no longer pending"
            : context.Application.HasEnded(stage) ? "the application has ended"
            : !email.IsConfigured ? "this host has no email sender"
            : !links.IsConfigured ? "the careers site address is not set up"
            : null;
        if (reason is not null)
        {
            LogNotSent(logger, message.SendId, reason);
            return outgoing;
        }

        var (interview, application, job, candidate, organization) = context!;
        if (await store.FindAsync(db, EmailTemplateIds.InterviewNewTimes, cancellationToken) is not { } template)
        {
            LogNotSent(logger, message.SendId, "the new times template is missing");
            return outgoing;
        }

        var model = ApplicationEmails.ModelFor(candidate, job.Title, organization.Name, stageName: null) with
        {
            BookingLink = links.Url(organization.Slug, links.Issue(interview)),
            InterviewLength = InterviewText.Length(interview.DurationMinutes),
            InterviewTime = interview.StartsAt is { } start ? InterviewText.Time(start, start + interview.Duration, interview.TimeZone) : null,
        };
        var header = await headers.ForAsync(db, organization, cancellationToken);
        // Routed like every email about an application: a reply address and the link to the candidate's own page.
        var route = await routing.ForAsync(db, application.OrganizationId, message.SendId, cancellationToken, candidate.Id);
        await email.SendAsync(EmailTemplateIds.InterviewNewTimes, template.Content, candidate.Email, model, header, cancellationToken, route);

        // The team's copy says where the candidate's own link was: it lets whoever holds it move or cancel the interview.
        var kept = renderer.Render(
            template.Content, candidate.Email, model with { BookingLink = ApplicationEmails.QuestionnaireLinkNotKept },
            EmailTemplateIds.InterviewNewTimes, header);
        var copy = application.PrepareEmail(message.SendId, EmailTemplateIds.InterviewNewTimes, kept.To, kept.Subject, kept.TextBody, kept.HtmlBody ?? "", now);
        db.ApplicationEmails.Add(copy);
        db.ApplicationEvents.Add(application.EmailSent(copy, now, message.ActorUserId));
        outgoing.Add(new ApplicationEmailed(application.OrganizationId, application.Id, application.JobId, message.SendId, now, message.ActorUserId));
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "New interview times email {SendId} was not sent: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid sendId, string reason);
}

/// <summary>The email an interviewer gets the day before a booked interview: ScreenHire's own wording, like the mention email.</summary>
public static class InterviewReminderEmail
{
    public static EmailMessage Compose(
        string to, string candidateName, string jobTitle, string organizationName, string time, string? meetingUrl, Uri? applicationLink)
    {
        var who = InterviewText.Plain(candidateName);
        var where = $"{who}, {InterviewText.Plain(jobTitle)}, at {organizationName}";
        var text = new List<string> { $"You interview a candidate in about a day: {where}.", time };
        var html = new List<string>
        {
            $"<p>You interview a candidate in about a day: {WebUtility.HtmlEncode(where)}.</p>",
            $"<p>{WebUtility.HtmlEncode(time)}</p>",
        };
        if (!string.IsNullOrEmpty(meetingUrl))
        {
            text.Add($"Meeting link: {meetingUrl}");
            html.Add($"<p><a href=\"{WebUtility.HtmlEncode(meetingUrl)}\">Join the meeting</a></p>");
        }

        if (applicationLink is not null)
        {
            text.Add($"Open the application: {applicationLink}");
            html.Add($"<p><a href=\"{WebUtility.HtmlEncode(applicationLink.ToString())}\">Open the application</a></p>");
        }

        return new EmailMessage(to, $"Interview in a day: {who}", string.Join("\n\n", text), string.Join('\n', html));
    }
}

/// <summary>
/// Reminds the people at a booked interview of it, in the worker: the candidate (a day and an hour before) and every
/// interviewer (a day before). <see cref="InterviewCalendarSync"/> schedules the message for a time and makes it about one
/// start time (<see cref="SendInterviewReminder"/>), so it sends only while the interview is still Booked at that time and
/// has not begun. Idempotent in two layers beyond <c>[Deduplicated]</c>: the candidate's reminder is a timeline entry with
/// an id derived from the interview, the kind and the start, and an interviewer's is
/// <see cref="InterviewAttendee.RemindedForStart"/>. Each email goes before its record commits, and all of them commit
/// together, so a failed send to one interviewer repeats on retry the reminders already sent in that attempt. An interviewer
/// who has left, or no longer sees the job, is not emailed.
/// </summary>
public static partial class SendInterviewReminderHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(SendInterviewReminder.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        SendInterviewReminder message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        InterviewLinks links,
        Branding.EmailHeaders headers,
        Applications.CandidateMailRouting routing,
        IOptions<DashboardLinkOptions> dashboard,
        TimeProvider time,
        ILogger<SendInterviewReminder> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var now = time.GetUtcNow();
        var context = await InterviewContexts.LoadAsync(db, message.InterviewId, cancellationToken);
        var stage = context?.Job.Workflow.FindStage(context.Application.CurrentStageId);
        var reason = context is null ? "the interview or its application is gone"
            : context.Interview.Status != InterviewStatus.Booked || context.Interview.StartsAt != message.StartsAt ? "the interview was moved or cancelled"
            : message.StartsAt <= now ? "the interview has started"
            : context.Application.HasEnded(stage) ? "the application has ended"
            : !email.IsConfigured ? "this host has no email sender"
            : null;
        if (reason is not null)
        {
            LogNotSent(logger, message.InterviewId, message.Kind, reason);
            return outgoing;
        }

        var (interview, application, job, candidate, organization) = context!;
        var start = message.StartsAt;
        var timeText = InterviewText.Time(start, start + interview.Duration, interview.TimeZone);

        var sendId = InterviewText.ReminderSendId(message.Kind, interview.Id, start);
        if (links.IsConfigured
            && !await db.ApplicationEvents.AnyAsync(e => e.Id == sendId, cancellationToken)
            && await store.FindAsync(db, EmailTemplateIds.InterviewReminder, cancellationToken) is { } template)
        {
            var model = ApplicationEmails.ModelFor(candidate, job.Title, organization.Name, stageName: null) with
            {
                ManageLink = links.Url(organization.Slug, links.Issue(interview)),
                MeetingLink = interview.MeetingUrl,
                InterviewTime = timeText,
                InterviewLength = InterviewText.Length(interview.DurationMinutes),
            };
            await InterviewCalendarSync.SendAsync(
                db, email, renderer, headers, routing, organization, application, candidate, template, EmailTemplateIds.InterviewReminder, model, sendId, now, cancellationToken);
            outgoing.Add(new ApplicationEmailed(application.OrganizationId, application.Id, application.JobId, sendId, now));
        }

        if (message.Kind == InterviewReminderKind.DayBefore)
        {
            await RemindInterviewersAsync(db, email, dashboard.Value, interview, application, job, candidate, organization, timeText, start, cancellationToken);
        }

        return outgoing;
    }

    private static async Task RemindInterviewersAsync(
        ScreenHireDbContext db,
        TransactionalEmail email,
        DashboardLinkOptions dashboard,
        InterviewRequest interview,
        JobApplication application,
        Job job,
        Candidate candidate,
        Organization organization,
        string timeText,
        DateTimeOffset start,
        CancellationToken cancellationToken)
    {
        var attendees = await db.InterviewAttendees
            .Where(a => a.InterviewId == interview.Id && (a.RemindedForStart == null || a.RemindedForStart != start))
            .OrderByDescending(a => a.IsOrganizer).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        var link = dashboard.Url($"/applications/{application.Id}");
        foreach (var attendee in attendees)
        {
            // The worker sees every job, so whether the person still sees this one is checked here (ADR-0015).
            if (!await JobAccessQueries.SeesJobAsync(db, application.OrganizationId, attendee.UserId, application.JobId, cancellationToken))
            {
                continue;
            }

            var to = await db.Users.Where(u => u.Id == attendee.UserId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken);
            if (to is null)
            {
                continue;
            }

            await email.SendAsync(
                InterviewReminderEmail.Compose(to, candidate.FullName, job.Title, organization.Name, timeText, interview.MeetingUrl, link), cancellationToken);
            attendee.MarkReminded(start);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Interview {InterviewId} {Kind} reminder was not sent: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid interviewId, InterviewReminderKind kind, string reason);
}
