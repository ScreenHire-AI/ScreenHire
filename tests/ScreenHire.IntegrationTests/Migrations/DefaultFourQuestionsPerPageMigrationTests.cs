namespace ScreenHire.IntegrationTests.Migrations;

/// <summary>
/// The default page size goes from 8 to 4 (apply-flow.md section 7). An organization on 8 that never changed the setting
/// moves to 4; one whose history shows someone set it, or that is on any other number, keeps its number; and both tables
/// keep row-level security forced.
/// </summary>
public sealed class DefaultFourQuestionsPerPageMigrationTests(AppHostFixture fixture)
{
    private const string Before = "AddFormLibrary";
    private const string After = "DefaultFourQuestionsPerPage";

    [Fact]
    public async Task Organizations_on_the_old_default_move_to_four_and_a_chosen_number_stays()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scratch = await ScratchDatabase.CreateAsync(fixture, ct);
        await scratch.MigrateAsync(Before, ct);

        var (untouched, chose8, chose12) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        string Organization(Guid id, int perPage) => $"""
            INSERT INTO organizations (id, name, slug, created_at, embed_origins, home_region, indeed_eligible, is_trial, questions_per_page, signal_network_opt_in, setup_hidden)
            VALUES ('{id}', 'Studio {id:N}', 'org-{id:N}', now(), ARRAY[]::text[], 'EU', false, false, {perPage}, false, false);
            """;
        // A history line as SettingsChanges writes it: the lead in plain text, then the new value in bold.
        string Line(Guid id, string lead, string value) => $$"""
            INSERT INTO change_records (id, organization_id, subject_kind, subject_id, created, lines, occurred_at)
            VALUES ('{{Guid.CreateVersion7()}}', '{{id}}', 'Organization', '{{id}}', false,
                    '[{"parts":[{"text":"{{lead}}","strong":false},{"text":"{{value}}","strong":true}]}]', now());
            """;
        string Renamed(Guid id) => Line(id, "renamed the organization from Harbour Lane to ", "Harbour Lane Studio");
        string SetPerPage(Guid id, int from, int to) =>
            Line(id, $"changed questions per page from {from} to ", to.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // The owner is a superuser here, which row-level security does not bind.
        await scratch.ExecuteAsync($"""
            {Organization(untouched, 8)}
            {Renamed(untouched)}
            {Organization(chose8, 8)}
            {SetPerPage(chose8, 12, 8)}
            {Organization(chose12, 12)}
            {SetPerPage(chose12, 8, 12)}
            """, ct);

        await scratch.MigrateAsync(After, ct);

        (await PerPageAsync(scratch, untouched, ct)).Should().Be(4, "8 was the default and nobody changed it, only the name");
        (await PerPageAsync(scratch, chose8, ct)).Should().Be(8, "someone set 8 on purpose");
        (await PerPageAsync(scratch, chose12, ct)).Should().Be(12);
        (await scratch.UnforcedTablesAsync(ct)).Should().BeEmpty("FORCE comes back on the tables the update lifted it from");

        await scratch.MigrateAsync(Before, ct);

        (await PerPageAsync(scratch, untouched, ct)).Should().Be(8);
        (await PerPageAsync(scratch, chose8, ct)).Should().Be(8);
        (await PerPageAsync(scratch, chose12, ct)).Should().Be(12);
        (await scratch.UnforcedTablesAsync(ct)).Should().BeEmpty();
    }

    private static Task<int> PerPageAsync(ScratchDatabase scratch, Guid organization, CancellationToken ct) =>
        scratch.ScalarAsync<int>($"SELECT questions_per_page FROM organizations WHERE id = '{organization}'", ct);
}
