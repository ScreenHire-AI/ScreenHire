using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Distribution;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Distribution.GoogleIndexing;

/// <summary>
/// Google for Jobs through the Indexing API (docs/architecture/job-distribution.md §3). Google reads the careers page's
/// <c>JobPosting</c> JSON-LD; this adapter only tells it a URL changed (<c>URL_UPDATED</c>) or went (<c>URL_DELETED</c>),
/// so it crawls sooner. The calls share one daily quota across every tenant, so the adapter queues a notification and
/// the worker sends queued ones deletes first (<see cref="DispatchIndexingNotifications"/>). Google never reports what it
/// indexed: a posting is Submitted once Google accepted <c>URL_UPDATED</c>, and stays Closing after <c>URL_DELETED</c>.
/// Hidden while <c>ScreenHire:Distribution:GoogleIndexing:Mode</c> is Off, or is Google with no service account key.
/// </summary>
public sealed class GoogleIndexingAdapter(ScreenHireDbContext db, TimeProvider time, IOptions<DistributionOptions> options) : IJobBoardAdapter
{
    private GoogleIndexingOptions Settings => options.Value.GoogleIndexing;

    public string BoardId => Boards.GoogleIndexing;

    public string Name => "Google for Jobs";

    // A URL_DELETED for a page that still serves the job would be undone by Google's next crawl, so Google comes off only
    // with the job.
    public BoardCapabilities Capabilities => new(
        BoardMode.Notify, CanClose: true, CanUnpublishPerBoard: false, ConfirmsLive: false, ConfirmsRemoval: false,
        PublishEta: null, RemovalEta: null, DailyQuota: Settings.DailyQuota);

    public bool IsConfigured => Settings.Mode switch
    {
        GoogleIndexingMode.Local => true,
        GoogleIndexingMode.Google => !string.IsNullOrWhiteSpace(Settings.ServiceAccountKeyJson),
        _ => false,
    };

    // The careers page's JobPosting rules are the job's publish rules.
    public IReadOnlyList<string> Validate(PostingContent content) => [];

    public Task<BoardResult> PublishAsync(PostingRequest request, CancellationToken cancellationToken) =>
        EnqueueAsync(request, IndexingNotificationType.Updated, BoardResult.Pending(), cancellationToken);

    public Task<BoardResult> UpdateAsync(PostingRequest request, CancellationToken cancellationToken) =>
        PublishAsync(request, cancellationToken);

    public Task<BoardResult> CloseAsync(PostingRequest request, CancellationToken cancellationToken) =>
        EnqueueAsync(request, IndexingNotificationType.Deleted, new BoardResult(BoardOutcome.Closing), cancellationToken);

    public Task<BoardResult> GetStatusAsync(PostingRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(BoardResult.Pending());

    // A newer notification for the posting replaces any still waiting: an update queued before a close is never sent,
    // and a repeated update costs one call, not two.
    private async Task<BoardResult> EnqueueAsync(
        PostingRequest request, IndexingNotificationType type, BoardResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = time.GetUtcNow();
        var waiting = await db.IndexingNotifications
            .Where(n => n.PostingId == request.Posting.Id && n.SentAt == null && n.SupersededAt == null)
            .ToListAsync(cancellationToken);
        foreach (var notification in waiting)
        {
            notification.SupersededAt = now;
        }

        db.IndexingNotifications.Add(new IndexingNotification
        {
            OrganizationId = request.Posting.OrganizationId,
            PostingId = request.Posting.Id,
            Url = request.Posting.Url,
            Type = type,
            EnqueuedAt = now,
        });
        return result;
    }
}
