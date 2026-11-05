using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Distribution;

/// <summary>
/// One job on one board: the per job × board state machine of docs/architecture/job-distribution.md §2. A posting
/// carries the <see cref="JobVersion"/> it shows, so a board always shows a frozen version, never the live job.
/// </summary>
/// <remarks>
/// <para>
/// <c>Draft → Queued → Submitted → Live | Rejected → Closing → Closed</c>, plus <c>Error</c> when a board could not be
/// reached after retries. Draft is a selected board whose validation found issues; Queued waits for the worker to
/// call a push board. Hosted and pull boards skip Queued: nothing is sent, so the API moves them on at once.
/// </para>
/// <para>
/// A state is only as strong as what confirms it. Live and Closed mean the board, or our own server, confirmed it.
/// A board that never confirms (a feed crawler, Google) stays Submitted after publishing and Closing after a close,
/// with <see cref="ExpectedBy"/> as the board's own estimate. Nothing marks a removal done on a timer.
/// </para>
/// Every transition takes the time and returns whether anything changed, so a repeated or stale message is a no-op.
/// </remarks>
public sealed class JobPosting : IJobScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid JobId { get; init; }

    /// <summary>The board's stable id, such as <c>careers</c> or <c>careerjet</c>.</summary>
    public required string BoardId { get; init; }

    /// <summary>The job version this posting shows, or is being sent.</summary>
    public int JobVersion { get; private set; }

    public PostingState State { get; private set; } = PostingState.Draft;

    /// <summary>The job's careers-page URL: what boards link to and what Google is notified about.</summary>
    public string Url { get; private set; } = "";

    /// <summary>The board's own id for the posting, once it has one.</summary>
    public string? ExternalId { get; private set; }

    /// <summary>What the board needs fixed before it can take this version (Validate), in plain language.</summary>
    public List<string> Issues { get; private set; } = [];

    /// <summary>The board's reason for a rejection, or the last error reaching it. Never personal data.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// The board's estimate for the next step: when a Submitted posting should be live, or a Closing one gone. Null
    /// when the board gives none (Google publishes no timing).
    /// </summary>
    public DateTimeOffset? ExpectedBy { get; private set; }

    /// <summary>When reconciliation should next ask a push board for its status. Null when there is nothing to ask.</summary>
    public DateTimeOffset? NextCheckAt { get; private set; }

    /// <summary>Reconciliation checks or resends since the last state change.</summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// One more for every request the board must receive (a send, a close, a resend). With the job and version it makes
    /// the idempotency key of the worker's command, so one request is handled once and the next one is not mistaken
    /// for a duplicate of it.
    /// </summary>
    public int Sequence { get; private set; }

    /// <summary>
    /// The title this board receives instead of the job's, or null when it follows the job. Each overridable field is
    /// independent (docs/architecture/job-distribution.md, "Per-board customization").
    /// </summary>
    public string? TitleOverride { get; private set; }

    /// <summary>The description (sanitized HTML) this board receives instead of the job's, or null when it follows the job.</summary>
    public string? DescriptionOverride { get; private set; }

    /// <summary>The job's title when <see cref="TitleOverride"/> was set: what the person saw and chose to replace.</summary>
    public string? TitleBase { get; private set; }

    /// <summary>The job's description when <see cref="DescriptionOverride"/> was set.</summary>
    public string? DescriptionBase { get; private set; }

    /// <summary>When an override was last saved; null when the board has never been customized.</summary>
    public DateTimeOffset? CustomizedAt { get; private set; }

    /// <summary>
    /// The board's display name for the change history line, set by the code that saves an override. Not stored: the
    /// infrastructure layer that describes the change cannot see the board registry.
    /// </summary>
    public string? BoardLabel { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? LiveAt { get; private set; }

    public DateTimeOffset? ClosingAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>
    /// Is on the board, or on its way there: what feeds and the public job-board API list. <c>PostingContents.ActiveOnBoardAsync</c>
    /// spells out the same states in SQL; change both together.
    /// </summary>
    public bool IsActive => State is PostingState.Queued or PostingState.Submitted or PostingState.Live;

    /// <summary>Whether any field of this board's content is its own rather than the job's.</summary>
    public bool IsCustomized => TitleOverride is not null || DescriptionOverride is not null;

    /// <summary>
    /// The content this board receives: <paramref name="snapshot"/> with the fields this board has overridden replaced.
    /// Every adapter and feed reads content through here, so none of them knows about overrides.
    /// </summary>
    public JobSnapshot WithOverrides(JobSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return IsCustomized
            ? snapshot with { Title = TitleOverride ?? snapshot.Title, Description = DescriptionOverride ?? snapshot.Description }
            : snapshot;
    }

    /// <summary>
    /// Whether the job's <paramref name="latest"/> text differs from what it was when an overridden field was
    /// customized, so the person can see what changed under their customization. A field that follows the job is never
    /// stale.
    /// </summary>
    public bool IsStaleAgainst(JobSnapshot latest)
    {
        ArgumentNullException.ThrowIfNull(latest);
        return (TitleOverride is not null && TitleBase != latest.Title)
            || (DescriptionOverride is not null && DescriptionBase != latest.Description);
    }

    /// <summary>
    /// Sets what this board receives in place of the job's title and description. A null or blank field, or one equal to
    /// the job's own text in <paramref name="job"/>, follows the job. The job's text is kept as the base the override
    /// was written against. Returns whether anything this board shows, or the base it is compared with, changed.
    /// </summary>
    public bool Customize(string? title, string? description, JobSnapshot job, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(job);
        var newTitle = Normalize(title, job.Title);
        var newDescription = Normalize(description, job.Description);
        var newTitleBase = newTitle is null ? null : job.Title;
        var newDescriptionBase = newDescription is null ? null : job.Description;
        var changed = newTitle != TitleOverride
            || newDescription != DescriptionOverride
            || newTitleBase != TitleBase
            || newDescriptionBase != DescriptionBase;
        TitleOverride = newTitle;
        DescriptionOverride = newDescription;
        TitleBase = newTitleBase;
        DescriptionBase = newDescriptionBase;
        CustomizedAt = IsCustomized ? now : null;
        if (changed)
        {
            UpdatedAt = now;
        }

        return changed;

        static string? Normalize(string? value, string jobText) => string.IsNullOrWhiteSpace(value) || value == jobText ? null : value;
    }

    /// <summary>
    /// Selects <paramref name="version"/> for the board. With <paramref name="issues"/> the posting cannot go out: a
    /// posting not yet on the board waits in Draft, one already on it keeps its older version until the issues are
    /// fixed. Otherwise it is Queued, including a posting that was taken down and is now posted again. A stale version,
    /// or the version already sent, changes nothing, unless <paramref name="resend"/> says the board's own content
    /// changed (a customization), which sends the same version again. Returns whether the posting must be sent to the board.
    /// </summary>
    public bool Queue(int version, string url, IReadOnlyList<string> issues, DateTimeOffset now, bool resend = false)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (version < JobVersion)
        {
            return false;
        }

        if (version == JobVersion && IsActive && !resend)
        {
            return false;
        }

        if (issues.Count > 0)
        {
            Issues = [.. issues];
            if (!IsActive)
            {
                JobVersion = version;
                Url = url;
                Change(PostingState.Draft, now);
            }

            UpdatedAt = now;
            return false;
        }

        JobVersion = version;
        Url = url;
        Issues = [];
        LastError = null;
        ExpectedBy = null;
        NextCheckAt = null;
        if (State is PostingState.Closing or PostingState.Closed)
        {
            // Back on the board: the old close no longer describes it. A push board's handler sends it as an update, so
            // the board can withdraw a removal it has not finished.
            ClosingAt = null;
            ClosedAt = null;
        }

        Sequence++;
        Change(PostingState.Queued, now);
        return true;
    }

    /// <summary>
    /// The board took <paramref name="version"/> but has not confirmed it is showing. <paramref name="nextCheckAt"/> is
    /// when to ask it again; null for a board that cannot be asked.
    /// </summary>
    public bool MarkSubmitted(int version, string? externalId, DateTimeOffset? expectedBy, DateTimeOffset? nextCheckAt, DateTimeOffset now)
    {
        if (version != JobVersion || State is not (PostingState.Queued or PostingState.Submitted or PostingState.Live or PostingState.Error))
        {
            return false;
        }

        ExternalId = externalId ?? ExternalId;
        ExpectedBy = expectedBy;
        NextCheckAt = nextCheckAt;
        SubmittedAt = now;
        LastError = null;
        if (State == PostingState.Submitted)
        {
            UpdatedAt = now;
            return true;
        }

        Change(PostingState.Submitted, now);
        return true;
    }

    /// <summary>The board, or our own server for a hosted board, confirmed <paramref name="version"/> is showing.</summary>
    public bool MarkLive(int version, string? externalId, DateTimeOffset now)
    {
        if (version != JobVersion || State is not (PostingState.Queued or PostingState.Submitted or PostingState.Error))
        {
            return false;
        }

        ExternalId = externalId ?? ExternalId;
        ExpectedBy = null;
        NextCheckAt = null;
        LiveAt = now;
        LastError = null;
        Change(PostingState.Live, now);
        return true;
    }

    /// <summary>The board refused <paramref name="version"/>; <paramref name="reason"/> says why, in the board's words.</summary>
    public bool Reject(int version, string reason, DateTimeOffset now)
    {
        if (version != JobVersion || State is not (PostingState.Queued or PostingState.Submitted or PostingState.Error))
        {
            return false;
        }

        LastError = reason;
        ExpectedBy = null;
        NextCheckAt = null;
        Change(PostingState.Rejected, now);
        return true;
    }

    /// <summary>
    /// The board could not be reached after every retry. Reconciliation does not resend from here; publishing the job again
    /// queues it once more.
    /// </summary>
    public bool Fail(string error, DateTimeOffset now)
    {
        if (State is not (PostingState.Queued or PostingState.Error))
        {
            return false;
        }

        LastError = error;
        NextCheckAt = null;
        Change(PostingState.Error, now);
        return true;
    }

    /// <summary>
    /// Starts taking the posting down. A posting that never reached the board (Draft, Rejected) is Closed at once; a
    /// posting the board may show goes to Closing, with <paramref name="expectedBy"/> as the board's removal estimate.
    /// Returns what the board must be told: <see cref="CloseOutcome.SendClose"/> when it has to be asked to remove it.
    /// </summary>
    public CloseOutcome RequestClose(DateTimeOffset? expectedBy, DateTimeOffset? nextCheckAt, DateTimeOffset now)
    {
        switch (State)
        {
            case PostingState.Closing or PostingState.Closed:
                return CloseOutcome.Unchanged;
            case PostingState.Draft or PostingState.Rejected:
                ClosingAt = now;
                ClosedAt = now;
                ExpectedBy = null;
                NextCheckAt = null;
                Change(PostingState.Closed, now);
                return CloseOutcome.ClosedNow;
            default:
                // Queued and Error included: a send may be in flight, so the board is asked either way.
                ClosingAt = now;
                ExpectedBy = expectedBy;
                NextCheckAt = nextCheckAt;
                Sequence++;
                Change(PostingState.Closing, now);
                return CloseOutcome.SendClose;
        }
    }

    /// <summary>A Closing posting's board accepted the removal request but does not confirm removals; record its estimate.</summary>
    public bool AwaitRemoval(DateTimeOffset? expectedBy, DateTimeOffset? nextCheckAt, DateTimeOffset now)
    {
        if (State != PostingState.Closing)
        {
            return false;
        }

        ExpectedBy = expectedBy;
        NextCheckAt = nextCheckAt;
        LastError = null;
        UpdatedAt = now;
        return true;
    }

    /// <summary>The board, or our own server, confirmed the posting is gone.</summary>
    public bool MarkClosed(DateTimeOffset now)
    {
        if (State != PostingState.Closing)
        {
            return false;
        }

        ClosedAt = now;
        ExpectedBy = null;
        NextCheckAt = null;
        LastError = null;
        Change(PostingState.Closed, now);
        return true;
    }

    /// <summary>The board has not answered yet: ask again at <paramref name="nextCheckAt"/>.</summary>
    public void CheckAgainAt(DateTimeOffset nextCheckAt, DateTimeOffset now)
    {
        NextCheckAt = nextCheckAt;
        Attempts++;
        UpdatedAt = now;
    }

    /// <summary>
    /// Reconciliation found a Queued or Closing posting whose request never got through: send it again, counting the
    /// attempt. A Queued posting that used up <paramref name="maxAttempts"/> goes to Error instead, and this returns
    /// false.
    /// </summary>
    public bool Resend(int maxAttempts, DateTimeOffset now)
    {
        if (State is not (PostingState.Queued or PostingState.Closing))
        {
            return false;
        }

        if (Attempts >= maxAttempts && State == PostingState.Queued)
        {
            // Publishing again queues it once more; a close keeps being retried, because a board that may still show
            // the job must not be given up on.
            Fail("The board did not answer after repeated attempts.", now);
            return false;
        }

        Attempts++;
        Sequence++;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Records an error reaching the board without changing state; reconciliation retries it.</summary>
    public void RecordError(string error, DateTimeOffset now)
    {
        LastError = error;
        Attempts++;
        UpdatedAt = now;
    }

    private void Change(PostingState state, DateTimeOffset now)
    {
        State = state;
        Attempts = 0;
        UpdatedAt = now;
    }
}

public enum PostingState
{
    Draft,
    Queued,
    Submitted,
    Live,
    Rejected,
    Closing,
    Closed,
    Error,
}

/// <summary>What <see cref="JobPosting.RequestClose"/> did, and so what the board must be told.</summary>
public enum CloseOutcome
{
    /// <summary>Already closing or closed.</summary>
    Unchanged,

    /// <summary>It never reached the board, so it is Closed with nothing to send.</summary>
    ClosedNow,

    /// <summary>It is Closing; the board must be asked to remove it.</summary>
    SendClose,
}
