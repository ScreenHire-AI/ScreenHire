using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Distribution;

/// <summary>
/// A Google Indexing API notification waiting for quota, or sent (docs/architecture/job-distribution.md §3). The API
/// allows 200 publish calls a day for the whole project, shared by every tenant, so notifications queue here and the
/// worker sends them deletes first.
/// </summary>
public sealed class IndexingNotification : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid PostingId { get; init; }

    public required string Url { get; init; }

    public IndexingNotificationType Type { get; init; }

    public DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>When Google accepted it. Null while it waits for quota.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Set when a newer notification for the same URL made this one pointless, so it is never sent.</summary>
    public DateTimeOffset? SupersededAt { get; set; }

    public bool IsPending => SentAt is null && SupersededAt is null;
}

/// <summary>The Indexing API's notification types: <c>URL_UPDATED</c> and <c>URL_DELETED</c>.</summary>
public enum IndexingNotificationType
{
    Updated,
    Deleted,
}

/// <summary>
/// The Indexing API calls used on one UTC day, for the whole deployment. Not tenant data: a date and a count. The
/// dispatcher reserves calls here before it hands notifications to each organization to send.
/// </summary>
public sealed class IndexingQuotaDay
{
    public DateOnly Day { get; init; }

    public int Used { get; set; }
}
