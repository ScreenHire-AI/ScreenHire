using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        await SeedOwnerAsync(db, organization, now, cancellationToken);
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
