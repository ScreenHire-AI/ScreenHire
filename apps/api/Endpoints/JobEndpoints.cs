using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Recruiter-dashboard endpoints. They need a signed-in user, and the tenant query filter (backed by row-level
/// security) scopes every query to that user's organization, so no handler filters by organization itself.
/// </summary>
internal static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/jobs").WithTags("Jobs").RequireAuthorization();

        group.MapGet("/", ListJobs).WithName("ListJobs");
        group.MapGet("/{jobId:guid}", GetJob).WithName("GetJob");

        return app;
    }

    private static async Task<List<JobSummary>> ListJobs(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var jobs = await db.Jobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. jobs.Select(j => j.ToSummary())];
    }

    // Another organization's job is a 404, exactly like a job that does not exist.
    private static async Task<Results<Ok<JobSummary>, NotFound>> GetJob(Guid jobId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        return job is null ? TypedResults.NotFound() : TypedResults.Ok(job.ToSummary());
    }
}
