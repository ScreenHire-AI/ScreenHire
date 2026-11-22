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
using ScreenHire.Application.Notifications;
using System.Globalization;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// An interview request changed in a way open dashboards show: offered, booked, moved, cancelled, or its calendar events
/// written. Ids and the status only; the dashboard refetches the interview through the REST API, which applies the
/// caller's authorization. <see cref="Status"/> is the request's status name, or null when the event is only about the
/// calendar events.
/// </summary>
/// <remarks>
/// Who made the change and what it was, for the hiring team's toasts (docs/architecture/realtime.md): <see cref="Change"/>
/// is one of <see cref="InterviewChanges"/>, null for a calendar write or an organizer handed on when an interviewer left
/// (ScreenHire's own cancellation is Cancelled with no actor). <see cref="ActorUserId"/> is the member who made it; <see cref="ByCandidate"/> says the
/// candidate did, from their booking page. Neither for an automation or ScreenHire. <see cref="StartsAt"/> is the time the
/// change is about: the booked or new time, or the time a cancelled booking had. <see cref="Revision"/> is the request's
/// <see cref="Domain.Scheduling.InterviewRequest.Revision"/> after a booking, a new time or a cancellation, and
/// <see cref="ChangedAt"/> when it happened: together the stable key and time of the <c>interview.*</c> webhook events
/// (docs/architecture/workflow-engine.md section 5). Added last with defaults, as every field an event gains is.
/// </remarks>
public sealed record InterviewChanged(
    Guid OrganizationId,
    Guid ApplicationId,
    Guid JobId,
    Guid InterviewId,
    string? Status,
    string? Change = null,
    Guid? ActorUserId = null,
    bool ByCandidate = false,
    DateTimeOffset? StartsAt = null,
    int? Revision = null,
    DateTimeOffset? ChangedAt = null) : IJobEvent;

/// <summary>What happened to an interview, as <see cref="InterviewChanged.Change"/> says it.</summary>
public static class InterviewChanges
{
    public const string Invited = "Invited";
    public const string Booked = "Booked";
    public const string Rescheduled = "Rescheduled";
    public const string Cancelled = "Cancelled";
    public const string NewTimesOffered = "NewTimesOffered";

    /// <summary>The team changed how the candidate picks a time for an open interview.</summary>
    public const string OptionChanged = "OptionChanged";

    /// <summary>The candidate sent the times they are free.</summary>
    public const string TimesSuggested = "TimesSuggested";

    /// <summary>The candidate asked for another time than the team's.</summary>
    public const string AnotherTimeAsked = "AnotherTimeAsked";

