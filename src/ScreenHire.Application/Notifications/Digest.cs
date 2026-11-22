using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Notifications;

/// <summary>
/// When a person's daily digest is due (docs/architecture/notifications.md, "The daily digest"): at 08:00 in their time zone
/// (UTC while they have none), once a local day, holding what arrived before that day's 08:00. Something that arrives at
/// 10:00 waits for the next morning, so a day that starts with nothing never gets a "daily" email in the afternoon.
/// </summary>
public static class DigestSchedule
{
    public static readonly TimeOnly SendAt = new(8, 0);

    /// <summary>The person's time zone, or UTC for none or one this host does not know.</summary>
    public static TimeZoneInfo ZoneOf(string? timeZone) =>
        timeZone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>
    /// The local date and the cutoff (that date's 08:00 as an instant) when a digest is due at <paramref name="at"/>, or null when
    /// it is before 08:00 there or the person already had one that day.
    /// </summary>
    public static (DateOnly Date, DateTimeOffset Cutoff)? Due(DateTimeOffset at, string? timeZone, DateOnly? lastDigestOn)
    {
        var zone = ZoneOf(timeZone);
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        if (TimeOnly.FromDateTime(local.DateTime) < SendAt || lastDigestOn >= date)
        {
            return null;
        }

        var localCutoff = date.ToDateTime(SendAt);
        // 08:00 on a day a clock jumps over it does not exist; the hour after it does.
        while (zone.IsInvalidTime(localCutoff))
        {
            localCutoff = localCutoff.AddMinutes(30);
        }

        var cutoff = new DateTimeOffset(localCutoff, zone.GetUtcOffset(localCutoff)).ToUniversalTime();
        return (date, cutoff);
    }
}

/// <summary>Every half hour, UTC (a cron schedule in the worker): has no tenant, so it fans out one <see cref="SendOrganizationDigests"/> each.</summary>
public sealed record SendDueDigests;

/// <summary>Sends the digests due in one organization at <paramref name="At"/>, which tests choose.</summary>
public sealed record SendOrganizationDigests(Guid OrganizationId, DateTimeOffset At) : ITenantMessage;

/// <summary>One person's digest for one local date; the key is the person and the date, so a day sends at most one.</summary>
public sealed record SendDigest(Guid OrganizationId, Guid UserId, DateOnly LocalDate, DateTimeOffset Cutoff) : ITenantMessage
{
    public string Key => $"{OrganizationId:N}:{UserId:N}:{LocalDate:yyyyMMdd}";
}

public static class SendDueDigestsHandler
{
    public static async Task<OutgoingMessages> Handle(SendDueDigests message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new SendOrganizationDigests(id, now)));
        return outgoing;
    }
}

public static class SendOrganizationDigestsHandler
{
    public static async Task<OutgoingMessages> Handle(SendOrganizationDigests message, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var waiting = await db.DigestItems.AsNoTracking()
            .Where(i => i.SentAt == null)
            .GroupBy(i => i.UserId)
            .Select(g => new { UserId = g.Key, Oldest = g.Min(i => i.CreatedAt) })
            .ToListAsync(cancellationToken);
        if (waiting.Count == 0)
        {
            return outgoing;
        }

        var preferences = await PersonNotices.ForUsersAsync(db, message.OrganizationId, [.. waiting.Select(w => w.UserId)], cancellationToken);
        foreach (var person in waiting)
        {
            var mine = preferences.GetValueOrDefault(person.UserId);
            if (DigestSchedule.Due(message.At, mine?.TimeZone, mine?.LastDigestOn) is { } due && person.Oldest < due.Cutoff)
            {
                outgoing.Add(new SendDigest(message.OrganizationId, person.UserId, due.Date, due.Cutoff));
            }
        }

        return outgoing;
    }
}

