using ScreenHire.Domain.Verification;

namespace ScreenHire.Domain.Billing;

/// <summary>
/// What an organization pays for one call on ScreenHire's AI key: the provider's price plus the margin (docs/architecture/billing.md,
/// "AI credits"). The unit is the micro-dollar, a whole number, because a call often costs a fraction of a cent. The same function
/// prices a call's debit from the credit balance and every amount shown for it, so the balance and the pages agree.
/// </summary>
public static class AiCharge
{
    /// <summary>Micro-dollars in a dollar.</summary>
    public const long MicrosPerDollar = 1_000_000;

    /// <summary>
    /// The charge for a call whose provider cost was <paramref name="cost"/> US dollars, in micro-dollars: the cost times
    /// (1 + <paramref name="margin"/>), rounded half away from zero. Zero for a call that cost nothing or has no known cost.
    /// </summary>
    public static long Micros(decimal? cost, decimal margin)
    {
        if (cost is not { } amount || amount <= 0m)
        {
            return 0;
        }

        return (long)Math.Round(amount * (1m + margin) * MicrosPerDollar, MidpointRounding.AwayFromZero);
    }

    /// <summary>A sum of micro-dollars as US dollars.</summary>
    public static decimal Dollars(long micros) => (decimal)micros / MicrosPerDollar;

    /// <summary>US dollars as micro-dollars, rounded half away from zero.</summary>
    public static long ToMicros(decimal dollars) => (long)Math.Round(dollars * MicrosPerDollar, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The one amount a customer sees for a call (docs/architecture/ai-usage.md, "Cost"): on ScreenHire's key what it is billed, the
    /// provider's price plus <paramref name="margin"/> rounded to a micro-dollar as the meter is sent it; on the organization's own key
    /// the provider's price, an estimate of what that provider charges, which ScreenHire never bills. Null when the cost is unknown.
    /// </summary>
    public static decimal? Billed(decimal? cost, AiKeySource key, decimal margin)
    {
        if (cost is not { } amount)
        {
            return null;
        }

        return key == AiKeySource.BuiltIn ? Dollars(Micros(amount, margin)) : amount;
    }
}

