using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Jobs;

/// <summary>
/// A job went live, or its live content changed: <see cref="Version"/> is the <c>JobVersion</c> that is now published.
/// Published with the change, through the outbox. The same transaction posts that version to the boards
/// (docs/architecture/job-distribution.md §5).
/// </summary>
/// <remarks>
/// <c>Version</c> and <c>JobVersionId</c> came later than the other members, so they are optional: an envelope queued
/// before them still deserializes.
/// </remarks>
public sealed record JobPublished(Guid OrganizationId, Guid JobId, DateTimeOffset PublishedAt, int Version = 1, Guid? JobVersionId = null)
    : ITenantEvent;
