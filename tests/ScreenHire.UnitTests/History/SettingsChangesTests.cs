using ScreenHire.Domain.History;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.History;

/// <summary>
/// The settings pages' history lines (change-history.md): the organization's name, its brand colour and its integrity
/// settings, each only when it changed.
/// </summary>
public sealed class SettingsChangesTests
{
    [Fact]
    public void A_rename_is_the_names_history_and_nothing_in_the_brand_colours()
    {
        var before = Organization("Brightwater");
        var after = Organization("Brightwater Studio");

        Sentences(SettingsChanges.Name(before, after)).Should().Equal("renamed the organization from Brightwater to Brightwater Studio");
        SettingsChanges.Branding(before, after).Should().BeEmpty();
        SettingsChanges.Name(null, after).Should().BeEmpty("making the organization is not a change on its settings page");
    }

    [Fact]
    public void Each_site_that_may_embed_the_careers_page_has_its_own_line_on_the_organizations_history()
    {
        var none = Organization("Brightwater");
        var one = Organization("Brightwater");
        one.SetEmbedOrigins(["https://www.example.com"], allowLoopback: false);
        var other = Organization("Brightwater");
        other.SetEmbedOrigins(["https://jobs.example.com"], allowLoopback: false);

        Sentences(SettingsChanges.Name(none, one)).Should().Equal("let https://www.example.com embed the careers page");
        Sentences(SettingsChanges.Name(one, other)).Should().Equal(
            "let https://jobs.example.com embed the careers page", "stopped https://www.example.com embedding the careers page");
        SettingsChanges.Name(one, one).Should().BeEmpty();
        SettingsChanges.Branding(none, one).Should().BeEmpty();
    }

    [Fact]
    public void The_brand_colour_says_whether_it_was_set_changed_or_put_back_to_the_default()
    {
        var plain = Organization("Brightwater");
        var teal = Organization("Brightwater", "#1f4e5a");
        var indigo = Organization("Brightwater", "#2b2788");

        Sentences(SettingsChanges.Branding(plain, teal)).Should().Equal("set the brand colour to #1f4e5a");
        Sentences(SettingsChanges.Branding(teal, indigo)).Should().Equal("changed the brand colour from #1f4e5a to #2b2788");
        Sentences(SettingsChanges.Branding(indigo, plain)).Should().Equal("went back to ScreenHire's default colour");
        SettingsChanges.Name(teal, indigo).Should().BeEmpty();
    }

    [Fact]
    public void The_branding_names_each_part_that_changed_in_one_save()
    {
        var plain = Organization("Brightwater");
        var logo = Guid.NewGuid();
        var branded = Organization("Brightwater");
        branded.SetBranding(null, "#e9dcc4", HeadingFont.PlayfairDisplay, logo, null, null);
        var rebranded = Organization("Brightwater");
        rebranded.SetBranding(null, "#1f4e5a", null, Guid.NewGuid(), Guid.NewGuid(), null);

        Sentences(SettingsChanges.Branding(plain, branded)).Should().Equal(
            "set the second colour to #e9dcc4", "changed the heading font to Playfair Display", "added a logo");
        Sentences(SettingsChanges.Branding(branded, rebranded)).Should().Equal(
            "changed the second colour from #e9dcc4 to #1f4e5a", "went back to ScreenHire's heading font", "replaced the logo", "added a favicon");
        Sentences(SettingsChanges.Branding(rebranded, plain)).Should().Equal("removed the second colour", "removed the logo", "removed the favicon");
        SettingsChanges.Name(plain, branded).Should().BeEmpty();
    }

    [Fact]
    public void The_integrity_settings_name_each_change_and_compare_a_first_save_with_the_defaults()
    {
        var on = new IntegritySettings { LlmConsistencyCheckEnabled = true, MonthlyTokenBudget = 250_000 };
        var off = new IntegritySettings { LlmConsistencyCheckEnabled = false, MonthlyTokenBudget = null };

        Sentences(SettingsChanges.Integrity(null, on)).Should().Equal("turned on the consistency check", "set the monthly token budget to 250,000");
        Sentences(SettingsChanges.Integrity(on, off)).Should().Equal("turned off the consistency check", "went back to ScreenHire's default token budget");
        SettingsChanges.Integrity(null, off).Should().BeEmpty("saving the defaults changes nothing");
    }

    private static Organization Organization(string name, string? brandColor = null)
    {
        var organization = new Organization { Name = name, Slug = "brightwater", HomeRegion = "eu" };
        organization.SetBrandColor(brandColor);
        return organization;
    }

    private static IEnumerable<string> Sentences(IReadOnlyList<ChangeLine> lines) => lines.Select(line => line.Sentence());
}
