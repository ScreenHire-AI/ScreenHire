using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Endpoints;

/// <summary>Unauthenticated endpoints that back tenant careers pages.</summary>
internal static class PublicJobEndpoints
{
    public static IEndpointRouteBuilder MapPublicJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/public/organizations/{organizationSlug}").WithTags("Public careers");

        group.MapGet("/jobs", ListOpenJobs).WithName("ListPublicJobs");
        group.MapGet("/jobs/{jobSlug}", GetOpenJob).WithName("GetPublicJob");

        return app;
    }

    private static async Task<Results<Ok<PublicJobBoard>, NotFound>> ListOpenJobs(
        string organizationSlug, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking()
            .SingleOrDefaultAsync(o => o.Slug == organizationSlug, cancellationToken);
        if (organization is null)
        {
            return TypedResults.NotFound();
        }

        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => j.OrganizationId == organization.Id && j.Status == JobStatus.Open)
            .OrderByDescending(j => j.PublishedAt)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PublicJobBoard(organization.Name, organization.Slug, [.. jobs.Select(j => j.ToPublicJob())]));
    }

    // Closed or unknown jobs return 404 here; careers pages turn a closed job into 410 Gone (teardown, ADR-0003).
    private static async Task<Results<Ok<PublicJob>, NotFound>> GetOpenJob(
        string organizationSlug, string jobSlug, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking()
            .Where(j => j.Slug == jobSlug && j.Status == JobStatus.Open)
            .Where(j => db.Organizations.Any(o => o.Id == j.OrganizationId && o.Slug == organizationSlug))
            .SingleOrDefaultAsync(cancellationToken);

        return job is null ? TypedResults.NotFound() : TypedResults.Ok(job.ToPublicJob());
    }
}
