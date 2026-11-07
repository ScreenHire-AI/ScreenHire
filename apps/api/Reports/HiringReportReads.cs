using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Reports;

/// <summary>
/// The Reports page's numbers, computed in SQL (docs/architecture/reports.md). The queries are raw SQL because they read
/// each job's stage kinds out of its workflow JSON, which LINQ cannot reach through the column's converter, and take a
/// median. EF's query filters do not apply to raw SQL, so row-level security is the only scope: the tenant policy and
/// the restrictive <c>job_scope</c> policies on <c>jobs</c>, <c>applications</c> and <c>application_events</c>, which
/// <c>TenantSessionInterceptor</c> sets up for every command. The queries therefore read those three tables only, never
/// <c>candidates</c>, whose policy is tenant-only (ADR-0015), and no verification table.
/// </summary>
/// <remarks>
/// The queries share CTE text (<see cref="Scope"/>, <see cref="CohortCte"/>, <see cref="FurthestCte"/>,
/// <see cref="HiresCte"/>) written with named placeholders only. Every value reaches Postgres as an
/// <see cref="NpgsqlParameter"/> (<see cref="Parameters"/>), never concatenated into the text.
/// </remarks>
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

    // Positions in FunnelKinds, 1-based as array_position counts.
    private static readonly int HiredPosition = Position(StageKind.Hired);
    private static readonly int InterviewPosition = Position(StageKind.Interview);
    private static readonly int OfferPosition = Position(StageKind.Offer);

    // scoped_jobs: the jobs in scope (the filter, or all the person can see). stage_kinds: each stage of each of them with its
    // funnel position (null for a kind with no row).
    private const string Scope = """
        WITH scoped_jobs AS (
            SELECT id, title, status, published_at, workflow FROM jobs
            WHERE cardinality(@jobs) = 0 OR id = ANY(@jobs)
        ),
        stage_kinds AS (
            SELECT j.id AS job_id, s.value->>'id' AS stage_id, array_position(@kinds, s.value->>'kind') AS pos
            FROM scoped_jobs j CROSS JOIN LATERAL jsonb_array_elements(j.workflow->'stages') AS s(value)
        )
        """;

    // cohort: applications submitted in the range; hired while their current stage is a Hired one.
    private const string CohortCte = """
        ,
        cohort AS (
            SELECT a.id, a.job_id, a.submitted_at, a.source, a.source_site, a.source_board, coalesce(sk.pos = @hired, false) AS hired
            FROM applications a
            JOIN scoped_jobs j ON j.id = a.job_id
            LEFT JOIN stage_kinds sk ON sk.job_id = a.job_id AND sk.stage_id = a.current_stage_id
            WHERE a.submitted_at >= @from AND a.submitted_at < @to
        )
        """;

    // furthest: the furthest row an application reached, from the stages it entered (its Submitted and StageChanged
    // entries). Hired counts only while it is still hired, so a move back out of Hired counts at the next furthest.
    private const string FurthestCte = """
        ,
        furthest AS (
            SELECT c.id, c.job_id,
                CASE WHEN c.hired THEN @hired ELSE coalesce(max(sk.pos) FILTER (WHERE sk.pos < @hired), 1) END AS pos
            FROM cohort c
            LEFT JOIN application_events e ON e.application_id = c.id AND e.type IN ('Submitted', 'StageChanged')
            LEFT JOIN stage_kinds sk ON sk.job_id = c.job_id AND sk.stage_id = e.data->>'toStageId'
            GROUP BY c.id, c.job_id, c.hired
        )
        """;

    // hires: applications now in a Hired stage that they entered (stage_entered_at) inside the range, whenever they applied.
    private const string HiresCte = """
        ,
        hires AS (
            SELECT a.id, a.job_id, a.stage_entered_at AS hired_at,
                (extract(epoch FROM a.stage_entered_at - a.submitted_at) / 86400)::double precision AS days
            FROM applications a
            JOIN stage_kinds sk ON sk.job_id = a.job_id AND sk.stage_id = a.current_stage_id AND sk.pos = @hired
            WHERE a.stage_entered_at >= @from AND a.stage_entered_at < @to
        )
        """;

    /// <summary>Whether Postgres knows <paramref name="timeZone"/> as an IANA name.</summary>
    public static async Task<bool> TimeZoneExistsAsync(ScreenHireDbContext db, string timeZone, CancellationToken cancellationToken) =>
        (await db.Database
            .SqlQueryRaw<bool>(
                "SELECT EXISTS (SELECT 1 FROM pg_timezone_names WHERE name = @tz) AS \"Value\"",
                new NpgsqlParameter("tz", NpgsqlDbType.Text) { Value = timeZone })
            .ToListAsync(cancellationToken)).Single();

    public static async Task<HiringReport> ReadAsync(
        ScreenHireDbContext db,
        DateTimeOffset from,
        DateTimeOffset to,
        (DateTimeOffset From, DateTimeOffset To)? previous,
        IReadOnlyCollection<Guid> jobIds,
        string timeZone,
        DateTimeOffset now,
        Func<string, string> boardName,
        CancellationToken cancellationToken)
    {
        var jobs = jobIds.Distinct().ToArray();
        NpgsqlParameter[] Args(DateTimeOffset start, DateTimeOffset end) => Parameters(start, end, jobs, timeZone);

        // One snapshot for every panel, so they read the same data. Hired still has two bases (reports.md section 2): the
        // headline and the weeks count hires made in the range, the funnel and the sources applications hired now.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        var headline = await HeadlineAsync(db, Args(from, to), cancellationToken);
        var before = previous is { } window ? await HeadlineAsync(db, Args(window.From, window.To), cancellationToken) : null;

        var weeks = await db.Database.SqlQueryRaw<WeekRow>(
                Scope + CohortCte + HiresCte + """
                ,
                weeks AS (
                    SELECT generate_series(
                        date_trunc('week', (@from AT TIME ZONE @tz)),
                        date_trunc('week', ((@to - interval '1 microsecond') AT TIME ZONE @tz)),
                        interval '1 week') AS week
                )
                SELECT w.week::date AS week_start,
                    (SELECT count(*) FROM cohort c WHERE date_trunc('week', (c.submitted_at AT TIME ZONE @tz)) = w.week)::int AS applications,
                    (SELECT count(*) FROM hires h WHERE date_trunc('week', (h.hired_at AT TIME ZONE @tz)) = w.week)::int AS hires
                FROM weeks w
                ORDER BY w.week
                """,
                [.. Args(from, to)])
            .ToListAsync(cancellationToken);

        var funnel = await db.Database.SqlQueryRaw<FunnelRow>(
                Scope + CohortCte + FurthestCte + """
                ,
                shown AS (
                    SELECT pos FROM stage_kinds WHERE pos IS NOT NULL AND job_id IN (SELECT job_id FROM cohort)
                    UNION SELECT 1
                    UNION SELECT @hired
                )
                SELECT s.pos,
                    count(*) FILTER (WHERE f.pos >= s.pos)::int AS applications,
                    count(*) FILTER (WHERE f.pos > s.pos)::int AS moved_on
                FROM shown s CROSS JOIN furthest f
                GROUP BY s.pos
                ORDER BY s.pos
                """,
                [.. Args(from, to)])
            .ToListAsync(cancellationToken);

        // Per job, over the hires in the range.
        var hiresByJob = await db.Database.SqlQueryRaw<JobHireRow>(
                Scope + HiresCte + """

                SELECT h.job_id, j.title AS job_title, count(*)::int AS hires,
                    percentile_cont(0.5) WITHIN GROUP (ORDER BY h.days) AS median_days
                FROM hires h JOIN scoped_jobs j ON j.id = h.job_id
                GROUP BY h.job_id, j.title
                """,
                [.. Args(from, to)])
            .ToListAsync(cancellationToken);

        // Days in each stage kind: for each hire, the time between entering a stage and entering the next (the last stay
        // ends when the hire entered its Hired stage), summed per kind, then the median over the hires. Stays in a kind
        // with no row (Verification, Custom) separate the stays around them but are not counted (ADR-0004). So does a stay
        // in a stage removed from the workflow since, which is why the join is a left join: the entry must stay in the
        // window, or the stay before it would run on through the removed stage.
        var stages = await db.Database.SqlQueryRaw<StageDaysRow>(
                Scope + HiresCte + """
                ,
                stays AS (
                    SELECT e.application_id, sk.pos,
                        lead(e.occurred_at, 1, a.stage_entered_at) OVER (PARTITION BY e.application_id ORDER BY e.occurred_at, e.id) - e.occurred_at AS spent
                    FROM hires h
                    JOIN applications a ON a.id = h.id
                    JOIN application_events e ON e.application_id = h.id AND e.type IN ('Submitted', 'StageChanged')
                    LEFT JOIN stage_kinds sk ON sk.job_id = h.job_id AND sk.stage_id = e.data->>'toStageId'
                ),
                per_application AS (
                    SELECT application_id, pos, (sum(extract(epoch FROM spent)) / 86400)::double precision AS days
                    FROM stays
                    WHERE pos IS NOT NULL AND pos < @hired
                    GROUP BY application_id, pos
                )
                SELECT pos, count(*)::int AS hires, percentile_cont(0.5) WITHIN GROUP (ORDER BY days) AS median_days
                FROM per_application
                GROUP BY pos
                ORDER BY pos
                """,
                [.. Args(from, to)])
            .ToListAsync(cancellationToken);

        // One key per source: a recruiter's, then the organization's own site, then a job board's apply link, else the
        // careers page.
        var sources = await db.Database.SqlQueryRaw<SourceRow>(
                Scope + CohortCte + """

                SELECT CASE
                        WHEN source = 'Recruiter' THEN 'recruiter'
                        WHEN source_site IS NOT NULL THEN 'website'
                        WHEN source_board IS NOT NULL THEN 'board:' || source_board
                        ELSE 'careers' END AS key,
                    count(*)::int AS applications, count(*) FILTER (WHERE hired)::int AS hires
                FROM cohort
                GROUP BY 1
                """,
                [.. Args(from, to)])
            .ToListAsync(cancellationToken);

        var openJobs = await db.Database.SqlQueryRaw<OpenJobRow>(
                Scope + CohortCte + HiresCte + """

                SELECT j.id AS job_id, j.title, j.published_at,
                    (SELECT count(*) FROM cohort c WHERE c.job_id = j.id)::int AS applications,
                    (SELECT count(*) FROM applications a
                        JOIN stage_kinds sk ON sk.job_id = a.job_id AND sk.stage_id = a.current_stage_id AND sk.pos = @interview
                        WHERE a.job_id = j.id)::int AS in_interview,
                    (SELECT count(*) FROM hires h WHERE h.job_id = j.id)::int AS hires
                FROM scoped_jobs j
                WHERE j.status = 'Open'
                ORDER BY j.title, j.id
                """,
                [.. Args(from, to)])
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new HiringReport(
            from,
            to,
            headline,
            before,
            [.. weeks.Select(w => new ReportWeek(w.WeekStart, w.Applications, w.Hires))],
            [.. funnel.Select(row => new FunnelStage(FunnelKinds[row.Pos - 1], row.Applications, row.Pos == HiredPosition ? null : row.MovedOn))],
            new TimeToHire(
                [.. stages.Select(s => new StageDays(FunnelKinds[s.Pos - 1], s.MedianDays, s.Hires))],
                [
                    .. hiresByJob
                        .OrderBy(h => h.JobTitle, StringComparer.CurrentCulture)
                        .ThenBy(h => h.JobId)
                        .Select(h => new JobTimeToHire(h.JobId, h.JobTitle, h.Hires, h.MedianDays)),
                ]),
            [
                .. sources
                    .Select(s => new SourceCount(s.Key, SourceName(s.Key, boardName), s.Applications, s.Hires))
                    .OrderByDescending(s => s.Applications)
                    .ThenBy(s => s.Name, StringComparer.CurrentCulture),
            ],
            [
                .. openJobs.Select(j => new ReportOpenJob(
                    j.JobId, j.Title, j.PublishedAt, j.Applications, j.InInterview, DaysOpen(j.PublishedAt, now), j.Hires)),
            ]);
    }

    /// <summary>The ids of the applications submitted in the range that reached <paramref name="kind"/> or a later one.</summary>
    public static async Task<IReadOnlyList<Guid>> ReachedAsync(
        ScreenHireDbContext db,
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyCollection<Guid> jobIds,
        StageKind kind,
        CancellationToken cancellationToken)
    {
        var parameters = Parameters(from, to, jobIds.Distinct().ToArray(), "UTC");
        return await db.Database.SqlQueryRaw<Guid>(
                Scope + CohortCte + FurthestCte + """

                SELECT id AS "Value" FROM furthest WHERE pos >= @pos ORDER BY id
                """,
                [.. parameters, new NpgsqlParameter("pos", NpgsqlDbType.Integer) { Value = Position(kind) }])
            .ToListAsync(cancellationToken);
    }

    // The headline of one range: the cohort's counts at the Interview and Offer rows, and the hires made in it.
    private static async Task<ReportHeadline> HeadlineAsync(ScreenHireDbContext db, NpgsqlParameter[] args, CancellationToken cancellationToken)
    {
        var row = await db.Database.SqlQueryRaw<HeadlineRow>(
                Scope + CohortCte + FurthestCte + HiresCte + """

                SELECT (SELECT count(*) FROM furthest)::int AS applications,
                    (SELECT count(*) FROM furthest WHERE pos >= @interview)::int AS reached_interview,
                    (SELECT count(*) FROM furthest WHERE pos >= @offer)::int AS offers,
                    (SELECT count(*) FROM hires)::int AS hires,
                    (SELECT percentile_cont(0.5) WITHIN GROUP (ORDER BY days) FROM hires) AS median_days
                """,
                [.. args])
            .ToListAsync(cancellationToken);
        var only = row.Single();
        return new ReportHeadline(only.Applications, only.ReachedInterview, only.Offers, only.Hires, only.MedianDays);
    }

    // Fresh parameter objects for every command: a DbParameter belongs to one command.
    private static NpgsqlParameter[] Parameters(DateTimeOffset from, DateTimeOffset to, Guid[] jobs, string timeZone) =>
    [
        new("jobs", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = jobs },
        new("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = FunnelKinds.Select(k => k.ToString()).ToArray() },
        new("from", NpgsqlDbType.TimestampTz) { Value = from },
        new("to", NpgsqlDbType.TimestampTz) { Value = to },
        new("hired", NpgsqlDbType.Integer) { Value = HiredPosition },
        new("interview", NpgsqlDbType.Integer) { Value = InterviewPosition },
        new("offer", NpgsqlDbType.Integer) { Value = OfferPosition },
        new("tz", NpgsqlDbType.Text) { Value = timeZone },
    ];

    private static int Position(StageKind kind) => FunnelKinds.ToList().IndexOf(kind) + 1;

    private static string SourceName(string key, Func<string, string> boardName) => key switch
    {
        "recruiter" => "Added by a recruiter",
        "website" => "Your website",
        "careers" => "Careers page",
        _ when key.StartsWith("board:", StringComparison.Ordinal) => boardName(key["board:".Length..]),
        _ => key,
    };

    // Whole days since the job was published; a job with no publish date (not expected for an open one) counts as 0.
    private static int DaysOpen(DateTimeOffset? publishedAt, DateTimeOffset now) =>
        publishedAt is { } at ? Math.Max(0, (int)Math.Floor((now - at).TotalDays)) : 0;

    private sealed record HeadlineRow(int Applications, int ReachedInterview, int Offers, int Hires, double? MedianDays);

    private sealed record WeekRow(DateOnly WeekStart, int Applications, int Hires);

    /// <summary>A funnel row: its 1-based position in <see cref="FunnelKinds"/> and its counts.</summary>
    private sealed record FunnelRow(int Pos, int Applications, int MovedOn);

    private sealed record JobHireRow(Guid JobId, string JobTitle, int Hires, double MedianDays);

    private sealed record StageDaysRow(int Pos, int Hires, double MedianDays);

    private sealed record SourceRow(string Key, int Applications, int Hires);

    private sealed record OpenJobRow(Guid JobId, string Title, DateTimeOffset? PublishedAt, int Applications, int InInterview, int Hires);
}
