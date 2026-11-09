using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Notifications;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.Infrastructure.Verification;
using ScreenHire.Infrastructure.Verification.Llm;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The organization's AI usage (docs/architecture/ai-usage.md): a summary by day, model and key, the call list with the
/// provider's request ids, a CSV of it, and the monthly budget and cap. Reading is for who holds <c>ViewAiUsage</c> (Owners
/// and Admins), setting the limits for Owners (<c>ManageBilling</c>). Signed-in people only: an API key gets nothing here.
/// </summary>
internal static class AiUsageEndpoints
{
    public static IEndpointRouteBuilder MapAiUsageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/organization/ai-usage").WithTags("AiUsage").RequireAuthorization();
        group.MapGet("/summary", AiUsageHandlers.Summary).WithName("GetAiUsageSummary")
            .RequirePermission(Permission.ViewAiUsage)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/calls", AiUsageHandlers.Calls).WithName("ListAiUsageCalls")
            .RequirePermission(Permission.ViewAiUsage)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/calls/export", AiUsageHandlers.Export).WithName("ExportAiUsageCalls")
            .RequirePermission(Permission.ViewAiUsage)
            .Produces<string>(StatusCodes.Status200OK, "text/csv")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/limits", AiUsageHandlers.UpdateLimits).WithName("UpdateAiUsageLimits")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageBilling)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }
}

internal static class AiUsageHandlers
{
    /// <summary>The most calls one CSV holds. A range with more says so in a header, and the export is narrowed by date.</summary>
    public const int MaxExportRows = 50_000;

    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 200;

    /// <summary>The longest range a summary or export covers.</summary>
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(400);

    /// <summary>The month's limits in force, with what has been used against each. Shared by the summary and the settings page.</summary>
    public static async Task<AiUsageLimitsView> LimitsAsync(
        ScreenHireDbContext db, VerificationSettings? settings, LlmOptions llm, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (budget, budgetUnit) = AiUsageLimits.EffectiveBudget(settings, llm.DefaultMonthlyTokenBudget);
        var (cap, capUnit) = AiUsageLimits.EffectiveCap(settings, llm.DefaultMonthlyTokenCap);
        var usedByUnit = new Dictionary<UsageUnit, decimal>();
        async Task<AiUsageLimitView?> ViewAsync(decimal? amount, UsageUnit unit, decimal? own)
        {
            if (amount is not { } limit)
            {
                return null;
            }

            if (!usedByUnit.TryGetValue(unit, out var used))
            {
                used = await db.UsedThisMonthAsync(unit, now, cancellationToken);
                usedByUnit[unit] = used;
            }

            var percent = limit > 0 ? (int)Math.Min(1000m, Math.Floor(used * 100m / limit)) : 0;
            return new AiUsageLimitView(limit, unit, own is null, used, percent);
        }

        var effectiveBudget = await ViewAsync(budget, budgetUnit, settings?.MonthlyBudget);
        var effectiveCap = await ViewAsync(cap, capUnit, settings?.MonthlyCap);
        return new AiUsageLimitsView(settings?.BudgetUnit ?? UsageUnit.Tokens, settings?.MonthlyBudget, settings?.MonthlyCap, effectiveBudget, effectiveCap);
    }