/// <summary>
/// Sends one person's digest: one email, the items grouped by row, each line a link to its record. Items the person can no
/// longer see, or whose record is gone, are left out; with nothing left, nothing is sent. Deduplicated on the person and the
/// date; the items it read are marked sent in the same transaction, so a redelivery after the commit finds none.
/// </summary>
public static partial class SendDigestHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(SendDigest.Key))]
    public static async Task Handle(
        SendDigest message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        IOptions<DashboardLinkOptions> links,
        TimeProvider time,
        ILogger<SendDigest> logger,
        CancellationToken cancellationToken)
    {
        var items = await db.DigestItems
            .Where(i => i.UserId == message.UserId && i.SentAt == null && i.CreatedAt < message.Cutoff)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
        var member = await (
            from m in db.OrganizationMembers
            where m.OrganizationId == message.OrganizationId && m.UserId == message.UserId
            join role in db.CustomRoles on m.CustomRoleId equals role.Id into roles
            from role in roles.DefaultIfEmpty()
            select new { m.Id, m.Role, Custom = role == null ? null : role.Permissions })
            .SingleOrDefaultAsync(cancellationToken);
        var now = time.GetUtcNow();
        foreach (var item in items)
        {
            item.MarkSent(now);
        }

        if (items.Count == 0 || member is null || !email.IsConfigured)
        {
            LogNotSent(logger, message.UserId, items.Count);
            return;
        }

        // What the person may see and wants now, not when each item was queued: a role that lost View AI usage, or a row since
        // turned Off, leaves its items out.
        var permissions = member.Role == OrganizationRole.Custom ? RolePermissions.ForCustom(member.Custom ?? []) : RolePermissions.Of(member.Role);
        var preferences = await db.MemberPreferences.SingleOrDefaultAsync(p => p.MemberId == member.Id, cancellationToken);
        var alerts = await OrganizationAlertDefaults.ReadAsync(db, cancellationToken);
        var handlesBilling = permissions.Contains(Permission.ManageBilling);
        var wanted = items
            .Where(i => NoticeRows.For(i.Kind).IsFor(permissions)
                && PersonNotices.Email(preferences, i.Kind, alerts, handlesBilling) != EmailChoice.Off)
            .ToList();
        var lines = await LinesAsync(db, message, wanted, links.Value, cancellationToken);
        var to = await db.Users.Where(u => u.Id == message.UserId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken);
        var organization = await db.Organizations.Where(o => o.Id == message.OrganizationId).Select(o => o.Name).SingleOrDefaultAsync(cancellationToken);
        if (lines.Count == 0 || to is null || organization is null)
        {
            LogNotSent(logger, message.UserId, items.Count);
            return;
        }

        await email.SendAsync(TeamEmail.WithFooter(DigestEmail.Compose(to, organization, lines), links.Value), cancellationToken);
        if (preferences is null)
        {
            preferences = MemberPreferences.Create(message.OrganizationId, member.Id, message.UserId, now);
            db.MemberPreferences.Add(preferences);
        }

        preferences.MarkDigestSent(message.LocalDate);
        LogSent(logger, message.UserId, lines.Count);
    }

    private static async Task<List<DigestLine>> LinesAsync(
        ScreenHireDbContext db, SendDigest message, List<DigestItem> items, DashboardLinkOptions links, CancellationToken cancellationToken)
    {
        var applicationIds = items.Where(i => i.ApplicationId is not null).Select(i => i.ApplicationId!.Value).Distinct().ToList();
        var places = await (
            from application in db.Applications.AsNoTracking()
            join candidate in db.Candidates on application.CandidateId equals candidate.Id
            join job in db.Jobs on application.JobId equals job.Id
            where applicationIds.Contains(application.Id)
            select new { application.Id, Candidate = candidate.FullName, Job = job.Title }).ToDictionaryAsync(p => p.Id, cancellationToken);
        var actorIds = items.Where(i => i.ActorUserId is not null).Select(i => i.ActorUserId!.Value).Distinct().ToList();
        var actors = await db.Users.Where(u => actorIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.DisplayName ?? u.Email }).ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);
        var seen = new Dictionary<Guid, bool>();
        var lines = new List<DigestLine>();
        foreach (var item in items)
        {
            if (item.JobId is { } jobId)
            {
                if (!seen.TryGetValue(jobId, out var sees))
                {
                    sees = await JobAccessQueries.SeesJobAsync(db, message.OrganizationId, message.UserId, jobId, cancellationToken);
                    seen[jobId] = sees;
                }

                if (!sees)
                {
                    continue;
                }
            }

            var place = item.ApplicationId is { } applicationId ? places.GetValueOrDefault(applicationId) : null;
            if (item.ApplicationId is not null && place is null)
            {
                continue; // erased since
            }

            var actor = item.ActorUserId is { } actorId ? actors.GetValueOrDefault(actorId) : null;
            if (DigestEmail.Line(item, place?.Candidate, place?.Job, actor) is { } line && links.Url(DigestEmail.PathOf(item)) is { } link)
            {
                lines.Add(new DigestLine(item.Kind, line, link));
            }
        }

        return lines;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Digest for {UserId}: nothing sent ({Items} items read)")]
    private static partial void LogNotSent(ILogger logger, Guid userId, int items);

    [LoggerMessage(Level = LogLevel.Information, Message = "Digest for {UserId}: sent with {Lines} lines")]
    private static partial void LogSent(ILogger logger, Guid userId, int lines);
}

