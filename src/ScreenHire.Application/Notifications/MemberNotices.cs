using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Notifications;

/// <summary>
/// Tells one member about one thing on a job they see, by their own choices (docs/architecture/notifications.md, "Each
/// person's choices"): a row in their bell for what is addressed to them (a task, an interview), then an email now, a line
/// in tomorrow's digest, or nothing. The fan-out messages below pick the people; this one decides for each. The source id is
/// what it is about, unique for the person and kind (the task, the application, the move's timeline entry); the detail is a
/// word for what happened (<c>Booked</c>, <c>Moved</c>, <c>Noted</c>), never a name or text.
/// </summary>
public sealed record NotifyMember(
    Guid OrganizationId, Guid UserId, NoticeKind Kind, string SourceId, Guid ApplicationId, Guid JobId, Guid? ActorUserId, string? Detail = null)
    : ITenantMessage
{
    public string IdempotencyKey => $"notice:{Kind}:{SourceId}:{UserId:N}";
}

/// <summary>A candidate applied: tells the members whose scope covers the job and who want new applications by email.</summary>
public sealed record NotifyNewApplication(Guid OrganizationId, Guid ApplicationId, Guid JobId) : ITenantMessage;

/// <summary>
/// A member moved a candidate or added a note: tells the teammates whose scope covers the job and who want teammates'
/// changes by email (off unless they chose it). Never for an API key, an automation or a candidate, as with the toasts. The
/// change is <c>Moved</c> or <c>Noted</c>; the source is the timeline entry, so each change is told once.
/// </summary>
public sealed record NotifyTeammateChange(
    Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid ActorUserId, string Change, Guid SourceId) : ITenantMessage;

/// <summary>An interview was booked, moved or cancelled (the change: <c>Booked</c>, <c>Moved</c>, <c>Cancelled</c>): tells the members on it.</summary>
public sealed record NotifyInterviewChanged(Guid OrganizationId, Guid InterviewId, int Revision, string Change) : ITenantMessage;

/// <summary>The words ScreenHire's own emails about a member's work use. Names and titles only, never a note, task or reply's text.</summary>
public static class MemberNoticeEmails
{
    public static string ApplicationPath(Guid applicationId) => $"/applications/{applicationId}";

    public static EmailMessage TaskAssigned(string to, string? actor, NoticePlace place, Uri link) =>
        Compose(to, $"A task on {place.Candidate}",
            actor is null
                ? $"You have a new task on {place.Where}."
                : $"{actor} gave you a task on {place.Where}.",
            "Open the task", link);

    public static EmailMessage NewApplication(string to, NoticePlace place, Uri link) =>
        Compose(to, $"{place.Candidate} applied to {place.Job}", $"{place.Candidate} applied to {place.Job} at {place.Organization}.", "Open the application", link);

    public static EmailMessage InterviewChanged(string to, string change, NoticePlace place, Uri link)
    {
        var verb = InterviewVerb(change);
        return Compose(to, $"Interview {verb}: {place.Candidate}", $"The interview with {place.Where} was {verb}.", "Open the application", link);
    }

    public static EmailMessage TeammateChange(string to, string actor, string change, NoticePlace place, Uri link) =>
        Compose(to, $"{actor} {ChangeVerb(change)} {place.Candidate}", $"{actor} {ChangeVerb(change)} {place.Where}.", "Open the application", link);

    public static string InterviewVerb(string? change) => change switch
    {
        "Moved" => "moved",
        "Cancelled" => "cancelled",
        _ => "booked",
    };

    public static string ChangeVerb(string? change) => change == "Noted" ? "added a note on" : "moved";

    private static EmailMessage Compose(string to, string subject, string line, string action, Uri link)
    {
        var text = $"""
            {line}

            {action}: {link}
            """;
        var html = $"""
            <p>{WebUtility.HtmlEncode(line)}</p>
            <p><a href="{WebUtility.HtmlEncode(link.ToString())}">{WebUtility.HtmlEncode(action)}</a></p>
            """;
        return new EmailMessage(to, subject, text, html);
    }
}

/// <summary>Where a notice is: the candidate, the job and the organization, for its line.</summary>
public sealed record NoticePlace(string Candidate, string Job, string Organization)
{
    public string Where => $"{Candidate}, {Job}, at {Organization}";
}

/// <summary>The lookups the notices share.</summary>
public static class NoticeLookups
{
    public static async Task<NoticePlace?> PlaceAsync(ScreenHireDbContext db, Guid applicationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var place = await (
            from application in db.Applications.AsNoTracking()
            join candidate in db.Candidates on application.CandidateId equals candidate.Id
            join job in db.Jobs on application.JobId equals job.Id
            join organization in db.Organizations on application.OrganizationId equals organization.Id
            where application.Id == applicationId
            select new NoticePlace(candidate.FullName, job.Title, organization.Name)).SingleOrDefaultAsync(cancellationToken);
        return place;
    }

