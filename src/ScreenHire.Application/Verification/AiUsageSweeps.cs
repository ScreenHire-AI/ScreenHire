using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Verification.Content;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Verification;
using ScreenHire.Infrastructure.Verification.Llm;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Verification;

/// <summary>
/// Settings of the daily AI usage sweeps (<c>ScreenHire:AiUsage</c>, docs/architecture/ai-usage.md): the spike rule, and how many
/// paused checks one run asks again.
/// </summary>
public sealed class AiUsageOptions
{
    public const string Section = "ScreenHire:AiUsage";

    public SpikeOptions Spike { get; set; } = new();

    /// <summary>The most paused checks one organization's run asks again; a run that finds more sends itself again.</summary>
    public int ResumeBatchSize { get; set; } = 200;
}

/// <summary>The spike rule: a day is a spike when it used at least <see cref="MinimumTokens"/> and <see cref="Factor"/> times the recent daily average.</summary>
public sealed class SpikeOptions
{
    public decimal Factor { get; set; } = 3m;

    /// <summary>How many days before the judged day make the recent average.</summary>
    public int LookbackDays { get; set; } = 14;

    public long MinimumTokens { get; set; } = 100_000;

    /// <summary>How many of the look-back days must have had any use, so a new organization never spikes.</summary>
    public int MinimumActiveDays { get; set; } = 3;
}

