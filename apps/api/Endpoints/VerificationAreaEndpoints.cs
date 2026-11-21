using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Billing;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.Infrastructure.Verification.Llm;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// What the Verification area's Overview and Activity log read (docs/architecture/verification-engine.md 10.12). Both are for
/// any signed-in member and run under the member's row-level security, so a member limited to some jobs sees those jobs.
/// What a model call cost and its request id come only with View AI usage and All jobs, and so does the CSV. Nothing here is
/// about a person, and none of it holds a score.
/// </summary>
internal static class VerificationAreaEndpoints
{
    public static IEndpointRouteBuilder MapVerificationAreaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/verification").WithTags("Verification").RequireAuthorization();
        group.MapGet("/overview", VerificationAreaHandlers.Overview).WithName("GetVerificationOverview")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/activity", VerificationAreaHandlers.Activity).WithName("ListVerificationActivity")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/activity/export", VerificationAreaHandlers.Export).WithName("ExportVerificationActivity")
            .RequirePermission(Permission.ViewAiUsage)
            .Produces<string>(StatusCodes.Status200OK, "text/csv")
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }
}

internal static class VerificationAreaHandlers
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 200;

    /// <summary>The most rows one CSV holds.</summary>
    public const int MaxExportRows = 50_000;

    public static async Task<Ok<VerificationOverviewView>> Overview(ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var start = AiUsageQueries.MonthStart(now);
        var applications = db.Applications.AsNoTracking().Where(a => a.SubmittedAt >= start && a.SubmittedAt <= now);
        var applicationCount = await applications.CountAsync(cancellationToken);

        // The newest authenticity assessment of each application, as the lists read it.
        var assessments = await (
                from assessment in db.VerificationAssessments.AsNoTracking()
                where assessment.Kind == VerificationAssessmentKind.Authenticity
                join application in applications on assessment.ApplicationId equals application.Id
                select new { assessment.Id, assessment.ApplicationId, assessment.Level, assessment.AssessedAt, assessment.CountedSignalIds })
            .ToListAsync(cancellationToken);
        var current = assessments
            .GroupBy(a => a.ApplicationId)
            .Select(g => g.OrderByDescending(a => a.AssessedAt).ThenByDescending(a => a.Id).First())
            .ToList();

        int AtLevel(AuthenticityLevel level) => current.Count(a => a.Level == level.ToString());
        var levels = new VerificationLevelCounts(
            AtLevel(AuthenticityLevel.NoConcerns), AtLevel(AuthenticityLevel.ReviewSuggested), AtLevel(AuthenticityLevel.VerificationRecommended));

        // Every group, in the groups' order, with the applications in which it counted a signal towards a level (one count per
        // application), and whether the organization runs it: a group that is off is listed as off, never left out.
        var countedIds = current.SelectMany(a => a.CountedSignalIds).ToHashSet();
        var counts = new Dictionary<CheckGroup, int>();
        if (countedIds.Count > 0)
        {
            var signals = await (
                    from signal in db.VerificationSignals.AsNoTracking()
                    where signal.Assessment == VerificationAssessmentKind.Authenticity
                    join application in applications on signal.ApplicationId equals application.Id
                    select signal)
                .ToListAsync(cancellationToken);
            counts = signals.Where(s => countedIds.Contains(s.Id))
                .GroupBy(s => CheckGroups.OfSignal(s))
                .ToDictionary(g => g.Key, g => g.Select(s => s.ApplicationId).Distinct().Count());
        }

        var settings = await db.VerificationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        List<VerificationGroupCount> groups =
        [
            .. Enum.GetValues<CheckGroup>().Select(group => new VerificationGroupCount(
                group, CheckGroups.Label(group), counts.GetValueOrDefault(group), settings?.Runs(group) ?? true)),
        ];

        var lastCheckedAt = assessments.Count == 0 ? (DateTimeOffset?)null : assessments.Max(a => a.AssessedAt);
        return TypedResults.Ok(new VerificationOverviewView(
            start, now, current.Count, Math.Max(0, applicationCount - current.Count), levels, groups, lastCheckedAt));
    }

    public static async Task<Results<Ok<VerificationActivityPage>, ValidationProblem>> Activity(
        ScreenHireDbContext db, ITenantContext tenant, IOptions<BillingOptions> billing, CancellationToken cancellationToken,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CheckGroup? group = null, AiKeySource? keySource = null,
        VerificationActivityOutcome? outcome = null, string? before = null, int? limit = null)
    {
        var problems = AiUsageHandlers.RangeProblems(from, to);
        if (limit is { } size && size is < 1 or > MaxPageSize)
        {
            problems["limit"] = [$"A page holds 1 to {MaxPageSize} entries."];
        }

        VerificationActivity.Cursor? cursor = null;
        if (before is not null)
        {
            if (VerificationActivity.Cursor.TryParse(before, out var parsed))
            {
                cursor = parsed;
            }
            else
            {
                problems["before"] = ["Pass the nextBefore a page gave you."];
            }
        }

        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var usageVisible = await CanSeeUsageAsync(db, tenant, cancellationToken);
        var take = limit ?? DefaultPageSize;
        var passes = await PassesAsync(db, from, to, group, keySource, outcome, cursor, take + 1, cancellationToken);
        var lone = await LoneCallsAsync(db, usageVisible, from, to, group, keySource, outcome, cursor, take + 1, cancellationToken);
        var (page, more) = VerificationActivity.MergePage(passes, lone, take, h => h.At, h => h.SortId);
        var runs = await ToRunsAsync(db, page, usageVisible, billing.Value.AiMarginInForce, cancellationToken);
        var next = more && page.Count > 0 ? new VerificationActivity.Cursor(page[^1].At, page[^1].SortId).Encode() : null;
        return TypedResults.Ok(new VerificationActivityPage(runs, next, usageVisible));
    }

    public static async Task<Results<FileContentHttpResult, ValidationProblem>> Export(
        ScreenHireDbContext db, ITenantContext tenant, IOptions<BillingOptions> billing, TimeProvider time, CancellationToken cancellationToken,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CheckGroup? group = null, AiKeySource? keySource = null,
        VerificationActivityOutcome? outcome = null)
    {
        var problems = AiUsageHandlers.RangeProblems(from, to);
        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        // The export stays one line per check run or model call, with every column, so a bill can be compared line by line with
        // the provider's own logs. `run` says which run of the log the line belongs to, and `ruleset` which ruleset spoke then.
        var usageVisible = await CanSeeUsageAsync(db, tenant, cancellationToken);
        var runs = await RunsAsync(db, from, to, group, keySource, outcome, null, MaxExportRows + 1, cancellationToken);
        var calls = await CallsAsync(db, usageVisible, from, to, group, keySource, outcome, null, MaxExportRows + 1, cancellationToken);
        var (page, more) = VerificationActivity.MergePage(runs, calls, MaxExportRows, e => e.At, e => e.Id);
        var context = await RunContextAsync(db, page.Select(r => r.ApplicationId), cancellationToken);
        var margin = billing.Value.AiMarginInForce;

        var csv = new AiUsageCsv();
        csv.Row(
            "id", "at", "kind", "group", "check", "outcome", "application_id", "job", "note", "provider", "model", "key_source",
            "provider_request_id", "input_tokens", "output_tokens", "cost_usd", "duration_ms", "error_kind", "run", "ruleset");
        foreach (var row in page)
        {
            var call = usageVisible ? row.Call : null;
            var passAt = row.Kind == VerificationActivityKind.CheckRun ? row.At : context.PassOfCall(row.ApplicationId, row.At);
            var run = row.ApplicationId is { } applicationId && passAt is { } at
                ? VerificationActivity.RunId(at, applicationId)
                : VerificationActivity.CallRunId(row.Id);
            csv.Row(
                row.Id, row.At, row.Kind, row.Group, row.Check, row.Outcome, row.ApplicationId, context.TitleOf(row.ApplicationId),
                row.Note is null ? null : RunNotes.Describe(row.Check, row.Note),
                call?.Provider, call?.Model, call?.KeySource, call?.ProviderRequestId, call?.InputTokens, call?.OutputTokens,
                call is null ? null : AiCharge.Billed(call.Cost, call.KeySource, margin), call?.DurationMs, call?.ErrorKind,
                run, passAt is { } when ? context.RulesetAt(row.ApplicationId, when) : null);
        }

        if (more)
        {
            csv.Row($"Only the newest {MaxExportRows} entries are listed. Narrow the dates to export the rest.");
        }

        return TypedResults.File(csv.ToBytes(), "text/csv; charset=utf-8", $"verification-activity-{time.GetUtcNow():yyyyMMdd}.csv");
    }

    /// <summary>View AI usage covers every job's calls, so it needs All jobs as well, as everywhere else (RolePermissions.NeedsAllJobs).</summary>
    private static async Task<bool> CanSeeUsageAsync(ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        tenant.SeesAllJobs && (await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken)).Contains(Permission.ViewAiUsage);

    /// <summary>
    /// A run of the log before its rows are read: a pass of the checks over one application (<c>CallId</c> null), or a model
    /// call on its own. <c>SortId</c> breaks a tie in time the way the cursor does: the application for a pass, the call for a call.
    /// </summary>
    private sealed record RunHead(DateTimeOffset At, Guid SortId, Guid? ApplicationId, Guid? CallId);

    /// <summary>
    /// The passes of the checks, newest first. A pass is every analyzer run the pipeline wrote for one application at one
    /// instant (VerificationPipeline stamps them with the same time); a re-check's later revision is a pass of its own. A filter
    /// keeps a pass when one of its checks matches it: the group, something found, or a check that did not look. "Nothing found"
    /// keeps a pass in which nothing was found, and a key keeps a pass whose model calls used it.
    /// </summary>
    private static async Task<List<RunHead>> PassesAsync(
        ScreenHireDbContext db, DateTimeOffset? from, DateTimeOffset? to, CheckGroup? group, AiKeySource? keySource,
        VerificationActivityOutcome? outcome, VerificationActivity.Cursor? cursor, int take, CancellationToken cancellationToken)
    {
        if (outcome == VerificationActivityOutcome.Failed)
        {
            return []; // a failed model call is not a pass's outcome
        }

        var query = db.VerificationAnalyzerRuns.AsNoTracking().AsQueryable();
        if (from is { } start)
        {
            query = query.Where(r => r.CompletedAt >= start);
        }

        if (to is { } end)
        {
            query = query.Where(r => r.CompletedAt < end);
        }

        var trusted = VerificationActivity.TrustedNotes;
        var analyzers = group is { } wanted ? VerificationActivity.AnalyzersOf(wanted) : [];
        var heads = query
            .GroupBy(r => new { r.ApplicationId, r.CompletedAt })
            .Select(g => new
            {
                g.Key.ApplicationId,
                g.Key.CompletedAt,
                Found = g.Count(r => r.SignalCount > 0),
                NotRun = g.Count(r => r.SignalCount == 0 && r.Note != null && !trusted.Contains(r.Note)),
                InGroup = g.Count(r => analyzers.Contains(r.Analyzer)),
            });
        if (group is not null)
        {
            heads = heads.Where(h => h.InGroup > 0);
        }

        heads = outcome switch
        {
            VerificationActivityOutcome.Found => heads.Where(h => h.Found > 0),
            VerificationActivityOutcome.NotRun => heads.Where(h => h.NotRun > 0),
            VerificationActivityOutcome.Completed => heads.Where(h => h.Found == 0),
            _ => heads,
        };
        if (keySource is { } source)
        {
            // A call belongs to the first pass of its application at or after it (VerificationActivity.PassOfCall): no pass of
            // that application falls between the call and this one.
            heads = heads.Where(h => db.AiUsageCalls.Any(c =>
                c.ApplicationId == h.ApplicationId && c.KeySource == source && c.OccurredAt <= h.CompletedAt
                && !db.VerificationAnalyzerRuns.Any(p => p.ApplicationId == h.ApplicationId && p.CompletedAt < h.CompletedAt && p.CompletedAt >= c.OccurredAt)));
        }

        if (cursor is { } after)
        {
            heads = heads.Where(h => h.CompletedAt < after.At || (h.CompletedAt == after.At && h.ApplicationId.CompareTo(after.Id) < 0));
        }

        var rows = await heads.OrderByDescending(h => h.CompletedAt).ThenByDescending(h => h.ApplicationId).Take(take).ToListAsync(cancellationToken);
        return [.. rows.Select(h => new RunHead(h.CompletedAt, h.ApplicationId, h.ApplicationId, null))];
    }

    /// <summary>
    /// Model calls that belong to no pass, newest first: a connection test (no application), or a call whose application has no
    /// pass after it yet.
    /// </summary>
    private static async Task<List<RunHead>> LoneCallsAsync(
        ScreenHireDbContext db, bool usageVisible, DateTimeOffset? from, DateTimeOffset? to, CheckGroup? group, AiKeySource? keySource,
        VerificationActivityOutcome? outcome, VerificationActivity.Cursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = CallQuery(db, usageVisible, from, to, group, keySource, outcome);
        if (query is null)
        {
            return [];
        }

        query = query.Where(c => c.ApplicationId == null || !db.VerificationAnalyzerRuns.Any(r => r.ApplicationId == c.ApplicationId && r.CompletedAt >= c.OccurredAt));
        if (cursor is { } after)
        {
            query = query.Where(c => c.OccurredAt < after.At || (c.OccurredAt == after.At && c.Id.CompareTo(after.Id) < 0));
        }

        var rows = await query.OrderByDescending(c => c.OccurredAt).ThenByDescending(c => c.Id)
            .Select(c => new { c.Id, c.OccurredAt, c.ApplicationId })
            .Take(take).ToListAsync(cancellationToken);
        return [.. rows.Select(c => new RunHead(c.OccurredAt, c.Id, c.ApplicationId, c.Id))];
    }

    /// <summary>The model calls a filter keeps, or null when it keeps none (calls belong to Answers, and end completed or failed).</summary>
    private static IQueryable<AiUsageCall>? CallQuery(
        ScreenHireDbContext db, bool usageVisible, DateTimeOffset? from, DateTimeOffset? to, CheckGroup? group, AiKeySource? keySource,
        VerificationActivityOutcome? outcome)
    {
        if ((group is { } wantedGroup && wantedGroup != CheckGroup.Answers)
            || outcome is VerificationActivityOutcome.Found or VerificationActivityOutcome.NotRun)
        {
            return null;
        }

        var query = db.AiUsageCalls.AsNoTracking().AsQueryable();
        if (!usageVisible)
        {
            // Calls are the organization's, not a job's. A member who cannot see usage sees only calls about an application they can open.
            query = query.Where(c => c.ApplicationId != null && db.Applications.Any(a => a.Id == c.ApplicationId));
        }

        if (group is not null)
        {
            query = query.Where(c => c.CheckName.StartsWith(VerificationActivity.ConsistencyCallPrefix));
        }

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

        return outcome switch
        {
            VerificationActivityOutcome.Completed => query.Where(c => c.Outcome == AiCallOutcome.Ok),
            VerificationActivityOutcome.Failed => query.Where(c => c.Outcome != AiCallOutcome.Ok),
            _ => query,
        };
    }

    /// <summary>
    /// What the log and the export need to know about the applications on a page: every pass time (to place a model call in its
    /// run), the assessments' rulesets and the job titles. One query each, never one per row.
    /// </summary>
    private sealed class RunContext(
        Dictionary<Guid, List<DateTimeOffset>> passes,
        Dictionary<Guid, List<(DateTimeOffset AssessedAt, string RulesetVersion)>> assessments,
        Dictionary<Guid, string> titles)
    {
        public List<DateTimeOffset> PassesOf(Guid applicationId) => passes.GetValueOrDefault(applicationId) ?? [];

        public DateTimeOffset? PassOfCall(Guid? applicationId, DateTimeOffset occurredAt) =>
            applicationId is { } id ? VerificationActivity.PassOfCall(PassesOf(id), occurredAt) : null;

        public string? RulesetAt(Guid? applicationId, DateTimeOffset at) =>
            applicationId is { } id && assessments.TryGetValue(id, out var list) ? VerificationActivity.RulesetAt(list, at) : null;

        public string? TitleOf(Guid? applicationId) => applicationId is { } id ? titles.GetValueOrDefault(id) : null;
    }

    private static async Task<RunContext> RunContextAsync(ScreenHireDbContext db, IEnumerable<Guid?> applicationIds, CancellationToken cancellationToken)
    {
        var ids = applicationIds.OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
        {
            return new RunContext([], [], []);
        }

        var passes = (await db.VerificationAnalyzerRuns.AsNoTracking()
                .Where(r => ids.Contains(r.ApplicationId))
                .Select(r => new { r.ApplicationId, r.CompletedAt })
                .Distinct()
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.ApplicationId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.CompletedAt).ToList());
        var assessments = (await db.VerificationAssessments.AsNoTracking()
                .Where(a => ids.Contains(a.ApplicationId) && a.Kind == VerificationAssessmentKind.Authenticity)
                .Select(a => new { a.ApplicationId, a.AssessedAt, a.RulesetVersion })
                .ToListAsync(cancellationToken))
            .GroupBy(a => a.ApplicationId)
            .ToDictionary(g => g.Key, g => g.Select(a => (a.AssessedAt, a.RulesetVersion)).ToList());
        var titles = await db.Applications.AsNoTracking().Where(a => ids.Contains(a.Id))
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { a.Id, j.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken);
        return new RunContext(passes, assessments, titles);
    }

    /// <summary>Reads the rows of a page of runs: its check runs with what each found, and the model calls that belong to each.</summary>
    private static async Task<List<VerificationActivityRun>> ToRunsAsync(
        ScreenHireDbContext db, IReadOnlyList<RunHead> page, bool usageVisible, decimal margin, CancellationToken cancellationToken)
    {
        if (page.Count == 0)
        {
            return [];
        }

        var context = await RunContextAsync(db, page.Select(h => h.ApplicationId), cancellationToken);
        var passHeads = page.Where(h => h.CallId is null).ToList();
        var passApps = passHeads.Select(h => h.ApplicationId!.Value).Distinct().ToList();
        var passTimes = passHeads.Select(h => h.At).Distinct().ToList();
        var runRows = passApps.Count == 0
            ? []
            : await db.VerificationAnalyzerRuns.AsNoTracking()
                .Where(r => passApps.Contains(r.ApplicationId) && passTimes.Contains(r.CompletedAt))
                .ToListAsync(cancellationToken);
        var runIds = runRows.Select(r => r.Id).ToList();
        var signals = runIds.Count == 0
            ? []
            : await db.VerificationSignals.AsNoTracking()
                .Where(s => runIds.Contains(s.AnalyzerRunId))
                .Select(s => new { s.AnalyzerRunId, s.Code, s.Id })
                .ToListAsync(cancellationToken);
        var findings = signals
            .GroupBy(s => s.AnalyzerRunId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)[.. g.OrderBy(s => s.Id, Comparer<Guid>.Create(VerificationActivity.CompareIds)).Select(s => SignalCatalogue.TitleOf(s.Code)).Distinct()]);

        // The calls that belong to the page's passes, and the calls on their own. Only calls about an application the reader can
        // open reach a pass, since a pass is one of those applications.
        var latestPass = passTimes.Count == 0 ? DateTimeOffset.MinValue : passTimes.Max();
        var loneIds = page.Where(h => h.CallId is not null).Select(h => h.CallId!.Value).ToList();
        var calls = await db.AiUsageCalls.AsNoTracking()
            .Where(c => loneIds.Contains(c.Id) || (c.ApplicationId != null && passApps.Contains(c.ApplicationId.Value) && c.OccurredAt <= latestPass))
            .ToListAsync(cancellationToken);

        VerificationActivityCheck CallItem(AiUsageCall c) => new(
            c.Id, VerificationActivityKind.ModelCall, c.OccurredAt, VerificationActivity.GroupOfCall(c.CheckName), c.CheckName,
            VerificationActivity.OutcomeOfCall(c.Outcome), null, [],
            usageVisible
                ? new VerificationActivityCall(c.KeySource, c.Provider, c.Model, c.ProviderRequestId, c.InputTokens, c.OutputTokens,
                    AiCharge.Billed(c.Cost, c.KeySource, margin), c.DurationMs, c.ErrorKind)
                : null);

        var result = new List<VerificationActivityRun>(page.Count);
        foreach (var head in page)
        {
            if (head.CallId is { } callId)
            {
                var lone = calls.Single(c => c.Id == callId);
                var item = CallItem(lone);
                result.Add(new VerificationActivityRun(
                    VerificationActivity.CallRunId(callId), head.At, lone.ApplicationId, context.TitleOf(lone.ApplicationId), item.Outcome,
                    0, 0, 0, null, [item]));
                continue;
            }

            var applicationId = head.ApplicationId!.Value;
            var checks = runRows.Where(r => r.ApplicationId == applicationId && r.CompletedAt == head.At)
                .Select(r => (Run: r, Outcome: VerificationActivity.OutcomeOfRun(r.Analyzer, r.SignalCount, r.Note)))
                .ToList();
            var passes = context.PassesOf(applicationId);
            var items = checks
                .Select(c => new VerificationActivityCheck(
                    c.Run.Id, VerificationActivityKind.CheckRun, c.Run.CompletedAt, CheckGroups.Of(c.Run.Analyzer), c.Run.Analyzer, c.Outcome,
                    c.Run.Note is null ? null : RunNotes.Describe(c.Run.Analyzer, c.Run.Note),
                    findings.GetValueOrDefault(c.Run.Id) ?? [], null))
                .Concat(calls
                    .Where(c => c.ApplicationId == applicationId && VerificationActivity.PassOfCall(passes, c.OccurredAt) == head.At)
                    .Select(CallItem))
                // In the groups' order, a model call after the check it served, and the checks of a group by name.
                .OrderBy(i => i.Group is { } itemGroup ? (int)itemGroup : int.MaxValue)
                .ThenBy(i => i.Kind)
                .ThenBy(i => i.At)
                .ThenBy(i => i.Check, StringComparer.Ordinal)
                .ToList();
            var found = checks.Count(c => c.Outcome == VerificationActivityOutcome.Found);
            var notRun = checks.Count(c => c.Outcome == VerificationActivityOutcome.NotRun);
            result.Add(new VerificationActivityRun(
                VerificationActivity.RunId(head.At, applicationId), head.At, applicationId, context.TitleOf(applicationId),
                VerificationActivity.OutcomeOfPass(found, checks.Count - notRun), checks.Count, found, notRun,
                context.RulesetAt(applicationId, head.At), items));
        }

        return result;
    }

    /// <summary>One check run or model call, a line of the export.</summary>
    private sealed record Row(
        Guid Id, VerificationActivityKind Kind, DateTimeOffset At, CheckGroup? Group, string Check, VerificationActivityOutcome Outcome,
        string? Note, Guid? ApplicationId, AiUsageCall? Call);

    private static async Task<List<Row>> RunsAsync(
        ScreenHireDbContext db, DateTimeOffset? from, DateTimeOffset? to, CheckGroup? group, AiKeySource? keySource,
        VerificationActivityOutcome? outcome, VerificationActivity.Cursor? cursor, int take, CancellationToken cancellationToken)
    {
        // A key belongs to a model call, and a failed call is not a run's outcome.
        if (keySource is not null || outcome == VerificationActivityOutcome.Failed)
        {
            return [];
        }

        var query = db.VerificationAnalyzerRuns.AsNoTracking().AsQueryable();
        if (from is { } start)
        {
            query = query.Where(r => r.CompletedAt >= start);
        }

        if (to is { } end)
        {
            query = query.Where(r => r.CompletedAt < end);
        }

        if (group is { } wanted)
        {
            var analyzers = VerificationActivity.AnalyzersOf(wanted);
            query = query.Where(r => analyzers.Contains(r.Analyzer));
        }

        var trusted = VerificationActivity.TrustedNotes;
        query = outcome switch
        {
            VerificationActivityOutcome.Found => query.Where(r => r.SignalCount > 0),
            VerificationActivityOutcome.NotRun => query.Where(r => r.SignalCount == 0 && r.Note != null && !trusted.Contains(r.Note)),
            VerificationActivityOutcome.Completed => query.Where(r => r.SignalCount == 0 && (r.Note == null || trusted.Contains(r.Note))),
            _ => query,
        };
        if (cursor is { } after)
        {
            query = query.Where(r => r.CompletedAt < after.At || (r.CompletedAt == after.At && r.Id.CompareTo(after.Id) < 0));
        }

        var rows = await query.OrderByDescending(r => r.CompletedAt).ThenByDescending(r => r.Id).Take(take).ToListAsync(cancellationToken);
        return
        [
            .. rows.Select(r => new Row(
                r.Id, VerificationActivityKind.CheckRun, r.CompletedAt, CheckGroups.Of(r.Analyzer), r.Analyzer,
                VerificationActivity.OutcomeOfRun(r.Analyzer, r.SignalCount, r.Note), r.Note, r.ApplicationId, null)),
        ];
    }

    private static async Task<List<Row>> CallsAsync(
        ScreenHireDbContext db, bool usageVisible, DateTimeOffset? from, DateTimeOffset? to, CheckGroup? group, AiKeySource? keySource,
        VerificationActivityOutcome? outcome, VerificationActivity.Cursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = CallQuery(db, usageVisible, from, to, group, keySource, outcome);
        if (query is null)
        {
            return [];
        }

        if (cursor is { } after)
        {
            query = query.Where(c => c.OccurredAt < after.At || (c.OccurredAt == after.At && c.Id.CompareTo(after.Id) < 0));
        }

        var rows = await query.OrderByDescending(c => c.OccurredAt).ThenByDescending(c => c.Id).Take(take).ToListAsync(cancellationToken);
        return
        [
            .. rows.Select(c => new Row(
                c.Id, VerificationActivityKind.ModelCall, c.OccurredAt, VerificationActivity.GroupOfCall(c.CheckName), c.CheckName,
                VerificationActivity.OutcomeOfCall(c.Outcome), null, c.ApplicationId, c)),
        ];
    }
}
