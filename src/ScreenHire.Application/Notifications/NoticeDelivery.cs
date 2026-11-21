using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Notifications;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Notifications;

/// <summary>
/// The end of every email ScreenHire sends a member about their work: "Change what we email you", a link to the notification
/// grid on Your account (docs/architecture/email.md, "Email to the team"). Sign-in links and invitations are not
/// notifications and have none.
/// </summary>
public static class TeamEmail
{
    public const string SettingsPath = "/account/notifications";

    public const string FooterText = "Change what we email you";

    /// <summary>The message with the footer added, or as it is when this host has no dashboard address to link to.</summary>
    public static EmailMessage WithFooter(EmailMessage message, DashboardLinkOptions links)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(links);
        if (links.Url(SettingsPath) is not { } settings)
        {
            return message;
        }

        var href = WebUtility.HtmlEncode(settings.ToString());
        return message with
        {
            TextBody = $"{message.TextBody.TrimEnd()}\n\n--\n{FooterText}: {settings}",
            HtmlBody = message.HtmlBody is null
                ? null
                : $"{message.HtmlBody}\n<hr>\n<p><a href=\"{href}\">{FooterText}</a></p>",
        };
    }
}

/// <summary>
/// Something one person may be told about, by id: what the digest and the bell are built from. The source id is what it is
/// about, unique for that person and kind (the note, the task, the reply); the detail a word for what happened ("Booked",
/// "Moved"), never a name or text.
/// </summary>
public sealed record Notice(
    Guid OrganizationId, Guid UserId, NoticeKind Kind, string SourceId, Guid? ApplicationId = null, Guid? JobId = null,
    Guid? ActorUserId = null, string? Detail = null);

/// <summary>A row for one person's bell: a kind of <see cref="NotificationKinds"/>, its key, a small payload and a dashboard path.</summary>
public sealed record PersonalBell(string Kind, string Key, IReadOnlyDictionary<string, string> Payload, string Link);

/// <summary>What happened to the email side of a notice.</summary>
public enum NoticeOutcome
{
    Emailed,
    Queued,
    NotEmailed,
}

/// <summary>
/// Delivers a notice by the person's email choice: Instantly sends it now with the footer, Daily digest keeps a
/// <see cref="DigestItem"/> for the next digest, Off does nothing. Each is idempotent on its own: the digest row and the bell
/// row are unique, and the caller records an email it sent (a mention's <c>EmailedAt</c>) or deduplicates its message.
/// </summary>
public static class NoticeDelivery
{
    public static async Task<NoticeOutcome> EmailAsync(
        ScreenHireDbContext db,
        TransactionalEmail email,
        DashboardLinkOptions links,
        Notice notice,
        EmailChoice choice,
        Func<EmailMessage?> compose,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(compose);
        switch (choice)
        {
            case EmailChoice.Off:
                return NoticeOutcome.NotEmailed;
            case EmailChoice.Digest:
                await QueueAsync(db, notice, now, cancellationToken);
                return NoticeOutcome.Queued;
            default:
                if (!email.IsConfigured || compose() is not { } message)
                {
                    return NoticeOutcome.NotEmailed;
                }

                await email.SendAsync(TeamEmail.WithFooter(message, links), cancellationToken);
                return NoticeOutcome.Emailed;
        }
    }

    /// <summary>Keeps the notice for the person's next digest; the same notice twice is one row.</summary>
    public static Task<int> QueueAsync(ScreenHireDbContext db, Notice notice, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(notice);
        var item = DigestItem.Create(
            notice.OrganizationId, notice.UserId, notice.Kind, notice.SourceId, notice.ApplicationId, notice.JobId, notice.ActorUserId,
            notice.Detail, now);
        var kind = item.Kind.ToString();
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO digest_items (id, organization_id, user_id, kind, source_id, application_id, job_id, actor_user_id, detail, created_at)
            VALUES ({item.Id}, {item.OrganizationId}, {item.UserId}, {kind}, {item.SourceId}, {item.ApplicationId}, {item.JobId}, {item.ActorUserId}, {item.Detail}, {now})
            ON CONFLICT (organization_id, user_id, kind, source_id) DO NOTHING
            """,
            cancellationToken);
    }

    /// <summary>
    /// Puts one row in one person's bell (the per-person twin of <see cref="RaiseNotification"/>, which picks people by
    /// permission). Returns whether a row was added, so the caller sends <see cref="NotificationsChanged"/>.
    /// </summary>
    public static async Task<bool> RaiseForPersonAsync(
        ScreenHireDbContext db, Guid organizationId, Guid userId, PersonalBell bell, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(bell);
        if (!Notification.IsDashboardPath(bell.Link))
        {
            return false;
        }

        var payload = JsonSerializer.Serialize(bell.Payload);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO notifications (id, organization_id, user_id, kind, key, payload, link, created_at)
            VALUES ({Guid.CreateVersion7()}, {organizationId}, {userId}, {bell.Kind}, {bell.Key}, {payload}::jsonb, {bell.Link}, {now})
            ON CONFLICT (organization_id, user_id, kind, key) DO NOTHING
            """,
            cancellationToken);
        return inserted > 0;
    }
}
