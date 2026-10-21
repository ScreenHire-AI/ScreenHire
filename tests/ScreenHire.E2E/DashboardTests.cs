using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// The recruiter dashboard SPA in a real browser against the API: signing in the ways a person does (Dev SSO, the
/// local mock provider, and an emailed link read from Mailpit), onboarding, and the seeded Brightwater jobs.
/// </summary>
public sealed partial class DashboardTests(E2EFixture fixture) : BrowserTest(fixture)
{
    [Fact]
    public async Task Signed_out_visitor_is_sent_to_sign_in()
    {
        await GoToAsync("dashboard", "/");

        await Expect(Page).ToHaveURLAsync(DashboardUrl("/sign-in"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Sign in to ScreenHire");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Work email")).ToBeVisibleAsync();
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
        await Page.GetByLabel("Organization name").FillAsync(name);
        await Expect(Page.GetByLabel("Careers page address")).ToHaveValueAsync(name.ToLowerInvariant().Replace(' ', '-'));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create organization" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(DashboardUrl("/"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "All jobs" })).ToContainTextAsync("No jobs yet");
        await ExpectNoAccessibilityViolationsAsync();

        // The user menu names the account and signs out.
        await Page.GetByRole(AriaRole.Button, new() { Name = email }).ClickAsync();
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
        await Page.GetByRole(AriaRole.Button, new() { Name = "Email me a sign-in link" }).ClickAsync();
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
    }

    private async Task ExpectBrightwaterJobsAsync()
    {
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
        var table = Page.GetByRole(AriaRole.Region, new() { Name = "All jobs" }).GetByRole(AriaRole.Table, new() { Name = "Jobs" });
        var rows = table.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Cell) });
        await Expect(rows).ToHaveCountAsync(4);

        (string Title, string Location, string Status)[] seeded =
        [
            ("Senior Product Designer", "Remote (GB, IE)", "Open"),
            ("Senior .NET Engineer", "Remote (GB, IE)", "Open"),
            ("Frontend Engineer (Svelte)", "London, England, GB", "Open"),
            ("Customer Success Lead", "Manchester, England, GB", "Draft"),
        ];
        foreach (var (title, location, status) in seeded)
        {
            var cells = rows.Filter(new() { HasText = title }).GetByRole(AriaRole.Cell);
            // Status first (T2), then the title, location, type and publish date.
            await Expect(cells.Nth(0)).ToHaveTextAsync(status);
            await Expect(cells.Nth(1)).ToHaveTextAsync(title);
            await Expect(cells.Nth(2)).ToHaveTextAsync(location);
            // Published date ("Oct 20, 2026") for open jobs; a hyphen for the draft, which was never published.
            await Expect(cells.Nth(4)).ToHaveTextAsync(status == "Draft" ? new Regex("^-$") : PublishedDate());
        }

        // The status filter counts each status in a pill after its label.
        await Expect(Page.GetByRole(AriaRole.Group, new() { Name = "Status" }).GetByRole(AriaRole.Button))
            .ToHaveTextAsync([new Regex("^All$"), new Regex(@"^Open\s*3$"), new Regex(@"^Draft\s*1$"), new Regex(@"^Closed\s*0$")]);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Avery Quinn" })).ToContainTextAsync("Brightwater Studio");
        await ExpectNoAccessibilityViolationsAsync();
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();

    // The en-US medium date. Playwright runs the pattern as a JavaScript regex without the `u` flag, so use a plain
    // class: `\p{L}` would match a literal "p".
    [GeneratedRegex(@"^[A-Z][a-z]{2} \d{1,2}, \d{4}$")]
    private static partial Regex PublishedDate();
}
