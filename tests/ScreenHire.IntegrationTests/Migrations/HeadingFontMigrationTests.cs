#nullable enable

namespace ScreenHire.IntegrationTests.Migrations;

/// <summary>
/// The heading font went from one of six enum names to a Fontsource id with its family (ADR-0031). Nobody's choice
/// changes: each stored name becomes the same font's id, with the name History uses, and going back keeps the six.
/// </summary>
public sealed class HeadingFontMigrationTests(AppHostFixture fixture)
{
    private const string Before = "AddActivityFeed";
    private const string After = "HostAnyHeadingFont";

    [Fact]
    public async Task Every_saved_font_becomes_the_same_fonts_id_and_family_and_going_back_keeps_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scratch = await ScratchDatabase.CreateAsync(fixture, ct);
        await scratch.MigrateAsync(Before, ct);

        var playfair = Guid.CreateVersion7();
        var inter = Guid.CreateVersion7();
        var none = Guid.CreateVersion7();
        string Organization(Guid id, string? font) => $"""
            INSERT INTO organizations (id, name, slug, created_at, embed_origins, home_region, indeed_eligible, is_trial, questions_per_page, signal_network_opt_in, heading_font)
            VALUES ('{id}', 'Font Studio', 'font-{id:N}', now(), ARRAY[]::text[], 'EU', false, false, 0, false, {(font is null ? "NULL" : $"'{font}'")});
            """;

        // The owner is a superuser here, which row-level security does not bind.
        await scratch.ExecuteAsync(Organization(playfair, "PlayfairDisplay") + Organization(inter, "Inter") + Organization(none, null), ct);

        await scratch.MigrateAsync(After, ct);

        async Task<string?> ReadAsync(string column, Guid id) =>
            await scratch.ScalarAsync<object>($"SELECT {column} FROM organizations WHERE id = '{id}'", ct) is string value ? value : null;

        (await ReadAsync("heading_font", playfair)).Should().Be("playfair-display");
        (await ReadAsync("heading_font_family", playfair)).Should().Be("Playfair Display");
        (await ReadAsync("heading_font", inter)).Should().Be("inter");
        (await ReadAsync("heading_font_family", inter)).Should().Be("Inter");
        (await ReadAsync("heading_font", none)).Should().BeNull("ScreenHire's font stays the default");
        (await ReadAsync("heading_font_family", none)).Should().BeNull();
        (await scratch.UnforcedTablesAsync(ct)).Should().BeEmpty("FORCE comes back on organizations after the data step");

        await scratch.ExecuteAsync($"UPDATE organizations SET heading_font = 'roboto-slab', heading_font_family = 'Roboto Slab' WHERE id = '{inter}'", ct);
        await scratch.MigrateAsync(Before, ct);

        (await ReadAsync("heading_font", playfair)).Should().Be("PlayfairDisplay", "the previous release reads its own enum name");
        (await ReadAsync("heading_font", inter)).Should().BeNull("a font the previous release has no name for goes back to ScreenHire's");
        (await scratch.UnforcedTablesAsync(ct)).Should().BeEmpty();
    }
}
