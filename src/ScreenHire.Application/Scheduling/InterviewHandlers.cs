using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Messaging;
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
/// An interview request changed in a way open dashboards show: offered, booked, moved, cancelled, or its calendar events
/// written. Ids and the status only; the dashboard refetches the interview through the REST API, which applies the
/// caller's authorization. <see cref="Status"/> is the request's status name, or null when the event is only about the
/// calendar events.
/// </summary>
public sealed record InterviewChanged(Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid InterviewId, string? Status) : IJobEvent;

/// <summary>What the interview handlers load for one interview: the rows an email or a calendar event is written from.</summary>
internal sealed record InterviewContext(
    InterviewRequest Interview, JobApplication Application, Job Job, Candidate Candidate, Organization Organization);

internal static class InterviewContexts
{
    /// <summary>
    /// <see cref="LoadAsync"/> under the interviewers' locks, which the handler's transaction then holds through its calendar
    /// calls. A cancellation or a move takes the same locks, so it waits for the handler to commit and then sees the event
    /// ids it needs, and the handler reads the status and revision only after any earlier change has committed.
    /// </summary>
    public static async Task<InterviewContext?> LockAndLoadAsync(ScreenHireDbContext db, Guid organizationId, Guid interviewId, CancellationToken cancellationToken)
    {
        var interviewers = await db.InterviewAttendees.AsNoTracking().Where(a => a.InterviewId == interviewId).Select(a => a.UserId).ToListAsync(cancellationToken);
        await InterviewLocks.TakeAsync(db, organizationId, interviewers, cancellationToken);
        return await LoadAsync(db, interviewId, cancellationToken);
    }

    /// <summary>The rows for <paramref name="interviewId"/>, or null when any is gone (the application was deleted since).</summary>
    public static async Task<InterviewContext?> LoadAsync(ScreenHireDbContext db, Guid interviewId, CancellationToken cancellationToken)
    {
        var interview = await db.InterviewRequests.SingleOrDefaultAsync(r => r.Id == interviewId, cancellationToken);
        if (interview is null)
        {
            return null;
        }

        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == interview.ApplicationId, cancellationToken);
        var job = application is null ? null : await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == application.JobId, cancellationToken);
        var candidate = application is null ? null : await db.Candidates.AsNoTracking().SingleOrDefaultAsync(c => c.Id == application.CandidateId, cancellationToken);
        var organization = application is null
            ? null
            : await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == application.OrganizationId, cancellationToken);
        return application is null || job is null || candidate is null || organization is null
            ? null
            : new InterviewContext(interview, application, job, candidate, organization);
    }
}

/// <summary>
/// Emails the candidate the link to pick an interview time, in the worker (docs/architecture/interview-scheduling.md).
/// Idempotent on its own: the send's id is the id of the email's timeline entry, so a redelivery that finds the entry
/// sends nothing, and <c>[Deduplicated]</c> drops a second envelope for the same send. The link is issued here, so it never
/// sits in a message or in the copy the team reads. A request that is no longer open (cancelled, booked, expired) sends
/// nothing, with a warning. The email goes before its entries commit, so a crash between the two can repeat it once.
/// </summary>
public static partial class SendInterviewInvitationHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(SendInterviewInvitation.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        SendInterviewInvitation message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        InterviewLinks links,
        Branding.EmailHeaders headers,
        TimeProvider time,
        ILogger<SendInterviewInvitation> logger,
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
            : context.Interview.EffectiveStatus(now) != InterviewStatus.Open ? "the interview is no longer open"
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
        if (await store.FindAsync(db, EmailTemplateIds.InterviewInvitation, cancellationToken) is not { } template)
        {
            LogNotSent(logger, message.SendId, "the invitation template is missing");
            return outgoing;
        }

        var model = ApplicationEmails.ModelFor(candidate, job.Title, organization.Name, stageName: null) with
        {
            BookingLink = links.Url(organization.Slug, links.Issue(interview)),
            InterviewLength = InterviewText.Length(interview.DurationMinutes),
        };
        var header = await headers.ForAsync(db, organization, cancellationToken);
        await email.SendAsync(EmailTemplateIds.InterviewInvitation, template.Content, candidate.Email, model, header, cancellationToken);

        // The team's copy says where the candidate's own link was: it lets whoever holds it book as the candidate.
        var kept = renderer.Render(
            template.Content, candidate.Email, model with { BookingLink = ApplicationEmails.QuestionnaireLinkNotKept },
            EmailTemplateIds.InterviewInvitation, header);
        var copy = application.PrepareEmail(message.SendId, EmailTemplateIds.InterviewInvitation, kept.To, kept.Subject, kept.TextBody, kept.HtmlBody ?? "", now);
        db.ApplicationEmails.Add(copy);
        db.ApplicationEvents.Add(application.EmailSent(copy, now, message.ActorUserId));
        outgoing.Add(new ApplicationEmailed(application.OrganizationId, application.Id, application.JobId, message.SendId, now, message.ActorUserId));
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Interview invitation {SendId} was not sent: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid sendId, string reason);
}