    /// <summary>A member's name as the dashboard shows it, or null for nobody.</summary>
    public static async Task<string?> NameAsync(ScreenHireDbContext db, Guid? userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return userId is { } id
            ? await db.Users.Where(u => u.Id == id).Select(u => u.DisplayName ?? u.Email).SingleOrDefaultAsync(cancellationToken)
            : null;
    }

    /// <summary>
    /// The members who see a job (ADR-0015) and whether each is on its hiring team. Narrowed to this organization: the members
    /// filter also shows a user's memberships elsewhere.
    /// </summary>
    public static async Task<List<(Guid UserId, bool OnTeam)>> JobMembersAsync(
        ScreenHireDbContext db, Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var team = await db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.HiringTeam).SingleOrDefaultAsync(cancellationToken);
        if (team is null)
        {
            return [];
        }

        var members = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .Select(m => new { m.Id, m.UserId, m.Role, m.JobAccess })
            .ToListAsync(cancellationToken);
        return
        [
            .. members
                .Where(m => team.Contains(m.Id) || JobAccessRules.SeesAllJobs(m.Role, m.JobAccess))
                .Select(m => (m.UserId, team.Contains(m.Id))),
        ];
    }

    /// <summary>
    /// The members a job row tells: those who see the job, on its hiring team unless their scope is Everything, other than
    /// <paramref name="except"/>, and only those whose email choice for <paramref name="kind"/> is not Off.
    /// </summary>
    public static async Task<List<Guid>> ScopedRecipientsAsync(
        ScreenHireDbContext db, Guid organizationId, Guid jobId, NoticeKind kind, Guid? except, CancellationToken cancellationToken)
    {
        var members = await JobMembersAsync(db, organizationId, jobId, cancellationToken);
        var userIds = members.Select(m => m.UserId).Where(id => id != except).ToList();
        var preferences = await PersonNotices.ForUsersAsync(db, organizationId, userIds, cancellationToken);
        var alerts = await OrganizationAlertDefaults.ReadAsync(db, cancellationToken);
        return
        [
            .. members
                .Where(m => m.UserId != except)
                .Where(m =>
                {
                    var mine = preferences.GetValueOrDefault(m.UserId);
                    return (m.OnTeam || mine?.Scope == NoticeScope.Everything)
                        && PersonNotices.Email(mine, kind, alerts) != EmailChoice.Off;
                })
                .Select(m => m.UserId),
        ];
    }
}

