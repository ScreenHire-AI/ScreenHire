using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Application.Distribution;

/// <summary>What one publish or close changed: postings to add, and messages to publish through the outbox with them.</summary>
public sealed record DistributionChanges(IReadOnlyList<JobPosting> Added, IReadOnlyList<object> Messages);

/// <summary>
/// One-click publish and teardown across boards, run by the API inside the job's own publish or close transaction, so
/// the response already shows every board's state and estimate. Hosted, pull and notify boards change only what we
/// serve, so their adapters run here; a push board is called by the worker (<see cref="SyncJobPosting"/>).
/// </summary>
public sealed class DistributionPlanner(BoardRegistry boards, TimeProvider time)
{
    /// <summary>
    /// Sends <paramref name="content"/>'s version to boards. <paramref name="boardIds"/> null means: every board this
    /// organization may use the first time, then the boards the job is already on that it may still use. Boards with validation issues wait in
    /// Draft with the issues. <paramref name="postings"/> are the job's postings, tracked.
    /// </summary>
    public async Task<DistributionChanges> PublishAsync(
        Organization organization,
        PostingContent content,
        IReadOnlyList<JobPosting> postings,
        IReadOnlyCollection<string>? boardIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(postings);

        // The API refuses a named board the organization cannot use. A board the job is already on that has since been
        // unconfigured, or that the organization may no longer use, is left as it is rather than failing the publish.
        var available = boards.AvailableTo(organization).Select(a => a.BoardId).ToList();
        var targets = boardIds is not null
            ? [.. boardIds.Distinct(StringComparer.Ordinal)]
            : postings.Count == 0
                ? available
                : postings
                    .Where(p => p.State is not (PostingState.Closed or PostingState.Closing) && available.Contains(p.BoardId, StringComparer.Ordinal))
                    .Select(p => p.BoardId)
                    .ToList();

        var now = time.GetUtcNow();
        var added = new List<JobPosting>();
        var messages = new List<object>();
        var changed = false;
        foreach (var boardId in targets)
        {
            var posting = postings.FirstOrDefault(p => p.BoardId == boardId);
            if (posting is null)
            {
                posting = new JobPosting { OrganizationId = organization.Id, JobId = content.JobId, BoardId = boardId, CreatedAt = now };
                added.Add(posting);
            }

            changed |= await SendAsync(posting, content, resend: false, messages, now, cancellationToken) || added.Contains(posting);
        }

        if (changed)
        {
            messages.Add(new JobPostingsChanged(organization.Id, content.JobId, now));
        }

        return new DistributionChanges(added, messages);
    }

    /// <summary>
    /// Sends <paramref name="content"/> to <paramref name="posting"/>'s board only, after the caller changed that board's own text
    /// (<see cref="JobPosting.Customize"/>) and checked it. With <paramref name="resend"/> the board gets the same job
    /// version again with the new text. Without it, for a save that changed nothing, a posting already on the board is
    /// left alone and spends none of the board's quota. No other board is touched and no job version is made.
    /// <paramref name="posting"/> is the board's tracked posting, or a new one this returns in
    /// <see cref="DistributionChanges.Added"/>. Always raises
    /// <see cref="JobPostingsChanged"/>: a customization can leave a Submitted posting Submitted, and other open
    /// dashboards still need to refetch the text.
    /// </summary>
    public async Task<DistributionChanges> RepostAsync(
        Organization organization, PostingContent content, JobPosting posting, bool isNew, bool resend, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(posting);
        var now = time.GetUtcNow();
        var messages = new List<object>();
        await SendAsync(posting, content, resend, messages, now, cancellationToken);
        messages.Add(new JobPostingsChanged(organization.Id, content.JobId, now));
        return new DistributionChanges(isNew ? [posting] : [], messages);
    }

    // One board's share of a publish: validates the board's own view of the content (the job's, with the board's
    // overrides), queues the posting and sends it. Returns whether the posting's state, version or sent request changed.
    private async Task<bool> SendAsync(
        JobPosting posting, PostingContent content, bool resend, List<object> messages, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var adapter = boards.Get(posting.BoardId);
        var boardContent = PostingContents.For(posting, content);
        var before = (posting.State, posting.JobVersion, posting.Sequence);
        var wasOnBoard = posting.ExternalId is not null || posting.State is PostingState.Submitted or PostingState.Live;
        if (posting.Queue(boardContent.Version, boardContent.Url, adapter.Validate(boardContent), now, resend))
        {
            var request = new PostingRequest(posting, boardContent);
            if (adapter.Capabilities.Mode == BoardMode.Push)
            {
                messages.Add(SyncJobPosting.For(posting, wasOnBoard ? PostingAction.Update : PostingAction.Publish));
            }
            else
            {
                var result = wasOnBoard
                    ? await adapter.UpdateAsync(request, cancellationToken)
                    : await adapter.PublishAsync(request, cancellationToken);
                posting.Apply(result, now);
            }
        }

        return before != (posting.State, posting.JobVersion, posting.Sequence);
    }

    /// <summary>
    /// Takes the job down from <paramref name="boardId"/>, or from every board when it is null (teardown). Nothing is
    /// claimed gone before the board, or our own server, confirms it: most postings go to Closing with the board's
    /// estimate, and only what we serve ourselves (the careers page) or what never reached a board is Closed at once.
    /// <paramref name="content"/> is the newest version's content, or null for a job never published.
    /// </summary>
    public async Task<DistributionChanges> CloseAsync(
        PostingContent? content, IReadOnlyList<JobPosting> postings, string? boardId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(postings);
        var now = time.GetUtcNow();
        var messages = new List<object>();
        JobPosting? changed = null;
        foreach (var posting in postings.Where(p => boardId is null || p.BoardId == boardId))
        {
            var adapter = boards.Find(posting.BoardId);
            var before = (posting.State, posting.JobVersion);
            var eta = adapter?.Capabilities.RemovalEta is { } removal && adapter.Capabilities.Mode == BoardMode.Push ? now + removal : (DateTimeOffset?)null;
            if (posting.RequestClose(eta, nextCheckAt: null, now) == CloseOutcome.SendClose)
            {
                if (adapter is null)
                {
                    // The board's configuration is gone, so nothing can reach it; say so instead of claiming removal.
                    posting.RecordError("This board is no longer configured, so the removal could not be sent.", now);
                }
                else if (adapter.Capabilities.Mode == BoardMode.Push)
                {
                    messages.Add(SyncJobPosting.For(posting, PostingAction.Close));
                }
                else if (content is not null)
                {
                    posting.Apply(await adapter.CloseAsync(new PostingRequest(posting, content), cancellationToken), now);
                }
            }

            if (before != (posting.State, posting.JobVersion))
            {
                changed = posting;
            }
        }

        if (changed is not null)
        {
            messages.Add(new JobPostingsChanged(changed.OrganizationId, changed.JobId, now));
        }

        return new DistributionChanges([], messages);
    }
}