    public static async Task<Results<Ok<AiUsageSummary>, ValidationProblem>> Summary(
        ScreenHireDbContext db, IOptions<VerificationOptions> verification, TimeProvider time, CancellationToken cancellationToken,
        DateTimeOffset? from = null, DateTimeOffset? to = null, string timeZone = "UTC")
    {
        var now = time.GetUtcNow();
        var start = from ?? AiUsageQueries.MonthStart(now);
        var end = to ?? now;
        var problems = RangeProblems(start, end);
        TimeZoneInfo? zone = null;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out zone))
        {
            problems["timeZone"] = ["Say the time zone by its name, such as Europe/London."];
        }

        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var groups = await db.AiUsageCalls.AsNoTracking()
            .Where(c => c.OccurredAt >= start && c.OccurredAt < end)
            .GroupBy(c => new { c.Provider, c.Model, c.KeySource })
            .Select(g => new
            {
                g.Key.Provider,
                g.Key.Model,
                g.Key.KeySource,
                Calls = g.Count(),
                Input = g.Sum(c => c.InputTokens ?? 0),
                Output = g.Sum(c => c.OutputTokens ?? 0),
                Cost = g.Sum(c => c.Cost ?? 0m),
                Unknown = g.Count(c => c.CostBasis == AiCostBasis.Unknown),
                Failed = g.Count(c => c.Outcome != AiCallOutcome.Ok),
            })
            .ToListAsync(cancellationToken);
        var rows = await db.AiUsageCalls.AsNoTracking()
            .Where(c => c.OccurredAt >= start && c.OccurredAt < end)
            .Select(c => new { c.OccurredAt, c.KeySource, Input = c.InputTokens ?? 0, Output = c.OutputTokens ?? 0, Cost = c.Cost ?? 0m })
            .ToListAsync(cancellationToken);

        var byModel = groups
            .OrderByDescending(g => g.Cost).ThenByDescending(g => g.Input + g.Output).ThenBy(g => g.Model, StringComparer.Ordinal)
            .Select(g => new AiUsageGroup(g.Provider, g.Model, g.KeySource, g.Calls, g.Input, g.Output, g.Cost, g.Unknown))
            .ToList();
        var byKey = groups.GroupBy(g => g.KeySource)
            .Select(k => new AiUsageGroup(
                "", "", k.Key, k.Sum(g => g.Calls), k.Sum(g => g.Input), k.Sum(g => g.Output), k.Sum(g => g.Cost), k.Sum(g => g.Unknown)))
            .OrderBy(g => g.KeySource)
            .ToList();
        var byDay = rows
            .GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.OccurredAt, zone!).DateTime))
            .OrderBy(g => g.Key)
            .Select(g => new AiUsageDay(
                g.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), g.Count(), g.Sum(r => r.Input), g.Sum(r => r.Output), g.Sum(r => r.Cost),
                g.Where(r => r.KeySource == AiKeySource.BuiltIn).Sum(r => r.Input + r.Output),
                g.Where(r => r.KeySource == AiKeySource.OwnKey).Sum(r => r.Input + r.Output),
                g.Where(r => r.KeySource == AiKeySource.BuiltIn).Sum(r => r.Cost),
                g.Where(r => r.KeySource == AiKeySource.OwnKey).Sum(r => r.Cost)))
            .ToList();
        var totals = new AiUsageTotals(
            groups.Sum(g => g.Calls), groups.Sum(g => g.Input), groups.Sum(g => g.Output), groups.Sum(g => g.Cost),
            groups.Sum(g => g.Unknown), groups.Sum(g => g.Failed));

        var settings = await db.VerificationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var limits = await LimitsAsync(db, settings, verification.Value.Llm, now, cancellationToken);
        return TypedResults.Ok(new AiUsageSummary(start, end, totals, byDay, byModel, byKey, limits));
    }

    public static async Task<Results<Ok<AiUsageCallPage>, ValidationProblem>> Calls(
        ScreenHireDbContext db, CancellationToken cancellationToken,
        DateTimeOffset? from = null, DateTimeOffset? to = null, AiKeySource? keySource = null, string? provider = null, string? model = null,
        AiCallOutcome? outcome = null, string? check = null, Guid? applicationId = null, Guid? before = null, int? limit = null)
    {
        var problems = RangeProblems(from, to);
        if (limit is { } size && size is < 1 or > MaxPageSize)
        {
            problems["limit"] = [$"A page holds 1 to {MaxPageSize} calls."];
        }

        if (before is { } cursor && cursor == Guid.Empty)
        {
            problems["before"] = ["Pass the nextBefore a page gave you."];
        }

        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var take = limit ?? DefaultPageSize;
        var query = Filter(db.AiUsageCalls.AsNoTracking(), from, to, keySource, provider, model, outcome, check, applicationId);
        if (before is { } after)
        {
            query = query.Where(c => c.Id.CompareTo(after) < 0);
        }

        // Newest first by id: a version 7 id sorts by the time of the call.
        var page = await query.OrderByDescending(c => c.Id).Take(take + 1).ToListAsync(cancellationToken);
        var more = page.Count > take;
        var calls = page.Take(take).Select(ToView).ToList();
        return TypedResults.Ok(new AiUsageCallPage(calls, more ? calls[^1].Id : null));
    }

    public static async Task<Results<FileContentHttpResult, ValidationProblem>> Export(
        ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken,
        DateTimeOffset? from = null, DateTimeOffset? to = null, AiKeySource? keySource = null, string? provider = null, string? model = null,
        AiCallOutcome? outcome = null, string? check = null, Guid? applicationId = null)
    {
        var problems = RangeProblems(from, to);
        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var calls = await Filter(db.AiUsageCalls.AsNoTracking(), from, to, keySource, provider, model, outcome, check, applicationId)
            .OrderByDescending(c => c.Id)
            .Take(MaxExportRows + 1)
            .ToListAsync(cancellationToken);
        var truncated = calls.Count > MaxExportRows;
        var csv = new AiUsageCsv();
        csv.Row(
            "id", "occurred_at", "check", "application_id", "provider", "model", "key_source", "provider_request_id",
            "input_tokens", "output_tokens", "cost_usd", "cost_basis", "duration_ms", "outcome", "error_kind");
        foreach (var call in calls.Take(MaxExportRows))
        {
            csv.Row(
                call.Id, call.OccurredAt, call.CheckName, call.ApplicationId, call.Provider, call.Model, call.KeySource, call.ProviderRequestId,
                call.InputTokens, call.OutputTokens, call.Cost, call.CostBasis, call.DurationMs, call.Outcome, call.ErrorKind);
        }

        if (truncated)
        {
            // Said in the file itself, where the person reading it will see it.
            csv.Row($"Only the newest {MaxExportRows} calls are listed. Narrow the dates to export the rest.");
        }

        var name = $"ai-usage-{time.GetUtcNow():yyyyMMdd}.csv";
        return TypedResults.File(csv.ToBytes(), "text/csv; charset=utf-8", name);
    }

    public static async Task<Results<Ok<AiUsageLimitsView>, ValidationProblem>> UpdateLimits(
        UpdateAiUsageLimitsRequest request,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        IOptions<VerificationOptions> verification,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var settings = await db.VerificationSettings.SingleOrDefaultAsync(cancellationToken);
        var problems = LimitProblems(request, settings);
        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var now = time.GetUtcNow();
        var organizationId = tenant.OrganizationId!.Value;
        if (settings is null)
        {
            settings = new VerificationSettings { OrganizationId = organizationId };
            db.VerificationSettings.Add(settings);
        }

        var capChanged = settings.MonthlyCap != request.Cap || settings.BudgetUnit != request.Unit;
        var budgetChanged = settings.MonthlyBudget != request.Budget || settings.BudgetUnit != request.Unit;
        settings.BudgetUnit = request.Unit;
        settings.MonthlyBudget = request.Budget;
        settings.MonthlyCap = request.Cap;
        settings.UpdatedAt = now;

        // A new budget or cap is a new decision: the month's alerts for it may go out again, measured against the new amount.
        // The claims are removed through the change tracker, so they go in the same save as the settings and the messages.
        var month = AiUsageQueries.Month(now);
        var cleared = new List<string>();
        if (budgetChanged)
        {
            cleared.AddRange(AiUsageLimits.BudgetThresholds.Select(percent => AiUsageAlertKeys.Budget(month, percent)));
        }

        if (capChanged)
        {
            cleared.Add(AiUsageAlertKeys.Cap(month));
        }

        if (cleared.Count > 0)
        {
            db.AiUsageAlerts.RemoveRange(await db.AiUsageAlerts.Where(a => cleared.Contains(a.Key)).ToListAsync(cancellationToken));
        }

        if (budgetChanged)
        {
            foreach (var percent in AiUsageLimits.BudgetThresholds)
            {
                await outbox.PublishAsync(new ClearNotification(organizationId, NotificationKinds.AiUsageBudget, AiUsageAlertKeys.Budget(month, percent)));
            }
        }

        if (capChanged)
        {
            // The AI checks the old cap paused are asked again (they stay paused if the new cap still holds).
            await outbox.PublishAsync(new ClearNotification(organizationId, NotificationKinds.AiUsageCap, AiUsageAlertKeys.Cap(month)));
            await outbox.PublishAsync(new ResumeOrganizationAiChecks(organizationId, $"limits-{now.UtcTicks}"));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Ok(await LimitsAsync(db, settings, verification.Value.Llm, now, cancellationToken));
    }

    /// <summary>What is wrong with a request to set the limits, by field.</summary>
    public static Dictionary<string, string[]> LimitProblems(UpdateAiUsageLimitsRequest request, VerificationSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problems = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(request.Unit))
        {
            problems["unit"] = ["Choose tokens or dollars."];
            return problems;
        }

        AddAmountProblem(problems, "budget", request.Budget, request.Unit);
        AddAmountProblem(problems, "cap", request.Cap, request.Unit);
        if (request.Unit == UsageUnit.Money && settings is { UsesOwnKey: true } && (settings.OwnKeyInputPrice is null || settings.OwnKeyOutputPrice is null))
        {
            problems["unit"] = ["Counting in dollars with your own key needs the key's prices: enter them in the AI model settings first."];
        }

        return problems;
    }

    private static void AddAmountProblem(Dictionary<string, string[]> problems, string field, decimal? amount, UsageUnit unit)
    {
        if (amount is not { } value)
        {
            return;
        }

        if (unit == UsageUnit.Tokens)
        {
            if (value < 1 || value > VerificationSettings.MaxMonthlyTokens || value != decimal.Truncate(value))
            {
                problems[field] = [$"Enter a whole number of tokens from 1 to {VerificationSettings.MaxMonthlyTokens.ToString("N0", CultureInfo.InvariantCulture)}."];
            }

            return;
        }

        if (value < 0.01m || value > VerificationSettings.MaxMonthlyMoney || value != decimal.Round(value, 2))
        {
            problems[field] = [$"Enter an amount in dollars from 0.01 to {VerificationSettings.MaxMonthlyMoney.ToString("N0", CultureInfo.InvariantCulture)}, to the cent."];
        }
    }

    internal static Dictionary<string, string[]> RangeProblems(DateTimeOffset? from, DateTimeOffset? to)
    {
        var problems = new Dictionary<string, string[]>();
        if (from is { } start && to is { } end)
        {
            if (end <= start)
            {
                problems["to"] = ["The end of the range must be after its start."];
            }
            else if (end - start > MaxRange)
            {
                problems["to"] = ["A range is at most 400 days."];
            }
        }

        return problems;
    }

    private static IQueryable<AiUsageCall> Filter(
        IQueryable<AiUsageCall> query, DateTimeOffset? from, DateTimeOffset? to, AiKeySource? keySource, string? provider, string? model,
        AiCallOutcome? outcome, string? check, Guid? applicationId)
    {
        if (from is { } start)
        {
            query = query.Where(c => c.OccurredAt >= start);
        }

        if (to is { } end)
        {
            query = query.Where(c => c.OccurredAt < end);
        }

        if (keySource is { } source)
        {
            query = query.Where(c => c.KeySource == source);
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            var wanted = provider.Trim();
            query = query.Where(c => c.Provider == wanted);
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            var wanted = model.Trim();
            query = query.Where(c => c.Model == wanted);
        }

        if (outcome is { } result)
        {
            query = query.Where(c => c.Outcome == result);
        }

        if (!string.IsNullOrWhiteSpace(check))
        {
            var wanted = check.Trim();
            query = query.Where(c => c.CheckName == wanted);
        }

        if (applicationId is { } application)
        {
            query = query.Where(c => c.ApplicationId == application);
        }

        return query;
    }

    private static AiUsageCallView ToView(AiUsageCall c) => new(
        c.Id, c.OccurredAt, c.CheckName, c.ApplicationId, c.Provider, c.Model, c.KeySource, c.ProviderRequestId, c.InputTokens, c.OutputTokens,
        c.Cost, c.CostBasis, c.DurationMs, c.Outcome, c.ErrorKind);
}
