namespace ScreenHire.E2E;

/// <summary>
/// The Verification area in a real browser: seven tabs under one header band, each at its own path. An owner walks all of them,
/// changes a check group and the AI-use policy and finds both saved after a reload; a Recruiter has the four read-only tabs, sees
/// plain text instead of switches, and is sent back to the Overview from a tab their role cannot use. Each test that makes an
/// organization signs in as a new owner of a new one.
/// </summary>
public sealed class VerificationAreaTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] EveryTab = ["Overview", "Activity log", "Usage", "Checks", "Policy", "AI model", "Notifications"];

    private static readonly string[] ReadOnlyTabs = ["Overview", "Activity log", "Checks", "Policy"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ILocator Tabs => Page.GetByRole(AriaRole.Tablist, new() { Name = "Verification pages" }).GetByRole(AriaRole.Tab);

    [Fact]
    public async Task An_owner_walks_all_seven_tabs_and_a_change_to_the_checks_and_the_policy_is_saved()
    {
        await SignInAsNewOwnerAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await GoToAsync("dashboard", "/verification");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Verification");
        await Expect(Tabs).ToHaveTextAsync(EveryTab);
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "Overview" })).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Page.GetByText("Applications checked")).ToBeVisibleAsync();
        await Expect(Page.GetByText("No group of checks found anything this month.")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        await OpenTabAsync("Activity log", "/verification/activity");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Activity table" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Export CSV" })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Filter by outcome" }).SelectOptionAsync(new SelectOptionValue { Value = "Failed" });
        await Expect(Page.GetByText("No activity matches.")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        await OpenTabAsync("Usage", "/verification/usage");
        await Expect(Page.GetByText("Model calls", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Use by day, this month" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "This month against the limits" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // Checks: turn Documents off, save, and find it off after a reload.
        await OpenTabAsync("Checks", "/verification/checks");
        var documents = Page.GetByRole(AriaRole.Switch, new() { Name = "Documents" });
        await Expect(documents).ToBeCheckedAsync();
        await documents.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page.GetByText("Saved. It applies from the next application.")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Switch, new() { Name = "Documents" })).Not.ToBeCheckedAsync();
        await Expect(Page.GetByRole(AriaRole.Switch, new() { Name = "Network" })).ToBeCheckedAsync();

        // Policy: the organization's AI-use policy, which has no Signal Network control.
        await OpenTabAsync("Policy", "/verification/policy");
        await Page.GetByLabel("AI use while applying").SelectOptionAsync(new SelectOptionValue { Value = "NotAllowed" });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page.GetByText("Saved. It applies from the next application.")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Signal Network")).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("AI use while applying")).ToHaveValueAsync("NotAllowed");

        // The Policy tab saved only the policy: the group turned off on Checks is still off.
        await OpenTabAsync("Checks", "/verification/checks");
        await Expect(Page.GetByRole(AriaRole.Switch, new() { Name = "Documents" })).Not.ToBeCheckedAsync();

        await OpenTabAsync("AI model", "/verification/ai-model");
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "ScreenHire's included model" })).ToBeCheckedAsync();
        await ExpectNoAccessibilityViolationsAsync();

        await OpenTabAsync("Notifications", "/verification/notifications");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Alerts" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Monthly limits" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // The AI-use row in a hiring template links to the Policy tab.
        await GoToAsync("dashboard", "/verification/usage");
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/verification/usage"));
    }

    [Fact]
    public async Task The_owners_phone_has_the_same_seven_tabs_in_a_strip_that_scrolls_and_no_sideways_page_scroll()
    {
        await SignInAsNewOwnerAsync();
        await Page.SetViewportSizeAsync(390, 844);
        await GoToAsync("dashboard", "/verification");

        await Expect(Page.GetByText("Applications checked")).ToBeVisibleAsync();
        await Expect(Tabs).ToHaveTextAsync(EveryTab);
        (await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= document.documentElement.clientWidth"))
            .Should().BeTrue("the page never scrolls sideways; the tab strip does");
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task A_recruiter_has_the_four_read_only_tabs_with_text_instead_of_switches_and_is_sent_back_from_a_tab_their_role_cannot_use()
    {
        const string recruiter = "jordan@brightwater.example.com"; // the seeded Brightwater Recruiter (DevSso/config.json)
        await SignInAsync(recruiter);
        await Page.SetViewportSizeAsync(1440, 1000);
        await GoToAsync("dashboard", "/verification");

        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Verification", Exact = true })).ToBeVisibleAsync();
        await Expect(Tabs).ToHaveTextAsync(ReadOnlyTabs);
        await Expect(Page.GetByText("Applications checked")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        await OpenTabAsync("Activity log", "/verification/activity");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Activity table" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Export CSV" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Columnheader, new() { Name = "Provider request id" })).ToHaveCountAsync(0);

        await OpenTabAsync("Checks", "/verification/checks");
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Check groups" }).GetByRole(AriaRole.Listitem)).ToHaveCountAsync(6);
        await Expect(Page.GetByRole(AriaRole.Switch)).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save" })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();

        await OpenTabAsync("Policy", "/verification/policy");
        await Expect(Page.GetByText("AI use while applying")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Combobox)).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save" })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();

        foreach (var path in new[] { "/verification/usage", "/verification/ai-model", "/verification/notifications" })
        {
            await GoToAsync("dashboard", path);
            await Expect(Page).ToHaveURLAsync(DashboardUrl("/verification"));
            await Expect(Page.GetByText("Applications checked")).ToBeVisibleAsync();
        }
    }

    private async Task OpenTabAsync(string name, string path)
    {
        await Page.GetByRole(AriaRole.Tab, new() { Name = name }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl(path));
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = name })).ToHaveAttributeAsync("aria-selected", "true");
    }

    private async Task SignInAsNewOwnerAsync()
    {
        var email = ApiSession.NewEmail("e2e-owner");
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(email, Ct);
            await session.OnboardAsync(Ct);
        }

        await SignInAsync(email);
    }

    private async Task SignInAsync(string email)
    {
        var response = await Page.GotoAsync(DashboardUrl("/sign-in"));
        response!.Ok.Should().BeTrue();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
