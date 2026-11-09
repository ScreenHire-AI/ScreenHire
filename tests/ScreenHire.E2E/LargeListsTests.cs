using System.Globalization;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// Design system S9 in a real browser: an organization with the volume a small team reaches (<see cref="BusyOrganization"/>)
/// opens every list and the pipeline board at 1440 and 390 px. Each page must say how many there are, stay inside the
/// window sideways, keep its search usable and not grow without bound. The organization is built once through the API.
/// </summary>
public sealed class LargeListsTests(E2EFixture fixture) : BrowserTest(fixture)
{
    // A page of a phone's list: ApplicationTable's and the Jobs page's phonePageSize.
    private const int PhonePage = 50;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<int, int> Windows => new() { { 1440, 1000 }, { 390, 844 } };

    [Theory]
    [MemberData(nameof(Windows))]
    public async Task Jobs_and_the_three_kinds_of_template_show_their_count_and_search_narrows_them(int width, int height)
    {
        _ = await SignedInAsync(width, height);

        await GoToPageAsync("/", "Jobs");
        await ExpectCountAsync("26 jobs");
        await ExpectFitsAsync($"jobs-{width}");
        await SearchAsync("Kitchen Manager 01", "Kitchen Manager 01");

        foreach (var (tab, prefix) in new[] { ("hiring", "Hiring template"), ("scorecards", "Scorecard"), ("emails", "Email template") })
        {
            await GoToPageAsync(tab == "hiring" ? "/templates" : $"/templates?tab={tab}", "Templates");
            (await TabCountAsync(tab)).Should().BeGreaterThanOrEqualTo(BusyOrganization.Count, $"the {tab} tab counts every template the organization made");
            await ExpectFitsAsync($"templates-{tab}-{width}");
            await SearchAsync($"{prefix} 07", $"{prefix} 07");
        }
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public async Task A_job_with_two_hundred_applications_pages_on_a_phone_and_scrolls_inside_itself_on_a_desktop(int width, int height)
    {
        var org = await SignedInAsync(width, height);

        await Page.GotoAsync(DashboardUrl($"/jobs/{org.BusyJobId}"));
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Candidates", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Candidates") })).ToContainTextAsync(BusyOrganization.Number(BusyOrganization.Applications));
        await ExpectFitsAsync($"job-table-{width}");

        if (width < 768)
        {
            // A phone lists a page and offers the rest: the page is not as tall as the job is busy.
            var list = Page.GetByRole(AriaRole.List, new() { NameRegex = new("^Candidates") });
            await Expect(list.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(PhonePage);
            await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = $"Showing {PhonePage} of {BusyOrganization.Applications}" })).ToBeVisibleAsync();
            (await PageHeightAsync()).Should().BeLessThan(height * 25, "fifty candidates and a button make a page, not two hundred");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Show more" }).ClickAsync();
            await Expect(list.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(PhonePage * 2);
            return;
        }

        // The table is virtual: only the rows in view are in the page, and the table scrolls in its panel.
        var rows = Page.GetByRole(AriaRole.Table, new() { Name = "Candidates" }).GetByRole(AriaRole.Row);
        (await rows.CountAsync()).Should().BeLessThan(BusyOrganization.Applications / 2, "a table of 210 candidates draws the rows in view");
        await SearchAsync("Number150", "Applicant150 Number150");

        // The board keeps its stage a column that scrolls by itself: the window does not grow with the cards in it.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Board", Exact = true }).ClickAsync();
        var applied = Page.GetByRole(AriaRole.List, new() { Name = "Applied stage" });
        await Expect(applied.GetByRole(AriaRole.Listitem).First).ToBeVisibleAsync();
        await Expect(applied.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(BusyOrganization.Applications);
        var sizes = await applied.EvaluateAsync<int[]>("el => [el.scrollHeight, el.clientHeight]");
        sizes[0].Should().BeGreaterThan(sizes[1], "the stage's cards scroll inside the column");
        (await PageHeightAsync()).Should().BeLessThanOrEqualTo(height * 3, "the board fills the window and the stage scrolls inside it");
        await ExpectFitsAsync($"job-board-{width}");
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public async Task Candidates_tasks_team_keys_and_webhooks_show_their_count_and_stay_inside_the_window(int width, int height)
    {
        _ = await SignedInAsync(width, height);

        await GoToPageAsync("/candidates", "Candidates");
        await ExpectFitsAsync($"candidates-{width}");
        if (width < 768)
        {
            await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Candidates", Exact = true }).GetByRole(AriaRole.Listitem)).ToHaveCountAsync(PhonePage);
            (await PageHeightAsync()).Should().BeLessThan(height * 25);
        }

        await GoToPageAsync("/tasks", "My tasks");
        await ExpectFitsAsync($"tasks-{width}");

        await GoToPageAsync("/settings/team", "Team");
        await ExpectCountAsync("26 members");
        await ExpectFitsAsync($"team-{width}");
        await SearchAsync("member07", "member07");

        await GoToPageAsync("/settings/api-keys", "API keys");
        await ExpectCountAsync("26 keys");
        await ExpectFitsAsync($"api-keys-{width}");
        await SearchAsync("Integration key 07", "Integration key 07");

        await GoToPageAsync("/settings/webhooks", "Webhooks");
        await ExpectCountAsync($"{BusyOrganization.WebhookEndpoints} endpoints");
        await ExpectFitsAsync($"webhooks-{width}");
        await SearchAsync("Receiver 07", "Receiver 07");
    }

    [Fact]
    public async Task The_twenty_first_webhook_endpoint_is_refused_in_plain_words()
    {
        var org = await SignedInAsync(1440, 1000);
        org.Slug.Should().NotBeNull();

        // The limit is a product rule (each event goes to every endpoint), so the form says so and what to do about it.
        await Page.GotoAsync(DashboardUrl("/settings/webhooks/new"));
        await Page.GetByLabel("URL").FillAsync("https://hooks.example.com/in/21");
        await Page.GetByRole(AriaRole.Checkbox).First.CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add endpoint" }).ClickAsync();
        await Expect(Page.GetByText("An organization has at most 20 webhook endpoints. Delete one first.")).ToBeVisibleAsync();
    }

    private async Task<BusyOrganization> SignedInAsync(int width, int height)
    {
        var org = await BusyOrganization.GetAsync(Fixture, Ct);
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync(DashboardUrl("/sign-in"));
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(org.OwnerEmail);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
        return org;
    }

    private async Task GoToPageAsync(string path, string heading)
    {
        await Page.GotoAsync(DashboardUrl(path));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(heading);
    }

    private Task ExpectCountAsync(string text) => Expect(Page.Locator("[data-slot=header-band]").First).ToContainTextAsync(text);

    // The tab's count is the number in its name, "Hiring 29".
    private async Task<int> TabCountAsync(string tab)
    {
        var name = tab switch { "hiring" => "Hiring", "scorecards" => "Scorecards", _ => "Emails" };
        var tabLocator = Page.GetByRole(AriaRole.Tab, new() { NameRegex = new($"^{name}") });
        await Expect(tabLocator).ToContainTextAsync(new Regex(@"\d+"));
        var text = await tabLocator.InnerTextAsync();
        return int.Parse(Regex.Match(text, @"\d+").Value, CultureInfo.InvariantCulture);
    }

    // Typing narrows the list to the row, and clearing brings the rest back.
    private async Task SearchAsync(string term, string expectedText)
    {
        var box = Page.GetByRole(AriaRole.Searchbox).First;
        await box.FillAsync(term);
        await Expect(Page.GetByText(expectedText).Filter(new() { Visible = true }).First).ToBeVisibleAsync();
        await box.FillAsync("");
    }

    private Task<int> PageHeightAsync() => Page.EvaluateAsync<int>("() => document.documentElement.scrollHeight");

    // Nothing pushes the page sideways: the one S9 failure a screenshot shows last.
    private async Task ExpectFitsAsync(string name)
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var widths = await Page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth, window.innerWidth]");
        _ = await SaveScreenshotAsync(Page, $"large-lists-{name}");
        widths[0].Should().BeLessThanOrEqualTo(widths[1], $"{name} scrolls sideways ({widths[0]} wide in a {widths[1]} window)");
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
