using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Application.Distribution;

/// <summary>
/// One board's integration (docs/architecture/job-distribution.md §2). Every adapter validates and maps a frozen job
/// version and reports what the board did. Push adapters call the board from the worker; every other mode only changes
/// what we serve, so the API runs it inside the publish or close transaction.
/// </summary>
public interface IJobBoardAdapter
{
    /// <summary>The stable id stored on postings and used in URLs, such as <c>careerjet</c>.</summary>
    string BoardId { get; }

    /// <summary>The board's name as people know it.</summary>
    string Name { get; }

    BoardCapabilities Capabilities { get; }

    /// <summary>
    /// Whether the adapter has what it needs (credentials, a sandbox switch). An unconfigured board is hidden: it is
    /// never listed, offered or posted to.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>What the board needs fixed before it can take this content, in plain language. Empty when it can.</summary>
    IReadOnlyList<string> Validate(PostingContent content);

    Task<BoardResult> PublishAsync(PostingRequest request, CancellationToken cancellationToken);

    /// <summary>Sends a newer version of a posting the board already has.</summary>
    Task<BoardResult> UpdateAsync(PostingRequest request, CancellationToken cancellationToken);

    Task<BoardResult> CloseAsync(PostingRequest request, CancellationToken cancellationToken);

    /// <summary>Asks the board where the posting stands. Only boards that confirm anything have something to say.</summary>
    Task<BoardResult> GetStatusAsync(PostingRequest request, CancellationToken cancellationToken);
}

/// <summary>An adapter whose board takes a payload of its own shape: an XML element for a feed, a listing for the sandbox.</summary>
public interface IJobBoardAdapter<out TPayload> : IJobBoardAdapter
{
    TPayload Map(PostingContent content);
}

/// <summary>How a board gets jobs from us.</summary>
public enum BoardMode
{
    /// <summary>We serve the page ourselves (the careers page): what we serve is what is live.</summary>
    Hosted,

    /// <summary>The board crawls a feed we serve, on its own schedule, and never reports back.</summary>
    Pull,

    /// <summary>The board, or an aggregator, reads our public job-board API on its own schedule.</summary>
    PullFromAts,

    /// <summary>We tell the board a URL changed (Google's Indexing API); it crawls the page itself and never reports back.</summary>
    Notify,

    /// <summary>We call the board's API, and it can tell us where a posting stands.</summary>
    Push,
}

/// <summary>
/// What a board can do, which drives the UI's promises (job-distribution.md §1). <see cref="ConfirmsLive"/> and
/// <see cref="ConfirmsRemoval"/> say whether Live and Closed can ever be reached; a board that does not confirm stays
/// Submitted or Closing with <see cref="PublishEta"/> or <see cref="RemovalEta"/> as its estimate.
/// </summary>
/// <param name="Mode">How the board gets jobs.</param>
/// <param name="CanClose">Whether we can take a posting down at all.</param>
/// <param name="CanUnpublishPerBoard">Whether one board can be taken down while the job stays open.</param>
/// <param name="ConfirmsLive">Whether the board, or our own server, confirms a posting is showing.</param>
/// <param name="ConfirmsRemoval">Whether the board, or our own server, confirms a posting is gone.</param>
/// <param name="PublishEta">How long the board usually takes to show a posting, if known.</param>
/// <param name="RemovalEta">How long the board usually takes to remove a posting, if known.</param>
/// <param name="DailyQuota">Calls a day the board allows for the whole deployment, if it limits them.</param>
/// <param name="PartnerGated">
/// The board admits only cleared, paying customers (Indeed's partner rules): offered only when
/// <c>Organization.MayUsePartnerBoards</c>.
/// </param>
public sealed record BoardCapabilities(
    BoardMode Mode,
    bool CanClose,
    bool CanUnpublishPerBoard,
    bool ConfirmsLive,
    bool ConfirmsRemoval,
    TimeSpan? PublishEta,
    TimeSpan? RemovalEta,
    int? DailyQuota = null,
    bool PartnerGated = false);

/// <summary>
/// The content a board receives: one frozen <see cref="JobVersion"/> and where the job lives. Adapters read this,
/// never the live job.
/// </summary>
public sealed record PostingContent(
    Guid JobId,
    int Version,
    JobSnapshot Snapshot,
    DateTimeOffset DatePosted,
    DateTimeOffset VersionPublishedAt,
    string Url,
    string OrganizationName,
    string OrganizationSlug);

/// <summary>A posting and the content of its version, as an adapter call receives them.</summary>
public sealed record PostingRequest(JobPosting Posting, PostingContent Content);

/// <summary>What happened on the board after a call.</summary>
public enum BoardOutcome
{
    /// <summary>Nothing new: the request waits (for quota, for the board's answer).</summary>
    Pending,

    /// <summary>The board took it but has not confirmed it is showing.</summary>
    Submitted,

    /// <summary>Confirmed showing.</summary>
    Live,

    /// <summary>The board refused it.</summary>
    Rejected,

    /// <summary>Removal requested; the board has not confirmed it.</summary>
    Closing,

    /// <summary>Confirmed gone.</summary>
    Closed,
}

/// <summary>
/// An adapter's answer. <paramref name="ExpectedBy"/> is the board's estimate for the next step;
/// <paramref name="CheckAgainAt"/> is when reconciliation should ask a push board again (null: nothing to ask).
/// </summary>
public sealed record BoardResult(
    BoardOutcome Outcome,
    string? ExternalId = null,
    DateTimeOffset? ExpectedBy = null,
    DateTimeOffset? CheckAgainAt = null,
    string? Reason = null)
{
    public static BoardResult Pending(DateTimeOffset? checkAgainAt = null) => new(BoardOutcome.Pending, CheckAgainAt: checkAgainAt);
}

/// <summary>Applies a <see cref="BoardResult"/> to its posting: the one place adapters' answers become state.</summary>
public static class BoardResults
{
    /// <summary>Returns whether the posting's state or version changed, which is when <c>JobPostingsChanged</c> is raised.</summary>
    public static bool Apply(this JobPosting posting, BoardResult result, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(posting);
        ArgumentNullException.ThrowIfNull(result);
        var before = posting.State;
        switch (result.Outcome)
        {
            case BoardOutcome.Submitted:
                posting.MarkSubmitted(posting.JobVersion, result.ExternalId, result.ExpectedBy, result.CheckAgainAt, now);
                break;
            case BoardOutcome.Live:
                posting.MarkLive(posting.JobVersion, result.ExternalId, now);
                break;
            case BoardOutcome.Rejected:
                posting.Reject(posting.JobVersion, result.Reason ?? "The board refused the posting.", now);
                break;
            case BoardOutcome.Closing:
                posting.AwaitRemoval(result.ExpectedBy, result.CheckAgainAt, now);
                break;
            case BoardOutcome.Closed:
                posting.MarkClosed(now);
                break;
            case BoardOutcome.Pending when result.CheckAgainAt is { } checkAgainAt:
                posting.CheckAgainAt(checkAgainAt, now);
                break;
        }

        return posting.State != before;
    }
}
