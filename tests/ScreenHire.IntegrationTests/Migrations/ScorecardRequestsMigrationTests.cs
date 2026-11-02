#nullable enable

using Npgsql;
using NpgsqlTypes;

namespace ScreenHire.IntegrationTests.Migrations;

/// <summary>
/// Migration <c>AddScorecardRequests</c> finishes the review tasks a scorecard already answered. Both tables force
/// row-level security, which binds a table's owner unless it is a superuser, so the update has to lift FORCE around
/// itself. The test stack's owner is a superuser, which no policy ever binds: a migration run as that login would pass
/// without the lift. So this one runs the migration as a role that owns the tables and is not a superuser, the position
/// a hosted database's migration role is in.
/// </summary>
public sealed class ScorecardRequestsMigrationTests(AppHostFixture fixture)
{
    private const string Before = "ConvertPlainTextToRichText";
    private const string Target = "AddScorecardRequests";

    [Fact]
    public async Task Open_reviews_a_scorecard_already_answered_are_finished_by_a_migration_that_row_level_security_binds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scratch = await ScratchDatabase.CreateAsync(fixture, ct);
        await scratch.MigrateAsync(Before, ct);

        var role = $"migrator_{Guid.NewGuid():N}"[..20];
        var organizationId = Guid.CreateVersion7();
        var answered = Guid.CreateVersion7();
        var unanswered = Guid.CreateVersion7();
        var reviewer = Guid.CreateVersion7();
        var firstApplication = Guid.CreateVersion7();
        var secondApplication = Guid.CreateVersion7();
        var created = DateTimeOffset.UtcNow.AddDays(-2);
        await InsertAsync(scratch, "organizations", ct, ("id", organizationId), ("name", "Scorecard Studio"), ("slug", $"scorecards-{organizationId:N}"));
        // Foreign keys are off for these inserts, so the rows need no job, candidate or stage behind them.
        await InsertAsync(scratch, "application_tasks", ct, ("id", answered), ("organization_id", organizationId), ("application_id", firstApplication), ("kind", "Review"), ("created_at", created));
        await InsertAsync(scratch, "application_tasks", ct, ("id", unanswered), ("organization_id", organizationId), ("application_id", secondApplication), ("kind", "Review"), ("created_at", created));
        await InsertAsync(scratch, "application_scorecards", ct,
            ("organization_id", organizationId), ("application_id", firstApplication), ("reviewer_user_id", reviewer), ("submitted_at", created.AddHours(5)));

        // The migration's role owns every table and is not a superuser, so row-level security binds it.
        await scratch.ExecuteAsync($$"""
            CREATE ROLE {{role}} NOLOGIN NOSUPERUSER NOBYPASSRLS;
            GRANT ALL ON SCHEMA public TO {{role}};
            DO $$
            DECLARE t record;
            BEGIN
                FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
                    EXECUTE format('ALTER TABLE public.%I OWNER TO {{role}}', t.tablename);
                END LOOP;
            END $$;
            """, ct);
        (await scratch.ScalarAsync<bool>($"SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = '{role}'", ct)).Should().BeFalse();

        await scratch.MigrateAsync(Target, scratch.ConnectionStringAs(role), ct);

        (await scratch.ScalarAsync<bool>($"SELECT completed_at IS NOT NULL AND completed_by_user_id = '{reviewer}' FROM application_tasks WHERE id = '{answered}'", ct))
            .Should().BeTrue("the review its reviewer already answered is finished, by that reviewer");
        (await scratch.ScalarAsync<bool>($"SELECT completed_at IS NULL FROM application_tasks WHERE id = '{unanswered}'", ct))
            .Should().BeTrue("a review nobody answered stays open");
        (await scratch.UnforcedTablesAsync(ct)).Should().BeEmpty("FORCE is back on the tables the update lifted it from");
    }

    // One row with every column the table requires and a plausible value of its type, the given ones overriding. Run as the
    // stack's owner with foreign keys off, so the rows stand alone.
    private static async Task InsertAsync(ScratchDatabase scratch, string table, CancellationToken ct, params (string Column, object Value)[] given)
    {
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(ct);
        var columns = new List<(string Name, string Type)>();
        await using (var describe = new NpgsqlCommand(
            "SELECT column_name, data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @table AND is_nullable = 'NO' AND column_default IS NULL AND is_identity = 'NO'",
            connection))
        {
            describe.Parameters.AddWithValue("table", table);
            await using var reader = await describe.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                columns.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var overrides = given.ToDictionary(g => g.Column, g => g.Value);
        var names = columns.Select(c => c.Name).Union(overrides.Keys).ToList();
        await using (var replica = new NpgsqlCommand("SET session_replication_role = replica", connection))
        {
            await replica.ExecuteNonQueryAsync(ct);
        }

        var values = string.Join(", ", names.Select((_, i) => $"@p{i}"));
        await using var insert = new NpgsqlCommand($"INSERT INTO {table} ({string.Join(", ", names)}) VALUES ({values})", connection);
        for (var i = 0; i < names.Count; i++)
        {
            var parameter = overrides.TryGetValue(names[i], out var chosen)
                ? new NpgsqlParameter($"p{i}", chosen)
                : Plausible($"p{i}", names[i], columns.Single(c => c.Name == names[i]).Type);
            insert.Parameters.Add(parameter);
        }

        await insert.ExecuteNonQueryAsync(ct);
    }

    private static NpgsqlParameter Plausible(string parameter, string column, string type) => type switch
    {
        "uuid" => new(parameter, Guid.CreateVersion7()),
        "character varying" or "text" => new(parameter, "x"),
        "timestamp with time zone" => new(parameter, DateTimeOffset.UtcNow),
        "integer" => new(parameter, (object)0),
        "bigint" => new(parameter, (object)0L),
        "boolean" => new(parameter, false),
        "jsonb" => new(parameter, NpgsqlDbType.Jsonb) { Value = "[]" },
        _ => throw new NotSupportedException($"No plausible value for {column} ({type})."),
    };
}
