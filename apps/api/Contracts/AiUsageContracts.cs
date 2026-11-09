using ScreenHire.Domain.Verification;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// One monthly limit in force. <c>amount</c> is in <c>unit</c>; <c>isDefault</c> says it is the deployment's default for
/// ScreenHire's key, not one the organization set; <c>used</c> is this month's use in the same unit; <c>percent</c> is how far
/// that is, whole percent, and can pass 100 (the cap is checked before each check, so a check in flight can pass it).
/// </summary>
public sealed record AiUsageLimitView(decimal Amount, UsageUnit Unit, bool IsDefault, decimal Used, int Percent);

/// <summary>
/// The organization's monthly AI budget (alerts at 50%, 80% and 100%, stops nothing) and cap (pauses the AI-based checks), in
/// one <c>unit</c>. <c>budget</c> and <c>cap</c> are what the organization set; <c>effectiveBudget</c> and <c>effectiveCap</c> are
/// what applies, which for ScreenHire's key falls back to the deployment's defaults, in tokens. An own key has no default.
/// </summary>
public sealed record AiUsageLimitsView(
    UsageUnit Unit, decimal? Budget, decimal? Cap, AiUsageLimitView? EffectiveBudget, AiUsageLimitView? EffectiveCap);

/// <summary>
/// Sets the monthly budget and cap. Both are in <c>unit</c> (whole tokens, or dollars to the cent); null removes one. With
/// <c>Money</c> and an own key, the own key's prices must be set, since a call with no known cost counts as nothing.
/// </summary>
public sealed record UpdateAiUsageLimitsRequest(UsageUnit Unit, decimal? Budget = null, decimal? Cap = null);

/// <summary>
/// This organization's AI use between <c>from</c> and <c>to</c> (default: this month, UTC). Totals count calls; <c>callsWithUnknownCost</c>
/// are calls nothing could price, which count as no cost. Days are the requester's days (<c>timeZone</c>). <c>limits</c> is
/// the month's position against the budget and cap, whatever range was asked for.
/// </summary>
public sealed record AiUsageSummary(
    DateTimeOffset From,
    DateTimeOffset To,
    AiUsageTotals Totals,
    IReadOnlyList<AiUsageDay> ByDay,
    IReadOnlyList<AiUsageGroup> ByModel,
    IReadOnlyList<AiUsageGroup> ByKeySource,
    AiUsageLimitsView Limits);

/// <summary>Calls, tokens and cost over a range. <c>cost</c> is in US dollars and counts only calls with a known cost.</summary>
public sealed record AiUsageTotals(int Calls, long InputTokens, long OutputTokens, decimal Cost, int CallsWithUnknownCost, int FailedCalls);

/// <summary>One day of use, by the date in the requested time zone (<c>yyyy-MM-dd</c>).</summary>
public sealed record AiUsageDay(string Date, int Calls, long InputTokens, long OutputTokens, decimal Cost);

/// <summary>Use by one provider, model and key source.</summary>
public sealed record AiUsageGroup(
    string Provider, string Model, AiKeySource KeySource, int Calls, long InputTokens, long OutputTokens, decimal Cost, int CallsWithUnknownCost);

/// <summary>
/// One model call, as it happened. <c>providerRequestId</c> is the provider's id for the response, which finds the call in the
/// provider's own logs. Tokens are null when the provider reported none. <c>cost</c> is null when <c>costBasis</c> is
/// <c>Unknown</c>. <c>check</c> names the check (for example <c>consistency.extraction</c>). Holds nothing that was sent or received.
/// </summary>
public sealed record AiUsageCallView(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Check,
    Guid? ApplicationId,
    string Provider,
    string Model,
    AiKeySource KeySource,
    string? ProviderRequestId,
    long? InputTokens,
    long? OutputTokens,
    decimal? Cost,
    AiCostBasis CostBasis,
    int DurationMs,
    AiCallOutcome Outcome,
    string? ErrorKind);

/// <summary>A page of calls, newest first. <c>nextBefore</c> is the id to pass as <c>before</c> for the next page, null on the last.</summary>
public sealed record AiUsageCallPage(IReadOnlyList<AiUsageCallView> Calls, Guid? NextBefore);
