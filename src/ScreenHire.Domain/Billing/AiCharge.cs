using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Billing;

/// <summary>
/// What an organization pays for one call on ScreenHire's AI key: the provider's price plus the margin (docs/architecture/billing.md,
/// "AI usage"). The unit is the micro-dollar, a whole number, because Stripe's meter takes positive integers and a call often costs
/// a fraction of a cent. The same function prices a call on the Billing page and in the meter event, so the page and the invoice agree.
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
}

/// <summary>
/// A ledger call that billing has settled (docs/architecture/billing.md, "AI usage"): either reported to the provider's meter or
/// waived. The row's id is the ledger call's id, so a call is settled once (the primary key) and the id is also the meter event's
/// identifier at Stripe. Insert-only: the application role cannot update a row.
/// </summary>
public sealed class AiUsageCharge : ITenantOwned
{
    /// <summary>The id of the <c>AiUsageCall</c> this settles.</summary>
    public required Guid Id { get; init; }

    public Guid OrganizationId { get; init; }

    public AiChargeState State { get; init; }

    /// <summary>The charge in micro-dollars: what was reported, or what was waived (zero for a call that cost nothing).</summary>
    public long Micros { get; init; }

    /// <summary>The margin the charge was priced with, kept so a later change to the setting leaves it explainable.</summary>
    public decimal Margin { get; init; }

    public AiChargeProvider Provider { get; init; }

    public DateTimeOffset SettledAt { get; init; }
}

public enum AiChargeState
{
    /// <summary>Sent to the provider's meter, which bills it on the subscription.</summary>
    Reported,

    /// <summary>Not billed: a call that cost nothing, or one covered by the trial's allowance.</summary>
    Waived,
}

public enum AiChargeProvider
{
    Stripe,

    /// <summary>The local fake: it charges nobody, and the row is what shows the call was reported.</summary>
    Sandbox,
}

/// <summary>A built-in call waiting to be settled: its ledger id, when it started, and its charge in micro-dollars.</summary>
public readonly record struct PendingAiCall(Guid Id, DateTimeOffset OccurredAt, long Micros);

/// <summary>What billing does with one pending call.</summary>
public readonly record struct AiChargeDecision(Guid Id, long Micros, AiChargeState State);

/// <summary>
/// Decides which pending calls the trial's allowance covers (pure). A call that started before the trial ended is a trial call.
/// Trial calls are waived in time order while the waived total stays within the allowance; the first one that would pass it, and
/// every call after it, is reported in full. A call that cost nothing is waived, because the meter takes positive values only.
/// </summary>
public static class AiChargePlanner
{
    public static IReadOnlyList<AiChargeDecision> Plan(
        IEnumerable<PendingAiCall> pending, DateTimeOffset trialEndsAt, long allowanceMicros, long alreadyWaivedMicros)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var waived = Math.Max(0, alreadyWaivedMicros);
        var allowanceOpen = true;
        var decisions = new List<AiChargeDecision>();
        foreach (var call in pending.OrderBy(c => c.OccurredAt).ThenBy(c => c.Id))
        {
            if (call.Micros <= 0)
            {
                decisions.Add(new AiChargeDecision(call.Id, 0, AiChargeState.Waived));
                continue;
            }

            if (allowanceOpen && call.OccurredAt < trialEndsAt)
            {
                if (waived + call.Micros <= allowanceMicros)
                {
                    waived += call.Micros;
                    decisions.Add(new AiChargeDecision(call.Id, call.Micros, AiChargeState.Waived));
                    continue;
                }

                allowanceOpen = false;
            }

            decisions.Add(new AiChargeDecision(call.Id, call.Micros, AiChargeState.Reported));
        }

        return decisions;
    }
}
