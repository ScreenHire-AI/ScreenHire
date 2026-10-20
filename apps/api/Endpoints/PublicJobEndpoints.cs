using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Configuration;
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
        group.MapGet("/jobs/{jobSlug}", GetOpenJob).WithName("GetPublicJob").Produces<PublicJob>(StatusCodes.Status410Gone);

        return app;
    }

    private static async Task<Results<Ok<PublicJobBoard>, NotFound>> ListOpenJobs(
        string organizationSlug, ScreenHireDbContext db, TenantContext tenant, IOptions<DomainOptions> domains, CancellationToken cancellationToken)
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

        return TypedResults.Ok(new PublicJobBoard(
            organization.Name, organization.Slug, [.. jobs.Select(j => j.ToPublicJob(domains.Value.CareersJobUrl(organization.Slug, j.Slug)))],
            domains.Value.CareersBoardUrl(organization.Slug)));
    }

    // An open job is 200. A closed job that was once published is 410 Gone with its last content and validThrough set to
    // when it closed, so the careers page can serve 410 with expired JobPosting JSON-LD (teardown, job-distribution.md).
    // A draft, a job closed before it was ever published, or an unknown slug is 404: nothing public ever existed.
    private static async Task<Results<Ok<PublicJob>, JsonHttpResult<PublicJob>, NotFound>> GetOpenJob(
        string organizationSlug, string jobSlug, ScreenHireDbContext db, TenantContext tenant, IOptions<DomainOptions> domains,
        CancellationToken cancellationToken)
    {
        if (await FindOrganizationAsync(organizationSlug, db, tenant, cancellationToken) is not { } organization)
        {
            return TypedResults.NotFound();
        }

        var job = await db.Jobs.AsNoTracking()
            .SingleOrDefaultAsync(j => j.Slug == jobSlug && j.Status != JobStatus.Draft && j.PublishedAt != null, cancellationToken);

        return job switch
        {
            null => TypedResults.NotFound(),
            { Status: JobStatus.Closed } => TypedResults.Json(
                job.ToPublicJob(domains.Value.CareersJobUrl(organization.Slug, job.Slug)), statusCode: StatusCodes.Status410Gone),
            _ => TypedResults.Ok(job.ToPublicJob(domains.Value.CareersJobUrl(organization.Slug, job.Slug))),
        };
    }

    private static Task<Organization?> FindOrganizationAsync(
        string slug, ScreenHireDbContext db, TenantContext tenant, CancellationToken cancellationToken) =>
        PublicOrganizations.FindAsync(slug, db, tenant, cancellationToken);
}
