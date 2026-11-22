using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Verification.Llm;

namespace ScreenHire.Application.Billing;

/// <summary>What the Billing page says about this month's use of ScreenHire's key (docs/architecture/billing.md, "AI credits").</summary>
public sealed record AiUsageBillingSummary(int BuiltInCalls, long ChargeMicros, int CallsWithUnknownCost, int OwnKeyCalls);

public static class AiUsageBillingQueries
{
    /// <summary>
    /// This UTC month's calls on the built-in key, priced as the credit debits price them: each call's cost times (1 + margin) rounded to
    /// a micro-dollar, then summed (Postgres rounds numeric half away from zero, as <c>AiCharge.Micros</c> does). A call with no known
    /// cost is counted, and draws nothing. Own-key calls are counted and never drawn from the balance.
    /// </summary>
    public static async Task<AiUsageBillingSummary> ThisMonthAsync(
        ScreenHireDbContext db, DateTimeOffset now, decimal margin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var monthStart = AiUsageQueries.MonthStart(now);
        var factor = 1m + margin;
        var builtIn = await db.AiUsageCalls.AsNoTracking()
            .Where(c => c.KeySource == AiKeySource.BuiltIn && c.OccurredAt >= monthStart)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Calls = g.Count(),
                // AiCharge.Micros per call, as the balance is drawn (AiUsageQueries.BilledAmount is the same rule in dollars).
                Micros = g.Sum(c => c.Cost == null ? 0m : Math.Round(c.Cost.Value * factor * 1000000m)),
                Unknown = g.Count(c => c.Cost == null),
            })
            .SingleOrDefaultAsync(cancellationToken);
        var ownKey = await db.AiUsageCalls.AsNoTracking().CountAsync(c => c.KeySource == AiKeySource.OwnKey && c.OccurredAt >= monthStart, cancellationToken);
        return new AiUsageBillingSummary(builtIn?.Calls ?? 0, (long)(builtIn?.Micros ?? 0m), builtIn?.Unknown ?? 0, ownKey);
    }
}

/// <summary>
/// The old 15-minute report of AI use to Stripe's meter, which prepaid credits replaced (ADR-0032). Its schedule is gone, but Wolverine keeps
/// a recurring schedule's next occurrence as a scheduled envelope, so a database that ran the report still holds one. This type and its
/// handler, under the same name, let that envelope be handled and do nothing rather than land among the dead letters. Remove both a
/// release after ADR-0032.
/// </summary>
public sealed record ReportAiUsage;

public static class ReportAiUsageHandler
{
    /// <summary>Nothing to do: AI use is no longer reported to a meter.</summary>
    public static void Handle(ReportAiUsage message) => ArgumentNullException.ThrowIfNull(message);
}
