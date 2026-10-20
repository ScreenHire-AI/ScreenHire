using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Distribution;
using ScreenHire.Application.Distribution.Adapters;
using ScreenHire.Application.Distribution.Feeds;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// What pull boards read (docs/architecture/job-distribution.md): one XML feed per board in its own format, the
/// read-only public job-board API (Greenhouse-style JSON), and the local sandbox board's listings. Unauthenticated and
/// read-only. Each resolves the organization from the slug in the route and makes it the tenant, so the tenant filter
/// and row-level security scope every query to it, as on the careers endpoints. Everything lists a job only while its
/// posting on that board is active, which a close ends in the same transaction.
/// </summary>
internal static class PublicDistributionEndpoints
{
    public static IEndpointRouteBuilder MapPublicDistributionEndpoints(this IEndpointRouteBuilder app)
    {
        // Feeds are for crawlers, not for the TypeScript client, and their bodies are XML.
        app.MapGet("/v1/public/organizations/{organizationSlug}/feeds/{boardId}.xml", GetFeed)
            .WithName("GetJobFeed").WithTags("Public distribution").AllowAnonymous().ExcludeFromDescription();

        var board = app.MapGroup("/v1/public/boards/{organizationSlug}").WithTags("Public job board").AllowAnonymous();
        board.MapGet("/", GetJobBoard).WithName("GetJobBoard");
        board.MapGet("/jobs", ListJobBoardJobs).WithName("ListJobBoardJobs");
        board.MapGet("/jobs/{jobId:guid}", GetJobBoardJob).WithName("GetJobBoardJob");

        app.MapGet("/v1/public/sandbox-board/{organizationSlug}/listings", ListSandboxListings)
            .WithName("ListSandboxListings").WithTags("Public distribution").AllowAnonymous();

        return app;
    }

    // A full snapshot of the organization's postings for one board, in the board's format. Clients may cache it for
    // PullBoardAdapter.CacheLifetime, which every removal estimate includes; the ETag lets a crawler ask cheaply.
    private static async Task<Results<FileContentHttpResult, StatusCodeHttpResult, NotFound>> GetFeed(
        string organizationSlug,
        string boardId,
        HttpContext context,
        ScreenHireDbContext db,
        TenantContext tenant,
        BoardRegistry boards,
        IOptions<DomainOptions> domains,
        CancellationToken cancellationToken)
    {
        var organization = await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken);
        if (organization is null || boards.Find(boardId) is not FeedAdapter feed || !IsAvailable(feed, organization))
        {
            return TypedResults.NotFound();
        }

        var contents = await PostingContents.ActiveOnBoardAsync(db, boardId, organization.Name, organization.Slug, cancellationToken);
        // The feed's build time is its last change, not the request time, so an unchanged feed has an unchanged body.
        var lastChange = await db.JobPostings.Where(p => p.BoardId == boardId).MaxAsync(p => (DateTimeOffset?)p.UpdatedAt, cancellationToken)
            ?? organization.CreatedAt;
        var body = feed.Render(new FeedHeader(organization.Name, domains.Value.CareersBoardUrl(organization.Slug), lastChange), contents);

