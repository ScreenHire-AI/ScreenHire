using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// The Settings area in a real browser: the sidebar's one Settings item opens the Settings menu; an owner invites a
/// colleague, starting from Viewer, who accepts through the Mailpit email, then changes their role and removes them;
/// Compare roles opens the grid of what each role can do; an owner makes an Interviewer role and gives it to a member,
/// who can then add a note but not move a candidate; branding saved here (both colours, a font and three images)
/// reaches the careers pages on their own origin and host; an API key is
/// shown once, works, and stops working when revoked. Each test signs in as a new owner of a new organization.
/// </summary>
public sealed class SettingsTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] ReadOnly = ["Read"];

    private static readonly string[] JobPublished = ["job.published"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_owner_invites_a_colleague_who_accepts_from_the_email_then_changes_their_role_and_removes_them()
    {
        var (owner, _) = await OwnerAsync();
        await SignInAsync(owner);

        // Settings is one sidebar item; its pages are in the Settings menu.
        var main = Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" });
        await Expect(main.GetByRole(AriaRole.Link, new() { Name = "Settings" })).ToHaveCountAsync(1);
        await Expect(main.GetByRole(AriaRole.Link, new() { Name = "Team" })).ToHaveCountAsync(0);
        await main.GetByRole(AriaRole.Link, new() { Name = "Settings" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Organization");
        var menu = Page.GetByRole(AriaRole.Navigation, new() { Name = "Settings" });
        await menu.GetByRole(AriaRole.Link, new() { Name = "Team" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/team"));
        await Expect(menu.GetByRole(AriaRole.Link, new() { Name = "Team" })).ToHaveAttributeAsync("aria-current", "page");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Team");
        var members = Page.GetByRole(AriaRole.Region, new() { Name = "Members" });
        await Expect(members.GetByRole(AriaRole.Row).Filter(new() { HasText = owner })).ToContainTextAsync("Owner");
        await ExpectNoAccessibilityViolationsAsync();

        var colleague = ApiSession.NewEmail("colleague");
        await Page.Locator("[data-slot=header-band]").GetByRole(AriaRole.Button, new() { Name = "Invite" }).ClickAsync();
        var invite = Page.GetByRole(AriaRole.Dialog, new() { Name = "Invite a colleague" });
        // The sheet starts at Viewer, the least access, and says what each role is for.
        await Expect(invite.GetByRole(AriaRole.Radio, new() { NameRegex = new("^Viewer") })).ToBeCheckedAsync();
        await Expect(invite.GetByRole(AriaRole.Radio, new() { NameRegex = new("^Recruiter") })).ToHaveAccessibleNameAsync(new Regex("Works the jobs"));
        await invite.GetByLabel("Email", new() { Exact = true }).FillAsync(colleague);
        await invite.GetByRole(AriaRole.Radio, new() { NameRegex = new("^Recruiter") }).CheckAsync();
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
            await Expect(page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Settings" })).ToHaveCountAsync(0);
        }

        await Page.ReloadAsync();
        var row = members.GetByRole(AriaRole.Row).Filter(new() { HasText = colleague });
        await Expect(row).ToContainTextAsync("Recruiter");
        await Expect(invitations).ToContainTextAsync("No open invitations.");

        // Change their role in the access sheet the row's menu opens, with the jobs they see beside it.
        await row.GetByRole(AriaRole.Button).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Edit access" }).ClickAsync();
        var access = Page.GetByRole(AriaRole.Dialog, new() { NameRegex = new("access$") });
        await access.GetByRole(AriaRole.Combobox, new() { Name = "Role" }).SelectOptionAsync(new SelectOptionValue { Label = "Viewer" });
        await access.GetByRole(AriaRole.Button, new() { Name = "Save access" }).ClickAsync();
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
    public async Task Compare_roles_in_the_invite_sheet_opens_what_each_role_can_do()
    {
        var (owner, _) = await OwnerAsync();
        await SignInAsync(owner);
        await GoToAsync("dashboard", "/settings/team");

        await Page.Locator("[data-slot=header-band]").GetByRole(AriaRole.Button, new() { Name = "Invite" }).ClickAsync();
        var invite = Page.GetByRole(AriaRole.Dialog, new() { Name = "Invite a colleague" });
        await Expect(invite.GetByRole(AriaRole.Radio, new() { NameRegex = new("^Viewer") })).ToBeCheckedAsync();
        await invite.GetByRole(AriaRole.Link, new() { Name = "Compare roles" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/roles/compare"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Compare roles");
        var grid = Page.GetByRole(AriaRole.Region, new() { Name = "What each role can do" }).GetByRole(AriaRole.Table);
        // From the API's catalogue: a Recruiter moves candidates, a Viewer does not, and only an Owner manages Owners.
        await Expect(grid.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Rowheader, new() { Name = "Move candidates", Exact = true }) }))
            .ToContainTextAsync(new Regex(@"Yes\s*Yes\s*Yes\s*No"));
        await Expect(grid.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Rowheader, new() { Name = "Manage Owners", Exact = true }) }))
            .ToContainTextAsync(new Regex(@"Yes\s*No\s*No\s*No"));
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task An_owner_makes_an_interviewer_role_and_gives_it_to_a_member()
    {
        // A new organization with one candidate, and a colleague who joins as a Viewer from the emailed invitation.
        var owner = ApiSession.NewEmail("e2e-owner");
        var colleague = ApiSession.NewEmail("interviewer");
        string applicationId;
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            await session.OnboardAsync(Ct);
            using var job = await session.PostAsync(
                "/v1/jobs",
                new { title = "Platform Engineer", description = "<p>Keep the platform running.</p>", employmentType = "FullTime", workplaceType = "Remote" },
                Ct);
            job.StatusCode.Should().Be(HttpStatusCode.Created);
            var jobId = (await job.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString();
            using var application = await session.PostAsync(
                $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = "Casey Morgan", email = ApiSession.NewEmail("candidate") } }, Ct);
            application.StatusCode.Should().Be(HttpStatusCode.Created);
            applicationId = (await application.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
            using var invited = await session.PostAsync("/v1/organization/invitations", new { email = colleague, role = "Viewer" }, Ct);
            invited.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await using var theirs = await Fixture.Browser.NewContextAsync(new() { Locale = "en-US" });
        var theirPage = await theirs.NewPageAsync();
        await theirPage.GotoAsync((await Fixture.Mailpit.WaitForInvitationLinkAsync(colleague, Ct)).ToString());
        await theirPage.GetByRole(AriaRole.Button, new() { Name = "Accept invitation" }).ClickAsync();
        await Expect(theirPage.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");

        // The owner makes the role on the Roles page: a copy of Viewer, then two more permissions on its own page.
        await SignInAsync(owner);
        await GoToAsync("dashboard", "/settings/roles");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Roles");
        await Page.Locator("[data-slot=header-band]").GetByRole(AriaRole.Button, new() { Name = "New role" }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "New role" });
        await Expect(sheet.GetByRole(AriaRole.Radio, new() { NameRegex = new("^Viewer") })).ToBeCheckedAsync();
        await sheet.GetByLabel("Name").FillAsync("Interviewer");
        await ExpectNoAccessibilityViolationsAsync();
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Create and edit" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/settings/roles/[0-9a-f-]{36}$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Interviewer");
        await Page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new("^Add notes and tags") }).CheckAsync();
        await Page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new("^Fill in scorecards") }).CheckAsync();
        await Expect(Page.GetByText("Owner only. A custom role cannot have it.")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page.GetByText("Saved.")).ToBeVisibleAsync();

        // Team gives it to the colleague, and shows it by its name alone (W6).
        await GoToAsync("dashboard", "/settings/team");
        var row = Page.GetByRole(AriaRole.Region, new() { Name = "Members" }).GetByRole(AriaRole.Row).Filter(new() { HasText = colleague });
        await Expect(row).ToContainTextAsync("Viewer");
        await row.GetByRole(AriaRole.Button).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Edit access" }).ClickAsync();
        var access = Page.GetByRole(AriaRole.Dialog, new() { NameRegex = new("access$") });
        await access.GetByRole(AriaRole.Combobox, new() { Name = "Role" }).SelectOptionAsync(new SelectOptionValue { Label = "Interviewer" });
        await access.GetByRole(AriaRole.Button, new() { Name = "Save access" }).ClickAsync();
        await Expect(row).ToContainTextAsync("Interviewer");
        await Expect(row).Not.ToContainTextAsync("Custom");

        // The colleague can now add a note to the candidate, and is not offered a move.
        await theirPage.GotoAsync(DashboardUrl($"/applications/{applicationId}"));
        await Expect(theirPage.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Casey Morgan");
        await Expect(theirPage.GetByRole(AriaRole.Region, new() { Name = "Activity" }).GetByLabel("Note for the hiring team")).ToBeVisibleAsync();
        await Expect(theirPage.GetByRole(AriaRole.Button, new() { Name = "Move to stage, Casey Morgan" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Branding_saved_in_settings_reaches_the_careers_pages_on_their_own_origin()
    {
        var (owner, slug) = await OwnerAsync();
        await SignInAsync(owner);
        await GoToAsync("dashboard", "/settings/branding");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Branding");
        await ExpectNoAccessibilityViolationsAsync();

        // A pale brand colour fails as links on the page; the field offers the nearest shade that passes.
        await Page.GetByLabel("Brand colour", new() { Exact = true }).FillAsync("ffd400");
        await Expect(Page.GetByText("Links in #ffd400 are hard to read on the page")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Use #[0-9a-f]{6}$") }).ClickAsync();
        await Expect(Page.GetByText("Links in #ffd400 are hard to read on the page")).ToHaveCountAsync(0);
        await Page.GetByLabel("Second colour", new() { Exact = true }).FillAsync("E9DCC4");
        await Page.GetByRole(AriaRole.Radio, new() { Name = "Fraunces" }).CheckAsync();

        // Each image goes straight to storage, is checked and scanned, and waits for Save.
        await Page.GetByLabel("Logo", new() { Exact = true }).SetInputFilesAsync(new FilePayload { Name = "logo.png", MimeType = "image/png", Buffer = TestPng.Solid(400, 100) });
        await Page.GetByLabel("Favicon", new() { Exact = true }).SetInputFilesAsync(new FilePayload { Name = "icon.png", MimeType = "image/png", Buffer = TestPng.Solid(64, 64) });
        await Page.GetByLabel("Sharing image", new() { Exact = true })
            .SetInputFilesAsync(new FilePayload { Name = "share.png", MimeType = "image/png", Buffer = TestPng.Solid(1200, 630) });
        await Expect(Page.GetByText("400 by 100 pixels")).ToBeVisibleAsync();
        await Expect(Page.GetByText("1200 by 630 pixels")).ToBeVisibleAsync();
        await Expect(Page.GetByText("64 by 64 pixels")).ToBeVisibleAsync();
        var save = Page.GetByRole(AriaRole.Button, new() { Name = "Save branding" });
        await Expect(save).ToBeEnabledAsync();
        await save.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved" })).ToHaveTextAsync("Saved.");
        await ExpectNoAccessibilityViolationsAsync();

        var response = await OpenAsync("careers", $"/{slug}");
        await ExpectCareersBrandingAsync(response, $"/{slug}");

        // The organization's own host serves the same images, linked without the organization in the path.
        var careers = Fixture.GetEndpoint("careers");
        var tenantHost = await Page.GotoAsync($"{careers.Scheme}://{slug}.localhost:{careers.Port}/");
        await ExpectCareersBrandingAsync(tenantHost!, "");
    }

    private async Task ExpectCareersBrandingAsync(IResponse response, string basePath)
    {
        var banner = Page.GetByRole(AriaRole.Banner);
        var logo = banner.GetByRole(AriaRole.Img);
        (await logo.GetAttributeAsync("src")).Should().MatchRegex($"^{Regex.Escape(basePath)}/brand/images/[0-9a-f-]{{36}}\\.png$");
        await Expect(logo).ToHaveAttributeAsync("alt", new Regex(".+"));
        (await logo.EvaluateAsync<int>("e => e.complete ? e.naturalWidth : 0")).Should().Be(400, "the logo loads from the careers origin");
        (await banner.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor")).Should().Be("rgb(233, 220, 196)");
        (await Page.GetByRole(AriaRole.Heading, new() { Level = 1 }).EvaluateAsync<string>("e => getComputedStyle(e).fontFamily"))
            .Should().StartWith("\"Fraunces Variable\"");
        (await Page.Locator("link[rel='icon']").GetAttributeAsync("href")).Should().MatchRegex($"^{Regex.Escape(basePath)}/brand/images/[0-9a-f-]{{36}}\\.png$");
        (await Page.Locator("meta[property='og:image']").GetAttributeAsync("content")).Should().MatchRegex("^https?://.+/brand/images/[0-9a-f-]{36}\\.png$");

        // Everything in the header band reads in its own text colour, the focus ring included.
        var allJobs = banner.GetByRole(AriaRole.Link, new() { Name = "All jobs" });
        await allJobs.FocusAsync();
        (await allJobs.EvaluateAsync<string>("e => getComputedStyle(e).outlineColor")).Should().Be("rgb(35, 31, 26)");

        var apply = (await response.AllHeadersAsync())["content-security-policy"];
        apply.Should().Contain("default-src 'none'").And.NotContain("unsafe-inline");
        (await Page.Locator("[style]").CountAsync()).Should().Be(0, "no inline styles");
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

        await keys.GetByRole(AriaRole.Button, new() { Name = "Create key" }).ClickAsync();
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
    public async Task A_recruiter_sees_no_settings_and_is_told_their_role_cannot_change_them()
    {
        const string recruiter = "jordan@brightwater.example.com"; // the seeded Brightwater Recruiter (DevSso/config.json)
        await SignInAsync(recruiter);

        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Settings" })).ToHaveCountAsync(0);
        await GoToAsync("dashboard", "/settings/team");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Not in your role" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Members" })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(768, 700)]
    [InlineData(390, 844)]
    public async Task Every_settings_page_fits_the_screen_and_passes_axe(int width, int height)
    {
        // A member, an open invitation, a custom role, a key and a webhook endpoint, so the lists and the record pages have rows.
        var owner = ApiSession.NewEmail("e2e-owner");
        string endpointId;
        string roleId;
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            await session.OnboardAsync(Ct);
            using (var invited = await session.PostAsync("/v1/organization/invitations", new { email = ApiSession.NewEmail("invitee"), role = "Viewer" }, Ct))
            {
                invited.StatusCode.Should().Be(HttpStatusCode.Created);
            }

            using (var role = await session.PostAsync("/v1/organization/roles", new { name = "Interviewer", copyFrom = "Viewer" }, Ct))
            {
                role.StatusCode.Should().Be(HttpStatusCode.Created);
                roleId = (await role.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
            }

            using (var key = await session.PostAsync("/v1/organization/api-keys", new { name = "Payroll sync", scopes = ReadOnly }, Ct))
            {
                key.StatusCode.Should().Be(HttpStatusCode.Created);
            }

            using var endpoint = await session.PostAsync(
                "/v1/webhooks", new { url = "https://hooks.example.com/in", events = JobPublished, description = "Applicant sync" }, Ct);
            endpoint.StatusCode.Should().Be(HttpStatusCode.Created);
            endpointId = (await endpoint.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("endpoint").GetProperty("id").GetString()!;
        }

        await Page.SetViewportSizeAsync(width, height);
        await SignInAsync(owner);

        if (width is >= 768 and < 1024)
        {
            // The icon rail scrolls, so its Settings item, the last, is reachable on a short window.
            var settings = Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Settings" });
            await settings.ScrollIntoViewIfNeededAsync();
            var box = await settings.BoundingBoxAsync();
            box.Should().NotBeNull();
            (box!.Y + box.Height).Should().BeLessThanOrEqualTo(height, "the rail's Settings item is on screen");
        }

        (string Path, string Title)[] pages =
        [
            ("/settings/organization", "Organization"),
            ("/settings/team", "Team"),
            ("/settings/roles", "Roles"),
            ("/settings/roles/compare", "Compare roles"),
            ("/settings/roles/viewer", "Viewer"),
            ($"/settings/roles/{roleId}", "Interviewer"),
            ("/settings/branding", "Branding"),
            ("/settings/verification", "Verification"),
            ("/settings/api-keys", "API keys"),
            ("/settings/webhooks", "Webhooks"),
            ("/settings/mcp-server", "MCP server"),
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
            var name = path.Replace(endpointId, "endpoint", StringComparison.Ordinal).Replace(roleId, "custom", StringComparison.Ordinal).Replace('/', '-');
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
