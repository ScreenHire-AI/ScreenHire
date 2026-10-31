using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Branding;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Integrity;
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
        group.MapGet("/jobs/{jobSlug}", GetOpenJob).WithName("GetPublicJob").Produces<PublicJob>(StatusCodes.Status410Gone);
        group.MapGet("/jobs/{jobSlug}/application-form", GetApplicationForm).WithName("GetPublicApplicationForm")
            .ProducesProblem(StatusCodes.Status410Gone);
        group.MapGet("/branding/images/{imageId:guid}", GetBrandImage).WithName("GetPublicBrandImage")
            .Produces(StatusCodes.Status200OK, contentType: "image/png", additionalContentTypes: ["image/jpeg"]);

        return app;
    }

    private static async Task<Results<Ok<PublicOrganization>, NotFound>> GetOrganization(
        string organizationSlug, ScreenHireDbContext db, TenantContext tenant, IOptions<DomainOptions> domains, CancellationToken cancellationToken)
    {
        var organization = await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken);
        return organization is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(await organization.ToPublicAsync(db, domains.Value, cancellationToken));
    }

    private static async Task<Results<Ok<PublicJobBoard>, NotFound>> ListOpenJobs(
        string organizationSlug, ScreenHireDbContext db, TenantContext tenant, IOptions<DomainOptions> domains, CancellationToken cancellationToken)
    {
        var organization = await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken);
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
            domains.Value.CareersBoardUrl(organization.Slug), await PublicBrandings.LoadAsync(db, organization, domains.Value, cancellationToken)));
    }

    // An open job is 200. A closed job that was once published is 410 Gone with its last content and validThrough set to
    // when it closed, so the careers page can serve 410 with expired JobPosting JSON-LD (teardown, job-distribution.md).
    // A draft, a job closed before it was ever published, or an unknown slug is 404: nothing public ever existed.
    private static async Task<Results<Ok<PublicJob>, JsonHttpResult<PublicJob>, NotFound>> GetOpenJob(
        string organizationSlug, string jobSlug, ScreenHireDbContext db, TenantContext tenant, IOptions<DomainOptions> domains,
        CancellationToken cancellationToken)
    {
        if (await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is not { } organization)
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

    // A brand image's bytes, for the careers pages to serve on their own origin (docs/architecture/branding.md). Only an
    // image that passed its scan (or that no scanner checked) is served. The bytes never change under an id, so the
    // answer is cached for a year; it is the same for every caller and carries nothing about who asked.
    private static async Task<Results<FileStreamHttpResult, NotFound>> GetBrandImage(
        string organizationSlug, Guid imageId, HttpContext http, ScreenHireDbContext db, TenantContext tenant, BrandImageUploads storage,
        CancellationToken cancellationToken)
    {
        if (await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is null
            || await db.BrandImages.AsNoTracking().SingleOrDefaultAsync(i => i.Id == imageId, cancellationToken) is not { IsServable: true } image
            || await storage.OpenReadAsync(image.BlobName, cancellationToken) is not { } bytes)
        {
            return TypedResults.NotFound();
        }

        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
        return TypedResults.Stream(bytes, image.ContentType);
    }

    /// <summary>410 for a job that was published and has closed, where no job content is returned (the apply flow).</summary>
    internal static ProblemHttpResult Gone() =>
        TypedResults.Problem(title: "This job is closed", detail: "It no longer takes applications.", statusCode: StatusCodes.Status410Gone);

    private static async Task<Results<Ok<PublicApplicationForm>, NotFound, ProblemHttpResult>> GetApplicationForm(
        string organizationSlug,
        string jobSlug,
        ScreenHireDbContext db,
        TenantContext tenant,
        ApplicantUploads uploads,
        TurnstileVerifier turnstile,
        TelemetrySessions telemetry,
        IOptions<IntegrityOptions> integrity,
        IOptions<DomainOptions> domains,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var organization = await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken);
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
            await organization.ToPublicAsync(db, domains.Value, cancellationToken),
            job.ToPublicJob(domains.Value.CareersJobUrl(organization.Slug, job.Slug)),
            [.. ApplicationForm.QuestionsFor(job.Workflow).Select(q => q.ToPublic(FileTypes.AcceptedBy))],
            job.Workflow.IntegrityPolicy.AiUse,
            new PublicUploadSettings(uploads.MaxBytes, uploads.Origin),
            turnstile.SiteKey,
            new PublicTelemetrySettings(
                telemetry.Issue(job.OrganizationId, job.Id, time.GetUtcNow()), TelemetrySessions.DeviceCheckFor(job, integrity.Value))));
    }
}
