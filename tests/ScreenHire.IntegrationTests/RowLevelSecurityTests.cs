using Microsoft.EntityFrameworkCore;
using Npgsql;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Tenant isolation in the database itself, as the <c>screenhire_app</c> role the API and worker use: the backstop
/// that holds even if a query forgets the EF filter (ADR-0008, CLAUDE.md non-negotiable 6).
/// </summary>
public sealed class RowLevelSecurityTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Raw_query_without_a_tenant_returns_no_rows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.CreateOrganizationAsync(org => [TestData.NewJob(org, "visible-to-owner-only")], cancellationToken);

        var asApp = await CountAsync(await fixture.GetAppConnectionStringAsync(cancellationToken), "jobs", cancellationToken);
        var asOwner = await CountAsync(await fixture.GetOwnerConnectionStringAsync(cancellationToken), "jobs", cancellationToken);

        asApp.Should().Be(0);
        asOwner.Should().BeGreaterThan(0, "the rows exist; only the application role is denied them");
        (await CountAsync(await fixture.GetAppConnectionStringAsync(cancellationToken), "organization_members", cancellationToken))
            .Should().Be(0);
    }

    [Fact]
    public async Task Raw_query_with_a_tenant_returns_only_that_organizations_rows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mine = await fixture.CreateOrganizationAsync(org => [TestData.NewJob(org, "a"), TestData.NewJob(org, "b")], cancellationToken);
        await fixture.CreateOrganizationAsync(org => [TestData.NewJob(org, "c")], cancellationToken);

        await using var connection = new NpgsqlConnection(await fixture.GetAppConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant, true); SELECT array_agg(slug ORDER BY slug) FROM jobs;", connection, transaction);
        command.Parameters.AddWithValue("tenant", mine.Id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        reader.GetFieldValue<string[]>(0).Should().Equal("a", "b");
    }

    [Fact]
    public async Task Tenant_setting_does_not_outlive_its_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var organization = await fixture.CreateOrganizationAsync(org => [TestData.NewJob(org, "a")], cancellationToken);
        await using var connection = new NpgsqlConnection(await fixture.GetAppConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);

        await using (var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true)", connection))
        {
            set.Parameters.AddWithValue("tenant", organization.Id.ToString());
            await set.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var count = new NpgsqlCommand("SELECT count(*) FROM jobs", connection);
        ((long)(await count.ExecuteScalarAsync(cancellationToken))!).Should().Be(0, "the setting was local to its own transaction");
    }

    [Fact]
    public async Task App_role_cannot_switch_row_security_off()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(await fixture.GetAppConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SET row_security = off; SELECT count(*) FROM jobs;", connection);

        var query = () => command.ExecuteScalarAsync(cancellationToken);

        (await query.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Context_reads_and_writes_its_own_tenant_in_one_batch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var organization = await fixture.CreateOrganizationAsync(
            org => [TestData.NewJob(org, "one"), TestData.NewJob(org, "two"), TestData.NewJob(org, "three")], cancellationToken);
        var tenant = new TenantContext();
        tenant.SetOrganization(organization.Id);

        await using (var db = await fixture.CreateDbContextAsync(tenant, cancellationToken))
        {
            var jobs = await db.Jobs.OrderBy(j => j.Slug).ToListAsync(cancellationToken);
            jobs.Select(j => j.Slug).Should().Equal("one", "three", "two");

            // Two updates and a delete in one SaveChanges: EF checks each statement's rows affected, which the
            // prepended session statement must not disturb.
            jobs[0].Title = "One (renamed)";
            jobs[1].Title = "Three (renamed)";
            db.Jobs.Remove(jobs[2]);
            await db.SaveChangesAsync(cancellationToken);

            (await db.Jobs.Where(j => j.Slug == "one").ExecuteUpdateAsync(s => s.SetProperty(j => j.Location, "Leeds, UK"), cancellationToken))
                .Should().Be(1);
            db.Jobs.Add(TestData.NewJob(organization, "four"));
            db.SaveChanges(); // the synchronous path too
            db.Jobs.Count().Should().Be(3);
        }

        await using var check = await fixture.CreateDbContextAsync(tenant, cancellationToken);
        (await check.Jobs.AsNoTracking().OrderBy(j => j.Slug).Select(j => new { j.Slug, j.Title, j.Location }).ToListAsync(cancellationToken))
            .Should().Equal(
                new { Slug = "four", Title = "Job four", Location = "Remote" },
                new { Slug = "one", Title = "One (renamed)", Location = "Leeds, UK" },
                new { Slug = "three", Title = "Three (renamed)", Location = "Remote" });
    }

    [Fact]
    public async Task Tenant_cannot_change_or_add_another_organizations_rows_even_without_the_query_filter()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mine = await fixture.CreateOrganizationAsync(cancellationToken: cancellationToken);
        var theirs = await fixture.CreateOrganizationAsync(org => [TestData.NewJob(org, "theirs")], cancellationToken);
        var tenant = new TenantContext();
        tenant.SetOrganization(mine.Id);
        await using var db = await fixture.CreateDbContextAsync(tenant, cancellationToken);

        // Bypass the EF filter on purpose: row-level security alone must stop these.
        var jobsUpdated = await db.Jobs.IgnoreQueryFilters([ScreenHireDbContext.TenantFilter])
            .Where(j => j.OrganizationId == theirs.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Title, "Taken over"), cancellationToken);
        var organizationsRenamed = await db.Organizations.Where(o => o.Id == theirs.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Name, "Taken over"), cancellationToken);
        var membersVisible = await db.OrganizationMembers.IgnoreQueryFilters([ScreenHireDbContext.TenantFilter])
            .CountAsync(m => m.OrganizationId == theirs.Id, cancellationToken);

        jobsUpdated.Should().Be(0);
        organizationsRenamed.Should().Be(0);
        membersVisible.Should().Be(0);

        db.Jobs.Add(TestData.NewJob(theirs, "planted"));
        var insert = () => db.SaveChangesAsync(cancellationToken);
        (await insert.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, "the new row fails the policy's WITH CHECK");

        db.ChangeTracker.Clear();
        db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = theirs.Id,
            UserId = Guid.NewGuid(),
            Role = OrganizationRole.Owner,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        (await insert.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Every_table_with_an_organization_id_has_forced_row_level_security_and_a_policy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);

        // A new tenant table that forgot its migration's RLS block would be readable by every tenant: the default
        // privileges grant the application role every new table. This catches it.
        await using var command = new NpgsqlCommand("""
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'public'
            WHERE c.relkind = 'r'
              AND (c.relname = 'organizations'
                   OR EXISTS (SELECT FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'organization_id' AND NOT a.attisdropped))
              AND NOT (c.relrowsecurity AND c.relforcerowsecurity
                       AND EXISTS (SELECT FROM pg_policies p WHERE p.schemaname = 'public' AND p.tablename = c.relname))
            """, connection);
        var unprotected = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                unprotected.Add(reader.GetString(0));
            }
        }

        unprotected.Should().BeEmpty("every tenant-owned table needs the tenant_isolation policy (see /add-migration)");
    }

    [Fact]
    public async Task Pooled_connection_carries_no_tenant_into_the_next_context()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await fixture.CreateOrganizationAsync(org => [TestData.NewJob(org, "first")], cancellationToken);
        var second = await fixture.CreateOrganizationAsync(org => [TestData.NewJob(org, "second")], cancellationToken);
        // One physical connection, so every context below reuses the one before it.
        var connectionString = new NpgsqlConnectionStringBuilder(await fixture.GetAppConnectionStringAsync(cancellationToken))
        {
            MaxPoolSize = 1,
            ApplicationName = $"pool-test-{Guid.NewGuid():N}",
        }.ConnectionString;

        ScreenHireDbContext Context(Guid? organizationId)
        {
            var tenant = new TenantContext();
            if (organizationId is { } id)
            {
                tenant.SetOrganization(id);
            }

            return new ScreenHireDbContext(
                new DbContextOptionsBuilder<ScreenHireDbContext>().UseNpgsql(connectionString).UseScreenHireConventions().Options, tenant);
        }

        await using (var db = Context(first.Id))
        {
            (await db.Jobs.Select(j => j.Slug).ToListAsync(cancellationToken)).Should().Equal("first"); // no transaction
            db.Jobs.Add(TestData.NewJob(first, "first-again"));
            await db.SaveChangesAsync(cancellationToken); // in a transaction
        }

        await using (var none = Context(null))
        {
            (await none.Jobs.IgnoreQueryFilters([ScreenHireDbContext.TenantFilter]).CountAsync(cancellationToken))
                .Should().Be(0, "the first context's tenant ended with its transactions");
        }

        await using var next = Context(second.Id);
        (await next.Jobs.IgnoreQueryFilters([ScreenHireDbContext.TenantFilter]).Select(j => j.Slug).ToListAsync(cancellationToken))
            .Should().Equal("second");
    }

    [Fact]
    public async Task Tenant_cannot_delete_another_organization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mine = await fixture.CreateOrganizationAsync(cancellationToken: cancellationToken);
        var theirs = await fixture.CreateOrganizationAsync(org => [TestData.NewJob(org, "kept")], cancellationToken);
        var tenant = new TenantContext();
        tenant.SetOrganization(mine.Id);
        await using var db = await fixture.CreateDbContextAsync(tenant, cancellationToken);

        (await db.Organizations.Where(o => o.Id == theirs.Id).ExecuteDeleteAsync(cancellationToken)).Should().Be(0);
        (await db.Organizations.AnyAsync(o => o.Id == theirs.Id, cancellationToken)).Should().BeTrue("organizations are publicly readable");
    }

    private static async Task<long> CountAsync(string connectionString, string table, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
