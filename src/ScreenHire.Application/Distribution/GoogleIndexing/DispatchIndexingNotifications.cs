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
/// The worker's cron, every minute: spends what is left of today's Indexing API quota on the waiting notifications of
/// every organization, deletes first. It reserves the calls in <c>indexing_quota_days</c> and hands each notification
/// to <see cref="SendIndexingNotification"/>, which runs as the notification's organization. It is not about one
/// organization, so like <c>ReportOpenJobs</c> it reads each organization in turn and never goes around row-level
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

        // The day's row is locked for the rest of the transaction, so two dispatchers never spend the same calls.
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO indexing_quota_days (day, used) VALUES ({today}, 0) ON CONFLICT (day) DO NOTHING", cancellationToken);
        var quota = await db.IndexingQuotaDays.FromSql($"SELECT * FROM indexing_quota_days WHERE day = {today} FOR UPDATE")
            .SingleAsync(cancellationToken);

        var chosen = IndexingPlan.Choose(pending, options.Value.GoogleIndexing.DailyQuota - quota.Used);
        quota.Used += chosen.Count;
        LogDispatched(logger, chosen.Count, pending.Count, quota.Used);
        return [.. chosen.Select(n => new SendIndexingNotification(n.OrganizationId, n.Id))];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dispatching {Chosen} of {Pending} Indexing API notifications; {Used} calls used today")]
    private static partial void LogDispatched(ILogger logger, int chosen, int pending, int used);
}

/// <summary>Sends one queued Indexing API notification, as its organization, and records what Google accepted.</summary>
public sealed record SendIndexingNotification(Guid OrganizationId, Guid NotificationId) : ITenantMessage;

public static class SendIndexingNotificationHandler
{
    public static async Task<IReadOnlyList<JobPostingChanged>> Handle(
        SendIndexingNotification message,
        ScreenHireDbContext db,
        IIndexingClient client,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var notification = await db.IndexingNotifications.SingleOrDefaultAsync(n => n.Id == message.NotificationId, cancellationToken);
        if (notification is not { IsPending: true })
        {
            // Sent already, or replaced by a newer notification for the same posting.
            return [];
        }

        await client.NotifyAsync(notification.Url, notification.Type, cancellationToken);
        var now = time.GetUtcNow();
        notification.SentAt = now;

        var posting = await db.JobPostings.SingleAsync(p => p.Id == notification.PostingId, cancellationToken);
        var before = (posting.State, posting.JobVersion);
        if (notification.Type == IndexingNotificationType.Updated)
        {
            posting.MarkSubmitted(posting.JobVersion, externalId: null, expectedBy: null, nextCheckAt: null, now);
        }
        else
        {
            posting.AwaitRemoval(expectedBy: null, nextCheckAt: null, now);
        }

        return before == (posting.State, posting.JobVersion) ? [] : [JobPostingChanged.From(posting)];
    }
}
