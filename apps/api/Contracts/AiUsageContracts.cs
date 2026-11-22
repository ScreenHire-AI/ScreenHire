using ScreenHire.Domain.Verification;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// This organization's AI use between <c>from</c> and <c>to</c> (default: this month, UTC). Totals count calls; <c>callsWithUnknownCost</c>
/// are calls nothing could price, which count as no cost. Days are the requester's days (<c>timeZone</c>). <c>lastCallAt</c> is when the newest model call
/// happened, whatever the range, so the page can say how recent its numbers are (design system T7); null before the first.
/// </summary>
public sealed record AiUsageSummary(
    DateTimeOffset From,
    DateTimeOffset To,
    AiUsageTotals Totals,
    IReadOnlyList<AiUsageDay> ByDay,
    IReadOnlyList<AiUsageGroup> ByModel,
    IReadOnlyList<AiUsageGroup> ByKeySource,
    DateTimeOffset? LastCallAt);

/// <summary>
/// Calls, tokens and cost over a range. <c>cost</c> is in US dollars and counts only calls with a known cost. Each call counts at the
/// one amount a customer sees for it: on ScreenHire's key what it is billed, on an own key the provider's price, an estimate. So
/// <c>cost</c> here sums both keys; <c>byKeySource</c> keeps them apart, which is how Billing's AI use section shows them.
/// </summary>
public sealed record AiUsageTotals(int Calls, long InputTokens, long OutputTokens, decimal Cost, int CallsWithUnknownCost, int FailedCalls);

/// <summary>
/// One day of use, by the date in the requested time zone (<c>yyyy-MM-dd</c>). The built-in and own-key fields split the day's
/// tokens (in and out) and cost by whose key paid, so Billing's AI use section can chart the two side by side.
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
