using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Infrastructure.Verification.Llm;

/// <summary>
/// What the ledger says an organization used, counted per calendar month in UTC. One definition for the handler that
/// enforces the cap, the one that alerts and the endpoint that reports it, so the three never disagree.
/// </summary>
public static class AiUsageQueries
{
    /// <summary>The start of the UTC calendar month <paramref name="now"/> falls in.</summary>
    public static DateTimeOffset MonthStart(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>The UTC calendar month <paramref name="now"/> falls in, as <c>yyyy-MM</c>: what the alert keys carry.</summary>
    public static string Month(DateTimeOffset now) => now.ToUniversalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>The start of the UTC day <paramref name="now"/> falls in.</summary>
    public static DateTimeOffset DayStart(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>The UTC day <paramref name="day"/> falls in, as <c>yyyy-MM-dd</c>.</summary>
    public static string Day(DateTimeOffset day) => day.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Tokens the organization's calls have used this month, from both keys. A call that is still running counts nothing yet.</summary>
    public static Task<long> TokensUsedThisMonthAsync(this ScreenHireDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var monthStart = MonthStart(now);
        return db.AiUsageCalls.AsNoTracking()
            .Where(c => c.OccurredAt >= monthStart)
            .SumAsync(c => (c.InputTokens ?? 0) + (c.OutputTokens ?? 0), cancellationToken);
    }

    /// <summary>
    /// What a call comes to in the one amount a customer sees (<see cref="AiCharge.Billed"/>), as an expression Postgres runs inside a
    /// sum: ScreenHire's key at its cost times (1 + <paramref name="margin"/>) rounded to a micro-dollar (Postgres rounds numeric half
    /// away from zero, as the meter's pricing does), an own key at its cost. An unknown cost counts nothing.
    /// </summary>
    public static Expression<Func<AiUsageCall, decimal>> BilledAmount(decimal margin)
    {
        var factor = 1m + margin;
        return c => c.KeySource == AiKeySource.BuiltIn
            ? Math.Round((c.Cost ?? 0m) * factor * AiCharge.MicrosPerDollar) / AiCharge.MicrosPerDollar
            : c.Cost ?? 0m;
    }

    /// <summary>
    /// What the organization's calls have used this month, in <paramref name="unit"/>: tokens, or billed dollars at
    /// <paramref name="margin"/> (<see cref="BilledAmount"/>). A call with an unknown cost counts nothing toward dollars; Billing's AI use section
    /// says how many there are.
    /// </summary>
    public static async Task<decimal> UsedThisMonthAsync(
        this ScreenHireDbContext db, UsageUnit unit, decimal margin, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (unit == UsageUnit.Tokens)
        {
            return await db.TokensUsedThisMonthAsync(now, cancellationToken);
        }

        var monthStart = MonthStart(now);
        return await db.AiUsageCalls.AsNoTracking()
            .Where(c => c.OccurredAt >= monthStart)
            .SumAsync(BilledAmount(margin), cancellationToken);
    }

    /// <summary>Tokens used on each UTC day in [<paramref name="from"/>, <paramref name="to"/>), by day start. Days with no use are absent.</summary>
    public static async Task<IReadOnlyDictionary<DateTimeOffset, long>> TokensByDayAsync(
        this ScreenHireDbContext db, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var rows = await db.AiUsageCalls.AsNoTracking()
            .Where(c => c.OccurredAt >= from && c.OccurredAt < to)
            .Select(c => new { c.OccurredAt, Tokens = (c.InputTokens ?? 0) + (c.OutputTokens ?? 0) })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => DayStart(r.OccurredAt)).ToDictionary(g => g.Key, g => g.Sum(r => r.Tokens));
    }
}
