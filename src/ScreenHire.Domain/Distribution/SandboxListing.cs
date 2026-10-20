using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Distribution;

/// <summary>
/// One job on the sandbox board: a local stand-in for a push board's own database, so one-click publish and teardown
/// run end to end with no third party (docs/architecture/job-distribution.md). Like a real board it accepts a posting
/// after a delay and removes it after another; the times are set when the request arrives, and the board's status
/// reads them against the clock.
/// </summary>
public sealed class SandboxListing : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The posting this listing came from: the sandbox's idempotency key, one listing per posting.</summary>
    public Guid PostingId { get; init; }

    public Guid JobId { get; init; }

    public int JobVersion { get; set; }

    public required string Title { get; set; }

    public required string Url { get; set; }

    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>When the board shows the listing.</summary>
    public DateTimeOffset AcceptsAt { get; set; }

    public DateTimeOffset? RemovalRequestedAt { get; set; }

    /// <summary>When the board stops showing the listing, once a removal was requested.</summary>
    public DateTimeOffset? RemovesAt { get; set; }

    // ListSandboxListings (PublicDistributionEndpoints) spells this out in SQL; change both together.
    public bool IsShowing(DateTimeOffset now) => AcceptsAt <= now && (RemovesAt is null || RemovesAt > now);

    public bool IsRemoved(DateTimeOffset now) => RemovesAt <= now;
}
