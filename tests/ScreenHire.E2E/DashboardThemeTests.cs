namespace ScreenHire.E2E;

/// <summary>
/// The dashboard in its dark theme (design system 3.3): every route, every tab and the overlays at 1440 px, each with axe's
/// colour-contrast rule on. The walk is one row per group of routes; each row collects every failure and asserts once, so a
/// run lists all the pages to fix. This class walks the pages a team works in and the overlays
/// (<see cref="ProofWalk.WorkGroups"/>), <see cref="DashboardThemeSetupTests"/> the pages it sets things up in, and
/// <see cref="DashboardThemeEmptyTests"/> a new organization's pages. They are three classes because CI gives a whole class
/// to one shard, and the dark walk as one class outweighed an average shard.
/// </summary>
public sealed class DashboardThemeTests(E2EFixture fixture) : ProofWalk(fixture)
{
    [Theory]
    [MemberData(nameof(WorkGroups), MemberType = typeof(ProofWalk))]
    public async Task Every_dashboard_page_tab_and_overlay_passes_axe_in_the_dark_theme(string group)
    {
        await PrepareAsync();
        await UseDarkThemeAsync();
        await Page.SetViewportSizeAsync(1440, 1000);

        await WalkGroupAsync(group, "dark", name => CheckDarkAsync("dark", name));
        ExpectNoProblems($"The dark theme ({group})");
    }

    [Fact]
    public async Task The_walk_would_catch_text_that_stops_being_readable_in_the_dark_theme()
    {
        // A control for the walk above: a dark page whose heading is nearly the card's colour must fail axe's colour-contrast
        // rule, so a pass above means the check ran and looked.
        await PrepareAsync();
        await UseDarkThemeAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync();
        await Page.GotoAsync(DashboardUrl("/candidates"));
        await SettleAsync();
        (await AccessibilityProblemsAsync()).Should().BeNull("the candidates page passes as it is");

        _ = await Page.AddStyleTagAsync(new() { Content = "h1 { color: #2a2823 !important; }" });
        var problems = await AccessibilityProblemsAsync();
        problems.Should().NotBeNull().And.Contain("color-contrast");
    }
}
