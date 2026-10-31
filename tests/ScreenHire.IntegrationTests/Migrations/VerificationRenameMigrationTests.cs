using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Migrations;

/// <summary>
/// Integrity became Verification (ADR-0019) by a migration that renames tables, keys and indexes and rewrites the names
/// stored as data, and never drops anything. Data written before it survives it, in both directions, and the database
/// it leaves has exactly the names the EF model expects.
/// </summary>
public sealed class VerificationRenameMigrationTests(AppHostFixture fixture)
{
    private const string Before = "AddCareersEmbedding";
    private const string Rename = "RenameIntegrityToVerification";

    [Fact]
    public async Task Rows_written_before_the_rename_survive_it_with_their_names_rewritten_and_come_back_when_it_is_undone()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await fixture.GetOwnerConnectionStringAsync(ct);
        var scratch = $"rename_{Guid.NewGuid():N}"[..24];
        await ExecuteAsync(owner, $"CREATE DATABASE {scratch}", ct);
        var connectionString = new NpgsqlConnectionStringBuilder(owner) { Database = scratch, Pooling = false }.ConnectionString;
        try
        {
            await using (var db = Context(connectionString))
            {
                await db.GetService<IMigrator>().MigrateAsync(Before, ct);
            }

            var organizationId = Guid.CreateVersion7();
            // The owner is a superuser here, which row-level security does not bind.
            await ExecuteAsync(connectionString, $"""
                INSERT INTO organizations (id, name, slug, created_at, embed_origins, home_region, indeed_eligible, is_trial, questions_per_page, signal_network_opt_in)
                VALUES ('{organizationId}', 'Rename Studio', 'rename-{organizationId:N}', now(), ARRAY[]::text[], 'EU', false, false, 0, false);
                INSERT INTO integrity_settings (organization_id, llm_consistency_check_enabled, updated_at) VALUES ('{organizationId}', true, now());
                INSERT INTO custom_roles (id, organization_id, name, normalized_name, copied_from, permissions, created_at, updated_at)
                VALUES ('{Guid.CreateVersion7()}', '{organizationId}', 'Screener', 'SCREENER', 'Reviewer', ARRAY['ViewHiring', 'ReviewIntegrity'], now(), now());
                INSERT INTO webhook_endpoints (id, organization_id, url, events, enabled, secret_version, consecutive_failures, created_at, updated_at, secret_rotated_at)
                VALUES ('{Guid.CreateVersion7()}', '{organizationId}', 'https://hooks.example.org/in', ARRAY['application.submitted', 'integrity.assessed'], true, 1, 0, now(), now(), now());
                INSERT INTO change_records (id, organization_id, subject_kind, subject_id, created, lines, occurred_at)
                VALUES ('{Guid.CreateVersion7()}', '{organizationId}', 'IntegritySettings', '{organizationId}', true, '[]', now());
                """, ct);

            await using (var db = Context(connectionString))
            {
                await db.GetService<IMigrator>().MigrateAsync(Rename, ct);
            }

            (await ScalarAsync<bool>(connectionString, $"SELECT llm_consistency_check_enabled FROM verification_settings WHERE organization_id = '{organizationId}'", ct))
                .Should().BeTrue("the settings row moved with its table");
            (await ScalarAsync<string[]>(connectionString, $"SELECT permissions FROM custom_roles WHERE organization_id = '{organizationId}'", ct))
                .Should().Equal("ViewHiring", "ReviewVerification");
            (await ScalarAsync<string[]>(connectionString, $"SELECT events FROM webhook_endpoints WHERE organization_id = '{organizationId}'", ct))
                .Should().Equal("application.submitted", "verification.assessed");
            (await ScalarAsync<string>(connectionString, $"SELECT subject_kind FROM change_records WHERE organization_id = '{organizationId}'", ct))
                .Should().Be("VerificationSettings");
            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM hiring_template_versions WHERE definition::text LIKE '%integrity.%' OR definition::text LIKE '%IntegrityAssessed%' OR definition::text LIKE '%integrityPolicy%'", ct))
                .Should().Be(0, "every system template version reads the renamed policy, trigger and facts");
            (await ScalarAsync<int>(connectionString, "SELECT latest_published_version FROM hiring_templates WHERE id = '0199e8a0-0000-7000-8000-000000000001'", ct))
                .Should().Be(5);
            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM pg_policies WHERE schemaname = 'public' AND tablename LIKE 'verification_%'", ct))
                .Should().BeGreaterThanOrEqualTo(9, "five tenant_isolation policies and four job_scope policies follow their tables");
            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM pg_policies WHERE schemaname = 'public' AND tablename LIKE 'verification_%' AND policyname = 'job_scope' AND permissive = 'RESTRICTIVE'", ct))
                .Should().Be(4, "job_scope stays restrictive on the four rows under one application");
            (await ScalarAsync<bool>(connectionString, "SELECT has_table_privilege('screenhire_app', 'verification_review_actions', 'UPDATE') OR has_table_privilege('screenhire_app', 'verification_review_actions', 'DELETE') OR has_table_privilege('screenhire_app', 'verification_settings', 'DELETE')", ct))
                .Should().BeFalse("the revoked grants follow their tables");
            (await IntegrityNamesAsync(connectionString, ct)).Should().BeEmpty();
            (await UnforcedAsync(connectionString, ct)).Should().BeEmpty("FORCE comes back on every table the migration lifted it from");

            await using (var db = Context(connectionString))
            {
                await db.GetService<IMigrator>().MigrateAsync(Before, ct);
            }

            (await ScalarAsync<bool>(connectionString, $"SELECT llm_consistency_check_enabled FROM integrity_settings WHERE organization_id = '{organizationId}'", ct))
                .Should().BeTrue("undoing the rename keeps the data too");
            (await ScalarAsync<string[]>(connectionString, $"SELECT permissions FROM custom_roles WHERE organization_id = '{organizationId}'", ct))
                .Should().Equal("ViewHiring", "ReviewIntegrity");
            (await ScalarAsync<string[]>(connectionString, $"SELECT events FROM webhook_endpoints WHERE organization_id = '{organizationId}'", ct))
                .Should().Equal("application.submitted", "integrity.assessed");
            (await ScalarAsync<int>(connectionString, "SELECT latest_published_version FROM hiring_templates WHERE id = '0199e8a0-0000-7000-8000-000000000001'", ct))
                .Should().Be(4);
            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM hiring_template_versions WHERE id = '0199e8a0-0000-7000-8000-000000000105'", ct))
                .Should().Be(0, "version 5 exists only after the rename");
            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM hiring_template_versions WHERE definition::text LIKE '%verificationPolicy%' OR definition::text LIKE '%VerificationAssessed%' OR definition::text LIKE '%\"verification.authenticity\"%' OR definition::text LIKE '%\"verification.aiAssistance\"%'", ct))
                .Should().Be(0, "undoing the rename gives every definition its old policy, trigger and facts");
            (await ScalarAsync<string>(connectionString, "SELECT definition #>> '{workflow,automations,1,trigger,type}' FROM hiring_template_versions WHERE id = '0199e8a0-0000-7000-8000-000000000104'", ct))
                .Should().Be("IntegrityAssessed");
            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM pg_class WHERE relname LIKE 'verification_%'", ct)).Should().Be(0);
            (await UnforcedAsync(connectionString, ct)).Should().BeEmpty("undoing the rename restores FORCE too");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(owner, $"DROP DATABASE IF EXISTS {scratch} WITH (FORCE)", CancellationToken.None);
        }
    }

    [Fact]
    public async Task The_database_has_every_table_key_and_index_name_the_model_expects_and_none_that_says_integrity()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await fixture.GetOwnerConnectionStringAsync(ct);
        await using var db = Context(owner);
        var model = db.Model.GetRelationalModel();

        var relations = await NamesAsync(owner, "SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public'", ct);
        var constraints = await NamesAsync(owner, "SELECT c.conname FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname = 'public'", ct);
        var expectedRelations = model.Tables.Where(t => t.Schema is null or "public")
            .SelectMany(t => t.Indexes.Select(i => i.Name).Prepend(t.Name)).ToList();
        var expectedConstraints = model.Tables.Where(t => t.Schema is null or "public")
            .SelectMany(t => t.UniqueConstraints.Select(u => u.Name).Concat(t.ForeignKeyConstraints.Select(f => f.Name))).ToList();

        expectedRelations.Should().Contain(["verification_signals", "ix_verification_review_actions_one_dismissal"]);
        expectedRelations.Except(relations).Should().BeEmpty("a hand-written rename must leave the names EF expects");
        expectedConstraints.Except(constraints).Should().BeEmpty("a hand-written rename must leave the names EF expects");
        (await IntegrityNamesAsync(owner, ct)).Should().BeEmpty();
    }

    private static ScreenHireDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<ScreenHireDbContext>().UseNpgsql(connectionString).UseScreenHireConventions().Options, new TenantContext());

    // Every table with an organization_id, the renamed ones and the rewritten ones among them, keeps FORCE ROW LEVEL SECURITY.
    private static Task<List<string>> UnforcedAsync(string connectionString, CancellationToken ct) =>
        NamesAsync(connectionString, """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'public'
            WHERE c.relkind = 'r'
              AND EXISTS (SELECT FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'organization_id' AND NOT a.attisdropped)
              AND NOT (c.relrowsecurity AND c.relforcerowsecurity)
            """, ct);

    private static Task<List<string>> IntegrityNamesAsync(string connectionString, CancellationToken ct) =>
        NamesAsync(connectionString, """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relname LIKE '%integrity%'
            UNION ALL SELECT c.conname FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname = 'public' AND c.conname LIKE '%integrity%'
            UNION ALL SELECT policyname FROM pg_policies WHERE schemaname = 'public' AND policyname LIKE '%integrity%'
            """, ct);

    private static async Task<List<string>> NamesAsync(string connectionString, string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var names = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