/// <summary>Refuses settings the sweeps cannot work with, at startup.</summary>
public sealed class AiUsageOptionsCheck : IValidateOptions<AiUsageOptions>
{
    public ValidateOptionsResult Validate(string? name, AiUsageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        if (options.Spike.Factor <= 1m)
        {
            problems.Add("ScreenHire:AiUsage:Spike:Factor must be above 1.");
        }

        if (options.Spike.LookbackDays is < 3 or > 90)
        {
            problems.Add("ScreenHire:AiUsage:Spike:LookbackDays must be 3 to 90.");
        }

        if (options.Spike.MinimumTokens < 0 || options.Spike.MinimumActiveDays < 1 || options.Spike.MinimumActiveDays > options.Spike.LookbackDays)
        {
            problems.Add("ScreenHire:AiUsage:Spike:MinimumTokens must not be negative, and MinimumActiveDays must be 1 up to LookbackDays.");
        }

        if (options.ResumeBatchSize < 1)
        {
            problems.Add("ScreenHire:AiUsage:ResumeBatchSize must be at least 1.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

/// <summary>Refuses a negative deployment price or default limit, which would turn a cost or a cap into nonsense.</summary>
public sealed class LlmLimitsCheck : IValidateOptions<VerificationOptions>
{
    public ValidateOptionsResult Validate(string? name, VerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var llm = options.Llm;
        var problems = new List<string>();
        if (llm.DefaultMonthlyTokenBudget < 0 || llm.DefaultMonthlyTokenCap < 0)
        {
            problems.Add("ScreenHire:Verification:Llm:DefaultMonthlyTokenBudget and DefaultMonthlyTokenCap must not be negative (0 turns one off).");
        }

        problems.AddRange(llm.Prices
            .Where(p => p.Value.Input < 0 || p.Value.Output < 0)
            .Select(p => $"ScreenHire:Verification:Llm:Prices:{p.Key} must not be negative."));
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

/// <summary>Judges yesterday's AI use for every organization (daily, 06:07 UTC). It has no tenant, so it fans out one message each.</summary>
public sealed record CheckAiUsageSpikes;

/// <summary>Judges the UTC day before <paramref name="Today"/> for one organization.</summary>
public sealed record CheckOrganizationAiUsageSpike(Guid OrganizationId, DateTimeOffset Today) : ITenantMessage
{
    public string IdempotencyKey => $"ai-spike:{OrganizationId:N}:{AiUsageQueries.Day(Today)}";
}

public static class CheckAiUsageSpikesHandler
{
    public static async Task<OutgoingMessages> Handle(CheckAiUsageSpikes message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var today = AiUsageQueries.DayStart(time.GetUtcNow());
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new CheckOrganizationAiUsageSpike(id, today)));
        return outgoing;
    }
}

public static partial class CheckOrganizationAiUsageSpikeHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(CheckOrganizationAiUsageSpike.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        CheckOrganizationAiUsageSpike message,
        ScreenHireDbContext db,
        IOptions<AiUsageOptions> options,
        ILogger<CheckOrganizationAiUsageSpike> logger,
        CancellationToken cancellationToken)
    {
        var rule = options.Value.Spike;
        var yesterday = message.Today.AddDays(-1);
        var windowStart = yesterday.AddDays(-rule.LookbackDays);
        var byDay = await db.TokensByDayAsync(windowStart, message.Today, cancellationToken);
        var prior = Enumerable.Range(0, rule.LookbackDays).Select(i => byDay.GetValueOrDefault(windowStart.AddDays(i))).ToList();
        var used = byDay.GetValueOrDefault(yesterday);
        var day = AiUsageQueries.Day(yesterday);
        var outgoing = new OutgoingMessages();
        if (AiUsageSpike.IsSpike(used, prior, rule.Factor, rule.MinimumTokens, rule.MinimumActiveDays))
        {
            // The handler of the alert claims the key, so a second look at the same day tells nobody twice.
            outgoing.Add(new NotifyAiUsage(message.OrganizationId, AiUsageAlertKind.Spike, AiUsageAlertKeys.Spike(day)));
            LogSpike(logger, message.OrganizationId, day);
        }

        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: AI use on {Day} was a spike")]
    private static partial void LogSpike(ILogger logger, Guid organizationId, string day);
}

/// <summary>Deletes ledger rows past every organization's retention (daily, 04:42 UTC), one message per organization.</summary>
public sealed record PurgeExpiredAiUsage;

public sealed record PurgeOrganizationAiUsage(Guid OrganizationId, DateTimeOffset Now) : ITenantMessage;

public static class PurgeExpiredAiUsageHandler
{
    public static async Task<OutgoingMessages> Handle(PurgeExpiredAiUsage message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new PurgeOrganizationAiUsage(id, now)));
        return outgoing;
    }
}

public static partial class PurgeOrganizationAiUsageHandler
{
    /// <summary>
    /// The instant before which usage rows are deleted: the organization's own record retention period, but never newer than the
    /// start of the previous calendar month, so the last full month stays readable for billing. Null when retention is off.
    /// </summary>
    public static DateTimeOffset? CutoffFor(DateTimeOffset? retentionCutoff, DateTimeOffset now) =>
        retentionCutoff is { } cutoff ? new[] { cutoff, AiUsageQueries.MonthStart(now).AddMonths(-1) }.Min() : null;

    public static async Task<OutgoingMessages> Handle(
        PurgeOrganizationAiUsage message, ScreenHireDbContext db, ILogger<PurgeOrganizationAiUsage> logger, CancellationToken cancellationToken)
    {
        var settings = await db.RetentionSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (CutoffFor(settings?.Cutoff(message.Now), message.Now) is not { } cutoff)
        {
            return [];
        }

        // Rows nobody changes, so a plain delete is right: the ledger is not a tracked kind of settings.
        var calls = await db.AiUsageCalls.Where(c => c.OccurredAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        var alerts = await db.AiUsageAlerts.Where(a => a.RaisedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        if (calls + alerts > 0)
        {
            LogPurged(logger, message.OrganizationId, calls, alerts);
        }

        return [];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: retention deleted {Calls} AI usage rows and {Alerts} alert claims")]
    private static partial void LogPurged(ILogger logger, Guid organizationId, int calls, int alerts);
}

/// <summary>Asks again for the AI checks every organization paused, where the month changed or the cap now allows it (daily, 05:12 UTC).</summary>
public sealed record ResumePausedAiChecks;

/// <summary>
/// Asks again for one organization's paused AI checks. Sent by the daily run and when an Owner raises or removes the cap.
/// <paramref name="Why"/> only keys the message to its cause, so two causes on one day are two intents.
/// </summary>
public sealed record ResumeOrganizationAiChecks(Guid OrganizationId, string Why) : ITenantMessage
{
    public string IdempotencyKey => $"ai-resume:{OrganizationId:N}:{Why}";
}

public static class ResumePausedAiChecksHandler
{
    public static async Task<OutgoingMessages> Handle(ResumePausedAiChecks message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var day = AiUsageQueries.Day(time.GetUtcNow());
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new ResumeOrganizationAiChecks(id, $"daily-{day}")));
        return outgoing;
    }
}

public static partial class ResumeOrganizationAiChecksHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(ResumeOrganizationAiChecks.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        ResumeOrganizationAiChecks message,
        ScreenHireDbContext db,
        IOptions<VerificationOptions> verification,
        IOptions<AiUsageOptions> options,
        TimeProvider time,
        ILogger<ResumeOrganizationAiChecks> logger,
        CancellationToken cancellationToken)
    {
        var paused = await db.ConsistencyChecks.AsNoTracking()
            .Where(c => c.Status == ConsistencyCheckStatus.Paused)
            .OrderBy(c => c.StartedAt)
            .Select(c => c.ApplicationId)
            .Take(Math.Max(1, options.Value.ResumeBatchSize))
            .ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        if (paused.Count == 0)
        {
            return outgoing;
        }

        // Only when the cap no longer stops them. A check asked again while the cap still holds would pause again and send nothing new.
        var settings = await db.VerificationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var (cap, unit) = AiUsageLimits.EffectiveCap(settings, verification.Value.Llm.DefaultMonthlyTokenCap);
        if (cap is not null && AiUsageLimits.CapReached(await db.UsedThisMonthAsync(unit, time.GetUtcNow(), cancellationToken), cap))
        {
            return outgoing;
        }

        outgoing.AddRange(paused.Select(id => new CheckApplicationConsistency(message.OrganizationId, id)));
        LogResumed(logger, message.OrganizationId, paused.Count);
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: asked again for {Checks} paused AI checks")]
    private static partial void LogResumed(ILogger logger, Guid organizationId, int checks);
}

public static class AiUsageRegistration
{
    /// <summary>The sweeps' settings, validated at startup. Registered by the worker's messaging setup.</summary>
    public static IServiceCollection AddScreenHireAiUsage(this IServiceCollection services)
    {
        services.AddOptions<AiUsageOptions>().BindConfiguration(AiUsageOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AiUsageOptions>, AiUsageOptionsCheck>();
        return services;
    }
}
