using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Migrations;

/// <summary>
/// Saved candidates stay until the team removes them (docs/architecture/talent-pool.md), so a migration drops the permission
/// period: the settings table and each application's copy of it. The note written when someone was saved becomes the entry's
/// own. Row-level security is forced on the tables the backfill touches, so the test runs it as the table owner with no
/// tenant set, as a real deployment does, and checks the notes arrive and FORCE comes back. A permission that had already run
/// out must not come back with the period gone, so the test holds lapsed ones too.
/// </summary>
public sealed class SavedCandidatesMigrationTests(AppHostFixture fixture)
{
    private const string Before = "RenameIntegrityToVerification";
    private const string After = "SavedCandidatesWithoutExpiry";

    [Fact]
    public async Task Saved_candidates_keep_their_trimmed_notes_lose_the_permission_period_and_get_it_back_when_undone()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await fixture.GetOwnerConnectionStringAsync(ct);
        var scratch = $"saved_{Guid.NewGuid():N}"[..24];
        await ExecuteAsync(owner, $"CREATE DATABASE {scratch}", ct);
        var connectionString = new NpgsqlConnectionStringBuilder(owner) { Database = scratch, Pooling = false }.ConnectionString;
        var migrator = $"migrator_{Guid.NewGuid():N}"[..24];
        var migratorPassword = Guid.NewGuid().ToString("N");
        try
        {
            await using (var db = Context(connectionString))
            {
                await db.GetService<IMigrator>().MigrateAsync(Before, ct);
            }

            var organizationId = Guid.CreateVersion7();
            var jobId = Guid.CreateVersion7();
            var firstCandidateId = Guid.CreateVersion7();
            var secondCandidateId = Guid.CreateVersion7();
            var firstApplicationId = Guid.CreateVersion7();
            var secondApplicationId = Guid.CreateVersion7();
            var firstEventId = Guid.CreateVersion7();
            var secondEventId = Guid.CreateVersion7();
            var firstEntryId = Guid.CreateVersion7();
            var secondEntryId = Guid.CreateVersion7();
            var lapsedCandidateId = Guid.CreateVersion7();
            var lapsedApplicationId = Guid.CreateVersion7();
            var lapsedEventId = Guid.CreateVersion7();
            var lapsedEntryId = Guid.CreateVersion7();
            var cappedCandidateId = Guid.CreateVersion7();
            var cappedApplicationId = Guid.CreateVersion7();
            const string emptyJson = "{}";
            const string paddedNoteJson = """{"note":"  Strong on billing  "}""";

            // The owner is a superuser here, which row-level security does not bind.
            await ExecuteAsync(connectionString, $"""
                INSERT INTO organizations (id, name, slug, created_at, embed_origins, home_region, indeed_eligible, is_trial, questions_per_page, signal_network_opt_in)
                VALUES ('{organizationId}', 'Saved Studio', 'saved-{organizationId:N}', now(), ARRAY[]::text[], 'EU', false, false, 0, false);
                INSERT INTO talent_pool_settings (organization_id, permission_months, updated_at) VALUES ('{organizationId}', 18, now());
                INSERT INTO candidates (id, organization_id, created_at, email, first_name, full_name, last_name, normalized_email)
                VALUES ('{firstCandidateId}', '{organizationId}', now(), 'first@example.com', 'Alex', 'Alex Rivera', 'Rivera', 'first@example.com'),
                       ('{secondCandidateId}', '{organizationId}', now(), 'second@example.com', 'Sam', 'Sam Okafor', 'Okafor', 'second@example.com'),
                       ('{lapsedCandidateId}', '{organizationId}', now(), 'lapsed@example.com', 'Lee', 'Lee Tran', 'Tran', 'lapsed@example.com'),
                       ('{cappedCandidateId}', '{organizationId}', now(), 'capped@example.com', 'Ana', 'Ana Silva', 'Silva', 'capped@example.com');
                INSERT INTO jobs (id, organization_id, applicant_location_requirements, created_at, description, employment_type, locations, published_version, slug, status, title, workflow, workflow_version, workplace_type)
                VALUES ('{jobId}', '{organizationId}', ARRAY[]::text[], now(), 'Keeps the books.', 'FullTime', '[]', 0, 'bookkeeper', 'Draft', 'Bookkeeper', '{emptyJson}', 1, 'Remote');
                INSERT INTO applications (id, organization_id, accommodation_requested, ai_use_declaration, answers, bot_check, candidate_id, current_stage_id, honeypot_filled, job_id, source, stage_entered_at, submitted_at, workflow_version, future_roles_consent_given_at, future_roles_consent_months, kept_in_mind_at)
                VALUES ('{firstApplicationId}', '{organizationId}', false, 'None', '[]', 'Passed', '{firstCandidateId}', 'applied', false, '{jobId}', 'Careers', now(), now(), 1, now(), 6, now()),
                       ('{secondApplicationId}', '{organizationId}', false, 'None', '[]', 'Passed', '{secondCandidateId}', 'applied', false, '{jobId}', 'Careers', now(), now(), 1, NULL, NULL, now()),
                       ('{lapsedApplicationId}', '{organizationId}', false, 'None', '[]', 'Passed', '{lapsedCandidateId}', 'applied', false, '{jobId}', 'Careers', now(), now(), 1, now() - interval '13 months', 12, now()),
                       ('{cappedApplicationId}', '{organizationId}', false, 'None', '[]', 'Passed', '{cappedCandidateId}', 'applied', false, '{jobId}', 'Careers', now(), now(), 1, now() - interval '20 months', 24, NULL);
                INSERT INTO application_events (id, organization_id, application_id, type, data, occurred_at)
                VALUES ('{firstEventId}', '{organizationId}', '{firstApplicationId}', 'KeptInMind', '{paddedNoteJson}', now()),
                       ('{secondEventId}', '{organizationId}', '{secondApplicationId}', 'KeptInMind', '{emptyJson}', now()),
                       ('{lapsedEventId}', '{organizationId}', '{lapsedApplicationId}', 'KeptInMind', '{emptyJson}', now());
                INSERT INTO talent_pool_entries (id, organization_id, candidate_id, application_id, job_id, kept_event_id, kept_at)
                VALUES ('{firstEntryId}', '{organizationId}', '{firstCandidateId}', '{firstApplicationId}', '{jobId}', '{firstEventId}', now()),
                       ('{secondEntryId}', '{organizationId}', '{secondCandidateId}', '{secondApplicationId}', '{jobId}', '{secondEventId}', now()),
                       ('{lapsedEntryId}', '{organizationId}', '{lapsedCandidateId}', '{lapsedApplicationId}', '{jobId}', '{lapsedEventId}', now());
                """, ct);

            // The test's owner is a superuser, which row-level security never binds, so the backfill would find its rows
            // whatever the migration did about FORCE. A real owner is bound: hand the tables to a role that is not a
            // superuser and run both directions as it.
            await ExecuteAsync(owner, $"CREATE ROLE {migrator} LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{migratorPassword}'", ct);
            await ExecuteAsync(connectionString, $"""
                GRANT ALL ON SCHEMA public TO {migrator};
                DO $$
                DECLARE item record;
                BEGIN
                    FOR item IN SELECT tablename AS name FROM pg_tables WHERE schemaname = 'public' LOOP
                        EXECUTE format('ALTER TABLE public.%I OWNER TO {migrator}', item.name);
                    END LOOP;
                    FOR item IN SELECT p.oid::regprocedure AS name FROM pg_proc p WHERE p.pronamespace = 'public'::regnamespace
                                  AND NOT EXISTS (SELECT FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e') LOOP
                        EXECUTE format('ALTER FUNCTION %s OWNER TO {migrator}', item.name);
                    END LOOP;
                END $$;
                """, ct);
            var migratorConnectionString = new NpgsqlConnectionStringBuilder(connectionString) { Username = migrator, Password = migratorPassword }.ConnectionString;

            await using (var db = Context(migratorConnectionString))
            {
                await db.GetService<IMigrator>().MigrateAsync(After, ct);
            }

            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM pg_class WHERE relname = 'talent_pool_settings'", ct))
                .Should().Be(0, "the permission period setting is gone");
            (await ColumnCountAsync(connectionString, "applications", "future_roles_consent_months", ct))
                .Should().Be(0, "each application's copy of the period is gone");
            (await ScalarAsync<long>(connectionString, $"SELECT count(*) FROM talent_pool_entries WHERE organization_id = '{organizationId}'", ct))
                .Should().Be(2, "every saved candidate whose permission still held survives");
            (await ScalarAsync<bool>(connectionString, $"SELECT future_roles_withdrawn_at IS NULL FROM applications WHERE id = '{firstApplicationId}'", ct))
                .Should().BeTrue("a permission still running has no end date now");
            (await ScalarAsync<bool>(connectionString, $"SELECT future_roles_withdrawn_at = future_roles_consent_given_at + interval '12 months' FROM applications WHERE id = '{lapsedApplicationId}'", ct))
                .Should().BeTrue("a permission that had run out stays ended, at the day it ran out");
            (await ScalarAsync<bool>(connectionString, $"SELECT future_roles_withdrawn_at = future_roles_consent_given_at + interval '18 months' FROM applications WHERE id = '{cappedApplicationId}'", ct))
                .Should().BeTrue("the organization's shorter period ended it, even with nobody saved");
            (await ScalarAsync<long>(connectionString, $"SELECT count(*) FROM talent_pool_entries WHERE id = '{lapsedEntryId}'", ct))
                .Should().Be(0, "someone the last sweep missed leaves the list");
            (await ScalarAsync<long>(connectionString, $"SELECT count(*) FROM application_events WHERE application_id = '{lapsedApplicationId}' AND type = 'LeftTalentPool' AND data ->> 'poolExit' = 'Expired'", ct))
                .Should().Be(1, "their Activity says why, as the sweep's did");
            (await ScalarAsync<string>(connectionString, $"SELECT coalesce(note, '<null>') FROM talent_pool_entries WHERE id = '{firstEntryId}'", ct))
                .Should().Be("Strong on billing", "the note written when they were saved is trimmed and becomes the entry's own");
            (await ScalarAsync<bool>(connectionString, $"SELECT note IS NULL FROM talent_pool_entries WHERE id = '{secondEntryId}'", ct))
                .Should().BeTrue("an entry saved without a note has none");
            (await ForceAsync(connectionString, "application_events", ct)).Should().BeTrue("the backfill puts FORCE back on application_events");
            (await ForceAsync(connectionString, "talent_pool_entries", ct)).Should().BeTrue("the backfill puts FORCE back on talent_pool_entries");
            (await UnforcedAsync(connectionString, ct)).Should().BeEmpty("every tenant table keeps FORCE ROW LEVEL SECURITY");

            await using (var db = Context(migratorConnectionString))
            {
                await db.GetService<IMigrator>().MigrateAsync(Before, ct);
            }

            (await ColumnCountAsync(connectionString, "talent_pool_entries", "note", ct))
                .Should().Be(0, "undoing the migration drops the entry's note");
            (await ScalarAsync<long>(connectionString, $"SELECT count(*) FROM talent_pool_entries WHERE organization_id = '{organizationId}'", ct))
                .Should().Be(2, "the entries stay when it is undone");
            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM pg_class WHERE relname = 'talent_pool_settings'", ct))
                .Should().Be(1, "the settings table comes back");
            (await ForceAsync(connectionString, "talent_pool_settings", ct)).Should().BeTrue("the restored settings table has row-level security forced");
            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM pg_policies WHERE schemaname = 'public' AND tablename = 'talent_pool_settings' AND policyname = 'tenant_isolation'", ct))
                .Should().Be(1, "the restored settings table has its tenant_isolation policy");
            (await ScalarAsync<int>(connectionString, $"SELECT future_roles_consent_months FROM applications WHERE id = '{firstApplicationId}'", ct))
                .Should().Be(12, "an application that carried a permission gets the old 12 months back");
            (await ScalarAsync<bool>(connectionString, $"SELECT future_roles_consent_months IS NULL FROM applications WHERE id = '{secondApplicationId}'", ct))
                .Should().BeTrue("an application without a permission stays without one");
            (await ForceAsync(connectionString, "applications", ct)).Should().BeTrue("the restore puts FORCE back on applications");
            (await UnforcedAsync(connectionString, ct)).Should().BeEmpty("undoing the migration restores FORCE on every tenant table");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(owner, $"DROP DATABASE IF EXISTS {scratch} WITH (FORCE)", CancellationToken.None);
            await ExecuteAsync(owner, $"DROP ROLE IF EXISTS {migrator}", CancellationToken.None);
        }
    }

    private static ScreenHireDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<ScreenHireDbContext>().UseNpgsql(connectionString).UseScreenHireConventions().Options, new TenantContext());

    // Every table with an organization_id keeps row-level security enabled and forced.
    private static Task<List<string>> UnforcedAsync(string connectionString, CancellationToken ct) =>
        NamesAsync(connectionString, """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'public'
            WHERE c.relkind = 'r'
              AND EXISTS (SELECT FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'organization_id' AND NOT a.attisdropped)
              AND NOT (c.relrowsecurity AND c.relforcerowsecurity)
            """, ct);

    private static Task<bool> ForceAsync(string connectionString, string table, CancellationToken ct) =>
        ScalarAsync<bool>(connectionString, $"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = '{table}' AND relkind = 'r'", ct);

    private static Task<long> ColumnCountAsync(string connectionString, string table, string column, CancellationToken ct) =>
        ScalarAsync<long>(connectionString, $"SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{table}' AND column_name = '{column}'", ct);

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