/// <summary>
/// Writes a booked, moved or cancelled interview to each interviewer's own calendar and emails the candidate, in the
/// worker. Each interviewer has an event of their own, so it lands natively in every calendar whatever provider each uses:
/// the organizer's event invites the candidate and makes the meeting link, the others carry that link in their text.
/// </summary>
/// <remarks>
/// <para>
/// A message the request has moved past (a later revision, or a status that no longer fits) does nothing, because a
/// later message describes the request now. Effects are idempotent: an event id is derived from the interview and the
/// member, an attendee already Synced is skipped, and the email's timeline entry has a derived id. A calendar that is
/// only down throws <see cref="CalendarApiException"/>, so Wolverine retries the message and the events already made are
/// found again; one the provider refuses for good, or a member with no usable calendar, marks that attendee Failed with
/// a plain problem and the others go on.
/// </para>
/// </remarks>
internal static partial class InterviewCalendarSync
{
    private const string NoCalendar = "Their calendar is not connected, or needs connecting again.";

    private const string Refused = "Their calendar refused the event. Ask them to connect it again.";

    /// <summary>Creates, or when <paramref name="move"/> moves, each interviewer's event for a Booked interview.</summary>
    public static async Task<OutgoingMessages> WriteAsync(
        Guid organizationId,
        Guid interviewId,
        int revision,
        bool move,
        ScreenHireDbContext db,
        CalendarAccess calendar,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        InterviewLinks links,
        Branding.EmailHeaders headers,
        IOptions<DashboardLinkOptions> dashboard,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var context = await InterviewContexts.LockAndLoadAsync(db, organizationId, interviewId, cancellationToken);
        if (context is null || !InterviewText.IsCurrent(context.Interview, revision, InterviewStatus.Booked) || context.Interview.StartsAt is not { } start)
        {
            LogSuperseded(logger, interviewId, revision);
            return outgoing;
        }

        var (interview, application, job, candidate, organization) = context;
        var end = start + interview.Duration;
        var title = InterviewText.EventTitle(candidate.FullName, job.Title);
        var link = dashboard.Value.Url($"/applications/{application.Id}");
        var attendees = await LoadAttendeesAsync(db, interviewId, cancellationToken);
        foreach (var attendee in attendees.Where(a => a.SyncStatus != InterviewSyncStatus.Synced))
        {
            if (await calendar.GetSessionAsync(attendee.UserId, cancellationToken) is not { } session)
            {
                attendee.MarkFailed(NoCalendar);
                continue;
            }

            try
            {
                // The organizer's event is first: it makes the meeting link the others carry.
                var spec = new EventSpec(
                    InterviewText.EventIdempotencyId(interviewId, attendee.UserId),
                    title,
                    InterviewText.EventDescription(candidate.FullName, job.Title, link, attendee.IsOrganizer ? null : interview.MeetingUrl),
                    start,
                    end,
                    attendee.IsOrganizer ? [candidate.Email] : [],
                    CreateMeetingLink: attendee.IsOrganizer);
                var result = move && attendee.ProviderEventId is { } existing
                    ? await MoveOrCreateAsync(session, existing, spec, start, end, cancellationToken)
                    : await session.Provider.CreateEventAsync(session.AccessToken, spec, cancellationToken);
                attendee.MarkSynced(session.Connection.Provider, result.EventId);
                if (attendee.IsOrganizer && result.MeetingUrl is not null)
                {
                    interview.SetMeetingUrl(result.MeetingUrl);
                }
            }
            catch (CalendarApiException e) when (e.Permanent)
            {
                attendee.MarkFailed(Refused);
            }
        }

        var now = time.GetUtcNow();
        var sendId = InterviewText.SendId("confirmation", interviewId, revision);
        if (email.IsConfigured && links.IsConfigured && !await db.ApplicationEvents.AnyAsync(e => e.Id == sendId, cancellationToken)
            && await store.FindAsync(db, EmailTemplateIds.InterviewConfirmation, cancellationToken) is { } template)
        {
            var manageLink = links.Url(organization.Slug, links.Issue(interview));
            var model = ApplicationEmails.ModelFor(candidate, job.Title, organization.Name, stageName: null) with
            {
                ManageLink = manageLink,
                MeetingLink = interview.MeetingUrl,
                InterviewTime = InterviewText.Time(start, end, interview.TimeZone),
                InterviewLength = InterviewText.Length(interview.DurationMinutes),
            };
            await SendAsync(db, email, renderer, headers, organization, application, candidate, template, EmailTemplateIds.InterviewConfirmation, model, sendId, now, cancellationToken);
            outgoing.Add(new ApplicationEmailed(application.OrganizationId, application.Id, application.JobId, sendId, now));
        }

        outgoing.Add(new InterviewChanged(application.OrganizationId, application.Id, application.JobId, interviewId, interview.Status.ToString()));
        return outgoing;
    }

