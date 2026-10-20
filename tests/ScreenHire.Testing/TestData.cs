using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Testing;

/// <summary>
/// Test data isolation: every test that writes creates its own organization with a unique slug and touches only that
/// organization's rows, so tests sharing one AppHost never see each other's data and need no reset between them.
/// The seeded demo organization (<c>brightwater</c>) is read-only: assert on it, never change it.
/// Writes go through the application role with the organization as tenant, so row-level security checks them too.
/// </summary>
public static class TestData
{
    /// <summary>Creates an organization, plus any jobs <paramref name="jobs"/> builds for it, in one transaction.</summary>
    public static async Task<Organization> CreateOrganizationAsync(
        this AppHostFixture fixture, Func<Organization, IEnumerable<Job>>? jobs = null, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var organization = new Organization { Name = $"Test Organization {id[..8]}", Slug = $"test-{id}", HomeRegion = "eu" };

        var tenant = new TenantContext();
        tenant.SetOrganization(organization.Id);
        await using var db = await fixture.CreateDbContextAsync(tenant, cancellationToken);
        db.Organizations.Add(organization);
        if (jobs is not null)
        {
            db.Jobs.AddRange(jobs(organization));
        }

        await db.SaveChangesAsync(cancellationToken);
        return organization;
    }

    /// <summary>Adds jobs to an existing organization, such as one a test created by onboarding through the API.</summary>
    public static async Task AddJobsAsync(
        this AppHostFixture fixture, Guid organizationId, IEnumerable<Job> jobs, CancellationToken cancellationToken = default)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, cancellationToken);
        db.Jobs.AddRange(jobs);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A draft job with sensible defaults; call <see cref="Job.Publish"/> or <see cref="Job.Close"/> as needed.</summary>
    public static Job NewJob(Organization organization, string slug, string? title = null) => NewJob(organization.Id, slug, title);

    /// <inheritdoc cref="NewJob(Organization, string, string?)"/>
    public static Job NewJob(Guid organizationId, string slug, string? title = null) =>
        new()
        {
            OrganizationId = organizationId,
            Title = title ?? $"Job {slug}",
            Slug = slug,
            Location = "Remote",
            WorkplaceType = WorkplaceType.Remote,
            EmploymentType = EmploymentType.FullTime,
            SalaryMin = 50_000,
            SalaryMax = 60_000,
            SalaryCurrency = "GBP",
        };
}
