using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Distribution;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Application.Distribution.GoogleIndexing;

/// <summary>A notification waiting for quota, as the dispatcher sees it.</summary>
public sealed record PendingNotification(Guid OrganizationId, Guid Id, IndexingNotificationType Type, DateTimeOffset EnqueuedAt);

/// <summary>The quota-aware order (job-distribution.md §3): deletes first, because a closed job still listed misleads candidates, then oldest first.</summary>
public static class IndexingPlan
{
    public static IReadOnlyList<PendingNotification> Choose(IEnumerable<PendingNotification> pending, int remainingQuota) =>
        remainingQuota <= 0
            ? []
            : [.. pending
                .OrderBy(n => n.Type == IndexingNotificationType.Deleted ? 0 : 1)
                .ThenBy(n => n.EnqueuedAt)
                .ThenBy(n => n.Id)
                .Take(remainingQuota)];
}

/// <summary>
/// The worker's cron, every minute: hands as many waiting notifications as today's Indexing API quota has left, from
/// every organization and deletes first, to <see cref="SendIndexingNotification"/>, which runs as the notification's
/// organization and spends the call only when Google accepts it. A notification still waiting a minute later (its send
/// failed, or has not run yet) is handed out again, which costs nothing: only an accepted call counts. It is not about
/// one organization, so like <c>ReportOpenJobs</c> it reads each organization in turn and never goes around row-level
/// security.
/// </summary>
public sealed record DispatchIndexingNotifications;

public static partial class DispatchIndexingNotificationsHandler
{
    public static async Task<IReadOnlyList<SendIndexingNotification>> Handle(
        DispatchIndexingNotifications message,
        ScreenHireDbContext db,
        TenantContext tenant,
        BoardRegistry boards,
        IOptions<DistributionOptions> options,
        TimeProvider time,
        ILogger<DispatchIndexingNotifications> logger,
        CancellationToken cancellationToken)
    {
        if (boards.Find(Boards.GoogleIndexing) is null)
        {
            return [];
        }

        var organizationIds = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var pending = new List<PendingNotification>();
        foreach (var organizationId in organizationIds)
        {
            tenant.SetOrganization(organizationId);
            pending.AddRange(await db.IndexingNotifications.AsNoTracking()
                .Where(n => n.SentAt == null && n.SupersededAt == null)
                .Select(n => new PendingNotification(n.OrganizationId, n.Id, n.Type, n.EnqueuedAt))
                .ToListAsync(cancellationToken));
        }

        if (pending.Count == 0)
        {
            return [];
        }

        // Only a read: each send spends its own call (IndexingQuota.TrySpendAsync), so the quota is never overspent.
        var today = IndexingQuota.Today(time);
        var used = await db.IndexingQuotaDays.AsNoTracking().Where(d => d.Day == today).Select(d => (int?)d.Used)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        var chosen = IndexingPlan.Choose(pending, options.Value.GoogleIndexing.DailyQuota - used);
        LogDispatched(logger, chosen.Count, pending.Count, used);
        return [.. chosen.Select(n => new SendIndexingNotification(n.OrganizationId, n.Id))];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dispatching {Chosen} of {Pending} Indexing API notifications; {Used} calls used today")]
    private static partial void LogDispatched(ILogger logger, int chosen, int pending, int used);
}

/// <summary>Sends one queued Indexing API notification, as its organization, and records what Google accepted.</summary>
public sealed record SendIndexingNotification(Guid OrganizationId, Guid NotificationId) : ITenantMessage;

public static class SendIndexingNotificationHandler
{
    public static async Task<IReadOnlyList<JobPostingsChanged>> Handle(
        SendIndexingNotification message,
        ScreenHireDbContext db,
        IIndexingClient client,
        IOptions<DistributionOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var postingId = await db.IndexingNotifications.AsNoTracking()
            .Where(n => n.Id == message.NotificationId)
            .Select(n => (Guid?)n.PostingId)
            .SingleOrDefaultAsync(cancellationToken);
        if (postingId is null)
        {
            return [];
        }

        // The posting's row lock comes before the notification's first tracked read, so a second send of the same
        // notification (handed out again a minute later) waits for this one and then reads it as sent.
        var posting = await PostingLocks.LockAsync(db, postingId.Value, cancellationToken);
        var notification = await db.IndexingNotifications.SingleAsync(n => n.Id == message.NotificationId, cancellationToken);
        if (!notification.IsPending)
        {
            // Sent already, or replaced by a newer notification for the same posting.
            return [];
        }

        if (!await IndexingQuota.TrySpendAsync(db, IndexingQuota.Today(time), options.Value.GoogleIndexing.DailyQuota, cancellationToken))
        {
            // Today's quota is spent; it stays queued for tomorrow's first dispatch.
            return [];
        }

        // If Google refuses, the exception rolls the transaction back, quota call included, and the retry policy takes over.
        await client.NotifyAsync(notification.Url, notification.Type, cancellationToken);
        var now = time.GetUtcNow();
        notification.SentAt = now;

        var before = (posting.State, posting.JobVersion);
        if (notification.Type == IndexingNotificationType.Updated)
        {
            posting.MarkSubmitted(posting.JobVersion, externalId: null, expectedBy: null, nextCheckAt: null, now);
        }
        else
        {
            posting.AwaitRemoval(expectedBy: null, nextCheckAt: null, now);
        }

        return before == (posting.State, posting.JobVersion) ? [] : [new JobPostingsChanged(posting.OrganizationId, posting.JobId, posting.UpdatedAt)];
    }
}

/// <summary>
/// The Indexing API's daily quota, one counter per UTC day in <c>indexing_quota_days</c> for the whole deployment
/// (job-distribution.md section 3).
/// </summary>
internal static class IndexingQuota
{
    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    /// <summary>
    /// Spends one call of <paramref name="day"/>'s quota inside the handler's transaction, or returns false when none is
    /// left. The update holds the day's row lock until the transaction ends, so sends never overspend, and a send that
    /// fails rolls its call back.
    /// </summary>
    public static async Task<bool> TrySpendAsync(ScreenHireDbContext db, DateOnly day, int dailyQuota, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO indexing_quota_days (day, used) VALUES ({day}, 0) ON CONFLICT (day) DO NOTHING", cancellationToken);
        var spent = await db.Database.ExecuteSqlAsync(
            $"UPDATE indexing_quota_days SET used = used + 1 WHERE day = {day} AND used < {dailyQuota}", cancellationToken);
        return spent == 1;
    }
}
