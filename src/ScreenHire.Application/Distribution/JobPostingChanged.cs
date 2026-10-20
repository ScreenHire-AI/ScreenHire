using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Distribution;

namespace ScreenHire.Application.Distribution;

/// <summary>
/// A job's posting on one board changed state or version: raised by the API's publish and close transactions and by
/// the worker when a board answers. Open dashboards refresh the job's distribution panel on it. Ids, the board and the
/// state only.
/// </summary>
public sealed record JobPostingChanged(
    Guid OrganizationId, Guid JobId, Guid PostingId, string BoardId, PostingState State, int JobVersion, DateTimeOffset ChangedAt)
    : ITenantEvent
{
    public static JobPostingChanged From(JobPosting posting)
    {
        ArgumentNullException.ThrowIfNull(posting);
        return new(posting.OrganizationId, posting.JobId, posting.Id, posting.BoardId, posting.State, posting.JobVersion, posting.UpdatedAt);
    }
}