    /// <summary>The team asked the candidate for more times.</summary>
    public const string MoreTimesAsked = "MoreTimesAsked";
}

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
public static class SendInterviewInvitationHandler
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
        Applications.CandidateMailRouting routing,
        TimeProvider time,
        ILogger<SendInterviewInvitation> logger,
        CancellationToken cancellationToken)
    {
        // The template follows how the candidate picks, as the interview stands when the email goes.
        var option = await db.InterviewRequests.AsNoTracking().Where(r => r.Id == message.InterviewId).Select(r => (InterviewOption?)r.Option).SingleOrDefaultAsync(cancellationToken);
        return await InterviewLinkEmail.SendAsync(
            InterviewLinkEmail.InvitationTemplate(option ?? InterviewOption.OurTimes), message.InterviewId, message.SendId, message.ActorUserId,
            (interview, now) => interview.EffectiveStatus(now) == InterviewStatus.Open ? null : "the interview is no longer open",
            db, email, renderer, store, links, headers, routing, time, logger, cancellationToken);
    }
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
        Applications.CandidateMailRouting routing,
        IOptions<DashboardLinkOptions> dashboard,
        IOptions<EmailOptions> emailOptions,
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
        var title = InterviewText.EventTitle(candidate.FullName, job.Title, interview.MeetingName);
        var link = dashboard.Value.Url($"/applications/{application.Id}");
        var attendees = await LoadAttendeesAsync(db, interviewId, cancellationToken);
        var connected = await ConnectedAsync(db, attendees, cancellationToken);
        foreach (var attendee in attendees.Where(a => a.SyncStatus != InterviewSyncStatus.Synced))
        {
            if (await calendar.GetSessionAsync(attendee.UserId, cancellationToken) is not { } session)
            {
                // No calendar connected at all is ScreenHire's calendar: the emailed invitation is their event. A connection that
                // stopped working is a problem to fix, and they get the invitation meanwhile.
                if (connected.Contains(attendee.UserId))
                {
                    attendee.MarkFailed(NoCalendar);
                }
                else
                {
                    attendee.MarkOnScreenHireCalendar();
                }

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

                // A create that rolled back after the provider answered left an event with no saved id, at the old time.
                // Creating again would only find that event there, so a move looks for it first, across the lookup span (every window the interview has had).
                var eventId = attendee.ProviderEventId;
                if (move)
                {
                    eventId ??= await session.Provider.FindEventIdAsync(session.AccessToken, spec.IdempotencyId, interview.LookupStart, interview.LookupEnd, cancellationToken);
                }

                var result = move && eventId is not null
                    ? await MoveOrCreateAsync(session, eventId, spec, start, end, cancellationToken)
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
                MeetingName = interview.MeetingNameOrDefault(),
                ManageLink = manageLink,
                MeetingLink = interview.MeetingUrl,
                InterviewTime = InterviewText.Time(start, end, interview.TimeZone, CandidateLanguage.Culture(application.Language)),
                InterviewLength = InterviewText.Length(interview.DurationMinutes),
            };
            // The invitation goes with every confirmation: ScreenHire cannot tell whether the organizer's calendar invitation
            // reached the candidate's calendar app, and with no connected calendar it is the only one.
            await SendAsync(db, email, renderer, headers, routing, organization, application, candidate, template, EmailTemplateIds.InterviewConfirmation, model, sendId, now, cancellationToken,
                route => [InterviewInvites.ForCandidate(interview, organization.Name, job.Title, candidate.Email, Organizer(route, emailOptions.Value), manageLink, now)]);
            outgoing.Add(new ApplicationEmailed(application.OrganizationId, application.Id, application.JobId, sendId, now));
        }

        await InviteTeamAsync(db, email, dashboard.Value, emailOptions.Value, interview, candidate, job, attendees, moved: move, now, cancellationToken);

        // The reminders are about this time, not this revision: they check the interview is still Booked at `start` when they
        // go, so a later move or cancellation makes them harmless, and the move's own message schedules the new ones.
        foreach (var (kind, at) in InterviewText.Reminders(start, now))
        {
            outgoing.Schedule(new SendInterviewReminder(application.OrganizationId, application.Id, interviewId, start, kind), at);
        }

        outgoing.Add(new InterviewChanged(application.OrganizationId, application.Id, application.JobId, interviewId, interview.Status.ToString()));
        return outgoing;
    }

    /// <summary>
    /// Removes each interviewer's event for a Cancelled interview and, unless <paramref name="tellCandidate"/> is false,
    /// tells the candidate it is cancelled. An interviewer whose event id was never saved (the booking's transaction rolled
    /// back after the provider answered) is looked up by the id the create used, so no event stays behind.
    /// </summary>
    public static async Task<OutgoingMessages> CancelAsync(
        Guid organizationId,
        Guid interviewId,
        int revision,
        bool tellCandidate,
        ScreenHireDbContext db,
        CalendarAccess calendar,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        Branding.EmailHeaders headers,
        Applications.CandidateMailRouting routing,
        IOptions<DashboardLinkOptions> dashboard,
        IOptions<EmailOptions> emailOptions,
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
        // An event may exist with no saved id only when the interview was booked. It can sit at any time the interview had,
        // so the look-up covers the lookup span (every window the interview has had).
        var wasBooked = interview.StartsAt is not null;
        var connected = await ConnectedAsync(db, attendees, cancellationToken);
        foreach (var attendee in attendees.Where(a => a.SyncStatus != InterviewSyncStatus.Synced && (a.ProviderEventId is not null || wasBooked)))
        {
            if (await calendar.GetSessionAsync(attendee.UserId, cancellationToken) is not { } session)
            {
                if (!connected.Contains(attendee.UserId))
                {
                    attendee.MarkOnScreenHireCalendar(); // the cancellation they are emailed removes their event
                }
                else if (attendee.ProviderEventId is not null)
                {
                    attendee.MarkFailed(NoCalendar);
                }

                continue;
            }

            try
            {
                var eventId = attendee.ProviderEventId ?? await session.Provider.FindEventIdAsync(
                    session.AccessToken, InterviewText.EventIdempotencyId(interviewId, attendee.UserId), interview.LookupStart, interview.LookupEnd, cancellationToken);
                if (eventId is null)
                {
                    continue;
                }

                await session.Provider.CancelEventAsync(session.AccessToken, eventId, cancellationToken);
                attendee.MarkSynced(session.Connection.Provider, eventId);
            }
            catch (CalendarApiException e) when (e.Permanent)
            {
                attendee.MarkFailed(Refused);
            }
        }

        var now = time.GetUtcNow();
        var sendId = InterviewText.SendId("cancelled", interviewId, revision);
        if (tellCandidate && email.IsConfigured && !await db.ApplicationEvents.AnyAsync(e => e.Id == sendId, cancellationToken)
            && await store.FindAsync(db, EmailTemplateIds.InterviewCancelled, cancellationToken) is { } template)
        {
            var model = ApplicationEmails.ModelFor(candidate, job.Title, organization.Name, stageName: null) with
            {
                MeetingName = interview.MeetingNameOrDefault(),
                InterviewTime = interview.StartsAt is { } start
                    ? InterviewText.Time(start, start + interview.Duration, interview.TimeZone, CandidateLanguage.Culture(application.Language))
                    : null,
            };
            // A booked interview's invitation is withdrawn with the same UID; an open one never had an event.
            await SendAsync(db, email, renderer, headers, routing, organization, application, candidate, template, EmailTemplateIds.InterviewCancelled, model, sendId, now, cancellationToken,
                wasBooked ? route => [InterviewInvites.ForCandidate(interview, organization.Name, job.Title, candidate.Email, Organizer(route, emailOptions.Value), null, now)] : null);
            outgoing.Add(new ApplicationEmailed(application.OrganizationId, application.Id, application.JobId, sendId, now));
        }

        if (wasBooked)
        {
            await InviteTeamAsync(db, email, dashboard.Value, emailOptions.Value, interview, candidate, job, attendees, moved: false, now, cancellationToken);
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

    internal static async Task SendAsync(
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplates renderer,
        Branding.EmailHeaders headers,
        Applications.CandidateMailRouting routing,
        Organization organization,
        JobApplication application,
        Candidate candidate,
        OrganizationEmailTemplate template,
        string templateId,
        EmailTemplateModel model,
        Guid sendId,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        Func<MailRoute, IReadOnlyList<EmailAttachment>>? attach = null)
    {
        var header = await headers.ForAsync(db, organization, cancellationToken);
        var route = await routing.ForAsync(db, application.OrganizationId, sendId, cancellationToken, candidate.Id);
        await email.SendAsync(templateId, template.Content, candidate.Email, model, header, cancellationToken, route, attach?.Invoke(route));

        // The copy the team reads hides the candidate's own link: it lets whoever holds it change or cancel the interview.
        var kept = renderer.Render(
            template.Content, candidate.Email, model with { ManageLink = ApplicationEmails.QuestionnaireLinkNotKept }, templateId, header);
        var copy = application.PrepareEmail(sendId, templateId, kept.To, kept.Subject, kept.TextBody, kept.HtmlBody ?? "", now);
        db.ApplicationEmails.Add(copy);
        db.ApplicationEvents.Add(application.EmailSent(copy, now, actorUserId: null));
    }

    /// <summary>
    /// Emails the calendar invitation to each interviewer whose own calendar did not get the event: one on ScreenHire's
    /// calendar, or one whose connection failed. A cancellation goes to those who were sent the booking. Once per revision
    /// (<see cref="InterviewAttendee.InvitedRevision"/>), and only to a member who still sees the job (ADR-0015).
    /// </summary>
    private static async Task InviteTeamAsync(
        ScreenHireDbContext db,
        TransactionalEmail email,
        DashboardLinkOptions dashboard,
        EmailOptions emailOptions,
        InterviewRequest interview,
        Candidate candidate,
        Job job,
        IReadOnlyList<InterviewAttendee> attendees,
        bool moved,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!email.IsConfigured || interview.StartsAt is not { } start)
        {
            return;
        }

        var cancelled = interview.Status == InterviewStatus.Cancelled;
        var link = dashboard.Url($"/applications/{interview.ApplicationId}");
        var time = InterviewText.Time(start, start + interview.Duration, interview.TimeZone, CultureInfo.GetCultureInfo("en-US"));
        foreach (var attendee in attendees.Where(a => a.InvitedRevision != interview.Revision))
        {
            var needsOne = cancelled
                ? attendee.InvitedRevision is not null
                : attendee.Provider == CalendarProvider.ScreenHire || attendee.SyncStatus == InterviewSyncStatus.Failed;
            if (!needsOne || !await JobAccessQueries.SeesJobAsync(db, interview.OrganizationId, attendee.UserId, interview.JobId, cancellationToken))
            {
                continue;
            }

            var person = await db.Users.Where(u => u.Id == attendee.UserId).Select(u => new { u.Email, u.DisplayName }).SingleOrDefaultAsync(cancellationToken);
            if (person?.Email is not { } to)
            {
                continue;
            }

            var invite = InterviewInvites.ForInterviewer(
                interview, candidate.FullName, job.Title, new IcsPerson(to, person.DisplayName), InterviewInvites.AddressOf(emailOptions.From), link, now);
            await email.SendAsync(
                TeamEmail.WithFooter(
                    InterviewTeamInviteEmail.Compose(to, candidate.FullName, job.Title, time, cancelled, moved, interview.MeetingUrl, link, interview.MeetingNameOrDefault(), invite),
                    dashboard),
                cancellationToken);
            attendee.MarkInvited(interview.Revision);
        }
    }

    // Who of the attendees has a calendar connection row at all, working or not.
    private static async Task<HashSet<Guid>> ConnectedAsync(ScreenHireDbContext db, IReadOnlyList<InterviewAttendee> attendees, CancellationToken cancellationToken)
    {
        var userIds = attendees.Select(a => a.UserId).ToList();
        return [.. await db.CalendarConnections.AsNoTracking().Where(c => userIds.Contains(c.UserId)).Select(c => c.UserId).ToListAsync(cancellationToken)];
    }

    // The organizer an invitation names: where the candidate's replies go, else the address it comes from.
    private static string? Organizer(MailRoute route, EmailOptions options) => route.ReplyTo ?? route.From ?? options.From;

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
    public static async Task<OutgoingMessages> Handle(
        CreateInterviewEvents message,
        ScreenHireDbContext db,
        CalendarAccess calendar,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        InterviewLinks links,
        Branding.EmailHeaders headers,
        Applications.CandidateMailRouting routing,
        IOptions<DashboardLinkOptions> dashboard,
        IOptions<EmailOptions> emailOptions,
        TimeProvider time,
        ILogger<CreateInterviewEvents> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = await InterviewCalendarSync.WriteAsync(
            message.OrganizationId, message.InterviewId, message.Revision, move: false, db, calendar, email, renderer, store, links, headers, routing, dashboard, emailOptions, time, logger, cancellationToken);
        // The interviewers, by their own choices (docs/architecture/notifications.md); out of date by then, it tells nobody.
        outgoing.Add(new NotifyInterviewChanged(message.OrganizationId, message.InterviewId, message.Revision, "Booked"));
        return outgoing;
    }
}

