using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Endpoints;

/// <summary>Recruiter-dashboard endpoints.</summary>
internal static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        // Authentication and tenant resolution are added by the auth and multi-tenancy work items.
        // Until then this group serves the seeded demo organization only and must not be deployed.
        var group = app.MapGroup("/v1/jobs").WithTags("Jobs");

        group.MapGet("/", ListJobs).WithName("ListJobs");

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
}