public static class NotifyNewApplicationHandler
{
    public static async Task<OutgoingMessages> Handle(NotifyNewApplication message, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var recipients = await NoticeLookups.ScopedRecipientsAsync(
            db, message.OrganizationId, message.JobId, NoticeKind.NewApplications, except: null, cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(recipients.Select(userId => new NotifyMember(
            message.OrganizationId, userId, NoticeKind.NewApplications, message.ApplicationId.ToString("N"), message.ApplicationId, message.JobId, null)));
        return outgoing;
    }
}

public static class NotifyTeammateChangeHandler
{
    public static async Task<OutgoingMessages> Handle(NotifyTeammateChange message, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var recipients = await NoticeLookups.ScopedRecipientsAsync(
            db, message.OrganizationId, message.JobId, NoticeKind.TeammateChanges, message.ActorUserId, cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(recipients.Select(userId => new NotifyMember(
            message.OrganizationId, userId, NoticeKind.TeammateChanges, message.SourceId.ToString("N"), message.ApplicationId, message.JobId,
            message.ActorUserId, message.Change)));
        return outgoing;
    }
}

public static class NotifyInterviewChangedHandler
{
    public static async Task<OutgoingMessages> Handle(NotifyInterviewChanged message, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var interview = await db.InterviewRequests.AsNoTracking()
            .Where(i => i.Id == message.InterviewId)
            .Select(i => new { i.ApplicationId, i.JobId, i.Revision })
            .SingleOrDefaultAsync(cancellationToken);
        // A later change tells everyone again; this one says nothing once it is out of date.
        if (interview is null || interview.Revision != message.Revision)
        {
            return outgoing;
        }

        var attendees = await db.InterviewAttendees.AsNoTracking()
            .Where(a => a.InterviewId == message.InterviewId)
            .Select(a => a.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        outgoing.AddRange(attendees.Select(userId => new NotifyMember(
            message.OrganizationId, userId, NoticeKind.Interviews, $"{message.InterviewId:N}:{message.Revision}", interview.ApplicationId,
            interview.JobId, null, message.Change)));
        return outgoing;
    }
}

/// <summary>
/// Decides for one member. Nothing for someone who no longer sees the job (ADR-0015), a task done since, or an application
/// gone. Deduplicated on its key; the bell row and the digest row are unique; an email sent before a crash can repeat once,
/// as with a mention.
/// </summary>
public static partial class NotifyMemberHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(NotifyMember.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        NotifyMember message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        IOptions<DashboardLinkOptions> links,
        TimeProvider time,
        ILogger<NotifyMember> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        if (message.UserId == message.ActorUserId
            || !await JobAccessQueries.SeesJobAsync(db, message.OrganizationId, message.UserId, message.JobId, cancellationToken))
        {
            return outgoing;
        }

        var taskId = ParseId(message.SourceId);
        if (message.Kind == NoticeKind.Tasks
            && !await db.ApplicationTasks.AsNoTracking().AnyAsync(t => t.Id == taskId && t.CompletedAt == null, cancellationToken))
        {
            return outgoing; // done or removed since
        }

        var now = time.GetUtcNow();
        var path = MemberNoticeEmails.ApplicationPath(message.ApplicationId);
        var actor = await NoticeLookups.NameAsync(db, message.ActorUserId, cancellationToken);
        if (Bell(message, actor, path) is { } bell
            && await NoticeDelivery.RaiseForPersonAsync(db, message.OrganizationId, message.UserId, bell, now, cancellationToken))
        {
            outgoing.Add(new NotificationsChanged(message.OrganizationId));
        }

        var preferences = await PersonNotices.ForUserAsync(db, message.OrganizationId, message.UserId, cancellationToken);
        var alerts = await OrganizationAlertDefaults.ReadAsync(db, cancellationToken);
        var choice = PersonNotices.Email(preferences, message.Kind, alerts);
        var to = await db.Users.Where(u => u.Id == message.UserId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken);
        var place = await NoticeLookups.PlaceAsync(db, message.ApplicationId, cancellationToken);
        var link = links.Value.Url(path);
        if (to is null || place is null)
        {
            return outgoing;
        }

        var notice = new Notice(
            message.OrganizationId, message.UserId, message.Kind, message.SourceId, message.ApplicationId, message.JobId, message.ActorUserId, message.Detail);
        var outcome = await NoticeDelivery.EmailAsync(db, email, links.Value, notice, choice, () => link is null ? null : message.Kind switch
        {
            NoticeKind.Tasks => MemberNoticeEmails.TaskAssigned(to, actor, place, link),
            NoticeKind.NewApplications => MemberNoticeEmails.NewApplication(to, place, link),
            NoticeKind.Interviews => MemberNoticeEmails.InterviewChanged(to, message.Detail ?? "Booked", place, link),
            NoticeKind.TeammateChanges => MemberNoticeEmails.TeammateChange(to, actor ?? "A teammate", message.Detail ?? "Moved", place, link),
            _ => null,
        }, now, cancellationToken);
        LogDelivered(logger, message.Kind, message.SourceId, outcome);
        return outgoing;
    }

    private static Guid ParseId(string sourceId) => Guid.TryParse(sourceId, out var id) ? id : Guid.Empty;

    /// <summary>The bell row for what is addressed to the person; the team-wide rows have none (Activity keeps them).</summary>
    private static PersonalBell? Bell(NotifyMember message, string? actor, string path) => message.Kind switch
    {
        NoticeKind.Tasks => new PersonalBell(
            NotificationKinds.TaskAssigned, message.SourceId, actor is null ? new Dictionary<string, string>() : new() { ["actor"] = actor }, path),
        NoticeKind.Interviews => new PersonalBell(
            NotificationKinds.InterviewChanged, message.SourceId, new Dictionary<string, string> { ["change"] = message.Detail ?? "Booked" }, path),
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Notice {Kind} {SourceId}: {Outcome}")]
    private static partial void LogDelivered(ILogger logger, NoticeKind kind, string sourceId, NoticeOutcome outcome);
}

/// <summary>
/// A member's note, for the teammates who want teammates' changes by email (docs/architecture/notifications.md). The worker's
/// only handler of this event; the API pushes it to dashboards.
/// </summary>
public static class ApplicationNoteAddedNoticeHandler
{
    public static OutgoingMessages Handle(Applications.ApplicationNoteAdded message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var outgoing = new OutgoingMessages();
        if (message.ActorUserId is { } actor)
        {
            outgoing.Add(new NotifyTeammateChange(message.OrganizationId, message.ApplicationId, message.JobId, actor, "Noted", message.EventId));
        }

        return outgoing;
    }
}

/// <summary>
/// A task given to one person by a member or an API key, for that person by email or in the digest, by their choice. Never to
/// the member who gave it to themselves. An automation's task tells its person from the run (<c>ExecuteAutomationActionHandler</c>).
/// </summary>
public static class ApplicationTaskCreatedNoticeHandler
{
    public static OutgoingMessages Handle(Automations.ApplicationTaskCreated message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var outgoing = new OutgoingMessages();
        if (message.AssigneeUserId is { } assignee && assignee != message.ActorUserId)
        {
            outgoing.Add(new NotifyMember(
                message.OrganizationId, assignee, NoticeKind.Tasks, message.TaskId.ToString("N"), message.ApplicationId, message.JobId, message.ActorUserId));
        }

        return outgoing;
    }
}
