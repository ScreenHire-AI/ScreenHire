using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Unauthenticated endpoints that back tenant careers pages. Each resolves the organization from the slug in the
/// route and makes it the tenant for the request, so the tenant filter and row-level security scope every job query to
/// it, whoever is signed in.
/// </summary>
internal static class PublicJobEndpoints
{
    public static IEndpointRouteBuilder MapPublicJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/public/organizations/{organizationSlug}").WithTags("Public careers").AllowAnonymous();

        group.MapGet("/jobs", ListOpenJobs).WithName("ListPublicJobs");
        group.MapGet("/jobs/{jobSlug}", GetOpenJob).WithName("GetPublicJob");

        return app;
    }

    private static async Task<Results<Ok<PublicJobBoard>, NotFound>> ListOpenJobs(
        string organizationSlug, ScreenHireDbContext db, TenantContext tenant, CancellationToken cancellationToken)
    {
        var organization = await FindOrganizationAsync(organizationSlug, db, tenant, cancellationToken);
        if (organization is null)
        {
            return TypedResults.NotFound();
        }

        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => j.Status == JobStatus.Open)
            .OrderByDescending(j => j.PublishedAt)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PublicJobBoard(organization.Name, organization.Slug, [.. jobs.Select(j => j.ToPublicJob())]));
    }

    // Closed or unknown jobs return 404 here; careers pages turn a closed job into 410 Gone (teardown, ADR-0003).
    private static async Task<Results<Ok<PublicJob>, NotFound>> GetOpenJob(
        string organizationSlug, string jobSlug, ScreenHireDbContext db, TenantContext tenant, CancellationToken cancellationToken)
    {
        if (await FindOrganizationAsync(organizationSlug, db, tenant, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        var job = await db.Jobs.AsNoTracking()
            .SingleOrDefaultAsync(j => j.Slug == jobSlug && j.Status == JobStatus.Open, cancellationToken);

        return job is null ? TypedResults.NotFound() : TypedResults.Ok(job.ToPublicJob());
    }

    private static async Task<Organization?> FindOrganizationAsync(
        string slug, ScreenHireDbContext db, TenantContext tenant, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Slug == slug, cancellationToken);
        if (organization is not null)
        {
            tenant.SetOrganization(organization.Id);
        }

        return organization;
    }
}
