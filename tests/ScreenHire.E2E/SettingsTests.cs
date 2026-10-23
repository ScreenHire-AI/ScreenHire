using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// The Settings area in a real browser: an owner invites a colleague who accepts through the Mailpit email, then changes
/// their role and removes them; a brand colour saved here reaches the careers page; an API key is shown once, works, and
/// stops working when revoked. Each test signs in as a new owner of a new organization.
/// </summary>
public sealed class SettingsTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_owner_invites_a_colleague_who_accepts_from_the_email_then_changes_their_role_and_removes_them()
    {
        var (owner, _) = await OwnerAsync();
        await SignInAsync(owner);

        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Team" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/team"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Team");
        var members = Page.GetByRole(AriaRole.Region, new() { Name = "Members" });
        await Expect(members.GetByRole(AriaRole.Row).Filter(new() { HasText = owner })).ToContainTextAsync("Owner");
        await ExpectNoAccessibilityViolationsAsync();

        var colleague = ApiSession.NewEmail("colleague");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Invite" }).ClickAsync();
        var invite = Page.GetByRole(AriaRole.Dialog, new() { Name = "Invite a colleague" });
        await invite.GetByLabel("Email").FillAsync(colleague);
        await invite.GetByLabel("Role").SelectOptionAsync("Recruiter");
        await ExpectNoAccessibilityViolationsAsync();
        await invite.GetByRole(AriaRole.Button, new() { Name = "Send invitation" }).ClickAsync();
        await Expect(invite).ToBeHiddenAsync();
        var invitations = Page.GetByRole(AriaRole.Region, new() { Name = "Invitations" });
        await Expect(invitations.GetByRole(AriaRole.Row).Filter(new() { HasText = colleague })).ToContainTextAsync("Recruiter");

        // The colleague opens the emailed link in their own browser.
        var link = await Fixture.Mailpit.WaitForInvitationLinkAsync(colleague, Ct);
        await using (var theirs = await Fixture.Browser.NewContextAsync(new() { Locale = "en-US" }))
        {
            var page = await theirs.NewPageAsync();
            await page.GotoAsync(link.ToString());
            await page.GetByRole(AriaRole.Button, new() { Name = "Accept invitation" }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
            // A Recruiter has no Settings in the sidebar.
            await Expect(page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Team" })).ToHaveCountAsync(0);
        }

        await Page.ReloadAsync();
        var row = members.GetByRole(AriaRole.Row).Filter(new() { HasText = colleague });
        await Expect(row).ToContainTextAsync("Recruiter");
        await Expect(invitations).ToContainTextAsync("No open invitations.");

        // Change their role from the row's menu; nothing to confirm, the same menu changes it back.
        await row.GetByRole(AriaRole.Button).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitemradio, new() { Name = "Viewer" }).ClickAsync();
        await Expect(row).ToContainTextAsync("Viewer");

        // Removing is destructive: it is in the menu and confirmed in a dialog (A2).
        await row.GetByRole(AriaRole.Button).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Remove from team" }).ClickAsync();
        var confirm = Page.GetByRole(AriaRole.Dialog);
        await Expect(confirm).ToContainTextAsync("Remove");
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Remove from team" }).ClickAsync();
        await Expect(row).ToHaveCountAsync(0);
        await Expect(members.GetByRole(AriaRole.Row).Filter(new() { HasText = owner })).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task A_brand_colour_saved_in_settings_reaches_the_careers_page()
    {
        var (owner, slug) = await OwnerAsync();
        await SignInAsync(owner);
        await GoToAsync("dashboard", "/settings/branding");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Careers branding");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "On your careers pages" })).ToContainTextAsync("ScreenHire's colours");
        await ExpectNoAccessibilityViolationsAsync();

        // A deep violet, dark enough that the server keeps it as it is.
        await Page.GetByLabel("Hex colour").FillAsync("5B21B6");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save colour" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved" })).ToHaveTextAsync("Saved.");
        var colours = Page.GetByRole(AriaRole.Region, new() { Name = "On your careers pages" });
        await Expect(colours.GetByRole(AriaRole.Definition).First).ToHaveTextAsync("#5b21b6");
        await ExpectNoAccessibilityViolationsAsync();

        await GoToAsync("careers", $"/{slug}");
        var mark = Page.GetByRole(AriaRole.Banner).Locator("span[aria-hidden='true']").First;
        (await mark.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor")).Should().Be("rgb(91, 33, 182)");
    }

    [Fact]
    public async Task An_API_key_is_shown_once_works_and_stops_working_when_revoked()
    {
        var (owner, _) = await OwnerAsync();
        await SignInAsync(owner);
        await GoToAsync("dashboard", "/settings/api-keys");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("API keys");
        var keys = Page.GetByRole(AriaRole.Region, new() { Name = "Keys" });
        await Expect(keys).ToContainTextAsync("No API keys yet");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create key" }).ClickAsync();
        var create = Page.GetByRole(AriaRole.Dialog, new() { Name = "Create an API key" });
        await create.GetByLabel("Name").FillAsync("Payroll sync");
        await create.GetByLabel("Read and write").CheckAsync();
        await create.GetByRole(AriaRole.Button, new() { Name = "Create key" }).ClickAsync();

        var shown = Page.GetByRole(AriaRole.Dialog, new() { Name = "Your new API key" });
        await Expect(shown).ToContainTextAsync("You will not see it again.");
        var token = await shown.GetByLabel("API key").InputValueAsync();
        token.Should().StartWith("shk_");
        await ExpectNoAccessibilityViolationsAsync();
        await shown.GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();
        await Expect(shown).ToBeHiddenAsync();

        // Once closed, the key is gone from the page: the list shows its first characters only.
        (await Page.ContentAsync()).Should().NotContain(token);
        var row = keys.GetByRole(AriaRole.Row).Filter(new() { HasText = "Payroll sync" });
        await Expect(row).ToContainTextAsync("Active");
        await Expect(row).ToContainTextAsync("Read and write");
        await Expect(row).ToContainTextAsync("shk_");
        await ExpectNoAccessibilityViolationsAsync();
        (await JobsWithKeyAsync(token)).Should().Be(HttpStatusCode.OK);

        await row.GetByRole(AriaRole.Button, new() { Name = "Actions for Payroll sync" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Revoke key" }).ClickAsync();
        await Page.GetByRole(AriaRole.Dialog, new() { Name = "Revoke Payroll sync?" }).GetByRole(AriaRole.Button, new() { Name = "Revoke key" }).ClickAsync();
        await Expect(row).ToContainTextAsync("Revoked");
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = "Actions for Payroll sync" })).ToHaveCountAsync(0);
        (await JobsWithKeyAsync(token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_recruiter_sees_no_settings_and_is_told_who_can_change_them()
    {
        const string recruiter = "jordan@brightwater.example.com"; // the seeded Brightwater Recruiter (DevSso/config.json)
        await SignInAsync(recruiter);

        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Heading, new() { Name = "Settings" })).ToHaveCountAsync(0);
        await GoToAsync("dashboard", "/settings/team");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Owners and admins only" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Members" })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(768, 700)]
    [InlineData(390, 844)]
    public async Task Every_settings_page_fits_the_screen_and_passes_axe(int width, int height)
    {
        // A member, an open invitation, a key and a webhook endpoint, so the lists and the endpoint's page have rows.
        var owner = ApiSession.NewEmail("e2e-owner");
        string endpointId;
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            await session.OnboardAsync(Ct);
            using (var invited = await session.PostAsync("/v1/organization/invitations", new { email = ApiSession.NewEmail("invitee"), role = "Viewer" }, Ct))
            {
                invited.StatusCode.Should().Be(HttpStatusCode.Created);
            }

            using (var key = await session.PostAsync("/v1/organization/api-keys", new { name = "Payroll sync", scopes = new[] { "Read" } }, Ct))
            {
                key.StatusCode.Should().Be(HttpStatusCode.Created);
            }

            using var endpoint = await session.PostAsync(
                "/v1/webhooks", new { url = "https://hooks.example.com/in", events = new[] { "job.published" }, description = "Applicant sync" }, Ct);
            endpoint.StatusCode.Should().Be(HttpStatusCode.Created);
            endpointId = (await endpoint.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("endpoint").GetProperty("id").GetString()!;
        }

        await Page.SetViewportSizeAsync(width, height);
        await SignInAsync(owner);

        if (width is >= 768 and < 1024)
        {
            // The icon rail scrolls, so the last settings link is reachable on a short window.
            var webhooks = Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Webhooks" });
            await webhooks.ScrollIntoViewIfNeededAsync();
            var box = await webhooks.BoundingBoxAsync();
            box.Should().NotBeNull();
            (box!.Y + box.Height).Should().BeLessThanOrEqualTo(height, "the rail's last settings link is on screen");
        }

        (string Path, string Title)[] pages =
        [
            ("/settings/organization", "Organization"),
            ("/settings/team", "Team"),
            ("/settings/branding", "Careers branding"),
            ("/settings/integrity", "Integrity"),
            ("/settings/api-keys", "API keys"),
            ("/settings/webhooks", "Webhooks"),
            ("/settings/webhooks/new", "Add endpoint"),
            ($"/settings/webhooks/{endpointId}", "Applicant sync"),
            ($"/settings/webhooks/{endpointId}/edit", "Edit endpoint"),
        ];
        foreach (var (path, title) in pages)
        {
            await GoToAsync("dashboard", path);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(title);
            // Skeletons gone: every page's panels have loaded.
            await Expect(Page.Locator("main [data-slot=skeleton]")).ToHaveCountAsync(0);
            (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
                .Should().BeTrue("{0} scrolls sideways at {1}px", path, width);
            await ExpectNoAccessibilityViolationsAsync();
            var name = path.Replace(endpointId, "endpoint", StringComparison.Ordinal).Replace('/', '-');
            await SaveScreenshotAsync(Page, $"dashboard-settings{name}-{width}");
        }
    }

    private async Task<HttpStatusCode> JobsWithKeyAsync(string token)
    {
        using var client = Fixture.CreateHttpClient("api");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/jobs");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }

    // A new person who owns a new organization; returns their email and the organization's slug.
    private async Task<(string Email, string Slug)> OwnerAsync()
    {
        var email = ApiSession.NewEmail("e2e-owner");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        await session.OnboardAsync(Ct);
        var me = (await session.CurrentUserAsync(Ct))!.Value;
        return (email, me.GetProperty("organization").GetProperty("slug").GetString()!);
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
