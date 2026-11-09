using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Verification.Llm;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Billing;

/// <summary>
/// Reports AI use on ScreenHire's key to the payment provider's meter (every 15 minutes, docs/architecture/billing.md, "AI usage").
/// It has no tenant, so it fans out one message for each organization.
/// </summary>
public sealed record ReportAiUsage;

/// <summary>
/// Settles one organization's pending built-in calls: the 15-minute <paramref name="Slot"/> it was asked in, and, for the batch
/// after a full one, the id of the last call of the batch before (<paramref name="After"/>), which only keys the message.
/// </summary>
public sealed record ReportOrganizationAiUsage(Guid OrganizationId, string Slot, Guid? After = null) : ITenantMessage
{
    public string IdempotencyKey => After is { } after
        ? $"ai-report:{OrganizationId:N}:{Slot}:{after:N}"
        : $"ai-report:{OrganizationId:N}:{Slot}";
}

public static class ReportAiUsageHandler
{
    /// <summary>How often usage is reported, and so the length of a slot.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    // Like the other fan-outs: row-level security gives the worker no read across organizations, so it asks each one in turn.
    public static async Task<OutgoingMessages> Handle(ReportAiUsage message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var slot = (time.GetUtcNow().UtcTicks / Interval.Ticks).ToString(CultureInfo.InvariantCulture);
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new ReportOrganizationAiUsage(id, slot)));
        return outgoing;
    }
}

public static partial class ReportOrganizationAiUsageHandler
{
    /// <summary>
    /// How far back a call is still reported. Stripe takes events up to 35 days old, so a call older than 34 days is left alone
    /// rather than sent to be refused every run.
    /// </summary>
    public static readonly TimeSpan ReportWindow = TimeSpan.FromDays(34);

