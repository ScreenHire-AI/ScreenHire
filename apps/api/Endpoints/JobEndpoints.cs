using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Jobs;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>Recruiter-dashboard endpoints.</summary>
internal static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        // Authentication and tenant resolution are added by the auth and multi-tenancy work items.
        // Until then this group must not be deployed: ListJobs serves the seeded demo organization only, and
        // PublishJob changes any organization's job by id.
        var group = app.MapGroup("/v1/jobs").WithTags("Jobs");

        group.MapGet("/", ListJobs).WithName("ListJobs");
        group.MapPost("/{jobId:guid}/publish", PublishJob).WithName("PublishJob").ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<List<JobSummary>> ListJobs(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => db.Organizations.Any(o => o.Id == j.OrganizationId && o.Slug == DevelopmentDataSeeder.DemoOrganizationSlug))
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. jobs.Select(j => j.ToSummary())];
    }

    // Until tenancy lands this finds the job by id alone; the auth and multi-tenancy work items scope it to the
    // signed-in organization. The job change and JobPublished commit in one transaction (the EF Core outbox), and the
    // worker handles the event.
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
