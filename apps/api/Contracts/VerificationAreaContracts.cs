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
/// as on a job's Authenticity panel. <c>groups</c> lists all six groups in their
/// order, each with the applications in which it counted a signal (0 when none) and whether the organization runs it.
/// <c>lastCheckedAt</c> is when the newest of the month's checks finished, so the page can say how recent its numbers are;
/// null while nothing this month has been checked.
/// </remarks>
public sealed record VerificationOverviewView(
    DateTimeOffset From,
    DateTimeOffset To,
    int Checked,
    int Pending,
    VerificationLevelCounts Levels,
    IReadOnlyList<VerificationGroupCount> Groups,
    DateTimeOffset? LastCheckedAt);

/// <summary>Applications at each authenticity level, by the word the dashboard shows: Clear, Review, Verify.</summary>
public sealed record VerificationLevelCounts(int Clear, int Review, int Verify);

/// <summary>
/// The number of applications in which a check group counted a signal towards the level, and whether the organization runs the
/// group (<c>on</c>). A group that is off still has the count of the month's applications it found something in before it went off.
/// </summary>
public sealed record VerificationGroupCount(CheckGroup Group, string Label, int Applications, bool On);

/// <summary>
/// A page of the activity log, newest first, one row per run: a pass of the checks over one application, or a model call that
/// belongs to no pass (a connection test). <c>usageVisible</c> says the caller may see what a call cost and the provider's
/// request id (View AI usage and All jobs); without it a model call has no <c>call</c> details, and only calls about an
/// application the caller can open are listed. <c>nextBefore</c> goes back as <c>before</c> for the next page and is null on the last.
/// </summary>
public sealed record VerificationActivityPage(IReadOnlyList<VerificationActivityRun> Runs, string? NextBefore, bool UsageVisible);

/// <summary>
/// One run of the checks over one application, or one model call on its own. <c>checks</c>, <c>found</c> and <c>notRun</c> count
/// its check runs; its <c>outcome</c> is Found when one found something, NotRun when none looked, and Completed ("Nothing
/// found") otherwise. <c>rulesetVersion</c> is the ruleset of the application's newest assessment at the run, null for a call on
/// its own. <c>jobTitle</c> and <c>applicationId</c> let a reader open the application; neither names the candidate. Nothing here
/// holds a threshold, a weight or a rule.
/// </summary>
public sealed record VerificationActivityRun(
    string Id,
    DateTimeOffset At,
    Guid? ApplicationId,
    string? JobTitle,
    VerificationActivityOutcome Outcome,
    int Checks,
    int Found,
    int NotRun,
    string? RulesetVersion,
    IReadOnlyList<VerificationActivityCheck> Items);

/// <summary>
/// One check run or model call inside a run. <c>check</c> is the analyzer's name (<c>network</c>) or the model check's
/// (<c>consistency.extraction</c>). <c>group</c> is null for a call that belongs to none. <c>reason</c> is, in a sentence, why a
/// check did not look or what the organization's trusted lists kept it from counting. <c>findings</c> are the titles of what it
/// found, in the candidate's Verification tab's words.
/// </summary>
public sealed record VerificationActivityCheck(
    Guid Id,
    VerificationActivityKind Kind,
    DateTimeOffset At,
    CheckGroup? Group,
    string Check,
    VerificationActivityOutcome Outcome,
    string? Reason,
    IReadOnlyList<string> Findings,
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
