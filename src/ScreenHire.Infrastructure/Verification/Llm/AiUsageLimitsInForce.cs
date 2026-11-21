using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Infrastructure.Verification.Llm;

/// <summary>
/// One monthly limit as it is enforced and shown. <c>Amount</c> and <c>Used</c> are in <c>Unit</c>: billed dollars, or tokens for a
/// limit stored in tokens whose key has no price to convert it at. <c>IsDefault</c>: the deployment's default for ScreenHire's key.
/// <c>Percent</c> is whole percent of the limit used, and can pass 100.
/// </summary>
public sealed record AiLimitInForce(decimal Amount, UsageUnit Unit, bool IsDefault, decimal Used, int Percent)
{
    /// <summary>The budget's alert shares this month's use has reached.</summary>
    public IReadOnlyList<int> ThresholdsReached => AiUsageLimits.ThresholdsReached(Used, Amount);

    /// <summary>Whether the cap is reached, which pauses the AI-based checks.</summary>
    public bool Reached => AiUsageLimits.CapReached(Used, Amount);
}

/// <summary>
/// The budget and cap in force this month, worked out one way for the API that shows them and the worker that enforces them
/// (docs/architecture/ai-usage.md, "Budget, alerts and the cap"), so the badge, the alerts and the pause never disagree. Both count
/// billed dollars: the amount a customer sees for AI use. A limit stored in tokens (set before limits were in dollars, or the
/// deployment's default) is converted once at the key's price and the usual mix, and counted against the dollars billed; where the key
/// has no price it stays in tokens and counts tokens.
/// </summary>
public static class AiUsageLimitsInForce
{
    public static async Task<(AiLimitInForce? Budget, AiLimitInForce? Cap)> ResolveAsync(
        ScreenHireDbContext db, VerificationSettings? settings, LlmOptions llm, decimal margin, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(llm);
        var (budget, budgetUnit) = AiUsageLimits.EffectiveBudget(settings, llm.DefaultMonthlyTokenBudget);
        var (cap, capUnit) = AiUsageLimits.EffectiveCap(settings, llm.DefaultMonthlyTokenCap);
        var perMillion = AiUsageMix.BilledPerMillion(settings, llm.ConfiguredPrice(), margin);
        var usedByUnit = new Dictionary<UsageUnit, decimal>();

        async Task<AiLimitInForce?> InForceAsync(decimal? amount, UsageUnit unit, bool isDefault)
        {
            if (amount is not { } limit)
            {
                return null;
            }

            if (unit == UsageUnit.Tokens && perMillion is { } price)
            {
                (limit, unit) = (AiUsageMix.LimitInDollars(limit, price), UsageUnit.Money);
            }

            if (!usedByUnit.TryGetValue(unit, out var used))
            {
                used = await db.UsedThisMonthAsync(unit, margin, now, cancellationToken);
                usedByUnit[unit] = used;
            }

            var percent = limit > 0 ? (int)Math.Min(1000m, Math.Floor(used * 100m / limit)) : 0;
            return new AiLimitInForce(limit, unit, isDefault, used, percent);
        }

        return (
            await InForceAsync(budget, budgetUnit, settings?.MonthlyBudget is null),
            await InForceAsync(cap, capUnit, settings?.MonthlyCap is null));
    }
}
