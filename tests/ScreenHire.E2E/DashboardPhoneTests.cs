namespace ScreenHire.E2E;

/// <summary>
/// The dashboard on a phone (design system F1, F4, F5): every route, every tab and the overlays at 390 px with a touch
/// screen. No page scrolls sideways, nothing reaches past the window or is cut off without an ellipsis, a control is at
/// least 44 px to touch, and axe passes. The walk collects every failure and asserts once.
/// </summary>
public sealed class DashboardPhoneTests(E2EFixture fixture) : ProofWalk(fixture)
{
    [Fact]
    public async Task Every_dashboard_page_tab_and_overlay_fits_a_phone_and_is_touchable()
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

        await GoToAsync("dashboard", "/sign-in");
        await SettleAsync();
        await Check("/sign-in");

        await SignInAsync();
        foreach (var path in Routes())
        {
            await WalkAsync(path, "phone", Check);
        }

        await WalkOverlaysAsync("phone", Check);
        ExpectNoProblems("The phone width");
    }

    [Fact]
    public async Task Onboarding_and_the_empty_states_of_a_new_organization_fit_a_phone_and_are_touchable()
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

        await EmptyOrganizationAsync(Check);
        foreach (var path in EmptyRoutes())
        {
            await WalkAsync(path, "phone empty", Check);
        }

        ExpectNoProblems("The phone width, empty");
    }
}
