using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Identity;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Infrastructure.Persistence;

/// <summary>
/// Applies migrations and seeds a demo employer for local development. Never called outside Development;
/// production schema changes run as a separate migration step in the deploy pipeline, which also gives the
/// <c>screenhire_app</c> role its login.
/// </summary>
public static class DevelopmentDataSeeder
{
    public const string DemoOrganizationSlug = "brightwater";

    /// <summary>The Development-only mock OIDC provider's scheme (<c>ScreenHire:Auth:Oidc:dev-sso</c>).</summary>
    public const string DevSsoProvider = "dev-sso";

    // The demo organization's Owner. The subject and email must match the Dev SSO user in apps/apphost/DevSso/config.json.
    public const string DemoOwnerSubject = "brightwater-owner";
    public const string DemoOwnerEmail = "avery@brightwater.example.com";
    public const string DemoOwnerName = "Avery Quinn";

    /// <summary>Brightwater's brand colour, a deep teal, so the demo careers pages show employer branding.</summary>
    public const string DemoBrandColor = "#1f4e5a";

    /// <summary>
    /// Migrates as the table owner, lets the application role log in with the password from
    /// <paramref name="appConnectionString"/>, then seeds Brightwater and its Owner. Every step is idempotent, so it
    /// runs on every start, including against a data volume from before a step existed.
    /// </summary>
    public static async Task MigrateAndSeedAsync(
        string ownerConnectionString, string appConnectionString, TimeProvider time, CancellationToken cancellationToken = default)
    {
        var tenant = new TenantContext();
        var options = new DbContextOptionsBuilder<ScreenHireDbContext>()
            .UseNpgsql(ownerConnectionString)
            .UseScreenHireConventions()
            .Options;
        await using var db = new ScreenHireDbContext(options, tenant);

        await db.Database.MigrateAsync(cancellationToken);
        await GrantAppRoleLoginAsync(ownerConnectionString, appConnectionString, cancellationToken);

        var now = time.GetUtcNow();
        var organization = await SeedOrganizationAsync(db, now, cancellationToken);
        tenant.SetOrganization(organization.Id);
        await SeedBrandAsync(db, organization, cancellationToken);
        await SeedOwnerAsync(db, organization, now, cancellationToken);
    }

    // A step of its own, so a data volume seeded before brand colours existed gets one too.
    private static async Task SeedBrandAsync(ScreenHireDbContext db, Organization organization, CancellationToken cancellationToken)
    {
        if (organization.BrandColor is null)
        {
            organization.SetBrandColor(DemoBrandColor);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    // A raw connection, not EF: EF logs command text, and this command carries the password. ALTER ROLE is a utility
    // statement and cannot take parameters, so the literal is quoted here.
    private static async Task GrantAppRoleLoginAsync(string ownerConnectionString, string appConnectionString, CancellationToken cancellationToken)
    {
        var password = new NpgsqlConnectionStringBuilder(appConnectionString).Password
            ?? throw new InvalidOperationException($"ConnectionStrings:{DatabaseRegistration.AppConnectionName} has no password.");

        await using var connection = new NpgsqlConnection(ownerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER ROLE {TenantSecurity.AppRole} WITH LOGIN PASSWORD '{password.Replace("'", "''", StringComparison.Ordinal)}'";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Organization> SeedOrganizationAsync(ScreenHireDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Organizations.SingleOrDefaultAsync(o => o.Slug == DemoOrganizationSlug, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var organization = new Organization { Name = "Brightwater Studio", Slug = DemoOrganizationSlug, HomeRegion = "eu" };
        db.Organizations.Add(organization);

        // Fictional data only (.claude/rules/no-personal-info.md). The .NET role uses the "Software engineer" system
        // template, so the demo shows a job with a template source.
        string[] ukAndIreland = ["GB", "IE"];
        var engineer = NewJob(organization, "Senior .NET Engineer", "senior-dotnet-engineer", WorkplaceType.Remote, [], ukAndIreland, 75_000, 95_000);
        var softwareEngineer = await db.HiringTemplateVersions.SingleAsync(v => v.Id == SystemTemplates.SoftwareEngineerVersion1, cancellationToken);
        engineer.UseTemplate(softwareEngineer);

        Job[] jobs =
        [
            NewJob(organization, "Senior Product Designer", "senior-product-designer", WorkplaceType.Remote, [], ukAndIreland, 70_000, 85_000),
            engineer,
            NewJob(organization, "Frontend Engineer (Svelte)", "frontend-engineer-svelte", WorkplaceType.Hybrid, [new("London", "England", "GB")], [], 60_000, 75_000),
        ];

        foreach (var job in jobs)
        {
            db.JobVersions.Add(job.Publish(now)!);
        }

        db.Jobs.AddRange(jobs);
        db.Jobs.Add(NewJob(organization, "Customer Success Lead", "customer-success-lead", WorkplaceType.Onsite, [new("Manchester", "England", "GB")], [], 45_000, 55_000));

        await db.SaveChangesAsync(cancellationToken);
        return organization;
    }

    private static async Task SeedOwnerAsync(ScreenHireDbContext db, Organization organization, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Identity normalizes with UpperInvariantLookupNormalizer; match it so UserManager finds this user.
        var normalizedEmail = DemoOwnerEmail.ToUpperInvariant();
        var owner = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (owner is null)
        {
            owner = new ApplicationUser
            {
                UserName = DemoOwnerEmail,
                NormalizedUserName = normalizedEmail,
                Email = DemoOwnerEmail,
                NormalizedEmail = normalizedEmail,
                EmailConfirmed = true,
                DisplayName = DemoOwnerName,
                CreatedAt = now,
            };
            db.Users.Add(owner);
        }

        if (!await db.UserLogins.AnyAsync(l => l.LoginProvider == DevSsoProvider && l.ProviderKey == DemoOwnerSubject, cancellationToken))
        {
            db.UserLogins.Add(new IdentityUserLogin<Guid>
            {
                LoginProvider = DevSsoProvider,
                ProviderKey = DemoOwnerSubject,
                ProviderDisplayName = "Dev SSO",
                UserId = owner.Id,
            });
        }

        if (!await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == organization.Id && m.UserId == owner.Id, cancellationToken))
        {
            db.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = organization.Id,
                UserId = owner.Id,
                Role = OrganizationRole.Owner,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Job NewJob(
        Organization organization, string title, string slug, WorkplaceType workplace, JobLocation[] locations, string[] applicantLocations, decimal min, decimal max)
    {
        var job = new Job
        {
            OrganizationId = organization.Id,
            Title = title,
            Slug = slug,
            Description = $"<p>Join Brightwater Studio as our {title}.</p>",
            WorkplaceType = workplace,
            EmploymentType = EmploymentType.FullTime,
            Locations = [.. locations],
            ApplicantLocationRequirements = [.. applicantLocations],
        };
        job.SetSalary(new JobSalary(min, max, "GBP", SalaryPeriod.Year));
        return job;
    }
}
