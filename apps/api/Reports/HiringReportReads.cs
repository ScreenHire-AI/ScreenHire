using System.Data;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Reports;

/// <summary>
/// The Reports page's numbers, computed in SQL (docs/architecture/reports.md). The queries are raw SQL because they read
/// each job's stage kinds out of its workflow JSON, which LINQ cannot reach through the column's converter, and take a
/// median. EF's query filters do not apply to raw SQL, so row-level security is the only scope: the tenant policy and
/// the restrictive <c>job_scope</c> policies on <c>jobs</c>, <c>applications</c> and <c>application_events</c>, which
/// <c>TenantSessionInterceptor</c> sets up for every command. The queries therefore read those three tables only, never
/// <c>candidates</c>, whose policy is tenant-only (ADR-0015).
/// </summary>
internal static class HiringReportReads
{
    /// <summary>
    /// The funnel's rows in order: the stage kinds every pipeline moves through. Verification has no row, because
    /// verification conditions route candidates into it and a count of it would be verification data (ADR-0004); Custom has
    /// no fixed place; Rejected and Withdrawn end an application without moving it on. An application that passed through
    /// one of those counts at the rows it reached around it.
    /// </summary>
    public static IReadOnlyList<StageKind> FunnelKinds { get; } =
        [StageKind.Applied, StageKind.Screening, StageKind.Assessment, StageKind.Interview, StageKind.Offer, StageKind.Hired];

    /// <summary>The longest range a report covers.</summary>
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(1827);

