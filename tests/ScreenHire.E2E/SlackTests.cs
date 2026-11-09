using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ScreenHire.Application.Slack;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.E2E;

/// <summary>
/// Slack in a real browser, against the local sandbox workspace (docs/architecture/slack.md): an Owner connects it from
/// Settings > Slack through the OAuth round trip, picks #hiring and the events, links their own account from their menu,
/// and disconnects through the More menu and its dialog. A Recruiter, who has no Settings, links their own account from
/// their menu too. Each test signs in as a new owner of a new organization.
/// </summary>
public sealed class SlackTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] Gb = ["GB"];

    private static readonly string[] JobPublishedOnly = ["JobPublished"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_owner_connects_Slack_picks_a_channel_links_their_account_and_disconnects()
    {
        var owner = ApiSession.NewEmail("e2e-slack-owner");
        // Kept open to the end: it reads what the sandbox workspace received and publishes a job as the same owner.
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await session.OnboardAsync(Ct);

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

        // Send test message posts to the saved channel at once, and the sandbox workspace holds it: the page's "sent" is
        // not taken on its word.
        await Page.Locator("[data-slot=header-band]").GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Send test message" }).ClickAsync();
        await SlackSandbox.WaitForMessageAsync(session, SlackSandbox.HiringChannel, "ScreenHire is connected", Ct);

        // An event the owner picked reaches #hiring through the worker: publishing a job posts "is published".
        var title = $"Slack check {Guid.NewGuid():N}"[..24];
        using var created = await session.PostAsync("/v1/jobs", new
        {
            title,
            description = "<p>Build and run the things people rely on.</p>",
            employmentType = "FullTime",
            workplaceType = "Remote",
            applicantLocationRequirements = Gb,
            templateVersionId = SystemTemplates.SoftwareEngineerVersion1,
        }, Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var jobId = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct)).GetProperty("id").GetGuid();
        using var published = await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct);
        published.StatusCode.Should().Be(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Ct));
        var posted = await SlackSandbox.WaitForMessageAsync(session, SlackSandbox.HiringChannel, title, Ct);
        posted.Should().Contain("is published");

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
        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true })).ToHaveCountAsync(0);
        await LinkFromMenuAsync(Page);
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/"));
    }

    [Fact]
    public async Task Without_a_workspace_the_page_says_so_offers_Add_to_Slack_and_the_rest_of_the_app_is_untouched()
    {
        var owner = ApiSession.NewEmail("e2e-slack-none");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await session.OnboardAsync(Ct);

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, owner);
        await GoToAsync("dashboard", "/settings/slack");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Slack");
        await Expect(Page.GetByText("Post hiring events to a channel")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Add to Slack" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // Slack is one integration among others: the work pages load as they do with it connected.
        await GoToAsync("dashboard", "/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
        await SaveScreenshotAsync(Page, "dashboard-settings-slack-not-connected-1440");
    }

    [Theory]
    [InlineData("ChannelArchived", "ChannelArchived", "The channel is archived. Pick another one.")]
    [InlineData("ChannelRemoved", "ChannelNotFound", "Slack cannot find the channel. Pick another one.")]
    public async Task A_channel_archived_or_removed_in_Slack_is_explained_and_picking_another_channel_fixes_it(string kind, string problem, string words)
    {
        var owner = ApiSession.NewEmail("e2e-slack-channel");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await session.OnboardAsync(Ct);
        await ConnectSandboxAsync(session);
        await PickChannelAsync(session, SlackSandbox.HiringChannel);

        // Slack archives or deletes the channel and tells nobody: the next post finds out.
        await BreakAsync(session, kind);
        await PublishJobAsync(session, "Channel gone");
        await WaitForStatusAsync(session, status => status.At("settings", "problem") == problem, $"the {problem} problem to be recorded");

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, owner);
        await GoToAsync("dashboard", "/settings/slack");
        await Expect(Page.Locator("[data-slot=header-band]")).ToContainTextAsync("Not posting");
        await Expect(Page.GetByText(words)).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, $"dashboard-settings-slack-{kind}-1440");

        // Nothing else is hurt: the rest of the app loads.
        await GoToAsync("dashboard", "/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");

        // The way forward is the notice's own: pick another channel, save, and the problem is gone and posts arrive.
        await GoToAsync("dashboard", "/settings/slack");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Change channel" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { NameRegex = new("^recruiting") }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page.GetByText("Saved.")).ToBeVisibleAsync();
        await Expect(Page.GetByText(words)).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-slot=header-band]")).ToContainTextAsync("Connected");
        var title = await PublishJobAsync(session, "Channel fixed");
        await SlackSandbox.WaitForMessageAsync(session, "C0SBRECRUIT", title, Ct);
    }

    [Fact]
    public async Task A_revoked_token_shows_Disconnected_with_Reconnect_and_reconnecting_keeps_the_channel()
    {
        var owner = ApiSession.NewEmail("e2e-slack-revoked");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await session.OnboardAsync(Ct);
        await ConnectSandboxAsync(session);
        await PickChannelAsync(session, SlackSandbox.HiringChannel);
        await BreakAsync(session, "TokenRevoked");

        // The page's own channel list is the first call that finds out, and the status then reads Disconnected.
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, owner);
        await GoToAsync("dashboard", "/settings/slack");
        var band = Page.Locator("[data-slot=header-band]");
        await Expect(band).ToContainTextAsync("Disconnected");
        await Expect(Page.GetByText("Slack no longer accepts ScreenHire. Reconnect Slack to keep posting.")).ToBeVisibleAsync();
        await Expect(band.GetByRole(AriaRole.Button, new() { Name = "Reconnect" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-settings-slack-disconnected-1440");

        // A job still publishes (PublishJobAsync expects 200): a Slack that has gone away never holds back hiring work.
        // The post itself is the worker's, later, so the channel is not read here: an empty channel now proves nothing.
        await PublishJobAsync(session, "While disconnected");

        // Reconnect goes round the sandbox's sign-in and back; the channel and events the team picked are kept.
        await band.GetByRole(AriaRole.Button, new() { Name = "Reconnect" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/slack"));
        await Expect(band).ToContainTextAsync("Connected");
        await Expect(Page.GetByText("#hiring")).ToBeVisibleAsync();
        var title = await PublishJobAsync(session, "After reconnect");
        await SlackSandbox.WaitForMessageAsync(session, SlackSandbox.HiringChannel, title, Ct);
    }

    [Fact]
    public async Task A_post_that_fails_is_tried_again_and_arrives_once_and_a_failed_test_message_says_to_try_again()
    {
        var owner = ApiSession.NewEmail("e2e-slack-flaky");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await session.OnboardAsync(Ct);
        await ConnectSandboxAsync(session);
        await PickChannelAsync(session, SandboxSlackClient.FlakyChannelId);

        // The worker's first attempt gets an answer it cannot classify, as from an unreachable Slack, and retries.
        var title = await PublishJobAsync(session, "Retried post");
        await SlackSandbox.WaitForMessageAsync(session, SandboxSlackClient.FlakyChannelId, title, Ct);
        (await SlackSandbox.MessagesAsync(session, SandboxSlackClient.FlakyChannelId, Ct)).Count(m => m.Contains(title, StringComparison.Ordinal))
            .Should().Be(1, "a retried post is one message in the channel");

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, owner);
        await GoToAsync("dashboard", "/settings/slack");
        var band = Page.Locator("[data-slot=header-band]");
        await Expect(band).ToContainTextAsync("Connected");
        await Expect(Page.GetByText("Pick another one.")).ToHaveCountAsync(0);

        // The test message asks the person: the first try is refused with a way forward, the second goes.
        await band.GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Send test message" }).ClickAsync();
        await Expect(Page.GetByText("Slack did not answer in time. Try again in a minute.")).ToBeVisibleAsync();
        await Expect(band).ToContainTextAsync("Connected");
        await band.GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Send test message" }).ClickAsync();
        await Expect(Page.GetByText("The test message is in the channel.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_member_with_no_linked_Slack_account_gets_no_direct_message_and_gets_them_once_they_link()
    {
        var owner = ApiSession.NewEmail("e2e-slack-unlinked-owner");
        var recruiter = ApiSession.NewEmail("e2e-slack-unlinked");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await session.OnboardAsync(Ct);
        await ConnectSandboxAsync(session);
        using (var invited = await session.PostAsync("/v1/organization/invitations", new { email = recruiter, role = "Recruiter" }, Ct))
        {
            invited.StatusCode.Should().Be(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(Ct));
        }

        var token = await Fixture.Mailpit.WaitForInvitationTokenAsync(recruiter, Ct);
        Guid recruiterId;
        using (var theirs = new ApiSession(Fixture))
        {
            using var accepted = await theirs.PostAsync("/v1/auth/invitations/accept", new { token }, Ct);
            accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync(Ct));
            recruiterId = (await accepted.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        }

        var title = await PublishJobAsync(session, "Task for the unlinked");
        using var applied = await session.PostAsync(
            $"/v1/jobs/{await JobIdAsync(session, title)}/applications", new { candidate = new { fullName = "Robin Hale", email = "robin.hale@example.com" } }, Ct);
        applied.StatusCode.Should().Be(HttpStatusCode.Created, await applied.Content.ReadAsStringAsync(Ct));
        var applicationId = (await applied.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        // A task for someone with no linked account sends nothing, and nothing breaks: the task is made.
        using (var first = await session.PostAsync($"/v1/applications/{applicationId}/tasks", new { note = "Call Robin", assigneeUserId = recruiterId }, Ct))
        {
            first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(Ct));
        }

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, recruiter);
        await GoToAsync("dashboard", "/tasks");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("My tasks");
        await Expect(Page.GetByText("Call Robin")).ToBeVisibleAsync();

        // Their menu offers the way forward; linking turns the direct messages on for the next task, and only the next.
        await LinkFromMenuAsync(Page);
        var direct = SandboxSlackClient.DirectMessageId(SandboxSlackClient.SlackUserIdFor(recruiterId));
        using (var second = await session.PostAsync($"/v1/applications/{applicationId}/tasks", new { note = "Send the offer", assigneeUserId = recruiterId }, Ct))
        {
            second.StatusCode.Should().Be(HttpStatusCode.Created, await second.Content.ReadAsStringAsync(Ct));
        }

        await SlackSandbox.WaitForMessageAsync(session, direct, "You have a new task", Ct);
        (await SlackSandbox.MessagesAsync(session, direct, Ct)).Should().HaveCount(1, "the task made before they linked sent nothing");
    }

    private static async Task PickChannelAsync(ApiSession session, string channelId)
    {
        using var saved = await session.PutAsync("/v1/slack/channel", new { channelId, events = JobPublishedOnly }, Ct);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));
    }

    // What Slack does on its own side, through the sandbox's route (it is mapped only in sandbox mode).
    private static async Task BreakAsync(ApiSession session, string kind)
    {
        using var response = await session.PostAsync("/v1/slack/sandbox/break", new { kind }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<string> PublishJobAsync(ApiSession session, string prefix)
    {
        var title = $"{prefix} {Guid.NewGuid():N}"[..24];
        using var created = await session.PostAsync("/v1/jobs", new
        {
            title,
            description = "<p>Build and run the things people rely on.</p>",
            employmentType = "FullTime",
            workplaceType = "Remote",
            applicantLocationRequirements = Gb,
            templateVersionId = SystemTemplates.SoftwareEngineerVersion1,
        }, Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        using var published = await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct);
        published.StatusCode.Should().Be(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Ct));
        return title;
    }

    private static async Task<Guid> JobIdAsync(ApiSession session, string title)
    {
        using var response = await session.Client.GetAsync("/v1/jobs", Ct);
        var jobs = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return jobs.EnumerateArray().First(j => j.GetProperty("title").GetString() == title).GetProperty("id").GetGuid();
    }

    // Polls the Slack status (the page's own data) until the check holds; the worker records a problem after a post.
    private static async Task WaitForStatusAsync(ApiSession session, Func<JsonElement, bool> done, string because)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await session.Client.GetAsync("/v1/slack", Ct);
            if (done(await response.Content.ReadFromJsonAsync<JsonElement>(Ct)))
            {
                return;
            }

            await Task.Delay(200, Ct);
        }

        throw new TimeoutException($"Timed out waiting for {because}.");
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

internal static class SlackStatusJson
{
    /// <summary>The string at <paramref name="path"/>, or null when any step is missing or null.</summary>
    public static string? At(this JsonElement element, params string[] path)
    {
        foreach (var step in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(step, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
}
