namespace ScreenHire.E2E;

/// <summary>
/// A new organization on a phone (design system F1, F4, F5): the onboarding page a new person lands on, and the pages
/// that show empty states or that only an organization this new shows, at 390 px with a touch screen. One row per group
/// of pages (<see cref="ProofWalk.EmptyGroups"/>). A class of its own, apart from <see cref="DashboardPhoneTests"/>, so
/// CI can give the two walks to different shards.
/// </summary>
public sealed class DashboardPhoneEmptyTests(E2EFixture fixture) : ProofWalk(fixture)
{
    [Theory]
    [MemberData(nameof(EmptyGroups), MemberType = typeof(ProofWalk))]
    public async Task Onboarding_and_the_empty_states_of_a_new_organization_fit_a_phone_and_are_touchable(string group)
    {
        await UseTouchScreenAsync();
        await Page.SetViewportSizeAsync(390, 844);

        async Task Check(string name)
        {
            await CollectAxeAsync(name);
            await CollectOverflowAsync(name);
            await CollectFieldEdgesAsync(name);
            await CollectClippedAsync(name);
            await CollectSmallTargetsAsync(name);
            _ = await ScreenshotAsync("phone-empty", name);
        }

        await WalkEmptyGroupAsync(group, "phone empty", Check);
        ExpectNoProblems($"The phone width, empty ({group})");
    }
}
