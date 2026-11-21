namespace ScreenHire.Api.Contracts;

/// <summary>The kinds the workspace's Activity feed filters by (docs/architecture/notifications.md, "Activity").</summary>
public enum ActivityKind
{
    Moves,
    Applications,
    Messages,
    Interviews,
    Scorecards,
    Notes,
    Tasks,
    Jobs,
}

/// <summary>
/// Who made a change in the feed: a member, an organization API key, an automation, the candidate, or ScreenHire itself
/// (a calendar sync, an interview ended with the application).
/// </summary>
public enum ActivityActor
{
    Member,
    ApiKey,
    Automation,
    Candidate,
    ScreenHire,
}

/// <summary>Whose jobs the feed covers: the jobs whose hiring team the person is on, or every job they can see.</summary>
public enum ActivityScope
{
    MyJobs,
    Everything,
}

/// <summary>
/// One page of the workspace's activity, newest first. <c>nextBefore</c> is the cursor for the next page, or null after
/// the last. <c>seenAt</c> is when the person last opened the feed, null before the first time. <c>unseenCount</c> is how
/// many changes in this scope and these kinds came after that, other than the person's own, at most 100; null with no
/// <c>seenAt</c>.
/// </summary>
public sealed record ActivityPage(IReadOnlyList<ActivityEntry> Entries, string? NextBefore, DateTimeOffset? SeenAt, int? UnseenCount);

/// <summary>One change somewhere in the workspace that the person may see.</summary>
/// <remarks>
/// <c>by</c>: who made it. <c>actorUserId</c> is the member, by id, so the screen shows their current name;
/// <c>actorApiKeyName</c> the API key's name. <c>jobId</c>, <c>jobTitle</c>: the job the change is about, or the job the
/// application is for. <c>applicationId</c>, <c>candidateName</c>: the application, for a change to one.
/// <c>event</c>: the application's timeline entry, as the candidate's Activity tab has it, without a recommendation or a
/// note's text; a scorecard's id only when the reader may read it (ADR-0013). Verification levels are never in it.
/// <c>fromStageName</c>, <c>toStageName</c>, <c>stageName</c>: the stages its ids name. <c>followUpName</c>: the follow-up
/// form a questionnaire entry is about. <c>jobChange</c>: for a change to the job itself.
/// </remarks>
public sealed record ActivityEntry(
    Guid Id,
    ActivityKind Kind,
    DateTimeOffset OccurredAt,
    ActivityActor By,
    Guid? ActorUserId,
    string? ActorApiKeyName,
    Guid JobId,
    string JobTitle,
    Guid? ApplicationId,
    string? CandidateName,
    ApplicationTimelineEntry? Event,
    string? FromStageName,
    string? ToStageName,
    string? StageName,
    string? FollowUpName,
    JobActivity? JobChange);

/// <summary>
/// A save that changed a job: <c>created</c> when it made the job, else its first change in words (the job's History has
/// the rest) and how many more lines that save holds.
/// </summary>
public sealed record JobActivity(bool Created, HistoryLine? Line, int MoreLines);