    [Deduplicated(ValueSource.InputMember, nameof(ReportOrganizationAiUsage.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        ReportOrganizationAiUsage message,
        ScreenHireDbContext db,
        BillingAccess billing,
        IOptions<BillingOptions> options,
        TimeProvider time,
        ILogger<ReportOrganizationAiUsage> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        if (!billing.IsConfigured)
        {
            return outgoing;
        }

        // Only a paying organization is reported: a trial has no provider customer, and an ended subscription has nothing to bill on.
        // The calls wait (up to the window) for a plan to start.
        var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (account is null || account.Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue))
        {
            return outgoing;
        }

        var ai = options.Value.Ai;
        var sandbox = billing.Mode == BillingMode.Sandbox;
        if (!sandbox && (string.IsNullOrWhiteSpace(ai.StripePriceId) || string.IsNullOrEmpty(account.ProviderCustomerId)))
        {
            LogNotMetered(logger, message.OrganizationId, string.IsNullOrWhiteSpace(ai.StripePriceId) ? "no metered Stripe price is set (ScreenHire:Billing:Ai:StripePriceId)" : "the account has no Stripe customer");
            return outgoing;
        }

        var now = time.GetUtcNow();
        var floor = now - ReportWindow;
        var pending = await db.AiUsageCalls.AsNoTracking()
            .Where(c => c.KeySource == AiKeySource.BuiltIn && c.Cost != null && c.OccurredAt >= floor && c.OccurredAt <= now
                && !db.AiUsageCharges.Any(x => x.Id == c.Id))
            .OrderBy(c => c.OccurredAt).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.OccurredAt, c.Cost })
            .Take(ai.ReportBatchSize)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return outgoing;
        }

        var alreadyWaived = await db.AiUsageCharges.AsNoTracking().Where(c => c.State == AiChargeState.Waived).SumAsync(c => c.Micros, cancellationToken);
        var decisions = AiChargePlanner.Plan(
            pending.Select(c => new PendingAiCall(c.Id, c.OccurredAt, AiCharge.Micros(c.Cost, ai.Margin))),
            account.TrialEndsAt, AiCharge.ToMicros(ai.TrialAllowance), alreadyWaived);

        var provider = sandbox ? AiChargeProvider.Sandbox : AiChargeProvider.Stripe;
        foreach (var waived in decisions.Where(d => d.State == AiChargeState.Waived))
        {
            db.AiUsageCharges.Add(Settle(message.OrganizationId, waived, ai.Margin, provider, now));
        }

        var toReport = decisions.Where(d => d.State == AiChargeState.Reported).ToList();
        AiUsageReportResult? result = null;
        if (toReport.Count > 0)
        {
            result = await billing.Provider.ReportAiUsageAsync(
                new AiUsageReport(message.OrganizationId, account.ProviderCustomerId, [.. toReport.Select(d => new AiUsageEvent(d.Id, d.Micros))], now),
                cancellationToken);
            var taken = result.Accepted.ToHashSet();
            foreach (var reported in toReport.Where(d => taken.Contains(d.Id)))
            {
                db.AiUsageCharges.Add(Settle(message.OrganizationId, reported, ai.Margin, provider, now));
            }
        }

        // Committed whatever the provider said: what it took is saved with this handler's save, so a failure part-way through the
        // batch does not make the retry send the events it took (Stripe remembers an identifier for 24 hours only). Throwing here
        // would roll that save back. The rest waits for the next slot, 15 minutes on.
        if (result?.Failure is { } failure)
        {
            LogReportStopped(logger, message.OrganizationId, result.Accepted.Count, toReport.Count, failure.Status, failure.Code);
        }
        else if (pending.Count == ai.ReportBatchSize)
        {
            // A full batch may have left more behind: the next one starts after it.
            outgoing.Add(new ReportOrganizationAiUsage(message.OrganizationId, message.Slot, pending[^1].Id));
        }

        return outgoing;
    }

    private static AiUsageCharge Settle(Guid organizationId, AiChargeDecision decision, decimal margin, AiChargeProvider provider, DateTimeOffset now) => new()
    {
        Id = decision.Id,
        OrganizationId = organizationId,
        State = decision.State,
        Micros = decision.Micros,
        Margin = margin,
        Provider = provider,
        SettledAt = now,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Organization {OrganizationId}: AI use is not reported, {Reason}")]
    private static partial void LogNotMetered(ILogger logger, Guid organizationId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Organization {OrganizationId}: the payment provider took {Accepted} of {Sent} AI usage events and stopped (status {Status}, code {Code}); the rest is retried in the next run")]
    private static partial void LogReportStopped(ILogger logger, Guid organizationId, int accepted, int sent, int? status, string? code);
}

/// <summary>What the Billing page says about this month's use of ScreenHire's key (docs/architecture/billing.md, "AI usage").</summary>
public sealed record AiUsageBillingSummary(
    int BuiltInCalls,
    long Tokens,
    decimal ProviderCost,
    long ChargeMicros,
    int CallsWithUnknownCost,
    int OwnKeyCalls,
    long TrialAllowanceUsedMicros);

public static class AiUsageBillingQueries
{
    /// <summary>
    /// This UTC month's calls on the built-in key, priced as billing prices them: each call's cost times (1 + margin) rounded to a
    /// micro-dollar, then summed (Postgres rounds numeric half away from zero, as <see cref="AiCharge.Micros"/> does). A call with no
    /// known cost is counted, and charges nothing. Own-key calls are counted and never charged.
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
                Tokens = g.Sum(c => (c.InputTokens ?? 0) + (c.OutputTokens ?? 0)),
                Cost = g.Sum(c => c.Cost ?? 0m),
                Micros = g.Sum(c => c.Cost == null ? 0m : Math.Round(c.Cost.Value * factor * 1000000m)),
                Unknown = g.Count(c => c.Cost == null),
            })
            .SingleOrDefaultAsync(cancellationToken);
        var ownKey = await db.AiUsageCalls.AsNoTracking().CountAsync(c => c.KeySource == AiKeySource.OwnKey && c.OccurredAt >= monthStart, cancellationToken);
        var waived = await db.AiUsageCharges.AsNoTracking().Where(c => c.State == AiChargeState.Waived).SumAsync(c => c.Micros, cancellationToken);
        return new AiUsageBillingSummary(
            builtIn?.Calls ?? 0, builtIn?.Tokens ?? 0, builtIn?.Cost ?? 0m, (long)(builtIn?.Micros ?? 0m), builtIn?.Unknown ?? 0, ownKey, waived);
    }
}

public static class AiUsageBillingRegistration
{
    /// <summary>
    /// What the worker needs to report AI use: the billing settings and their checks, the plan catalogue and both payment providers
    /// behind <see cref="BillingAccess"/>. Registered by the worker's messaging setup; nothing here talks to a provider until a
    /// report runs.
    /// </summary>
    public static IServiceCollection AddScreenHireAiUsageBilling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<BillingOptions>().BindConfiguration(BillingOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BillingOptions>, BillingSandboxGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BillingOptions>, BillingAiGate>());
        services.TryAddSingleton<PlanCatalogue>();
        services.TryAddSingleton<StripeBillingProvider>();
        services.TryAddSingleton<SandboxBillingProvider>();
        services.TryAddScoped<BillingAccess>();
        return services;
    }
}
