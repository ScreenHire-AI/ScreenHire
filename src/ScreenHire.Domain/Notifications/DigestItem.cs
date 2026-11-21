using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Notifications;

/// <summary>
/// One thing waiting for a person's daily digest email (docs/architecture/notifications.md, "The daily digest"). It holds
/// ids only, never a name or text: the digest writes its lines when it sends, and leaves out an item whose record is
/// gone, so erasing a candidate leaves nothing here to erase.
/// </summary>
/// <remarks>
/// Row-level security is the organization's, as for <see cref="Notification"/>, because the worker writes rows for people it
/// does not act as, so it has no restrictive <c>job_scope</c> policy (ADR-0015) although it carries job ids. Every read for a
/// person narrows to <see cref="UserId"/>, and the digest checks the job again when it sends. Only the worker reads the table;
/// an API route that read it would need that policy first.
/// </remarks>
public sealed class DigestItem : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid UserId { get; init; }

    public NoticeKind Kind { get; init; }

    /// <summary>What it is about (the note mention, the task, the application), unique per person and kind.</summary>
    public string SourceId { get; init; } = "";

    public Guid? ApplicationId { get; init; }

    public Guid? JobId { get; init; }

    /// <summary>The member who made the change, when one did.</summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>What happened, as a word the digest turns into its line ("Booked", "Moved"); never a name or text.</summary>
    public string? Detail { get; init; }

    public const int MaxDetailLength = 30;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SentAt { get; private set; }

    public const int MaxSourceIdLength = 120;

    public static DigestItem Create(
        Guid organizationId, Guid userId, NoticeKind kind, string sourceId, Guid? applicationId, Guid? jobId, Guid? actorUserId,
        string? detail, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceId.Length, MaxSourceIdLength, nameof(sourceId));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(detail?.Length ?? 0, MaxDetailLength, nameof(detail));
        return new DigestItem
        {
            OrganizationId = organizationId,
            UserId = userId,
            Kind = kind,
            SourceId = sourceId,
            ApplicationId = applicationId,
            JobId = jobId,
            ActorUserId = actorUserId,
            Detail = detail,
            CreatedAt = now,
        };
    }

    public void MarkSent(DateTimeOffset now) => SentAt ??= now;
}
