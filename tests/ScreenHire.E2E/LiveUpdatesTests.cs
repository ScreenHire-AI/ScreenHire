using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// Live updates in the dashboard: two browser contexts signed in to the same organization, and a change made in one
/// appears in the other with no reload, through the API's real-time hub and the dashboard's WebSocket proxy.
/// </summary>
public sealed class LiveUpdatesTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string NoReloadMarker = "window.__screenhireNoReload";

    [Fact]
    public async Task Publishing_and_closing_a_job_in_one_session_updates_another_without_a_reload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = $"e2e-live-{Guid.NewGuid():N}@example.com";
        var title = $"Live Updates Engineer {Guid.NewGuid().ToString("N")[..6]}";

        // The first session creates the organization and gets a draft job.
        await SignInWithDevSsoAsync(Page, email);
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/onboarding"));
        await Page.GetByLabel("Organization name").FillAsync($"Live {Guid.NewGuid().ToString("N")[..8]}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create organization" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/"));
        var organizationId = await OrganizationIdAsync(Page);
        await Fixture.AddJobsAsync(organizationId, [TestData.NewJob(organizationId, "live-updates", title)], cancellationToken);
        await Page.ReloadAsync();
        await Expect(StatusOf(Page, title)).ToHaveTextAsync("Draft");

        // The second session: the same person in another browser, watching the same list.
        await using var otherContext = await Fixture.Browser.NewContextAsync();
        var other = await otherContext.NewPageAsync();
        // Frames on the hub's WebSocket itself. SignalR would fall back to server-sent events if the upgrade were
        // refused (the API admits only the dashboard's Origin), and the list would still update.
        var hubFrames = 0;
        other.WebSocket += (_, socket) =>
        {
            if (socket.Url.Contains("/v1/hubs/events", StringComparison.Ordinal))
            {
                socket.FrameReceived += (_, _) => Interlocked.Increment(ref hubFrames);
            }
        };
        await SignInWithDevSsoAsync(other, email);
        await Expect(other).ToHaveURLAsync(DashboardUrl("/"));
        await Expect(StatusOf(other, title)).ToHaveTextAsync("Draft");
        await Expect(LiveIndicator(other)).ToContainTextAsync("Live updates: Live");
        await Expect(LiveIndicator(Page)).ToContainTextAsync("Live updates: Live");
        await other.EvaluateAsync($"() => {{ {NoReloadMarker} = true; }}");

        await Page.GetByRole(AriaRole.Button, new() { Name = $"Actions for {title}" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Publish to every board" }).ClickAsync();

        await Expect(StatusOf(other, title)).ToHaveTextAsync("Open");
        await Expect(other.GetByRole(AriaRole.Group, new() { Name = "Status" }).GetByRole(AriaRole.Button, new() { NameRegex = new("^Open") }))
            .ToHaveTextAsync(new Regex(@"^Open\s*1$"));

        // Closing from the list is the row menu's destructive item, confirmed in a dialog (A2).
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Actions for {title}" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Close & remove everywhere" }).ClickAsync();
        var confirm = Page.GetByRole(AriaRole.Dialog, new() { Name = "Close this job and remove it everywhere?" });
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Close & remove everywhere" }).ClickAsync();
        await Expect(confirm).ToBeHiddenAsync();

        await Expect(StatusOf(other, title)).ToHaveTextAsync("Closed");
        (await other.EvaluateAsync<bool>($"() => {NoReloadMarker} === true")).Should().BeTrue("the list updated in place, without a reload");
        Volatile.Read(ref hubFrames).Should().BePositive("the hub's WebSocket from the dashboard's origin was accepted");
        await ExpectNoAccessibilityViolationsAsync();
    }

    private async Task SignInWithDevSsoAsync(IPage page, string email)
    {
        var response = await page.GotoAsync(DashboardUrl("/sign-in"));
        response!.Ok.Should().BeTrue();
        await page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await page.GetByLabel("Any email address").FillAsync(email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
    }

    // The signed-in user's organization, read through the dashboard's own /v1 proxy with the page's cookies.
    private async Task<Guid> OrganizationIdAsync(IPage page)
    {
        var response = await page.APIRequest.GetAsync(DashboardUrl("/v1/auth/me"));
        response.Ok.Should().BeTrue();
        var user = await response.JsonAsync();
        return user!.Value.GetProperty("organization").GetProperty("id").GetGuid();
    }

    private static ILocator StatusOf(IPage page, string title) =>
        page.GetByRole(AriaRole.Table, new() { Name = "Jobs" })
            .GetByRole(AriaRole.Row).Filter(new() { HasText = title })
            .GetByRole(AriaRole.Cell).Nth(0);

    private static ILocator LiveIndicator(IPage page) =>
        page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Live updates" });

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
