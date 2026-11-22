namespace ScreenHire.E2E;

/// <summary>
/// The Verification area in a real browser: four tabs under one header band, each at its own path. An owner walks all of them,
/// sees every check group's row and Save on one 1440x900 screen, turns a group off and changes the AI-use policy on Checks with
/// one Save and finds both saved after a reload; an old
/// tab's address lands on the tab that holds its settings now; a Recruiter has the three tabs their role may use, sees plain text
/// instead of switches, and is sent back to the Overview from AI use. Each test that makes an organization signs in as a new owner
/// of a new one.
/// </summary>
public sealed class VerificationAreaTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] EveryTab = ["Overview", "Activity log", "Checks", "AI use"];

    private static readonly string[] ReadOnlyTabs = ["Overview", "Activity log", "Checks"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ILocator Tabs => Page.GetByRole(AriaRole.Tablist, new() { Name = "Verification pages" }).GetByRole(AriaRole.Tab);

    [Fact]
    public async Task An_owner_walks_the_four_tabs_and_a_group_and_the_policy_saved_together_on_Checks_stay_saved()
    {
        await SignInAsNewOwnerAsync();
        await Page.SetViewportSizeAsync(1440, 900);
        await GoToAsync("dashboard", "/verification");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Verification");
        await Expect(Tabs).ToHaveTextAsync(EveryTab);
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "Overview" })).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Page.GetByText("Applications checked")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        await OpenTabAsync("Activity log", "/verification/activity");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Activity table" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Export CSV" })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Filter by outcome" }).SelectOptionAsync(new SelectOptionValue { Value = "Found" });
        await Expect(Page.GetByText("No activity matches.")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // Checks: every group's row and Save on one screen with nothing open, then Documents off and the AI-use policy to Not
        // allowed from the opened Answers row, one Save, and both found after a reload.
        await OpenTabAsync("Checks", "/verification/checks");
        var rows = Page.Locator("[data-check-group]");
        await Expect(rows).ToHaveCountAsync(6);
        for (var index = 0; index < 6; index++)
        {
            await Expect(rows.Nth(index)).ToBeInViewportAsync(new() { Ratio = 1 });
        }

        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save" })).ToBeInViewportAsync(new() { Ratio = 1 });
        var documents = Page.GetByRole(AriaRole.Switch, new() { Name = "Documents" });
        await Expect(documents).ToBeCheckedAsync();
        await documents.ClickAsync();
        var answers = Page.Locator("[data-check-group='Answers']");
        await answers.GetByRole(AriaRole.Button, new() { Expanded = false }).ClickAsync();
        await answers.GetByRole(AriaRole.Combobox, new() { Name = "AI use while applying", Exact = true }).SelectOptionAsync(new SelectOptionValue { Value = "NotAllowed" });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page.GetByText("Saved. It applies from the next application.")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Signal Network")).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Switch, new() { Name = "Documents" })).Not.ToBeCheckedAsync();
        await Expect(Page.GetByRole(AriaRole.Switch, new() { Name = "Network" })).ToBeCheckedAsync();
        await answers.GetByRole(AriaRole.Button, new() { Expanded = false }).ClickAsync();
        await Expect(answers.GetByRole(AriaRole.Combobox, new() { Name = "AI use while applying", Exact = true })).ToHaveValueAsync("NotAllowed");

        await OpenTabAsync("AI use", "/verification/ai-use");
        await Expect(Page.GetByText("Model calls", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Monthly limits" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "ScreenHire's key" })).ToBeCheckedAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // A link from before the tabs merged lands on the tab that holds its settings now.
        await GoToAsync("dashboard", "/verification/policy");
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/verification/checks"));
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "Checks" })).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Fact]
    public async Task The_owners_phone_has_the_same_four_tabs_and_no_sideways_page_scroll()
    {
        await SignInAsNewOwnerAsync();
        await Page.SetViewportSizeAsync(390, 844);
        await GoToAsync("dashboard", "/verification/checks");

        await Expect(Page.GetByRole(AriaRole.Switch, new() { Name = "Network" })).ToBeVisibleAsync();
        await Expect(Tabs).ToHaveTextAsync(EveryTab);
        (await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= document.documentElement.clientWidth"))
            .Should().BeTrue("the page never scrolls sideways; the tab strip does");

        // Every row open, its settings included, still fits the phone's width.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Expand all" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Textbox, new() { Name = "Trusted networks" })).ToBeVisibleAsync();
        (await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= document.documentElement.clientWidth"))
            .Should().BeTrue("an open row wraps to the phone's width");
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task A_recruiter_has_three_tabs_with_text_instead_of_switches_and_is_sent_back_from_AI_use()
    {
        const string recruiter = "jordan@brightwater.example.com"; // the seeded Brightwater Recruiter (DevSso/config.json)
        await SignInAsync(recruiter);
        await Page.SetViewportSizeAsync(1440, 1000);
        await GoToAsync("dashboard", "/verification");

        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Verification", Exact = true })).ToBeVisibleAsync();
        await Expect(Tabs).ToHaveTextAsync(ReadOnlyTabs);
        await Expect(Page.GetByText("Applications checked")).ToBeVisibleAsync();

        await OpenTabAsync("Activity log", "/verification/activity");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Activity table" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Export CSV" })).ToHaveCountAsync(0);

        await OpenTabAsync("Checks", "/verification/checks");
        await Expect(Page.Locator("[data-check-group]")).ToHaveCountAsync(6);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Expand all" }).ClickAsync();
        await Expect(Page.GetByText("AI use while applying")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Switch)).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Combobox)).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save" })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();

        await GoToAsync("dashboard", "/verification/ai-use");
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/verification"));
        await Expect(Page.GetByText("Applications checked")).ToBeVisibleAsync();
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
