using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// The frame of the Settings area in a real browser: every settings page fits the screen and passes axe at desktop,
/// tablet and phone widths; the frame is centred and the same on every settings and account page at 1440 and 2560; the menu
/// lists every page under its group, search finds a setting by everyday words, and the menu starts in the same place on
/// every page of a group. The journeys inside Settings are in
/// <see cref="SettingsTeamTests"/>, <see cref="SettingsBrandingTests"/> and <see cref="SettingsAccessTests"/>. Each test
/// signs in as a new owner of a new organization.
/// </summary>
public sealed class SettingsTests(E2EFixture fixture) : SettingsArea(fixture)
{
    private static readonly string[] ReadOnly = ["Read"];

    private static readonly string[] JobPublished = ["job.published"];

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
            // The icon rail scrolls, so its last item, Settings, is reachable on a short window.
            var settings = Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true });
            await settings.ScrollIntoViewIfNeededAsync();
            var box = await settings.BoundingBoxAsync();
            box.Should().NotBeNull();
            (box!.Y + box.Height).Should().BeLessThanOrEqualTo(height, "the rail's Settings item is on screen");
        }

        // A short create form is a sheet over its page (design system A16): the old Add endpoint and Edit addresses open the
        // list, or the endpoint, with the sheet open, and the sheet is what the row checks.
        (string Path, string Title)[] pages =
        [
            ("/settings/organization", "General"),
            ("/settings/careers-site", "Careers page"),
            ("/settings/team", "Team"),
            ("/settings/roles", "Roles"),
            ("/settings/roles/compare", "Compare roles"),
            ("/settings/roles/viewer", "Viewer"),
            ($"/settings/roles/{roleId}", "Interviewer"),
            ("/settings/tags", "Tags"),
            ("/settings/branding", "Branding"),
            ("/settings/sending-domain", "Email domain"),
            ("/verification", "Verification"),
            ("/settings/api-keys", "API keys"),
            ("/settings/webhooks", "Webhooks"),
            ("/settings/mcp-server", "MCP server"),
            ("/settings/slack", "Slack"),
            ("/settings/webhooks/new", "Add endpoint"),
            ($"/settings/webhooks/{endpointId}", "Applicant sync"),
            ($"/settings/webhooks/{endpointId}/edit", "Edit endpoint"),
        ];
        foreach (var (path, title) in pages)
        {
            await GoToAsync("dashboard", path);
            if (path.EndsWith("/new", StringComparison.Ordinal) || path.EndsWith("/edit", StringComparison.Ordinal))
            {
                var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = title });
                await Expect(sheet).ToBeVisibleAsync();
                await Expect(sheet.GetByRole(AriaRole.Checkbox).First).ToBeVisibleAsync();
            }
            else
            {
                await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(title);
            }
            // Skeletons gone: every page's panels have loaded.
            await Expect(Page.Locator("main [data-slot=skeleton]")).ToHaveCountAsync(0);
            (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
                .Should().BeTrue("{0} scrolls sideways at {1}px", path, width);
            await ExpectNoAccessibilityViolationsAsync();
            var name = path.Replace(endpointId, "endpoint", StringComparison.Ordinal).Replace(roleId, "custom", StringComparison.Ordinal).Replace('/', '-');
            await SaveScreenshotAsync(Page, $"dashboard-settings{name}-{width}");
        }
    }

    [Fact]
    public async Task The_settings_menu_lists_every_page_under_its_group_and_moving_between_them_keeps_the_frame_still()
    {
        var (owner, _) = await OwnerAsync();
        await Page.SetViewportSizeAsync(1440, 900);
        await SignInAsync(owner);

        await GoToAsync("dashboard", "/settings/team");
        var menu = Page.GetByRole(AriaRole.Navigation, new() { Name = "Settings", Exact = true });
        var organization = menu.GetByRole(AriaRole.List, new() { Name = "Organization", Exact = true });
        // Billing is on in the test stacks, so an Owner's menu ends with it once the shell's billing query answers. Waiting for
        // that list, not the one before the answer, is what keeps this from depending on which comes first. Email domain may
        // carry its mark after its name, so each entry is matched by its start.
        await Expect(organization.GetByRole(AriaRole.Link)).ToHaveTextAsync(
            [new Regex(@"^General\s*$"), new Regex(@"^Team\s*$"), new Regex(@"^Roles\s*$"), new Regex(@"^Tags\s*$"), new Regex(@"^Notification defaults\s*$"), new Regex("^Email domain"), new Regex(@"^Data requests\s*$"), new Regex(@"^Billing\s*$")]);
        await Expect(organization.GetByRole(AriaRole.Link, new() { Name = "Branding" })).ToHaveCountAsync(0);
        var team = await FrameAsync();

        await organization.GetByRole(AriaRole.Link, new() { Name = "Roles" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Roles");
        var roles = await FrameAsync();

        // S8: the header band and the menu start at the same place and size on each page.
        roles.MenuX.Should().BeApproximately(team.MenuX, 0.5);
        roles.MenuY.Should().BeApproximately(team.MenuY, 0.5);
        roles.MenuWidth.Should().BeApproximately(team.MenuWidth, 0.5);
        roles.BandHeight.Should().BeApproximately(team.BandHeight, 0.5);

        // The breadcrumb reads Settings, the group, then the page.
        var breadcrumb = Page.GetByRole(AriaRole.Navigation, new() { Name = "Breadcrumb" });
        await Expect(breadcrumb.GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true })).ToHaveAttributeAsync("href", "/settings");
        await Expect(breadcrumb.GetByRole(AriaRole.Listitem)).ToHaveTextAsync([new Regex("Settings$"), new Regex("Organization$"), new Regex("Roles$")]);

        // Careers site is its own group in the same menu, with its own pages, and the menu stays where it was.
        var careers = menu.GetByRole(AriaRole.List, new() { Name = "Careers site", Exact = true });
        await Expect(careers.GetByRole(AriaRole.Link)).ToHaveTextAsync(["Careers page", "Branding"]);
        await careers.GetByRole(AriaRole.Link, new() { Name = "Careers page" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Careers page");
        await Expect(careers.GetByRole(AriaRole.Link, new() { Name = "Careers page" })).ToHaveAttributeAsync("aria-current", "page");
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(1440, 900)]
    public async Task The_settings_frame_is_centred_and_the_same_on_a_form_a_list_an_overview_and_an_editor(int width, int height)
    {
        // S10: every page with the Settings or account menu sits in one frame, the menu and a 960px column, centred so a wide
        // window's spare width splits evenly on both sides. Whatever the page holds, the frame is the same, so the menu never
        // moves (S8), and the header band's title starts above the menu.
        var (owner, _) = await OwnerAsync();
        await Page.SetViewportSizeAsync(width, height);
        await SignInAsync(owner);

        (string Path, string Title, string Menu)[] pages =
        [
            ("/settings/organization", "General", "Settings"),
            ("/settings/team", "Team", "Settings"),
            ("/settings/billing", "Billing", "Settings"),
            ("/settings/branding", "Branding", "Settings"),
            ("/account/profile", "Profile", "Your account"),
        ];
        var frames = new List<(string Title, double MenuX, double MenuY, double MenuWidth, double BandHeight)>();
        foreach (var (path, title, menuName) in pages)
        {
            await GoToAsync("dashboard", path);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(title);
            await Expect(Page.Locator("main [data-slot=skeleton]")).ToHaveCountAsync(0);
            var menu = (await Page.GetByRole(AriaRole.Navigation, new() { Name = menuName, Exact = true }).BoundingBoxAsync())!;
            var heading = (await Page.GetByRole(AriaRole.Heading, new() { Level = 1 }).BoundingBoxAsync())!;
            var band = (await Page.Locator("[data-slot=header-band]").BoundingBoxAsync())!;
            var frame = (await Page.Locator("[data-settings-frame]").BoundingBoxAsync())!;
            var main = (await Page.GetByRole(AriaRole.Main).BoundingBoxAsync())!;
            ((double)heading.X).Should().BeApproximately(menu.X, 0.5, "{0}'s title starts above its menu at {1}px", title, width);
            double left = frame.X - main.X;
            double right = main.X + main.Width - (frame.X + frame.Width);
            left.Should().BeApproximately(right, 1, "{0}'s frame is centred at {1}px", title, width);
            (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
                .Should().BeTrue("{0} scrolls sideways at {1}px", path, width);
            frames.Add((title, menu.X, menu.Y, menu.Width, band.Height));
            if (path == "/settings/organization")
            {
                // At 2560 the frame stops at its width and the spare room is on both sides, not pooled on the right.
                if (width >= 2560)
                {
                    left.Should().BeGreaterThan(200, "a 2560px window leaves room on both sides of the frame");
                }

                await SaveScreenshotAsync(Page, $"dashboard-settings-organization-{width}");
            }
        }

        foreach (var frame in frames)
        {
            frame.MenuX.Should().BeApproximately(frames[0].MenuX, 0.5, "{0}'s menu starts where General's does at {1}px", frame.Title, width);
            frame.MenuY.Should().BeApproximately(frames[0].MenuY, 0.5, "{0}'s menu starts where General's does at {1}px", frame.Title, width);
            frame.MenuWidth.Should().BeApproximately(frames[0].MenuWidth, 0.5);
            frame.BandHeight.Should().BeApproximately(frames[0].BandHeight, 0.5, "{0}'s header band is General's height at {1}px", frame.Title, width);
        }
    }

    [Fact]
    public async Task The_settings_menu_lists_every_setting_in_groups_and_search_finds_one_by_everyday_words()
    {
        var owner = ApiSession.NewEmail("e2e-owner");
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            await session.OnboardAsync(Ct);
        }

        await Page.SetViewportSizeAsync(1440, 900);
        await SignInAsync(owner);
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/organization"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("General");

        // Three groups, each setting a link under its group's name: no counts, no icons. Billing is on in the test stacks and
        // arrives with the shell's answer. Email domain is optional, so an organization with none shows no mark beside it.
        var menu = Page.GetByRole(AriaRole.Navigation, new() { Name = "Settings", Exact = true });
        var integrations = menu.GetByRole(AriaRole.List, new() { Name = "Integrations", Exact = true });
        await Expect(integrations.GetByRole(AriaRole.Link)).ToHaveTextAsync(["AI model", "Slack", "MCP server", "Webhooks", "API keys"]);
        await Expect(menu.GetByRole(AriaRole.List, new() { Name = "Careers site", Exact = true }).GetByRole(AriaRole.Link)).ToHaveTextAsync(["Careers page", "Branding"]);
        var organization = menu.GetByRole(AriaRole.List, new() { Name = "Organization", Exact = true });
        await Expect(organization.GetByRole(AriaRole.Link)).ToHaveTextAsync(
            [new Regex(@"^General\s*$"), new Regex(@"^Team\s*$"), new Regex(@"^Roles\s*$"), new Regex(@"^Tags\s*$"), new Regex(@"^Notification defaults\s*$"), new Regex(@"^Email domain\s*$"), new Regex(@"^Data requests\s*$"), new Regex(@"^Billing\s*$")]);
        await Expect(menu.Locator("[data-setting-mark]")).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();

        // "logo" is not a setting's name: the words kept beside Branding find it, under its group's name.
        var search = Page.GetByRole(AriaRole.Searchbox, new() { Name = "Search settings" });
        await search.FillAsync("logo");
        await Expect(menu.GetByRole(AriaRole.Link)).ToHaveTextAsync(["Branding"]);
        await Expect(menu.GetByRole(AriaRole.List, new() { Name = "Careers site", Exact = true })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await search.FillAsync("zzz");
        await Expect(menu.GetByRole(AriaRole.Status)).ToHaveTextAsync("No setting matches.");
        await search.FillAsync("");
        await Expect(integrations).ToBeVisibleAsync();

        // An integration's page has the same menu and a breadcrumb that leads back to Settings.
        await integrations.GetByRole(AriaRole.Link, new() { Name = "Webhooks" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/webhooks"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Webhooks");
        await Expect(integrations.GetByRole(AriaRole.Link, new() { Name = "Webhooks" })).ToHaveAttributeAsync("aria-current", "page");
        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Breadcrumb" }).GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true })).ToHaveAttributeAsync("href", "/settings");

        // The old addresses land on the new ones; Settings itself opens its first page.
        await GoToAsync("dashboard", "/settings/integrations");
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/organization"));
        foreach (var old in new[] { "/settings/verification", "/settings/integrity" })
        {
            await GoToAsync("dashboard", old);
            await Expect(Page).ToHaveURLAsync(DashboardUrl("/verification"));
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Verification");
        }
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(768, 900)]
    public async Task The_menu_starts_in_the_same_place_on_every_page_of_a_group_at_phone_and_tablet_widths(int width, int height)
    {
        // Every page that has a Last updated fact has one (an edited organization, a domain), and the Email domain page has
        // the actions that only appear once there is a domain: the cases where the title's row used to wrap (S8).
        var owner = ApiSession.NewEmail("e2e-owner");
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            await session.OnboardAsync(Ct);
            using var renamed = await session.PutAsync("/v1/organization", new { name = "Harbour Lane Studio" }, Ct);
            renamed.StatusCode.Should().Be(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync(Ct));
            using var domain = await session.PutAsync("/v1/organization/sending-domain", new { domain = $"mail-{Guid.NewGuid():N}.example.com" }, Ct);
            domain.StatusCode.Should().Be(HttpStatusCode.OK, await domain.Content.ReadAsStringAsync(Ct));
        }

        await Page.SetViewportSizeAsync(width, height);
        await SignInAsync(owner);

        (string Area, (string Path, string Title, bool HasFact)[] Pages)[] sections =
        [
            ("Organization", [("/settings/organization", "General", true), ("/settings/team", "Team", true), ("/settings/roles", "Roles", false), ("/settings/notification-defaults", "Notification defaults", true), ("/settings/sending-domain", "Email domain", true), ("/settings/data-requests", "Data requests", false)]),
            ("Careers site", [("/settings/careers-site", "Careers page", true), ("/settings/branding", "Branding", true)]),
            ("Integrations", [("/settings/ai-model", "AI model", true), ("/settings/mcp-server", "MCP server", false), ("/settings/webhooks", "Webhooks", false), ("/settings/api-keys", "API keys", false)]),
        ];
        foreach (var (area, pages) in sections)
        {
            var tops = new List<(string Title, double Top)>();
            foreach (var (path, title, hasFact) in pages)
            {
                await GoToAsync("dashboard", path);
                await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(title);
                await Expect(Page.Locator("main [data-slot=skeleton]")).ToHaveCountAsync(0);
                if (hasFact)
                {
                    await Expect(Page.Locator("[data-slot=header-band] [data-last-updated]")).ToBeAttachedAsync();
                }

                var menu = await Page.GetByRole(AriaRole.Navigation, new() { Name = "Settings", Exact = true }).BoundingBoxAsync();
                menu.Should().NotBeNull();
                tops.Add((title, menu!.Y));
                await ExpectNoSidewaysScrollAsync();
            }

            tops.Select(top => Math.Round(top.Top)).Distinct().Should().HaveCount(1, "the menu starts at the same place on every page of {0} at {1}px: {2}", area, width, string.Join(", ", tops.Select(top => $"{top.Title} {top.Top}")));
        }
    }

    private async Task ExpectNoSidewaysScrollAsync() =>
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth")).Should().BeTrue("the page scrolls sideways");

    private async Task<(double MenuX, double MenuY, double MenuWidth, double BandHeight)> FrameAsync()
    {
        var menu = await Page.GetByRole(AriaRole.Navigation, new() { Name = "Settings", Exact = true }).BoundingBoxAsync();
        var band = await Page.Locator("[data-slot=header-band]").BoundingBoxAsync();
        menu.Should().NotBeNull();
        band.Should().NotBeNull();
        return (menu!.X, menu.Y, menu.Width, band!.Height);
    }
}
