using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// The recruiter dashboard SPA in a real browser against the API: signing in the ways a person does (Dev SSO, the
/// local mock provider, and an emailed link read from Mailpit), onboarding, and the seeded Brightwater jobs.
/// </summary>
public sealed partial class DashboardTests(E2EFixture fixture) : BrowserTest(fixture)
{
    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Signed_out_visitor_is_sent_to_sign_in_to_email_first_then_the_other_ways_and_the_agreement(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await GoToAsync("dashboard", "/");

        await Expect(Page).ToHaveURLAsync(DashboardUrl("/sign-in"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Sign in to ScreenHire");
        var provider = Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" });
        await Expect(provider).ToBeVisibleAsync();
        var email = Page.GetByLabel("Work email");
        await Expect(email).ToBeVisibleAsync();
        await Expect(email).ToHaveAttributeAsync("autocomplete", "email webauthn");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true })).ToBeVisibleAsync();

        // The email field comes first, then the "or" divider, then the other ways in (design system section 7).
        var emailBox = (await email.BoundingBoxAsync())!;
        var dividerBox = (await Page.Locator("[data-slot=or-divider]").BoundingBoxAsync())!;
        var providerBox = (await provider.BoundingBoxAsync())!;
        emailBox.Y.Should().BeLessThan(dividerBox.Y);
        dividerBox.Y.Should().BeLessThan(providerBox.Y);

