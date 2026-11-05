using System.Net;

namespace ScreenHire.E2E;

/// <summary>
/// Settings > Email domain in a real browser, against the local sandbox provider (docs/architecture/email.md): an Owner adds
/// the company's domain, sees the DNS records ScreenHire lists and what each check found, publishes the records in the
/// sandbox's stand-in for DNS, checks again until the domain is verified, and removes it through the More menu and its dialog.
/// A Recruiter has no Settings. Each test signs in as a new owner of a new organization with a domain of its own.
/// </summary>
public sealed class SendingDomainTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_owner_adds_a_domain_checks_its_records_until_they_pass_and_removes_it()
    {
        var domain = NewDomain();
        var owner = ApiSession.NewEmail("e2e-domain-owner");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await session.OnboardAsync(Ct);

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, owner);
        await GoToAsync("dashboard", "/settings/sending-domain");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Email domain");
        await ExpectNoAccessibilityViolationsAsync();

        // A domain a mail provider owns is refused with a message under the field.
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Domain" }).FillAsync("gmail.com");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add domain" }).ClickAsync();
        await Expect(Page.GetByText("Enter a domain your company owns.")).ToBeVisibleAsync();

        // The company's own domain lists the records the provider needs, none looked up yet.
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Domain" }).FillAsync(domain);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add domain" }).ClickAsync();
        var band = Page.Locator("[data-slot=header-band]");
        await Expect(band).ToContainTextAsync("Not checked");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "DKIM key" }).First).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Return path" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Replies" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Group, new() { Name = "Replies host" })).ToContainTextAsync($"reply.{domain}");
        await Expect(Page.GetByText("ScreenHire, until the domain is verified")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // Nothing is in DNS yet, so the records are not found and the status says what is left.
        await band.GetByRole(AriaRole.Button, new() { Name = "Check records" }).ClickAsync();
        await Expect(band).ToContainTextAsync("Records to add");
        await Expect(Page.GetByText("Not found").First).ToBeVisibleAsync();
        await SaveScreenshotAsync(Page, "dashboard-settings-sending-domain-pending-1440");

        // The sandbox's DNS gets the records; the next check passes, the provider confirms, and mail comes from the domain.
        using var published = await session.PostAsync("/v1/dev/sending-domains/publish", new { domain }, Ct);
        published.StatusCode.Should().Be(HttpStatusCode.OK);
        await band.GetByRole(AriaRole.Button, new() { Name = "Check records" }).ClickAsync();
        await Expect(band).ToContainTextAsync("Verified");
        await Expect(Page.GetByText($"hiring@{domain}")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // Removing is in the More menu, behind a dialog; afterwards the page offers to add a domain again.
        await band.GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Remove email domain" }).ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Remove the email domain?" });
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Remove domain" }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Add domain" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_recruiter_has_no_Settings_and_so_no_sending_domain_page()
    {
        var owner = ApiSession.NewEmail("e2e-domain-owner");
        var recruiter = ApiSession.NewEmail("e2e-domain-recruiter");
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            await session.OnboardAsync(Ct);
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
        await GoToAsync("dashboard", "/settings/sending-domain");
        await Expect(Page.GetByText("Not in your role")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Textbox, new() { Name = "Domain" })).ToHaveCountAsync(0);
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

    // One domain per organization and one organization per domain, so each test brings its own.
    private static string NewDomain() => $"e2e-{Guid.NewGuid():N}"[..14] + ".example.org";
}
