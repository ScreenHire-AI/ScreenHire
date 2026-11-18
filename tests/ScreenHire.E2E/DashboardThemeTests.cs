namespace ScreenHire.E2E;

/// <summary>
/// The dashboard in its dark theme (design system 3.3): every route, every tab and the overlays at 1440 px, each with axe's
/// colour-contrast rule on. The walk is one row per group of routes (<see cref="ProofWalk.WalkGroups"/>); each row collects
/// every failure and asserts once, so a run lists all the pages to fix. <see cref="DashboardThemeEmptyTests"/> walks a new
/// organization's pages the same way.
/// </summary>
public sealed class DashboardThemeTests(E2EFixture fixture) : ProofWalk(fixture)
{
    [Theory]
    [MemberData(nameof(WalkGroups), MemberType = typeof(ProofWalk))]
    public async Task Every_dashboard_page_tab_and_overlay_passes_axe_in_the_dark_theme(string group)
    {
        await PrepareAsync();
        await UseDarkThemeAsync();
        await Page.SetViewportSizeAsync(1440, 1000);

        async Task Check(string name)
        {
            // The theme follows the system until a person picks one, so a dark system opens the dashboard dark.
            var dark = await Page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('dark')");
            if (!dark)
            {
                Problems.Add($"{name}: the page is not in the dark theme");
            }

            await CollectAxeAsync(name);
            await CollectOverflowAsync(name);
            await CollectFieldEdgesAsync(name);
            _ = await ScreenshotAsync("dark", name);
        }

        await WalkGroupAsync(group, "dark", Check);
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
