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
