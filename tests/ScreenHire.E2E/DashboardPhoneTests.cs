namespace ScreenHire.E2E;

/// <summary>
/// The dashboard on a phone (design system F1, F4, F5): every route, every tab and the overlays at 390 px with a touch
/// screen. No page scrolls sideways, nothing reaches past the window or is cut off without an ellipsis, a control is at
/// least 44 px to touch, and axe passes. The walk is one row per group of routes (<see cref="ProofWalk.WalkGroups"/>);
/// each row collects every failure and asserts once. <see cref="DashboardPhoneEmptyTests"/> walks a new organization's
/// pages the same way.
/// </summary>
public sealed class DashboardPhoneTests(E2EFixture fixture) : ProofWalk(fixture)
{
    [Theory]
    [MemberData(nameof(WalkGroups), MemberType = typeof(ProofWalk))]
    public async Task Every_dashboard_page_tab_and_overlay_fits_a_phone_and_is_touchable(string group)
    {
        await PrepareAsync();
        await UseTouchScreenAsync();
        await Page.SetViewportSizeAsync(390, 844);

        async Task Check(string name)
        {
            await CollectAxeAsync(name);
            await CollectOverflowAsync(name);
            await CollectFieldEdgesAsync(name);
            await CollectClippedAsync(name);
            await CollectSmallTargetsAsync(name);
            _ = await ScreenshotAsync("phone", name);
        }

        await WalkGroupAsync(group, "phone", Check);
        ExpectNoProblems($"The phone width ({group})");
    }
}
