using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Verification;
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

        // The groups whose checks counted a signal towards a level, one count per application.
        var countedIds = current.SelectMany(a => a.CountedSignalIds).ToHashSet();
        var groups = new List<VerificationGroupCount>();
        if (countedIds.Count > 0)
        {
            var signals = await (
                    from signal in db.VerificationSignals.AsNoTracking()
                    where signal.Assessment == VerificationAssessmentKind.Authenticity
                    join application in applications on signal.ApplicationId equals application.Id
                    select signal)
                .ToListAsync(cancellationToken);
            groups =
            [
                .. signals.Where(s => countedIds.Contains(s.Id))
                    .GroupBy(s => CheckGroups.OfSignal(s))
                    .Select(g => new VerificationGroupCount(g.Key, CheckGroups.Label(g.Key), g.Select(s => s.ApplicationId).Distinct().Count()))
                    .OrderByDescending(g => g.Applications).ThenBy(g => g.Group),
            ];
        }

        return TypedResults.Ok(new VerificationOverviewView(start, now, current.Count, Math.Max(0, applicationCount - current.Count), levels, groups));
    }

    public static async Task<Results<Ok<VerificationActivityPage>, ValidationProblem>> Activity(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken,
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
        var runs = await RunsAsync(db, from, to, group, keySource, outcome, cursor, take + 1, cancellationToken);
        var calls = await CallsAsync(db, usageVisible, from, to, group, keySource, outcome, cursor, take + 1, cancellationToken);
        var (page, more) = VerificationActivity.MergePage(runs, calls, take, e => e.At, e => e.Id);
        var entries = await ToEntriesAsync(db, page, usageVisible, cancellationToken);
        var next = more && page.Count > 0 ? new VerificationActivity.Cursor(page[^1].At, page[^1].Id).Encode() : null;
        return TypedResults.Ok(new VerificationActivityPage(entries, next, usageVisible));
    }

    public static async Task<Results<FileContentHttpResult, ValidationProblem>> Export(
        ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CheckGroup? group = null, AiKeySource? keySource = null,
        VerificationActivityOutcome? outcome = null)
    {
        var problems = AiUsageHandlers.RangeProblems(from, to);
        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var usageVisible = await CanSeeUsageAsync(db, tenant, cancellationToken);
        var runs = await RunsAsync(db, from, to, group, keySource, outcome, null, MaxExportRows + 1, cancellationToken);
        var calls = await CallsAsync(db, usageVisible, from, to, group, keySource, outcome, null, MaxExportRows + 1, cancellationToken);
        var (page, more) = VerificationActivity.MergePage(runs, calls, MaxExportRows, e => e.At, e => e.Id);
        var entries = await ToEntriesAsync(db, page, usageVisible, cancellationToken);

        var csv = new AiUsageCsv();
        csv.Row(
            "id", "at", "kind", "group", "check", "outcome", "application_id", "job", "note", "provider", "model", "key_source",
            "provider_request_id", "input_tokens", "output_tokens", "cost_usd", "duration_ms", "error_kind");
        foreach (var entry in entries)
        {
            csv.Row(
                entry.Id, entry.At, entry.Kind, entry.Group, entry.Check, entry.Outcome, entry.ApplicationId, entry.JobTitle, entry.Note,
                entry.Call?.Provider, entry.Call?.Model, entry.Call?.KeySource, entry.Call?.ProviderRequestId, entry.Call?.InputTokens,
                entry.Call?.OutputTokens, entry.Call?.Cost, entry.Call?.DurationMs, entry.Call?.ErrorKind);
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

    /// <summary>An entry before it has its job title, with the sort keys the merge reads.</summary>
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
        // Calls belong to the Answers group (or none), and end completed or failed.
        if ((group is { } wantedGroup && wantedGroup != CheckGroup.Answers)
            || outcome is VerificationActivityOutcome.Found or VerificationActivityOutcome.NotRun)
        {
            return [];
        }

        var query = db.AiUsageCalls.AsNoTracking().AsQueryable();
        if (!usageVisible)
        {
            // Calls are the organization's, not a job's. A member who cannot see usage sees only calls about an application they can open.
            query = query.Where(c => c.ApplicationId != null && db.Applications.Any(a => a.Id == c.ApplicationId));
        }

        if (group is not null)
        {
            query = query.Where(c => c.CheckName.StartsWith("consistency"));
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

        query = outcome switch
        {
            VerificationActivityOutcome.Completed => query.Where(c => c.Outcome == AiCallOutcome.Ok),
            VerificationActivityOutcome.Failed => query.Where(c => c.Outcome != AiCallOutcome.Ok),
            _ => query,
        };
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

    private static async Task<List<VerificationActivityEntry>> ToEntriesAsync(
        ScreenHireDbContext db, IReadOnlyList<Row> rows, bool usageVisible, CancellationToken cancellationToken)
    {
        var ids = rows.Where(r => r.ApplicationId is not null).Select(r => r.ApplicationId!.Value).Distinct().ToList();
        var titles = ids.Count == 0
            ? []
            : await db.Applications.AsNoTracking().Where(a => ids.Contains(a.Id))
                .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { a.Id, j.Title })
                .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken);
        return
        [
            .. rows.Select(r => new VerificationActivityEntry(
                r.Id, r.Kind, r.At, r.Group, r.Check, r.Outcome, r.Note, r.ApplicationId,
                r.ApplicationId is { } id && titles.TryGetValue(id, out var title) ? title : null,
                usageVisible && r.Call is { } c
                    ? new VerificationActivityCall(c.KeySource, c.Provider, c.Model, c.ProviderRequestId, c.InputTokens, c.OutputTokens, c.Cost, c.DurationMs, c.ErrorKind)
                    : null)),
        ];
    }
}
