using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence;

/// <summary>
/// Applies migrations and seeds a demo employer for local development. Never called outside Development;
/// production schema changes run as a separate migration step in the deploy pipeline.
/// </summary>
public static class DevelopmentDataSeeder
{
    public const string DemoOrganizationSlug = "brightwater";

    public static async Task MigrateAndSeedAsync(ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);

        if (await db.Organizations.AnyAsync(o => o.Slug == DemoOrganizationSlug, cancellationToken))
        {
            return;
        }

        var now = time.GetUtcNow();
        var organization = new Organization { Name = "Brightwater Studio", Slug = DemoOrganizationSlug, HomeRegion = "eu" };
        db.Organizations.Add(organization);

        Job[] jobs =
        [
            NewJob(organization, "Senior Product Designer", "senior-product-designer", "Remote — UK & EU", WorkplaceType.Remote, 70_000, 85_000, "GBP"),
            NewJob(organization, "Senior .NET Engineer", "senior-dotnet-engineer", "Remote — UK & EU", WorkplaceType.Remote, 75_000, 95_000, "GBP"),
            NewJob(organization, "Frontend Engineer (Svelte)", "frontend-engineer-svelte", "London, UK", WorkplaceType.Hybrid, 60_000, 75_000, "GBP"),
        ];

        foreach (var job in jobs)
        {
            job.Publish(now);
        }

        db.Jobs.AddRange(jobs);
        db.Jobs.Add(NewJob(organization, "Customer Success Lead", "customer-success-lead", "Manchester, UK", WorkplaceType.Onsite, 45_000, 55_000, "GBP"));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Job NewJob(Organization organization, string title, string slug, string location, WorkplaceType workplace, decimal min, decimal max, string currency) =>
        new()
        {
            OrganizationId = organization.Id,
            Title = title,
            Slug = slug,
            Location = location,
            WorkplaceType = workplace,
            EmploymentType = EmploymentType.FullTime,
            SalaryMin = min,
            SalaryMax = max,
            SalaryCurrency = currency,
        };
}
