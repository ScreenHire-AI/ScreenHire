using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Testing;

/// <summary>
/// Test data isolation: every test that writes creates its own organization with a unique slug and touches only that
/// organization's rows, so tests sharing one AppHost never see each other's data and need no reset between them.
/// The seeded demo organization (<c>brightwater</c>) is read-only: assert on it, never change it.
/// </summary>
public static class TestData
{
    /// <summary>Creates an organization, plus any jobs <paramref name="jobs"/> builds for it, in one transaction.</summary>
    public static async Task<Organization> CreateOrganizationAsync(
        this AppHostFixture fixture, Func<Organization, IEnumerable<Job>>? jobs = null, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var organization = new Organization { Name = $"Test Organization {id[..8]}", Slug = $"test-{id}", HomeRegion = "eu" };

        await using var db = await fixture.CreateDbContextAsync(cancellationToken);
        db.Organizations.Add(organization);
        if (jobs is not null)
        {
            db.Jobs.AddRange(jobs(organization));
        }

        await db.SaveChangesAsync(cancellationToken);
        return organization;
    }

    /// <summary>A draft job with sensible defaults; call <see cref="Job.Publish"/> or <see cref="Job.Close"/> as needed.</summary>
    public static Job NewJob(Organization organization, string slug, string? title = null) =>
        new()
        {
            OrganizationId = organization.Id,
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
