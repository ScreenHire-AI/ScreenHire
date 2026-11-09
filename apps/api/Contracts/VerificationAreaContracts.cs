using ScreenHire.Application.Verification;
using ScreenHire.Domain.Verification;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The Verification area's Overview: this month's applications (UTC calendar month) by authenticity level, and which check groups
/// counted a signal in the most of them. Counts of applications only. It holds no score, no percentage, no AI-assistance level
/// (which never counts) and nothing about a person. For a member limited to some jobs it covers those jobs.
/// </summary>
/// <remarks>
/// <c>checked</c> is the applications with an authenticity level (the parts of <c>levels</c> add up to it); <c>pending</c> is the
/// month's applications whose checks have not finished. A reviewer's dismissal does not move an application between levels here,
/// as on a job's Authenticity panel. <c>groups</c> lists only the groups that counted a signal, most applications first.
/// </remarks>
public sealed record VerificationOverviewView(
    DateTimeOffset From,
    DateTimeOffset To,
    int Checked,
    int Pending,
    VerificationLevelCounts Levels,
    IReadOnlyList<VerificationGroupCount> Groups);

/// <summary>Applications at each authenticity level, by the word the dashboard shows: Clear, Review, Verify.</summary>
public sealed record VerificationLevelCounts(int Clear, int Review, int Verify);

/// <summary>The number of applications in which a check group counted a signal towards the level.</summary>
public sealed record VerificationGroupCount(CheckGroup Group, string Label, int Applications);

/// <summary>
/// A page of the activity log, newest first: check runs for everyone, and model calls (the AI checks). <c>usageVisible</c> says
/// the caller may see what a call cost and the provider's request id (View AI usage and All jobs); without it a model call has
/// no <c>call</c> details, and only calls about an application the caller can open are listed. <c>nextBefore</c> goes back as
/// <c>before</c> for the next page and is null on the last.
/// </summary>
public sealed record VerificationActivityPage(IReadOnlyList<VerificationActivityEntry> Entries, string? NextBefore, bool UsageVisible);

/// <summary>
/// One check run or model call. <c>check</c> is the analyzer's name (<c>network</c>) or the model check's
/// (<c>consistency.extraction</c>). <c>group</c> is null for a call that belongs to none. <c>note</c> is why a check did not look,
/// when it did not. <c>jobTitle</c> and <c>applicationId</c> let a reader open the application; neither names the candidate.
/// </summary>
public sealed record VerificationActivityEntry(
    Guid Id,
    VerificationActivityKind Kind,
    DateTimeOffset At,
    CheckGroup? Group,
    string Check,
    VerificationActivityOutcome Outcome,
    string? Note,
    Guid? ApplicationId,
    string? JobTitle,
    VerificationActivityCall? Call);

/// <summary>What a model call cost and where to find it in the provider's own logs. Only for View AI usage with All jobs.</summary>
public sealed record VerificationActivityCall(
    AiKeySource KeySource,
    string Provider,
    string Model,
    string? ProviderRequestId,
    long? InputTokens,
    long? OutputTokens,
    decimal? Cost,
    int DurationMs,
    string? ErrorKind);
