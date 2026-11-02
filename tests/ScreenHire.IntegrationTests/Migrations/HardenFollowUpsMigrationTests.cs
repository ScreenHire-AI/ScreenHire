#nullable enable

using Npgsql;

namespace ScreenHire.IntegrationTests.Migrations;

/// <summary>
/// The migration that follows up the last workflow: trigram indexes for the search box, a foreign key from a review
/// action to its signal, a backfill of <c>change_records.created</c>, and <c>brand_images.saved_at</c> for uploads made
/// before the column. Each test writes the rows an older release left, then runs the migration over them.
/// </summary>
public sealed class HardenFollowUpsMigrationTests(AppHostFixture fixture)
{
    private const string Before = "RenameIntegrityToVerification";
    private const string Target = "HardenFromWorkflowFollowUps";

    [Fact]
    public async Task Creation_records_written_before_the_created_column_are_marked_and_uploads_count_as_saved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scratch = await ScratchDatabase.CreateAsync(fixture, ct);
        await scratch.MigrateAsync(Before, ct);

        var organizationId = Guid.CreateVersion7();
        await scratch.ExecuteAsync(OrganizationSql(organizationId), ct);

        var creations = new[]
        {
            Record(organizationId, "Job", "created the job from ", created: false),
            Record(organizationId, "Job", "created the job", created: false),
            Record(organizationId, "HiringTemplate", "created the template from ", created: false),
            Record(organizationId, "EmailTemplate", "created the template", created: false),
            Record(organizationId, "ScorecardTemplate", "created the scorecard", created: false),
            Record(organizationId, "Role", "created the role from ", created: false),
            Record(organizationId, "Team", "created the team", created: false),
        };
        var untouched = new[]
        {
            Record(organizationId, "Job", "changed the title", created: false),
            // Keys never set Created, whatever their first line says.
            Record(organizationId, "ApiKeys", "created the key ", created: false),
            // The phrase after the first line, or in another kind's record, is not a creation.
            Record(organizationId, "Job", "renamed the job", created: false, secondLine: "created the job"),
            Record(organizationId, "VerificationSettings", "created the job", created: false),
        };
        var alreadyMarked = Record(organizationId, "Job", "created the job", created: true);
        foreach (var record in creations.Concat(untouched).Append(alreadyMarked))
        {
            await scratch.ExecuteAsync(record.Sql, ct);
        }

        var uploadedAt = DateTimeOffset.UtcNow.AddDays(-40);
        var logo = Guid.CreateVersion7();
        var favicon = Guid.CreateVersion7();
        await scratch.ExecuteAsync($"""
            INSERT INTO brand_images (id, organization_id, kind, blob_name, content_type, size_bytes, width, height, scan_status, uploaded_at)
            VALUES ('{logo}', '{organizationId}', 'Logo', '{organizationId}/{logo}/logo/a.png', 'image/png', 100, 10, 10, 'Clean', '{uploadedAt:O}'),
                   ('{favicon}', '{organizationId}', 'Favicon', '{organizationId}/{favicon}/favicon/b.png', 'image/png', 100, 32, 32, 'Clean', '{uploadedAt.AddDays(3):O}');
            """, ct);

        await scratch.MigrateAsync(Target, ct);

        foreach (var record in creations.Append(alreadyMarked))
        {
            (await scratch.ScalarAsync<bool>($"SELECT created FROM change_records WHERE id = '{record.Id}'", ct))
                .Should().BeTrue($"{record.Describe} is a creation");
        }

        foreach (var record in untouched)
        {
            (await scratch.ScalarAsync<bool>($"SELECT created FROM change_records WHERE id = '{record.Id}'", ct))
                .Should().BeFalse($"{record.Describe} is not a creation");
        }

