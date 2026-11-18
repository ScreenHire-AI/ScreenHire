namespace ScreenHire.E2E;

/// <summary>
/// A new organization in the dark theme (design system 3.3): the onboarding page a new person lands on, and the pages
/// that show empty states or that only an organization this new shows, at 1440 px with axe's colour-contrast rule on.
/// One row per group of pages (<see cref="ProofWalk.EmptyGroups"/>). A class of its own, apart from
/// <see cref="DashboardThemeTests"/>, so CI can give the two walks to different shards.
/// </summary>
public sealed class DashboardThemeEmptyTests(E2EFixture fixture) : ProofWalk(fixture)
{
    [Theory]
    [MemberData(nameof(EmptyGroups), MemberType = typeof(ProofWalk))]
    public async Task Onboarding_and_the_empty_states_of_a_new_organization_pass_axe_in_the_dark_theme(string group)
    {
        await UseDarkThemeAsync();
        await Page.SetViewportSizeAsync(1440, 1000);

        async Task Check(string name)
        {
            if (!await Page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('dark')"))
            {
                Problems.Add($"{name}: the page is not in the dark theme");
            }

            await CollectAxeAsync(name);
            await CollectOverflowAsync(name);
            await CollectFieldEdgesAsync(name);
            _ = await ScreenshotAsync("dark-empty", name);
        }

        await WalkEmptyGroupAsync(group, "dark empty", Check);
        ExpectNoProblems($"The dark theme, empty ({group})");
    }
}
