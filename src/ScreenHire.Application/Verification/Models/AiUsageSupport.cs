using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Verification;
using ScreenHire.Infrastructure.Verification.Llm;

namespace ScreenHire.Application.Verification;

/// <summary>Builds what the usage ledger records about a check's calls, from the organization's settings (docs/architecture/ai-usage.md).</summary>
public static class AiUsageContexts
{
    /// <summary>
    /// The context for calls made under <paramref name="settings"/>. The key source, provider and prices come from the
    /// settings, never from the client that answered: under the fake model an organization with its own key still records
    /// own-key calls, priced by its own setting. ScreenHire's key is priced from the deployment's configured prices.
    /// </summary>
    public static AiUsageContext For(VerificationSettings? settings, LlmOptions llm, Guid organizationId, Guid? applicationId)
    {
        ArgumentNullException.ThrowIfNull(llm);
        if (settings is { UsesOwnKey: true })
        {
            var own = (settings.OwnKeyInputPrice, settings.OwnKeyOutputPrice);
            return new AiUsageContext(
                organizationId, applicationId, settings.LlmProvider?.ToString() ?? "Unknown", AiKeySource.OwnKey, _ => own);
        }

        return new AiUsageContext(
            organizationId, applicationId, llm.Provider.ToString(), AiKeySource.BuiltIn,
            model => llm.Prices.TryGetValue(model, out var price) ? (price.Input, price.Output) : (null, null));
    }
}

/// <summary>
/// Which AI usage alerts are due now: the budget shares passed and the cap reached this month that no alert has gone out for
/// yet. The messages are cheap to send twice (the handler claims the key), so a check that finishes late sends what is missing.
/// </summary>
public static class AiUsageAlertChecks
{
    public static async Task<List<NotifyAiUsage>> DueAsync(
        ScreenHireDbContext db, Guid organizationId, VerificationSettings? settings, LlmOptions llm, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(llm);
        var (budget, budgetUnit) = AiUsageLimits.EffectiveBudget(settings, llm.DefaultMonthlyTokenBudget);
        var (cap, capUnit) = AiUsageLimits.EffectiveCap(settings, llm.DefaultMonthlyTokenCap);
        var due = new List<NotifyAiUsage>();
        if (budget is null && cap is null)
        {
            return due;
        }

        var month = AiUsageQueries.Month(now);
        var raised = (await db.AiUsageAlerts.AsNoTracking().Where(a => a.Key.StartsWith(month)).Select(a => a.Key).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        decimal? usedForBudget = null;
        if (budget is not null)
        {
            usedForBudget = await db.UsedThisMonthAsync(budgetUnit, now, cancellationToken);
            foreach (var percent in AiUsageLimits.ThresholdsReached(usedForBudget.Value, budget))
            {
                var key = AiUsageAlertKeys.Budget(month, percent);
                if (!raised.Contains(key))
                {
                    due.Add(new NotifyAiUsage(organizationId, AiUsageAlertKind.Budget, key, percent));
                }
            }
        }

        if (cap is not null && !raised.Contains(AiUsageAlertKeys.Cap(month)))
        {
            var used = capUnit == budgetUnit && usedForBudget is { } same ? same : await db.UsedThisMonthAsync(capUnit, now, cancellationToken);
            if (AiUsageLimits.CapReached(used, cap))
            {
                due.Add(new NotifyAiUsage(organizationId, AiUsageAlertKind.Cap, AiUsageAlertKeys.Cap(month)));
            }
        }

        return due;
    }
}