/// <summary>Moves each interviewer's event to the new time and confirms it to the candidate (<see cref="InterviewCalendarSync"/>).</summary>
public static class UpdateInterviewEventsHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(UpdateInterviewEvents.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        UpdateInterviewEvents message,
        ScreenHireDbContext db,
        CalendarAccess calendar,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        InterviewLinks links,
        Branding.EmailHeaders headers,
        Applications.CandidateMailRouting routing,
        IOptions<DashboardLinkOptions> dashboard,
        IOptions<EmailOptions> emailOptions,
        TimeProvider time,
        ILogger<UpdateInterviewEvents> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = await InterviewCalendarSync.WriteAsync(
            message.OrganizationId, message.InterviewId, message.Revision, move: true, db, calendar, email, renderer, store, links, headers, routing, dashboard, emailOptions, time, logger, cancellationToken);
        outgoing.Add(new NotifyInterviewChanged(message.OrganizationId, message.InterviewId, message.Revision, "Moved"));
        return outgoing;
    }
}

/// <summary>Removes each interviewer's event and tells the candidate the interview is cancelled (<see cref="InterviewCalendarSync"/>).</summary>
public static class CancelInterviewEventsHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(CancelInterviewEvents.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        CancelInterviewEvents message,
        ScreenHireDbContext db,
        CalendarAccess calendar,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        Branding.EmailHeaders headers,
        Applications.CandidateMailRouting routing,
        IOptions<DashboardLinkOptions> dashboard,
        IOptions<EmailOptions> emailOptions,
        TimeProvider time,
        ILogger<CancelInterviewEvents> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = await InterviewCalendarSync.CancelAsync(
            message.OrganizationId, message.InterviewId, message.Revision, message.TellCandidate, db, calendar, email, renderer, store, headers, routing, dashboard, emailOptions, time, logger, cancellationToken);
        outgoing.Add(new NotifyInterviewChanged(message.OrganizationId, message.InterviewId, message.Revision, "Cancelled", message.ActorUserId));
        return outgoing;
    }
}
