namespace ScreenHire.E2E;

/// <summary>
/// The other half of the dark theme's walk (<see cref="DashboardThemeTests"/>): the pages a team sets things up in, which
/// are Verification, the organization's settings and the integrations (<see cref="ProofWalk.SetupGroups"/>), at 1440 px
/// with axe's colour-contrast rule on.
/// </summary>
public sealed class DashboardThemeSetupTests(E2EFixture fixture) : ProofWalk(fixture)
{
    [Theory]
    [MemberData(nameof(SetupGroups), MemberType = typeof(ProofWalk))]
    public async Task Every_setup_page_and_tab_passes_axe_in_the_dark_theme(string group)
    {
        await PrepareAsync();
        await UseDarkThemeAsync();
        await Page.SetViewportSizeAsync(1440, 1000);

        await WalkGroupAsync(group, "dark", name => CheckDarkAsync("dark", name));
        ExpectNoProblems($"The dark theme ({group})");
    }
}
