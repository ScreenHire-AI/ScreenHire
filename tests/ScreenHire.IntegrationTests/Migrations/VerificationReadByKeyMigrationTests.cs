using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Migrations;

/// <summary>
/// Undoing AddVerificationReadByKey (ADR-0021) leaves a database the previous release can run on: no review log row
/// without a person, no key holding a scope name that release cannot parse, and FORCE ROW LEVEL SECURITY back on both
/// tables it lifted it from. People's own review actions and every key survive. An organization with key reads can
/// still be deleted, because its keys and their reads leave with it.
/// </summary>
public sealed class VerificationReadByKeyMigrationTests(AppHostFixture fixture)
{
    private const string Before = "RenameIntegrityToVerification";
    private const string Added = "AddVerificationReadByKey";

    [Fact]
    public async Task Undoing_it_removes_the_key_reads_and_the_scope_and_keeps_everything_else()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await fixture.GetOwnerConnectionStringAsync(ct);
        var scratch = $"readbykey_{Guid.NewGuid():N}"[..24];
        await ExecuteAsync(owner, $"CREATE DATABASE {scratch}", ct);
        var connectionString = new NpgsqlConnectionStringBuilder(owner) { Database = scratch, Pooling = false }.ConnectionString;
        try
        {
            await using (var db = Context(connectionString))
            {
                await db.GetService<IMigrator>().MigrateAsync(Added, ct);
            }

            var organizationId = Guid.CreateVersion7();
            var keyId = Guid.CreateVersion7();
            var person = Guid.CreateVersion7();
            // The owner is a superuser here, which row-level security does not bind. The replica role skips the foreign
            // keys, so the rows need no application: only the rows themselves matter to this migration.
            await ExecuteAsync(connectionString, $"""
                SET session_replication_role = replica;
                INSERT INTO organizations (id, name, slug, created_at, embed_origins, home_region, indeed_eligible, is_trial, questions_per_page, signal_network_opt_in)
                VALUES ('{organizationId}', 'Read Studio', 'read-{organizationId:N}', now(), ARRAY[]::text[], 'EU', false, false, 0, false);
                INSERT INTO api_keys (id, organization_id, name, prefix, secret_hash, scopes, created_at)
                VALUES ('{keyId}', '{organizationId}', 'Auditor', 'shk_read', repeat('a', 64), ARRAY['Read', 'Write', 'VerificationRead'], now());
                INSERT INTO verification_review_actions (id, organization_id, application_id, type, api_key_id, occurred_at)
                VALUES ('{Guid.CreateVersion7()}', '{organizationId}', '{Guid.CreateVersion7()}', 'ReadByKey', '{keyId}', now());
                INSERT INTO verification_review_actions (id, organization_id, application_id, type, note, actor_user_id, occurred_at)
                VALUES ('{person}', '{organizationId}', '{Guid.CreateVersion7()}', 'NoteAdded', 'Checked.', '{Guid.CreateVersion7()}', now());
                """, ct);

            await using (var db = Context(connectionString))
            {
                await db.GetService<IMigrator>().MigrateAsync(Before, ct);
            }

            (await ScalarAsync<string[]>(connectionString, $"SELECT scopes FROM api_keys WHERE id = '{keyId}'", ct))
                .Should().Equal(["Read", "Write"], "the previous release cannot parse the scope's name, so it leaves with its column's meaning");
            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM verification_review_actions WHERE actor_user_id IS NULL", ct))
                .Should().Be(0, "a key's read has no person, which the old column cannot hold");
            (await ScalarAsync<long>(connectionString, $"SELECT count(*) FROM verification_review_actions WHERE id = '{person}'", ct))
                .Should().Be(1, "a person's action survives");
            (await ScalarAsync<long>(connectionString, """
                SELECT count(*) FROM pg_class
                WHERE relname IN ('api_keys', 'verification_review_actions') AND relrowsecurity AND relforcerowsecurity
                """, ct)).Should().Be(2, "FORCE comes back on both tables");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(owner, $"DROP DATABASE IF EXISTS {scratch} WITH (FORCE)", CancellationToken.None);
        }
    }

    [Fact]
    public async Task Deleting_an_organization_takes_its_keys_and_their_reads_with_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await fixture.GetOwnerConnectionStringAsync(ct);
        var scratch = $"orgdelete_{Guid.NewGuid():N}"[..24];
        await ExecuteAsync(owner, $"CREATE DATABASE {scratch}", ct);
        var connectionString = new NpgsqlConnectionStringBuilder(owner) { Database = scratch, Pooling = false }.ConnectionString;
        try
        {
            await using (var db = Context(connectionString))
            {
                await db.GetService<IMigrator>().MigrateAsync(Added, ct);
            }

            var organizationId = Guid.CreateVersion7();
            var keyId = Guid.CreateVersion7();
            await ExecuteAsync(connectionString, $"""
                SET session_replication_role = replica;
                INSERT INTO organizations (id, name, slug, created_at, embed_origins, home_region, indeed_eligible, is_trial, questions_per_page, signal_network_opt_in)
                VALUES ('{organizationId}', 'Leaving Studio', 'leaving-{organizationId:N}', now(), ARRAY[]::text[], 'EU', false, false, 0, false);
                INSERT INTO api_keys (id, organization_id, name, prefix, secret_hash, scopes, created_at)
                VALUES ('{keyId}', '{organizationId}', 'Auditor', 'shk_read', repeat('a', 64), ARRAY['Read', 'VerificationRead'], now());
                INSERT INTO verification_review_actions (id, organization_id, application_id, type, api_key_id, occurred_at)
                VALUES ('{Guid.CreateVersion7()}', '{organizationId}', '{Guid.CreateVersion7()}', 'ReadByKey', '{keyId}', now());
                """, ct);

            await ExecuteAsync(connectionString, $"DELETE FROM organizations WHERE id = '{organizationId}'", ct);

            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM api_keys", ct)).Should().Be(0);
            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM verification_review_actions", ct)).Should().Be(0);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(owner, $"DROP DATABASE IF EXISTS {scratch} WITH (FORCE)", CancellationToken.None);
        }
    }

    private static ScreenHireDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<ScreenHireDbContext>().UseNpgsql(connectionString).UseScreenHireConventions().Options, new TenantContext());

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
