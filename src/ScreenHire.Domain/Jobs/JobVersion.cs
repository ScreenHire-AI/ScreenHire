using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Jobs;

/// <summary>
/// An immutable snapshot of a job's posting content, taken each time the job is published with new content
/// (docs/architecture/job-distribution.md §2). Job distribution publishes a version, never the live job, so a board
/// shows exactly what was published. The database lets the application role insert and read versions but never change
/// or delete one.
/// </summary>
public sealed class JobVersion : IJobScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid JobId { get; init; }

    /// <summary>1 for the first publish, then one more each time changed content is published.</summary>
    public int Version { get; init; }

    public required JobSnapshot Snapshot { get; init; }

    public DateTimeOffset PublishedAt { get; init; }
}