    /// <summary>Removes each interviewer's event for a Cancelled interview and tells the candidate it is cancelled.</summary>
    public static async Task<OutgoingMessages> CancelAsync(
        Guid organizationId,
        Guid interviewId,
        int revision,
        ScreenHireDbContext db,
        CalendarAccess calendar,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        Branding.EmailHeaders headers,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var context = await InterviewContexts.LockAndLoadAsync(db, organizationId, interviewId, cancellationToken);
        if (context is null || !InterviewText.IsCurrent(context.Interview, revision, InterviewStatus.Cancelled))
        {
            LogSuperseded(logger, interviewId, revision);
            return outgoing;
        }

        var (interview, application, job, candidate, organization) = context;
        var attendees = await LoadAttendeesAsync(db, interviewId, cancellationToken);
        foreach (var attendee in attendees.Where(a => a.ProviderEventId is not null && a.SyncStatus != InterviewSyncStatus.Synced))
        {
            if (await calendar.GetSessionAsync(attendee.UserId, cancellationToken) is not { } session)
            {
                attendee.MarkFailed(NoCalendar);
                continue;
            }

            try
            {
                await session.Provider.CancelEventAsync(session.AccessToken, attendee.ProviderEventId!, cancellationToken);
                attendee.MarkSynced(session.Connection.Provider, attendee.ProviderEventId!);
            }
            catch (CalendarApiException e) when (e.Permanent)
            {
                attendee.MarkFailed(Refused);
            }
        }

        var now = time.GetUtcNow();
        var sendId = InterviewText.SendId("cancelled", interviewId, revision);
        if (email.IsConfigured && !await db.ApplicationEvents.AnyAsync(e => e.Id == sendId, cancellationToken)
            && await store.FindAsync(db, EmailTemplateIds.InterviewCancelled, cancellationToken) is { } template)
        {
            var model = ApplicationEmails.ModelFor(candidate, job.Title, organization.Name, stageName: null) with
            {
                InterviewTime = interview.StartsAt is { } start ? InterviewText.Time(start, start + interview.Duration, interview.TimeZone) : null,
            };
            await SendAsync(db, email, renderer, headers, organization, application, candidate, template, EmailTemplateIds.InterviewCancelled, model, sendId, now, cancellationToken);
            outgoing.Add(new ApplicationEmailed(application.OrganizationId, application.Id, application.JobId, sendId, now));
        }

        outgoing.Add(new InterviewChanged(application.OrganizationId, application.Id, application.JobId, interviewId, interview.Status.ToString()));
        return outgoing;
    }