        var etag = new EntityTagHeaderValue($"\"{Convert.ToHexString(SHA256.HashData(body))[..32]}\"");
        var headers = context.Response.GetTypedHeaders();
        headers.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = PullBoardAdapter.CacheLifetime };
        headers.ETag = etag;
        headers.LastModified = lastChange;
        if (context.Request.GetTypedHeaders().IfNoneMatch.Any(tag => tag.Compare(etag, useStrongComparison: false)))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        return TypedResults.File(body, "application/xml; charset=utf-8");
    }

    private static async Task<Results<Ok<JobBoardInfo>, NotFound>> GetJobBoard(
        string organizationSlug, HttpContext context, ScreenHireDbContext db, TenantContext tenant, BoardRegistry boards,
        IOptions<DomainOptions> domains, CancellationToken cancellationToken)
    {
        var organization = await JobBoardOrganizationAsync(organizationSlug, db, tenant, boards, cancellationToken);
        if (organization is null)
        {
            return TypedResults.NotFound();
        }

        Cache(context);
        return TypedResults.Ok(new JobBoardInfo(organization.Name, "", domains.Value.CareersBoardUrl(organization.Slug)));
    }

    // Greenhouse's ?content=true adds each job's description.
    private static async Task<Results<Ok<JobBoardJobList>, NotFound>> ListJobBoardJobs(
        string organizationSlug, HttpContext context, ScreenHireDbContext db, TenantContext tenant, BoardRegistry boards,
        CancellationToken cancellationToken, bool content = false)
    {
        var organization = await JobBoardOrganizationAsync(organizationSlug, db, tenant, boards, cancellationToken);
        if (organization is null)
        {
            return TypedResults.NotFound();
        }

        var jobs = await PostingContents.ActiveOnBoardAsync(db, Boards.JobBoardApi, organization.Name, organization.Slug, cancellationToken);
        Cache(context);
        return TypedResults.Ok(new JobBoardJobList([.. jobs.Select(j => j.ToJobBoardJob(content))], new JobBoardMeta(jobs.Count)));
    }

    private static async Task<Results<Ok<JobBoardJob>, NotFound>> GetJobBoardJob(
        string organizationSlug, Guid jobId, HttpContext context, ScreenHireDbContext db, TenantContext tenant, BoardRegistry boards,
        CancellationToken cancellationToken)
    {
        var organization = await JobBoardOrganizationAsync(organizationSlug, db, tenant, boards, cancellationToken);
        if (organization is null)
        {
            return TypedResults.NotFound();
        }

        var job = (await PostingContents.ActiveOnBoardAsync(db, Boards.JobBoardApi, organization.Name, organization.Slug, cancellationToken, jobId))
            .SingleOrDefault();
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        Cache(context);
        return TypedResults.Ok(job.ToJobBoardJob(withContent: true));
    }

    // What the sandbox board shows right now: accepted listings that have not been removed. A 404 while the sandbox is off.
    private static async Task<Results<Ok<List<SandboxBoardListing>>, NotFound>> ListSandboxListings(
        string organizationSlug, ScreenHireDbContext db, TenantContext tenant, BoardRegistry boards, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (boards.Find(Boards.Sandbox) is null
            || await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        var listings = await db.SandboxListings.AsNoTracking()
            .Where(l => l.AcceptsAt <= now && (l.RemovesAt == null || l.RemovesAt > now))
            .OrderBy(l => l.AcceptsAt)
            .Select(l => new SandboxBoardListing(l.Id, l.JobId, l.JobVersion, l.Title, l.Url, l.AcceptsAt))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(listings);
    }

    private static async Task<Organization?> JobBoardOrganizationAsync(
        string slug, ScreenHireDbContext db, TenantContext tenant, BoardRegistry boards, CancellationToken cancellationToken)
    {
        var organization = await PublicOrganizations.FindAsync(slug, db, tenant, cancellationToken);
        return organization is not null && boards.Find(Boards.JobBoardApi) is { } api && IsAvailable(api, organization) ? organization : null;
    }

    private static bool IsAvailable(IJobBoardAdapter adapter, Organization organization) =>
        !adapter.Capabilities.PartnerGated || organization.MayUsePartnerBoards;

    private static void Cache(HttpContext context) =>
        context.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = PullBoardAdapter.CacheLifetime };
}

/// <summary>Resolves a public route's organization slug and makes that organization the request's tenant.</summary>
internal static class PublicOrganizations
{
    public static async Task<Organization?> FindAsync(string slug, ScreenHireDbContext db, TenantContext tenant, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Slug == slug, cancellationToken);
        if (organization is not null)
        {
            // Replaces a signed-in caller's own organization for this request: a public route acts for the organization
            // in its URL. Public routes read only what a careers page, feed or public board shows; the one write is an
            // applicant's own application (PublicApplicationEndpoints).
            tenant.SetOrganization(organization.Id);
        }

        return organization;
    }
}
