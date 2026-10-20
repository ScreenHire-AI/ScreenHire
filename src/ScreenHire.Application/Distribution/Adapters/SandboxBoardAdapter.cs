using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Distribution;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Distribution.Adapters;

/// <summary>What the sandbox board stores for a posting.</summary>
public sealed record SandboxPayload(string Title, string Url, int Version);

/// <summary>
/// The local sandbox board: a push board with delayed acceptance and delayed removal, so one-click publish and
/// teardown are exercised end to end with no third party (docs/architecture/job-distribution.md). Its "remote" storage
/// is the <c>sandbox_listings</c> table. Like a real push board it confirms both: a posting is Live only once the
/// acceptance delay has passed, and Closed only once the removal delay has. Hidden unless
/// <c>ScreenHire:Distribution:Sandbox:Enabled</c>.
/// </summary>
public sealed class SandboxBoardAdapter(ScreenHireDbContext db, TimeProvider time, IOptions<DistributionOptions> options)
    : IJobBoardAdapter<SandboxPayload>
{
    private SandboxBoardOptions Settings => options.Value.Sandbox;

    public string BoardId => Boards.Sandbox;

    public string Name => "Sandbox board";

    public BoardCapabilities Capabilities => new(
        BoardMode.Push, CanClose: true, CanUnpublishPerBoard: true, ConfirmsLive: true, ConfirmsRemoval: true,
        PublishEta: Settings.AcceptanceDelay, RemovalEta: Settings.RemovalDelay);

    public bool IsConfigured => Settings.Enabled;

    public IReadOnlyList<string> Validate(PostingContent content) => [];

    public SandboxPayload Map(PostingContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new SandboxPayload(content.Snapshot.Title, content.Url, content.Version);
    }

    public async Task<BoardResult> PublishAsync(PostingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await FindAsync(request, cancellationToken) is not null)
        {
            return await UpdateAsync(request, cancellationToken);
        }

        var now = time.GetUtcNow();
        var payload = Map(request.Content);
        var listing = new SandboxListing
        {
            OrganizationId = request.Posting.OrganizationId,
            PostingId = request.Posting.Id,
            JobId = request.Posting.JobId,
            JobVersion = payload.Version,
            Title = payload.Title,
            Url = payload.Url,
            ReceivedAt = now,
            AcceptsAt = now + Settings.AcceptanceDelay,
        };
        db.SandboxListings.Add(listing);
        return new BoardResult(BoardOutcome.Submitted, listing.Id.ToString(), listing.AcceptsAt, listing.AcceptsAt);
    }

    public async Task<BoardResult> UpdateAsync(PostingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var listing = await FindAsync(request, cancellationToken);
        if (listing is null)
        {
            return await PublishAsync(request, cancellationToken);
        }

        var now = time.GetUtcNow();
        var payload = Map(request.Content);
        listing.Title = payload.Title;
        listing.Url = payload.Url;
        listing.JobVersion = payload.Version;
        if (listing.RemovalRequestedAt is not null)
        {
            // Posted again after a close: the board reviews it like a new posting.
            listing.RemovalRequestedAt = null;
            listing.RemovesAt = null;
            listing.AcceptsAt = now + Settings.AcceptanceDelay;
        }

        return listing.IsShowing(now)
            ? new BoardResult(BoardOutcome.Live, listing.Id.ToString())
            : new BoardResult(BoardOutcome.Submitted, listing.Id.ToString(), listing.AcceptsAt, listing.AcceptsAt);
    }

    public async Task<BoardResult> CloseAsync(PostingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var listing = await FindAsync(request, cancellationToken);
        var now = time.GetUtcNow();
        if (listing is null || listing.IsRemoved(now))
        {
            return new BoardResult(BoardOutcome.Closed);
        }

        if (listing.RemovalRequestedAt is null)
        {
            listing.RemovalRequestedAt = now;
            listing.RemovesAt = now + Settings.RemovalDelay;
        }

        return new BoardResult(BoardOutcome.Closing, listing.Id.ToString(), listing.RemovesAt, listing.RemovesAt);
    }

    public async Task<BoardResult> GetStatusAsync(PostingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var listing = await FindAsync(request, cancellationToken);
        var now = time.GetUtcNow();
        if (listing is null)
        {
            return request.Posting.State == PostingState.Closing
                ? new BoardResult(BoardOutcome.Closed)
                : new BoardResult(BoardOutcome.Rejected, Reason: "The sandbox board has no listing for this posting.");
        }

        if (listing.IsRemoved(now))
        {
            return new BoardResult(BoardOutcome.Closed, listing.Id.ToString());
        }

        if (listing.RemovesAt is { } removesAt)
        {
            return new BoardResult(BoardOutcome.Closing, listing.Id.ToString(), removesAt, removesAt);
        }

        return listing.IsShowing(now)
            ? new BoardResult(BoardOutcome.Live, listing.Id.ToString())
            : BoardResult.Pending(listing.AcceptsAt);
    }

    // Tracked, so changes save with the handler's transaction. The posting id is the sandbox's idempotency key.
    private Task<SandboxListing?> FindAsync(PostingRequest request, CancellationToken cancellationToken) =>
        db.SandboxListings.SingleOrDefaultAsync(l => l.PostingId == request.Posting.Id, cancellationToken);
}
