using Microsoft.EntityFrameworkCore;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Infrastructure.Integrity.Llm;

/// <summary>
/// The consistency check's monthly token budget, counted per calendar month in UTC. One definition for the handler that
/// enforces it and the endpoint that reports it, so the two never disagree.
/// </summary>
public static class ConsistencyBudget
{
    /// <summary>The start of the UTC calendar month <paramref name="now"/> falls in.</summary>
    public static DateTimeOffset MonthStart(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>Tokens the organization's checks started this month have used. A check still running counts nothing yet.</summary>
    public static Task<long> TokensUsedThisMonthAsync(this ScreenHireDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var monthStart = MonthStart(now);
        return db.ConsistencyChecks.AsNoTracking()
            .Where(c => c.StartedAt >= monthStart)
            .SumAsync(c => c.InputTokens + c.OutputTokens, cancellationToken);
    }
}
