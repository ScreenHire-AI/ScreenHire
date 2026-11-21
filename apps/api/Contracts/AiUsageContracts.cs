using ScreenHire.Domain.Verification;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// One monthly limit in force. <c>amount</c> is in <c>unit</c>: billed dollars (<c>Money</c>, the amount a customer sees for AI use),
/// or <c>Tokens</c> only for a limit stored in tokens whose key has no price to convert it at. <c>isDefault</c> says it is the
/// deployment's default for ScreenHire's key, not one the organization set; <c>used</c> is this month's use in the same unit;
/// <c>percent</c> is how far that is, whole percent, and can pass 100 (the cap is checked before each check, so a check in flight can pass it).
/// </summary>
public sealed record AiUsageLimitView(decimal Amount, UsageUnit Unit, bool IsDefault, decimal Used, int Percent);

/// <summary>
/// The organization's monthly AI budget (alerts at 50%, 80% and 100%, stops nothing) and cap (pauses the AI-based checks), counted
/// in billed dollars. <c>unit</c>, <c>budget</c> and <c>cap</c> are what the organization stored (a limit set before limits were in
/// dollars is in tokens); <c>effectiveBudget</c> and <c>effectiveCap</c> are what applies, converted to dollars at the key's price,
/// which for ScreenHire's key falls back to the deployment's defaults. An own key has no default. <c>priced</c>: the key in use has a
/// price, so a limit can be set in dollars (an own key needs its two prices).
/// </summary>
public sealed record AiUsageLimitsView(
    UsageUnit Unit, decimal? Budget, decimal? Cap, AiUsageLimitView? EffectiveBudget, AiUsageLimitView? EffectiveCap, bool Priced);

/// <summary>
/// Sets the monthly budget and cap, in US dollars to the cent, counted against the amount billed for AI use; null removes one.
/// Setting one needs a price for the key in use (an own key's two prices), since a call with no known cost counts as nothing.
/// </summary>
public sealed record UpdateAiUsageLimitsRequest(decimal? Budget = null, decimal? Cap = null);

/// <summary>
/// This organization's AI use between <c>from</c> and <c>to</c> (default: this month, UTC). Totals count calls; <c>callsWithUnknownCost</c>
/// are calls nothing could price, which count as no cost. Days are the requester's days (<c>timeZone</c>). <c>limits</c> is
/// the month's position against the budget and cap, whatever range was asked for. <c>lastCallAt</c> is when the newest model call
/// happened, whatever the range, so the page can say how recent its numbers are (design system T7); null before the first.
/// </summary>
public sealed record AiUsageSummary(
    DateTimeOffset From,
    DateTimeOffset To,
    AiUsageTotals Totals,
    IReadOnlyList<AiUsageDay> ByDay,
    IReadOnlyList<AiUsageGroup> ByModel,
    IReadOnlyList<AiUsageGroup> ByKeySource,
    AiUsageLimitsView Limits,
    DateTimeOffset? LastCallAt);

/// <summary>
/// Calls, tokens and cost over a range. <c>cost</c> is in US dollars and counts only calls with a known cost. Each call counts at the
/// one amount a customer sees for it: on ScreenHire's key what it is billed, on an own key the provider's price, an estimate. So
/// <c>cost</c> here sums both keys; <c>byKeySource</c> keeps them apart, which is how the AI use tab shows them.
/// </summary>
public sealed record AiUsageTotals(int Calls, long InputTokens, long OutputTokens, decimal Cost, int CallsWithUnknownCost, int FailedCalls);

/// <summary>
/// One day of use, by the date in the requested time zone (<c>yyyy-MM-dd</c>). The built-in and own-key fields split the day's
/// tokens (in and out) and cost by whose key paid, so the AI use tab can chart the two side by side.
/// </summary>
public sealed record AiUsageDay(
    string Date, int Calls, long InputTokens, long OutputTokens, decimal Cost,
    long BuiltInTokens, long OwnKeyTokens, decimal BuiltInCost, decimal OwnKeyCost);

/// <summary>Use by one provider, model and key source.</summary>
public sealed record AiUsageGroup(
    string Provider, string Model, AiKeySource KeySource, int Calls, long InputTokens, long OutputTokens, decimal Cost, int CallsWithUnknownCost);

/// <summary>
/// One model call, as it happened. <c>providerRequestId</c> is the provider's id for the response, which finds the call in the
/// provider's own logs. Tokens are null when the provider reported none. <c>cost</c> is null when <c>costBasis</c> is
/// <c>Unknown</c>, and is the billed amount on ScreenHire's key and the provider's price on an own key. <c>check</c> names the check (for example <c>consistency.extraction</c>). Holds nothing that was sent or received.
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
