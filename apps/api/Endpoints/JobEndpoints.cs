using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Jobs;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

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

        // 401 comes from the authentication middleware, so the handlers' signatures cannot declare it.
        group.MapGet("/", ListJobs).WithName("ListJobs").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{jobId:guid}", GetJob).WithName("GetJob").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{jobId:guid}/publish", PublishJob).WithName("PublishJob")
            .RequireAntiforgeryToken()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

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

    // The tenant filter and row-level security scope the lookup to the signed-in organization, so another
    // organization's job is a 404 like a missing one. The job change and JobPublished commit in one transaction (the
    // EF Core outbox), and the worker handles the event.
    private static async Task<Results<Ok<JobSummary>, NotFound, ProblemHttpResult>> PublishJob(
        Guid jobId, IDbContextOutbox<ScreenHireDbContext> outbox, TimeProvider time, CancellationToken cancellationToken)
    {
        var job = await outbox.DbContext.Jobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        switch (job.Status)
        {
            case JobStatus.Closed:
                return TypedResults.Problem("A closed job cannot be republished; create a new job instead.", statusCode: StatusCodes.Status409Conflict);
            case JobStatus.Open:
                // Already live: publishing again changes nothing and raises no second event.
                return TypedResults.Ok(job.ToSummary());
        }

        job.Publish(time.GetUtcNow());
        await outbox.PublishAsync(new JobPublished(job.OrganizationId, job.Id, job.PublishedAt!.Value));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

        return TypedResults.Ok(job.ToSummary());
    }
}