    // An event the interviewer deleted by hand is made again at the new time.
    private static async Task<CalendarEventResult> MoveOrCreateAsync(
        CalendarSession session, string eventId, EventSpec spec, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        try
        {
            return await session.Provider.MoveEventAsync(session.AccessToken, eventId, start, end, cancellationToken);
        }
        catch (CalendarApiException e) when (e.Code == "event_not_found")
        {
            return await session.Provider.CreateEventAsync(session.AccessToken, spec, cancellationToken);
        }
    }

    private static async Task SendAsync(
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplates renderer,
        Branding.EmailHeaders headers,
        Organization organization,
        JobApplication application,
        Candidate candidate,
        OrganizationEmailTemplate template,
        string templateId,
        EmailTemplateModel model,
        Guid sendId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var header = await headers.ForAsync(db, organization, cancellationToken);
        await email.SendAsync(templateId, template.Content, candidate.Email, model, header, cancellationToken);

        // The copy the team reads hides the candidate's own link: it lets whoever holds it change or cancel the interview.
        var kept = renderer.Render(
            template.Content, candidate.Email, model with { ManageLink = ApplicationEmails.QuestionnaireLinkNotKept }, templateId, header);
        var copy = application.PrepareEmail(sendId, templateId, kept.To, kept.Subject, kept.TextBody, kept.HtmlBody ?? "", now);
        db.ApplicationEmails.Add(copy);
        db.ApplicationEvents.Add(application.EmailSent(copy, now, actorUserId: null));
    }

    private static Task<List<InterviewAttendee>> LoadAttendeesAsync(ScreenHireDbContext db, Guid interviewId, CancellationToken cancellationToken) =>
        db.InterviewAttendees.Where(a => a.InterviewId == interviewId)
            .OrderByDescending(a => a.IsOrganizer).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Interview {InterviewId} changed again since revision {Revision}, so this calendar message does nothing")]
    private static partial void LogSuperseded(ILogger logger, Guid interviewId, int revision);
}

/// <summary>Puts a booked interview on each interviewer's calendar and confirms it to the candidate (<see cref="InterviewCalendarSync"/>).</summary>
public static class CreateInterviewEventsHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(CreateInterviewEvents.IdempotencyKey))]
    public static Task<OutgoingMessages> Handle(
        CreateInterviewEvents message,
        ScreenHireDbContext db,
        CalendarAccess calendar,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        InterviewLinks links,
        Branding.EmailHeaders headers,
        IOptions<DashboardLinkOptions> dashboard,
        TimeProvider time,
        ILogger<CreateInterviewEvents> logger,
        CancellationToken cancellationToken) =>
        InterviewCalendarSync.WriteAsync(
            message.OrganizationId, message.InterviewId, message.Revision, move: false, db, calendar, email, renderer, store, links, headers, dashboard, time, logger, cancellationToken);
}

/// <summary>Moves each interviewer's event to the new time and confirms it to the candidate (<see cref="InterviewCalendarSync"/>).</summary>
public static class UpdateInterviewEventsHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(UpdateInterviewEvents.IdempotencyKey))]
    public static Task<OutgoingMessages> Handle(
        UpdateInterviewEvents message,
        ScreenHireDbContext db,
        CalendarAccess calendar,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        InterviewLinks links,
        Branding.EmailHeaders headers,
        IOptions<DashboardLinkOptions> dashboard,
        TimeProvider time,
        ILogger<UpdateInterviewEvents> logger,
        CancellationToken cancellationToken) =>
        InterviewCalendarSync.WriteAsync(
            message.OrganizationId, message.InterviewId, message.Revision, move: true, db, calendar, email, renderer, store, links, headers, dashboard, time, logger, cancellationToken);
}

/// <summary>Removes each interviewer's event and tells the candidate the interview is cancelled (<see cref="InterviewCalendarSync"/>).</summary>
public static class CancelInterviewEventsHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(CancelInterviewEvents.IdempotencyKey))]
    public static Task<OutgoingMessages> Handle(
        CancelInterviewEvents message,
        ScreenHireDbContext db,
        CalendarAccess calendar,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        Branding.EmailHeaders headers,
        TimeProvider time,
        ILogger<CancelInterviewEvents> logger,
        CancellationToken cancellationToken) =>
        InterviewCalendarSync.CancelAsync(
            message.OrganizationId, message.InterviewId, message.Revision, db, calendar, email, renderer, store, headers, time, logger, cancellationToken);
}
