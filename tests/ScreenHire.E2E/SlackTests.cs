using System.Net;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// Slack in a real browser, against the local sandbox workspace (docs/architecture/slack.md): an Owner connects it from
/// Settings > Slack through the OAuth round trip, picks #hiring and the events, links their own account from their menu,
/// and disconnects through the More menu and its dialog. A Recruiter, who has no Settings, links their own account from
/// their menu too. Each test signs in as a new owner of a new organization.
/// </summary>
public sealed class SlackTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_owner_connects_Slack_picks_a_channel_links_their_account_and_disconnects()
    {
        var owner = ApiSession.NewEmail("e2e-slack-owner");
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            await session.OnboardAsync(Ct);
        }

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, owner);
        await GoToAsync("dashboard", "/settings/slack");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Slack");
        await ExpectNoAccessibilityViolationsAsync();

        // Add to Slack goes to the sandbox's "Slack" and straight back, connected, with the outcome gone from the address.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add to Slack" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/slack"));
        await Expect(Page.Locator("[data-slot=header-band]")).ToContainTextAsync("Connected");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Workspace", Exact = true })).ToContainTextAsync("ScreenHire sandbox");

        // Pick #hiring from the channel list and the events, and save.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Pick a channel" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { NameRegex = new("^hiring") }).ClickAsync();
        await Expect(Page.GetByText("#hiring")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "New application" }).CheckAsync();
        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Job published" }).CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page.GetByText("Saved.")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-settings-slack-connected-1440");

        // The Owner links their own account from their menu, and comes back to this page with the sheet open.
        await LinkFromMenuAsync(Page);
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/slack"));

        // Disconnect is in the More menu, behind a dialog; afterwards the page offers Add to Slack again.
        await Page.Locator("[data-slot=header-band]").GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Disconnect Slack" }).ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Disconnect Slack?" });
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Disconnect Slack" }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Add to Slack" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_recruiter_with_no_settings_links_their_own_Slack_account_from_their_menu()
    {
        var owner = ApiSession.NewEmail("e2e-slack-owner");
        var recruiter = ApiSession.NewEmail("e2e-slack-recruiter");
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            await session.OnboardAsync(Ct);
            await ConnectSandboxAsync(session);
            using var invited = await session.PostAsync("/v1/organization/invitations", new { email = recruiter, role = "Recruiter" }, Ct);
            invited.StatusCode.Should().Be(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(Ct));
        }

        var token = await Fixture.Mailpit.WaitForInvitationTokenAsync(recruiter, Ct);
        using (var session = new ApiSession(Fixture))
        {
            using var accepted = await session.PostAsync("/v1/auth/invitations/accept", new { token }, Ct);
            accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync(Ct));
        }

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, recruiter);
        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Settings" })).ToHaveCountAsync(0);
        await LinkFromMenuAsync(Page);
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/"));
    }

    // The person's menu holds Slack account, whose sheet links the account through the sandbox's sign-in and opens again
    // on return, showing the link and the two switches.
    private async Task LinkFromMenuAsync(IPage page)
    {
        await page.Locator("[data-sidebar=footer]").GetByRole(AriaRole.Button).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Slack account" }).ClickAsync();
        var sheet = page.GetByRole(AriaRole.Dialog, new() { Name = "Slack account" });
        await Expect(sheet).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Link my Slack account" }).ClickAsync();
        await Expect(sheet).ToContainTextAsync(new Regex("Linked as"));
        await Expect(sheet.GetByRole(AriaRole.Switch, new() { Name = "Mentions" })).ToBeCheckedAsync();
        await Expect(sheet.GetByRole(AriaRole.Switch, new() { Name = "Tasks given to me" })).ToBeCheckedAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(sheet).ToHaveCountAsync(0);
    }

    // Connects the sandbox workspace through the API, as the page's Add to Slack does: start, then follow the redirects.
    private static async Task ConnectSandboxAsync(ApiSession session)
    {
        using var started = await session.PostAsync("/v1/slack/install", new { }, Ct);
        started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync(Ct));
        using var body = System.Text.Json.JsonDocument.Parse(await started.Content.ReadAsStringAsync(Ct));
        var url = new Uri(body.RootElement.GetProperty("authorizeUrl").GetString()!);
        for (var hop = 0; hop < 3; hop++)
        {
            using var response = await session.Client.GetAsync(url.PathAndQuery, Ct);
            if (response.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther))
            {
                break;
            }

            url = response.Headers.Location!;
            if (url.AbsolutePath == "/settings/slack")
            {
                url.Query.Should().Contain("slack=connected");
                return;
            }
        }

        throw new InvalidOperationException("Connecting the sandbox did not come back to /settings/slack.");
    }

    private async Task SignInAsync(IPage page, string email)
    {
        await page.GotoAsync(DashboardUrl("/sign-in"));
        await page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await page.GetByLabel("Any email address").FillAsync(email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
