using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Applications;
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
/// <remarks>
/// A job that was published and then closed is 410 Gone, so careers pages and search engines drop it for good (ADR-0003);
/// a draft or unknown job is 404.
/// </remarks>
internal static class PublicJobEndpoints
{
    public static IEndpointRouteBuilder MapPublicJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/public/organizations/{organizationSlug}").WithTags("Public careers").AllowAnonymous();

        group.MapGet("/", GetOrganization).WithName("GetPublicOrganization");
        group.MapGet("/jobs", ListOpenJobs).WithName("ListPublicJobs");
        group.MapGet("/jobs/{jobSlug}", GetOpenJob).WithName("GetPublicJob").ProducesProblem(StatusCodes.Status410Gone);
        group.MapGet("/jobs/{jobSlug}/application-form", GetApplicationForm).WithName("GetPublicApplicationForm")
            .ProducesProblem(StatusCodes.Status410Gone);

        return app;
    }

    private static async Task<Results<Ok<PublicOrganization>, NotFound>> GetOrganization(
        string organizationSlug, ScreenHireDbContext db, TenantContext tenant, CancellationToken cancellationToken)
    {
        var organization = await FindOrganizationAsync(organizationSlug, db, tenant, cancellationToken);
        return organization is null ? TypedResults.NotFound() : TypedResults.Ok(organization.ToPublic());
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

        return TypedResults.Ok(new PublicJobBoard(
            organization.Name, organization.Slug, [.. jobs.Select(j => j.ToPublicJob())], organization.ToPublic().Branding));
    }

    private static async Task<Results<Ok<PublicJob>, NotFound, ProblemHttpResult>> GetOpenJob(
        string organizationSlug, string jobSlug, ScreenHireDbContext db, TenantContext tenant, CancellationToken cancellationToken)
    {
        if (await FindOrganizationAsync(organizationSlug, db, tenant, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Slug == jobSlug, cancellationToken);
        return job switch
        {
            { Status: JobStatus.Open } => TypedResults.Ok(job.ToPublicJob()),
            { Status: JobStatus.Closed, PublishedAt: not null } => Gone(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<Ok<PublicApplicationForm>, NotFound, ProblemHttpResult>> GetApplicationForm(
        string organizationSlug,
        string jobSlug,
        ScreenHireDbContext db,
        TenantContext tenant,
        ApplicantUploads uploads,
        TurnstileVerifier turnstile,
        CancellationToken cancellationToken)
    {
        var organization = await FindOrganizationAsync(organizationSlug, db, tenant, cancellationToken);
        if (organization is null)
        {
            return TypedResults.NotFound();
        }

        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Slug == jobSlug, cancellationToken);
        if (job is not { Status: JobStatus.Open })
        {
            return job is { Status: JobStatus.Closed, PublishedAt: not null } ? Gone() : TypedResults.NotFound();
        }

        return TypedResults.Ok(new PublicApplicationForm(
            organization.ToPublic(),
            job.ToPublicJob(),
            [.. ApplicationForm.QuestionsFor(job.Workflow).Select(q => q.ToPublic(FileTypes.AcceptedBy))],
            job.Workflow.IntegrityPolicy.AiUse,
            new PublicUploadSettings(uploads.MaxBytes, uploads.Origin),
            turnstile.SiteKey));
    }

    /// <summary>410 for a job that was published and has closed.</summary>
    internal static ProblemHttpResult Gone() =>
        TypedResults.Problem(title: "This job is closed", detail: "It no longer takes applications.", statusCode: StatusCodes.Status410Gone);

    /// <summary>
    /// The organization with <paramref name="slug"/>, made the request's tenant. This replaces a signed-in caller's own
    /// organization for the request, which is safe here because these routes read only what a careers page shows.
    /// </summary>
    internal static async Task<Organization?> FindOrganizationAsync(
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
