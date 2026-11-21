namespace ScreenHire.Domain.Verification;

/// <summary>What a call cost, from what is known about it (ai-usage.md, "Cost").</summary>
public static class AiUsageCost
{
    private const decimal PerMillion = 1_000_000m;

    /// <summary>
    /// The provider's own figure when it sent one; else tokens times the price per million tokens; else unknown. Prices
    /// come from configuration or the organization's own setting, never from code. A call with no reported tokens, or with
    /// a missing price for a side that used tokens, has an unknown cost rather than a made-up one.
    /// </summary>
    public static (decimal? Cost, AiCostBasis Basis) Compute(
        decimal? reported, long? inputTokens, long? outputTokens, decimal? inputPerMillion, decimal? outputPerMillion)
    {
        if (reported is { } said and >= 0)
        {
            return (said, AiCostBasis.Reported);
        }

        if (inputTokens is null && outputTokens is null)
        {
            return (null, AiCostBasis.Unknown);
        }

        if ((inputTokens > 0 && inputPerMillion is null) || (outputTokens > 0 && outputPerMillion is null)
            || (inputPerMillion is null && outputPerMillion is null))
        {
            return (null, AiCostBasis.Unknown);
        }

        var cost = ((inputTokens ?? 0) * (inputPerMillion ?? 0m) / PerMillion)
            + ((outputTokens ?? 0) * (outputPerMillion ?? 0m) / PerMillion);
        return (cost, AiCostBasis.Priced);
    }
}

/// <summary>
/// The usual split of a check's tokens between input and output, which turns a token count into a dollar figure when the
/// deployment's default budget and cap are shown in dollars (ai-usage.md, "Defaults"). The consistency check reads long sources
/// and answers briefly, so most of its tokens are input. An estimate for display: enforcement stays in tokens.
/// </summary>
public static class AiUsageMix
{
    /// <summary>The share of a check's tokens that is input; the rest is output.</summary>
    public const decimal InputShare = 0.8m;

    private const decimal PerMillion = 1_000_000m;

    /// <summary>One price per million tokens for the usual mix, from a model's input and output prices.</summary>
    public static decimal BlendedPerMillion(decimal inputPerMillion, decimal outputPerMillion)
        => (inputPerMillion * InputShare) + (outputPerMillion * (1m - InputShare));

    /// <summary>What <paramref name="tokens"/> cost at <paramref name="blendedPerMillion"/> dollars per million tokens.</summary>
    public static decimal Dollars(decimal tokens, decimal blendedPerMillion) => tokens * blendedPerMillion / PerMillion;
}

/// <summary>The monthly budget, alert and cap rules, as pure functions of what was used (ai-usage.md, "Budget, alerts and the cap").</summary>
public static class AiUsageLimits
{
    /// <summary>The shares of the budget that raise an alert, in percent.</summary>
    public static readonly IReadOnlyList<int> BudgetThresholds = [50, 80, 100];

    /// <summary>The thresholds <paramref name="used"/> has reached of <paramref name="budget"/>, lowest first. None when there is no budget.</summary>
    public static IReadOnlyList<int> ThresholdsReached(decimal used, decimal? budget)
    {
        if (budget is not > 0)
        {
            return [];
        }

        return [.. BudgetThresholds.Where(percent => used * 100m >= budget.Value * percent)];
    }

    /// <summary>Has <paramref name="used"/> reached the cap? No cap never pauses anything. At the cap is reached: the cap is a limit, not a last allowed amount.</summary>
    public static bool CapReached(decimal used, decimal? cap) => cap is { } limit && used >= limit;

    /// <summary>
    /// The cap in force. An organization's own cap wins. With none, ScreenHire's key falls back to the deployment's default
    /// (in tokens, so only when the organization counts in tokens or has set no unit of its own); an own key has no cap.
    /// </summary>
    public static (decimal? Cap, UsageUnit Unit) EffectiveCap(VerificationSettings? settings, long? deploymentTokenCap)
    {
        if (settings is { MonthlyCap: { } own })
        {
            return (own, settings.BudgetUnit);
        }

        if (settings is { UsesOwnKey: true } || deploymentTokenCap is not > 0)
        {
            return (null, settings?.BudgetUnit ?? UsageUnit.Tokens);
        }

        return (deploymentTokenCap, UsageUnit.Tokens);
    }

    /// <summary>The budget in force: the organization's own, else ScreenHire's key falls back to the deployment's default tokens. An own key has none.</summary>
    public static (decimal? Budget, UsageUnit Unit) EffectiveBudget(VerificationSettings? settings, long? deploymentTokenBudget)
    {
        if (settings is { MonthlyBudget: { } own })
        {
            return (own, settings.BudgetUnit);
        }

        if (settings is { UsesOwnKey: true } || deploymentTokenBudget is not > 0)
        {
            return (null, settings?.BudgetUnit ?? UsageUnit.Tokens);
        }

        return (deploymentTokenBudget, UsageUnit.Tokens);
    }
}

/// <summary>
/// The spike rule: a day well above the recent daily average. Judged on the previous completed UTC day, in tokens
/// (ai-usage.md, "The spike rule").
/// </summary>
public static class AiUsageSpike
{
    /// <summary>
    /// Is <paramref name="day"/> a spike? Yes when it used at least <paramref name="minimumTokens"/>, at least
    /// <paramref name="factor"/> times the average of <paramref name="priorDays"/> (one entry per day of the look-back
    /// window, zero for a quiet day), and at least <paramref name="minimumActiveDays"/> of those days had any use. A new
    /// organization with no history never spikes.
    /// </summary>
    public static bool IsSpike(long day, IReadOnlyList<long> priorDays, decimal factor, long minimumTokens, int minimumActiveDays)
    {
        ArgumentNullException.ThrowIfNull(priorDays);
        if (priorDays.Count == 0 || day < minimumTokens || priorDays.Count(d => d > 0) < minimumActiveDays)
        {
            return false;
        }

        var average = (decimal)priorDays.Sum() / priorDays.Count;
        return day >= factor * average;
    }
}
