using Microsoft.AspNetCore.Http.HttpResults;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Reports;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The dashboard's Reports page (docs/architecture/reports.md). Session only: the hiring API groups are the ones API keys
/// reach (ADR-0011). Every member may read it, because seeing jobs and candidates is in every role (ADR-0014); job access
/// narrows the numbers to the jobs the person sees, through row-level security (ADR-0015).
/// </summary>
internal static class ReportEndpoints
{
    // The most jobs one report filters by; more ids than any small team has jobs.
    private const int MaxJobs = 200;

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/reports").WithTags("Reports").RequireAuthorization();
        group.MapGet("/hiring", GetHiringReport).WithName("GetHiringReport").Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // The browser turns "last 30 days" or a custom range into the person's own midnights, so the API never guesses a time
    // zone: from inclusive, to exclusive. A job id the person cannot see, or that does not exist, adds nothing.
    private static async Task<Results<Ok<HiringReport>, ValidationProblem>> GetHiringReport(
        ScreenHireDbContext db,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        Guid[]? jobId = null)
    {
        if (Problems(from, to, jobId) is { Count: > 0 } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        return TypedResults.Ok(await HiringReportReads.ReadAsync(
            db, from!.Value.ToUniversalTime(), to!.Value.ToUniversalTime(), jobId ?? [], cancellationToken));
    }

    /// <summary>What is wrong with a report's range and job filter, by parameter; empty when nothing is.</summary>
    internal static Dictionary<string, string[]> Problems(DateTimeOffset? from, DateTimeOffset? to, Guid[]? jobId)
    {
        var problems = new Dictionary<string, string[]>();
        if (from is null)
        {
            problems["from"] = ["Say when the report starts."];
        }

        if (to is null)
        {
            problems["to"] = ["Say when the report ends."];
        }

        if (from is { } start && to is { } end)
        {
            if (start >= end)
            {
                problems["to"] = ["The report has to end after it starts."];
            }
            else if (end - start > HiringReportReads.MaxRange)
            {
                problems["to"] = ["A report covers 5 years at most."];
            }
        }

        if (jobId is { Length: > MaxJobs })
        {
            problems["jobId"] = [$"A report filters by {MaxJobs} jobs at most."];
        }

        return problems;
    }
}
