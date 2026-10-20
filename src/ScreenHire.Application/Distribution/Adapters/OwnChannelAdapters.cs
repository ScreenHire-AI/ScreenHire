using System.Text.RegularExpressions;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Application.Distribution.Adapters;

/// <summary>
/// The tenant's careers page: the job's own page with <c>JobPosting</c> JSON-LD, listed in the tenant's sitemap. We
/// serve it, so publishing makes it live and closing it serves <c>410 Gone</c> at once, both confirmed by our own server.
/// It is the job's home, so it cannot be taken down while the job stays open.
/// </summary>
public sealed class CareersPageAdapter : IJobBoardAdapter
{
    public string BoardId => Boards.CareersPage;

    public string Name => "Careers page";

    public BoardCapabilities Capabilities { get; } = new(
        BoardMode.Hosted, CanClose: true, CanUnpublishPerBoard: false, ConfirmsLive: true, ConfirmsRemoval: true,
        PublishEta: TimeSpan.Zero, RemovalEta: TimeSpan.Zero);

    public bool IsConfigured => true;

    // The job's own publish rules (a description, a place) already cover what the page and its JSON-LD need.
    public IReadOnlyList<string> Validate(PostingContent content) => [];

    public Task<BoardResult> PublishAsync(PostingRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new BoardResult(BoardOutcome.Live));

    public Task<BoardResult> UpdateAsync(PostingRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new BoardResult(BoardOutcome.Live));

    public Task<BoardResult> CloseAsync(PostingRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new BoardResult(BoardOutcome.Closed));

    public Task<BoardResult> GetStatusAsync(PostingRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(BoardResult.Pending());
}

/// <summary>
/// A board that reads what we serve on its own schedule and never reports back: a feed crawler or a reader of the public
/// job-board API. Publishing puts the job in what we serve at once (Submitted, with the board's refresh interval as the
/// estimate); closing takes it out at once (Closing, likewise). Neither Live nor Closed is ever claimed, because the
/// board never confirms either.
/// </summary>
public abstract class PullBoardAdapter(TimeProvider time) : IJobBoardAdapter
{
    /// <summary>How long a client may cache what we serve (<c>Cache-Control: max-age</c>); it adds to every estimate.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    public abstract string BoardId { get; }

    public abstract string Name { get; }

    public abstract BoardCapabilities Capabilities { get; }

    public virtual bool IsConfigured => true;

    public abstract IReadOnlyList<string> Validate(PostingContent content);

    public Task<BoardResult> PublishAsync(PostingRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new BoardResult(BoardOutcome.Submitted, ExpectedBy: Estimate(Capabilities.PublishEta)));

    public Task<BoardResult> UpdateAsync(PostingRequest request, CancellationToken cancellationToken) =>
        PublishAsync(request, cancellationToken);

    public Task<BoardResult> CloseAsync(PostingRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new BoardResult(BoardOutcome.Closing, ExpectedBy: Estimate(Capabilities.RemovalEta)));

    public Task<BoardResult> GetStatusAsync(PostingRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(BoardResult.Pending());

    /// <summary>A board's refresh interval as a time, plus our cache lifetime. Unknown stays unknown.</summary>
    protected DateTimeOffset? Estimate(TimeSpan? refreshInterval) =>
        refreshInterval is { } interval ? time.GetUtcNow() + interval + CacheLifetime : null;

    /// <summary>Capabilities of a feed board that refreshes every <paramref name="refreshInterval"/>, if it says.</summary>
    protected static BoardCapabilities FeedCapabilities(BoardMode mode, TimeSpan? refreshInterval) => new(
        mode, CanClose: true, CanUnpublishPerBoard: true, ConfirmsLive: false, ConfirmsRemoval: false,
        PublishEta: refreshInterval, RemovalEta: refreshInterval);
}

/// <summary>
/// The read-only public job-board API (Greenhouse-style JSON) that pull-from-ATS boards and aggregators read
/// (job-boards.md §4). Consumers poll it on their own schedules, which nobody publishes.
/// </summary>
public sealed class JobBoardApiAdapter(TimeProvider time) : PullBoardAdapter(time)
{
    public override string BoardId => Boards.JobBoardApi;

    public override string Name => "Public job-board API";

    public override BoardCapabilities Capabilities { get; } = FeedCapabilities(BoardMode.PullFromAts, refreshInterval: null);

    public override IReadOnlyList<string> Validate(PostingContent content) => [];
}

/// <summary>Plain-language checks shared by adapters.</summary>
internal static partial class Checks
{
    /// <summary>The description's text length with tags removed, which is what boards' minimums count.</summary>
    public static int TextLength(string html) => Tags().Replace(html, " ").Trim().Length;

    public static bool HasCity(JobSnapshot snapshot) => snapshot.Locations.Any(l => !string.IsNullOrWhiteSpace(l.City));

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();
}