        // The stack sets the Terms and the Privacy Policy and no Data Processing Agreement: the line names those two.
        var line = Page.Locator("[data-slot=sign-in-footer]");
        await Expect(line).ToHaveTextAsync(new Regex(@"^By continuing, you agree to ScreenHire's Terms of Service( \(opens in a new tab\))? and Privacy Policy( \(opens in a new tab\))?\.$"));
        await Expect(line.GetByRole(AriaRole.Link, new() { Name = "Terms of Service" })).ToHaveAttributeAsync("href", "https://example.com/terms");
        await Expect(line.GetByRole(AriaRole.Link, new() { Name = "Privacy Policy" })).ToHaveAttributeAsync("href", "https://example.com/privacy");
        await Expect(line.GetByText("Data Processing Agreement")).ToHaveCountAsync(0);
        (await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= document.documentElement.clientWidth"))
            .Should().BeTrue("the card never scrolls the page sideways");
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task Sign_in_with_only_email_links_has_the_field_and_Continue_and_no_divider()
    {
        // The shared stack always has Dev SSO and the browser has passkeys, so the answer to "which ways are set up" is trimmed
        // to email links and the browser's passkey support is taken away.
        await Page.AddInitScriptAsync("delete window.PublicKeyCredential");
        await Page.RouteAsync("**/v1/auth/providers", async route =>
        {
            var response = await route.FetchAsync();
            var methods = JsonNode.Parse(await response.TextAsync())!.AsObject();
            methods["providers"] = new JsonArray();
            await route.FulfillAsync(new() { Response = response, Body = methods.ToJsonString() });
        });
        await GoToAsync("dashboard", "/sign-in");

        await Expect(Page.GetByLabel("Work email")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-slot=or-divider]")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" })).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-slot=sign-in-footer]")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task Style_guide_is_a_development_page_only()
    {
        // The end-to-end tests serve the production build, where the style guide's load answers 404.
        await GoToAsync("dashboard", "/style-guide");

        await Expect(Page).ToHaveURLAsync(DashboardUrl("/style-guide"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Page not found");
        await Expect(Page.GetByText("Error 404")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Signed_out_deep_link_is_remembered_for_after_sign_in()
    {
        await GoToAsync("dashboard", "/?status=open");

        await Expect(Page).ToHaveURLAsync(DashboardUrl("/sign-in?returnUrl=%2F%3Fstatus%3Dopen"));
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }))
            .ToHaveAttributeAsync("href", "/v1/auth/challenge/dev-sso?returnUrl=%2F%3Fstatus%3Dopen");
    }

    [Fact]
    public async Task Dev_SSO_sign_in_then_onboarding_then_an_empty_jobs_list()
    {
        var email = $"e2e-{Guid.NewGuid():N}@example.com";
        await GoToAsync("dashboard", "/sign-in");

        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Dev SSO" })).ToBeVisibleAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(DashboardUrl("/onboarding"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Set up your organization");
        await ExpectNoAccessibilityViolationsAsync();
        var name = $"Northwind {Guid.NewGuid().ToString("N")[..8]}";
        await Page.GetByLabel("Your name").FillAsync("Riley Harper");
        await Page.GetByLabel("Organization name").FillAsync(name);
        await Expect(Page.GetByLabel("Careers page address")).ToHaveValueAsync(name.ToLowerInvariant().Replace(' ', '-'));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create organization" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(DashboardUrl("/"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "All jobs" })).ToContainTextAsync("No jobs yet");

        // A new organization's bell opens by itself once, at the setup steps, with the focus there.
        var bell = Page.GetByRole(AriaRole.Dialog, new() { Name = "Notifications" });
        var steps = bell.GetByRole(AriaRole.Region, new() { Name = "Finish setting up" });
        await Expect(steps).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(bell).ToBeHiddenAsync();

        // The empty state says the steps are there, and its button opens them again.
        var jobs = Page.GetByRole(AriaRole.Region, new() { Name = "All jobs" });
        await Expect(jobs).ToContainTextAsync("left to set up your organization");
        await ExpectNoAccessibilityViolationsAsync();
        await jobs.GetByRole(AriaRole.Button, new() { Name = "Open the setup checklist" }).ClickAsync();
        await Expect(steps).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(bell).ToBeHiddenAsync();

        // A reload does not open it again: it opened by itself once. The line needs the steps, so they are in by then.
        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "All jobs" })).ToContainTextAsync("left to set up your organization");
        await Expect(bell).ToBeHiddenAsync();

        // The person's block shows the name they typed, not the address; the menu still names the account and signs out.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Riley Harper" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Menu)).ToContainTextAsync(email);
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Sign out" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/sign-in"));
        await GoToAsync("dashboard", "/");
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/sign-in"));
    }

    [Fact]
    public async Task Magic_link_from_Mailpit_signs_in_to_Brightwaters_jobs()
    {
        const string owner = "avery@brightwater.example.com"; // the seeded Owner (DevelopmentDataSeeder)
        await GoToAsync("dashboard", "/sign-in");

        await Page.GetByLabel("Work email").FillAsync(owner);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Check your email");
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync(owner);

        var link = await Fixture.Mailpit.WaitForSignInLinkAsync(owner, TestContext.Current.CancellationToken);
        link.GetLeftPart(UriPartial.Authority).Should().Be(Fixture.GetEndpoint("dashboard").GetLeftPart(UriPartial.Authority),
            "the email links to the dashboard (ScreenHire:Domains:AppHost)");
        await Page.GotoAsync(link.ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Finish signing in");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(DashboardUrl("/"));
        await ExpectBrightwaterJobsAsync();

        // A click anywhere on a row opens its record (L5), here on the Location cell rather than the title's link. Ctrl or
        // Cmd opens it in a new tab, as the link itself would, and leaves the list where it was.
        var location = Page.GetByRole(AriaRole.Table, new() { Name = "Jobs" }).GetByRole(AriaRole.Row)
            .Filter(new() { HasText = "Senior .NET Engineer" }).GetByRole(AriaRole.Cell).Nth(4);
        var jobPage = new Regex(@"/jobs/[0-9a-f-]{36}$");
        var tab = await Page.Context.RunAndWaitForPageAsync(() => location.ClickAsync(new() { Modifiers = [KeyboardModifier.ControlOrMeta] }));
        await Expect(tab).ToHaveURLAsync(jobPage);
        await tab.CloseAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"[?&]status=all"));

        await location.ClickAsync();
        await Expect(Page).ToHaveURLAsync(jobPage);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Senior .NET Engineer");
    }

    [Fact]
    public async Task An_emailed_invitation_signs_the_invitee_in_to_the_team()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = new ApiSession(Fixture);
        await owner.SignInWithMagicLinkAsync(ApiSession.NewEmail("owner"), cancellationToken);
        await owner.OnboardAsync(cancellationToken);
        var invitee = ApiSession.NewEmail("invitee");
        using (var invited = await owner.PostAsync("/v1/organization/invitations", new { email = invitee, role = "Recruiter" }, cancellationToken))
        {
            invited.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var link = await Fixture.Mailpit.WaitForInvitationLinkAsync(invitee, cancellationToken);
        link.GetLeftPart(UriPartial.Authority).Should().Be(Fixture.GetEndpoint("dashboard").GetLeftPart(UriPartial.Authority));
        await Page.GotoAsync(link.ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Join your team");
        await ExpectNoAccessibilityViolationsAsync();
        // The address has no name yet, so the page asks for one, and the person's block then shows it, not the address.
        await Page.GetByLabel("Your name").FillAsync("Jordan Lee");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Accept invitation" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(DashboardUrl("/"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Jordan Lee" })).ToBeVisibleAsync();

        // The first landing says which team this is, once: it leaves with a click, and a reload does not bring it back.
        var welcome = Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "You joined" });
        await Expect(welcome).ToContainTextAsync("as Recruiter.");
        await ExpectNoAccessibilityViolationsAsync();
        await welcome.GetByRole(AriaRole.Button, new() { Name = "Dismiss" }).ClickAsync();
        await Expect(welcome).ToHaveCountAsync(0);
        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
        await Expect(welcome).ToHaveCountAsync(0);

        // The link works once: opening it again explains that at once, from the invitation lookup, with no button to press.
        await Page.GotoAsync(link.ToString());
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Accept invitation" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("This invitation doesn't work");
    }

    private async Task ExpectBrightwaterJobsAsync()
    {
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
        // The list opens on Open, the jobs the team is hiring for, while one is open.
        var table = Page.GetByRole(AriaRole.Region, new() { Name = "Open jobs" }).GetByRole(AriaRole.Table, new() { Name = "Jobs" });
        var rows = table.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Cell) });
        await Expect(rows).ToHaveCountAsync(3);

        (string Title, string Location)[] seeded =
        [
            ("Senior Product Designer", "Remote (United Kingdom, Ireland)"),
            ("Senior .NET Engineer", "Remote (United Kingdom, Ireland)"),
            ("Frontend Engineer (Svelte)", "London, England, United Kingdom"),
        ];
        foreach (var (title, location) in seeded)
        {
            var cells = rows.Filter(new() { HasText = title }).GetByRole(AriaRole.Cell);
            // Status first (T2), then the title, the candidates in play, when the latest applied, the location and type.
            await Expect(cells.Nth(0)).ToHaveTextAsync("Open");
            await Expect(cells.Nth(1)).ToHaveTextAsync(title);
            // A number for a job with people on it, and a blank cell, its words for screen readers only, for one with nobody (L1).
            // This stack seeds the jobs without the demo's applicants, so each reads blank.
            await Expect(cells.Nth(2)).ToHaveTextAsync(new Regex(@"^(No candidates|\d+)$"));
            await Expect(cells.Nth(3)).ToHaveTextAsync(new Regex(@"^(No applications yet|[A-Z][a-z]{2} \d{1,2}, \d{4})$"));
            await Expect(cells.Nth(4)).ToHaveTextAsync(location);
        }

        // All shows the draft too, and the address keeps the choice.
        await Page.GetByRole(AriaRole.Group, new() { Name = "Status" }).GetByRole(AriaRole.Button, new() { Name = "All" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"[?&]status=all"));
        var all = Page.GetByRole(AriaRole.Region, new() { Name = "All jobs" }).GetByRole(AriaRole.Table, new() { Name = "Jobs" });
        var allRows = all.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Cell) });
        await Expect(allRows).ToHaveCountAsync(4);
        await Expect(allRows.Filter(new() { HasText = "Customer Success Lead" }).GetByRole(AriaRole.Cell).Nth(0)).ToHaveTextAsync("Draft");

        // The status filter counts each status in a count badge after its label, All included, and the title has none.
        await Expect(Page.GetByRole(AriaRole.Group, new() { Name = "Status" }).GetByRole(AriaRole.Button))
            .ToHaveTextAsync([new Regex(@"^All\s*4$"), new Regex(@"^Open\s*3$"), new Regex(@"^Draft\s*1$"), new Regex(@"^Closed\s*0$")]);
        // The person's block shows their role; the workspace button above it names the organization (W7).
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Avery Quinn" })).ToContainTextAsync("Owner");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Workspace: Brightwater Studio" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