/// <summary>One line of a digest: its row, its words and where it goes.</summary>
public sealed record DigestLine(NoticeKind Kind, string Text, Uri Link);

/// <summary>The digest's words. Names and titles only; verification and AI use lines never name a candidate (ADR-0004).</summary>
public static class DigestEmail
{
    public static string Heading(NoticeKind kind) => kind switch
    {
        NoticeKind.Mentions => "Mentions",
        NoticeKind.Replies => "Candidate replies",
        NoticeKind.Interviews => "Interviews",
        NoticeKind.Tasks => "Tasks for you",
        NoticeKind.NewApplications => "New applications",
        NoticeKind.TeammateChanges => "Teammates' moves and notes",
        NoticeKind.VerificationSuggested => "Verification suggested",
        _ => "AI use and billing",
    };

    /// <summary>Where an item's line goes: its record, or the page that explains it.</summary>
    public static string PathOf(DigestItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var application = item.ApplicationId is { } id ? MemberNoticeEmails.ApplicationPath(id) : "/";
        return item.Kind switch
        {
            NoticeKind.Mentions when Guid.TryParse(item.SourceId, out var noteId) && item.ApplicationId is { } appId => MentionEmail.NotePath(appId, noteId),
            NoticeKind.Replies when item.ApplicationId is { } appId => ReplyEmail.MessagesPath(appId),
            NoticeKind.AiUseAndBilling => item.Detail == "Trial" ? "/settings/billing" : "/verification/ai-use",
            _ => application,
        };
    }

    public static string? Line(DigestItem item, string? candidate, string? job, string? actor)
    {
        ArgumentNullException.ThrowIfNull(item);
        var someone = actor ?? "A teammate";
        return item.Kind switch
        {
            NoticeKind.Mentions => $"{someone} mentioned you in a note on {candidate}, {job}",
            NoticeKind.Replies => $"{candidate} replied to your email about {job}",
            NoticeKind.Interviews => MemberNoticeEmails.InterviewLine(item.Detail, candidate, job),
            NoticeKind.Tasks => actor is null ? $"A new task on {candidate}, {job}" : $"{actor} gave you a task on {candidate}, {job}",
            NoticeKind.NewApplications => $"{candidate} applied to {job}",
            NoticeKind.TeammateChanges => $"{someone} {MemberNoticeEmails.ChangeVerb(item.Detail)} {candidate}, {job}",
            NoticeKind.VerificationSuggested => VerificationAlertWording.Suggested(job ?? "a job"),
            NoticeKind.AiUseAndBilling => item.Detail == "Trial" ? "Your free trial ends soon" : "AI use needs a look",
            _ => null,
        };
    }

    public static EmailMessage Compose(string to, string organization, IReadOnlyList<DigestLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var text = new StringBuilder($"Here is what happened at {organization} since yesterday's digest.\n");
        var html = new StringBuilder($"<p>Here is what happened at {WebUtility.HtmlEncode(organization)} since yesterday's digest.</p>\n");
        foreach (var group in lines.GroupBy(l => l.Kind).OrderBy(g => g.Key))
        {
            var heading = Heading(group.Key);
            text.Append('\n').Append(heading).Append('\n');
            html.Append("<h3>").Append(WebUtility.HtmlEncode(heading)).Append("</h3>\n<ul>\n");
            foreach (var line in group)
            {
                text.Append("- ").Append(line.Text).Append(": ").Append(line.Link).Append('\n');
                html.Append("<li><a href=\"").Append(WebUtility.HtmlEncode(line.Link.ToString())).Append("\">")
                    .Append(WebUtility.HtmlEncode(line.Text)).Append("</a></li>\n");
            }

            html.Append("</ul>\n");
        }

        var count = lines.Count;
        return new EmailMessage(to, $"Your ScreenHire digest: {count} {(count == 1 ? "update" : "updates")}", text.ToString(), html.ToString());
    }
}