        (await scratch.ScalarAsync<bool>($"SELECT saved_at = uploaded_at FROM brand_images WHERE id = '{logo}'", ct))
            .Should().BeTrue("an upload from before the column counts as saved, so the clean-up never removes it");
        (await scratch.ScalarAsync<bool>($"SELECT saved_at = uploaded_at FROM brand_images WHERE id = '{favicon}'", ct)).Should().BeTrue();
        (await scratch.UnforcedTablesAsync(ct)).Should().BeEmpty("FORCE is back on every table the backfills lifted it from");

        (await scratch.ScalarAsync<long>("SELECT count(*) FROM pg_extension WHERE extname = 'pg_trgm'", ct)).Should().Be(1);
        (await scratch.ScalarAsync<long>(
            "SELECT count(*) FROM pg_indexes WHERE schemaname = 'public' AND indexname IN ('ix_candidates_full_name', 'ix_candidates_email', 'ix_jobs_title') AND indexdef LIKE '%USING gin%gin_trgm_ops%'", ct))
            .Should().Be(3, "the search box's three columns each have a trigram index");
        (await scratch.ScalarAsync<long>(
            "SELECT count(*) FROM pg_constraint WHERE conname LIKE 'fk_verification_review_actions_verification_signals%' AND contype = 'f'", ct))
            .Should().Be(1);

        await scratch.MigrateAsync(Before, ct);
        (await scratch.ScalarAsync<long>("SELECT count(*) FROM pg_indexes WHERE indexname = 'ix_jobs_title'", ct))
            .Should().Be(0, "undoing the migration drops what it added");
        (await scratch.UnforcedTablesAsync(ct)).Should().BeEmpty();
        await scratch.MigrateAsync(Target, ct);
    }

    [Fact]
    public async Task A_review_action_naming_a_signal_that_does_not_exist_stops_the_migration_and_says_so()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scratch = await ScratchDatabase.CreateAsync(fixture, ct);
        await scratch.MigrateAsync(Before, ct);

        var organizationId = Guid.CreateVersion7();
        await scratch.ExecuteAsync(OrganizationSql(organizationId), ct);
        // Replica mode skips the foreign keys, as a restore from a backup might, so the row has no application or signal.
        await scratch.ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO verification_review_actions (id, organization_id, application_id, assessment_id, type, signal_id, explain_reason, actor_user_id, occurred_at)
            VALUES ('{Guid.CreateVersion7()}', '{organizationId}', '{Guid.CreateVersion7()}', NULL, 'ReasonExplained', '{Guid.CreateVersion7()}', 'Other', '{Guid.CreateVersion7()}', now());
            """, ct);

        var act = () => scratch.MigrateAsync(Target, ct);

        (await act.Should().ThrowAsync<PostgresException>()).Which.MessageText
            .Should().Contain("name a signal that does not exist");
    }

    private static string OrganizationSql(Guid organizationId) => $"""
        INSERT INTO organizations (id, name, slug, created_at, embed_origins, home_region, indeed_eligible, is_trial, questions_per_page, signal_network_opt_in)
        VALUES ('{organizationId}', 'Harden Studio', 'harden-{organizationId:N}', now(), ARRAY[]::text[], 'EU', false, false, 0, false);
        """;

    private static SeededRecord Record(Guid organizationId, string kind, string firstLine, bool created, string? secondLine = null)
    {
        var id = Guid.CreateVersion7();
        var second = secondLine is null ? "" : $$"""
            ,{"parts":[{"text":"{{secondLine}}","strong":false}]}
            """;
        var lines = $$"""[{"parts":[{"text":"{{firstLine}}","strong":false},{"text":"Name","strong":true}]}{{second}}]""";
        var sql = $"""
            INSERT INTO change_records (id, organization_id, subject_kind, subject_id, created, lines, occurred_at)
            VALUES ('{id}', '{organizationId}', '{kind}', '{Guid.CreateVersion7()}', {(created ? "true" : "false")}, '{lines}', now());
            """;
        return new SeededRecord(id, sql, $"a {kind} record that starts \"{firstLine.Trim()}\"");
    }

    private sealed record SeededRecord(Guid Id, string Sql, string Describe);
}
