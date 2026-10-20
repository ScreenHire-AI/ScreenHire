using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Distribution;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Persistence;

namespace ScreenHire.Application.Distribution;

public enum PostingAction
{
    Publish,
    Update,
    Close,
}

/// <summary>
/// Tells the worker to call a push board for one posting: publish or update <see cref="JobVersion"/>, or close it.
/// Published by the API with the posting's change, through the outbox.
/// </summary>
/// <remarks>
/// <see cref="IdempotencyKey"/> is <c>(jobId, boardId, jobVersion)</c> from job-distribution.md, plus the action and the
/// posting's request sequence: the same request delivered twice runs once, while a later request for the same version
/// (posted again after a close, or resent by reconciliation) is new.
/// </remarks>
public sealed record SyncJobPosting(
    Guid OrganizationId, Guid PostingId, Guid JobId, string BoardId, int JobVersion, PostingAction Action, int Sequence)
    : ITenantMessage
{
    public string IdempotencyKey => $"{JobId}:{BoardId}:{JobVersion}:{Action}:{Sequence}";

    public static SyncJobPosting For(JobPosting posting, PostingAction action)
    {
        ArgumentNullException.ThrowIfNull(posting);
        return new(posting.OrganizationId, posting.Id, posting.JobId, posting.BoardId, posting.JobVersion, action, posting.Sequence);
    }
}

public static partial class SyncJobPostingHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(SyncJobPosting.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        SyncJobPosting message,
        ScreenHireDbContext db,
        BoardRegistry boards,
        TimeProvider time,
        ILogger<SyncJobPosting> logger,
        CancellationToken cancellationToken)
    {
        var posting = await PostingLocks.LockAsync(db, message.PostingId, cancellationToken);

        // Effect idempotency: act only while the posting still wants exactly this request. A stale version, a publish
        // overtaken by a close, or a request already answered is a no-op.
        var wanted = message.Action == PostingAction.Close
            ? posting.State == PostingState.Closing
            : posting.State == PostingState.Queued && posting.JobVersion == message.JobVersion;
        if (!wanted || posting.Sequence != message.Sequence)
        {
            LogSkipped(logger, message.PostingId, message.Action, posting.State);
            return [];
        }

        var content = await PostingContents.LoadAsync(db, posting, cancellationToken)
            ?? throw new InvalidOperationException($"Job version {posting.JobVersion} of posting {posting.Id} is not visible.");
        var adapter = boards.Get(posting.BoardId);
        var request = new PostingRequest(posting, content);
        var result = message.Action switch
        {
            PostingAction.Publish => await adapter.PublishAsync(request, cancellationToken),
            PostingAction.Update => await adapter.UpdateAsync(request, cancellationToken),
            _ => await adapter.CloseAsync(request, cancellationToken),
        };

        return PostingLocks.Outcome(posting, posting.Apply(result, time.GetUtcNow()));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped {Action} for posting {PostingId}, now {State}")]
    private static partial void LogSkipped(ILogger logger, Guid postingId, PostingAction action, PostingState state);
}

/// <summary>
/// Asks a push board where one posting stands, at the time the board said it would be done, and resends a request that
/// never got through. Scheduled by the worker after each board call, and sent by the <see cref="ReconcileJobPostings"/>
/// sweep for anything a lost schedule left behind.
/// </summary>
public sealed record ReconcileJobPosting(Guid OrganizationId, Guid PostingId) : ITenantMessage;

public static class ReconcileJobPostingHandler
{
    public static async Task<OutgoingMessages> Handle(
        ReconcileJobPosting message,
        ScreenHireDbContext db,
        BoardRegistry boards,
        IOptions<DistributionOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var posting = await PostingLocks.LockAsync(db, message.PostingId, cancellationToken);
        var adapter = boards.Find(posting.BoardId);
        var now = time.GetUtcNow();
        if (adapter?.Capabilities.Mode != BoardMode.Push)
        {
            return [];
        }

        var settings = options.Value;
        // A request that never reached the board: no answer, no schedule, and older than the resend delay.
        var stuck = posting.NextCheckAt is null && posting.UpdatedAt + settings.ResendAfter <= now
            && posting.State is PostingState.Queued or PostingState.Closing;
        if (stuck)
        {
            var before = posting.State;
            var messages = new OutgoingMessages();
            if (posting.Resend(settings.MaxResends, now))
            {
                messages.Add(SyncJobPosting.For(posting, posting.State == PostingState.Closing ? PostingAction.Close : ResendAction(posting)));
            }

            if (posting.State != before)
            {
                messages.Add(new JobPostingsChanged(posting.OrganizationId, posting.JobId, posting.UpdatedAt));
            }

            return messages;
        }

        // Only a posting waiting on the board, and only once its check is due: a duplicate or early schedule does nothing.
        if (posting.State is not (PostingState.Submitted or PostingState.Closing) || posting.NextCheckAt is not { } due || due > now)
        {
            return [];
        }

        var content = await PostingContents.LoadAsync(db, posting, cancellationToken)
            ?? throw new InvalidOperationException($"Job version {posting.JobVersion} of posting {posting.Id} is not visible.");
        var result = await adapter.GetStatusAsync(new PostingRequest(posting, content), cancellationToken);
        if (result.Outcome is BoardOutcome.Pending or BoardOutcome.Submitted or BoardOutcome.Closing && result.CheckAgainAt is null)
        {
            // Still waiting and the board gave no time: ask again later, backing off up to an hour.
            var delay = TimeSpan.FromTicks(Math.Min(
                settings.StatusPollInterval.Ticks * (1L << Math.Min(posting.Attempts, 6)), TimeSpan.FromHours(1).Ticks));
            result = result with { CheckAgainAt = now + delay };
        }

        return PostingLocks.Outcome(posting, posting.Apply(result, now));
    }

    private static PostingAction ResendAction(JobPosting posting) =>
        posting.ExternalId is null ? PostingAction.Publish : PostingAction.Update;
}

/// <summary>Shared steps of the posting handlers.</summary>
internal static class PostingLocks
{
    /// <summary>
    /// Loads the posting with a row lock held to the end of the handler's transaction, so a publish, a close and a status
    /// check for one posting never interleave around a board call. Runs as the message's organization.
    /// </summary>
    public static async Task<JobPosting> LockAsync(ScreenHireDbContext db, Guid postingId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM job_postings WHERE id = {postingId} FOR UPDATE", cancellationToken);
        return await db.JobPostings.SingleOrDefaultAsync(p => p.Id == postingId, cancellationToken)
            ?? throw new InvalidOperationException($"Posting {postingId} is not visible to the message's organization.");
    }

    /// <summary>The event for a state change, and the status check the board's answer asked for.</summary>
    public static OutgoingMessages Outcome(JobPosting posting, bool changed)
    {
        var messages = new OutgoingMessages();
        if (changed)
        {
            messages.Add(new JobPostingsChanged(posting.OrganizationId, posting.JobId, posting.UpdatedAt));
        }

        if (posting.NextCheckAt is { } checkAt)
        {
            messages.Schedule(new ReconcileJobPosting(posting.OrganizationId, posting.Id), checkAt);
        }

        return messages;
    }
}