    public static async Task<HiringReport> ReadAsync(
        ScreenHireDbContext db, DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken)
    {
        var kinds = FunnelKinds.Select(k => k.ToString()).ToArray();
        var hired = kinds.Length; // Hired is the last row; positions are 1-based, as array_position counts.
        var jobs = jobIds.Distinct().ToArray();

        // One snapshot for the three panels, so the funnel's Hired row and the sources' hires agree.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        // stage_kinds: each stage of each job in scope, with its funnel position (null for a kind with no row).
        // cohort: applications submitted in the range; hired while their current stage is a Hired one.
        // furthest: the furthest row an application reached, from the stages it entered (its Submitted and StageChanged
        // entries). Hired counts only while it is still hired, so a move back out of Hired counts at the next furthest.
        var funnel = await db.Database.SqlQuery<FunnelRow>(
                $"""
                WITH scoped_jobs AS (
                    SELECT id, workflow FROM jobs
                    WHERE cardinality({jobs}::uuid[]) = 0 OR id = ANY({jobs}::uuid[])
                ),
                stage_kinds AS (
                    SELECT j.id AS job_id, s.value->>'id' AS stage_id, array_position({kinds}::text[], s.value->>'kind') AS pos
                    FROM scoped_jobs j CROSS JOIN LATERAL jsonb_array_elements(j.workflow->'stages') AS s(value)
                ),
                cohort AS (
                    SELECT a.id, a.job_id, coalesce(sk.pos = {hired}, false) AS hired
                    FROM applications a
                    JOIN scoped_jobs j ON j.id = a.job_id
                    LEFT JOIN stage_kinds sk ON sk.job_id = a.job_id AND sk.stage_id = a.current_stage_id
                    WHERE a.submitted_at >= {from} AND a.submitted_at < {to}
                ),
                furthest AS (
                    SELECT c.id,
                        CASE WHEN c.hired THEN {hired} ELSE coalesce(max(sk.pos) FILTER (WHERE sk.pos < {hired}), 1) END AS pos
                    FROM cohort c
                    LEFT JOIN application_events e ON e.application_id = c.id AND e.type IN ('Submitted', 'StageChanged')
                    LEFT JOIN stage_kinds sk ON sk.job_id = c.job_id AND sk.stage_id = e.data->>'toStageId'
                    GROUP BY c.id, c.hired
                ),
                shown AS (
                    SELECT pos FROM stage_kinds WHERE pos IS NOT NULL AND job_id IN (SELECT job_id FROM cohort)
                    UNION SELECT 1
                    UNION SELECT {hired}
                )
                SELECT s.pos,
                    count(*) FILTER (WHERE f.pos >= s.pos)::int AS applications,
                    count(*) FILTER (WHERE f.pos > s.pos)::int AS moved_on
                FROM shown s CROSS JOIN furthest f
                GROUP BY s.pos
                ORDER BY s.pos
                """)
            .ToListAsync(cancellationToken);

        // Hires made in the range: applications now in a Hired stage, which they entered at stage_entered_at. The row with
        // no job is the median over every hire (the empty grouping set).
        var hires = await db.Database.SqlQuery<HireRow>(
                $"""
                WITH hires AS (
                    SELECT a.job_id, (extract(epoch FROM a.stage_entered_at - a.submitted_at) / 86400)::double precision AS days
                    FROM applications a
                    JOIN jobs j ON j.id = a.job_id
                    CROSS JOIN LATERAL jsonb_array_elements(j.workflow->'stages') AS s(value)
                    WHERE (cardinality({jobs}::uuid[]) = 0 OR j.id = ANY({jobs}::uuid[]))
                        AND s.value->>'id' = a.current_stage_id AND s.value->>'kind' = 'Hired'
                        AND a.stage_entered_at >= {from} AND a.stage_entered_at < {to}
                )
                SELECT h.job_id, j.title AS job_title, count(*)::int AS hires,
                    percentile_cont(0.5) WITHIN GROUP (ORDER BY h.days) AS median_days
                FROM hires h JOIN jobs j ON j.id = h.job_id
                GROUP BY GROUPING SETS ((h.job_id, j.title), ())
                """)
            .ToListAsync(cancellationToken);

        var sources = await db.Database.SqlQuery<SourceRow>(
                $"""
                WITH scoped_jobs AS (
                    SELECT id, workflow FROM jobs
                    WHERE cardinality({jobs}::uuid[]) = 0 OR id = ANY({jobs}::uuid[])
                ),
                cohort AS (
                    SELECT a.source,
                        EXISTS (
                            SELECT 1 FROM jsonb_array_elements(j.workflow->'stages') AS s(value)
                            WHERE s.value->>'id' = a.current_stage_id AND s.value->>'kind' = 'Hired') AS hired
                    FROM applications a
                    JOIN scoped_jobs j ON j.id = a.job_id
                    WHERE a.submitted_at >= {from} AND a.submitted_at < {to}
                )
                SELECT source, count(*)::int AS applications, count(*) FILTER (WHERE hired)::int AS hires
                FROM cohort
                GROUP BY source
                """)
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var overall = hires.SingleOrDefault(h => h.JobId is null);
        return new HiringReport(
            from,
            to,
            [.. funnel.Select(row => new FunnelStage(FunnelKinds[row.Pos - 1], row.Applications, row.Pos == hired ? null : row.MovedOn))],
            new TimeToHire(
                overall?.MedianDays,
                overall?.Hires ?? 0,
                [
                    .. hires.Where(h => h.JobId is not null)
                        .OrderBy(h => h.JobTitle, StringComparer.CurrentCulture)
                        .ThenBy(h => h.JobId)
                        .Select(h => new JobTimeToHire(h.JobId!.Value, h.JobTitle!, h.Hires, h.MedianDays ?? 0)),
                ]),
            [
                .. sources
                    .Select(s => new SourceCount(Enum.Parse<ApplicationSource>(s.Source), s.Applications, s.Hires))
                    .OrderByDescending(s => s.Applications)
                    .ThenBy(s => s.Source),
            ]);
    }

    /// <summary>A funnel row: its 1-based position in <see cref="FunnelKinds"/> and its counts.</summary>
    private sealed record FunnelRow(int Pos, int Applications, int MovedOn);

    /// <summary>One job's hires, or every hire when <see cref="JobId"/> is null.</summary>
    private sealed record HireRow(Guid? JobId, string? JobTitle, int Hires, double? MedianDays);

    /// <summary>A source as stored (the enum's name) and its counts.</summary>
    private sealed record SourceRow(string Source, int Applications, int Hires);
}
