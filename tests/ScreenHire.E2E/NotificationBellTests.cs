using System.Net;

namespace ScreenHire.E2E;

/// <summary>
/// The notification bell in the top bar (design system section 11): the setup a new organization still has to do lives in the
/// bell, not on Jobs. A new Owner sees the count and the steps in the panel, a step folds into the done line once the
/// organization's own data says it is done, and a step marked Not needed or a hidden checklist is gone for the same Owner in
/// another browser, because both are saved for the organization.
/// </summary>
public sealed class NotificationBellTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_owner_sees_setup_in_the_bell_and_not_on_Jobs_and_a_skipped_step_is_gone_in_another_browser()
    {
        var owner = ApiSession.NewEmail("e2e-bell");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await session.OnboardAsync(Ct);

        await Page.SetViewportSizeAsync(1440, 900);
        await SignInAsync(Page, owner);

        // A new organization has no jobs, so Jobs shows its empty state with what to do next: no setup panel and no four
        // zeros. The numbers start with the first job.
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Set up your organization" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Group, new() { Name = "Overview" })).ToHaveCountAsync(0);
        await Expect(Page.GetByText("Start from a template, then publish it to your careers page and the job boards.")).ToBeVisibleAsync();

        // Four steps open, three counted on the bell: sending from the team's own domain is Optional and never counts, so a
        // team without a domain is not left with a badge it cannot clear (the owner's walk-through, 2026-11-21).
        var bell = Page.GetByRole(AriaRole.Button, new() { Name = "Notifications, 3 need attention" });
        await Expect(bell).ToBeVisibleAsync();
        await bell.ClickAsync();

        var panel = Page.GetByRole(AriaRole.Dialog, new() { Name = "Notifications" });
        var setup = panel.GetByRole(AriaRole.Region, new() { Name = "Finish setting up" });
        await Expect(setup).ToBeVisibleAsync();
        await Expect(setup.GetByRole(AriaRole.Link)).ToHaveTextAsync(
            ["Set your branding", "Publish your first job", "Invite your team", "Send email from your own domain"]);
        await Expect(setup.GetByRole(AriaRole.Link, new() { Name = "Set your branding" })).ToHaveAttributeAsync("href", "/settings/branding");
        await Expect(setup.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "Send email from your own domain" })).ToContainTextAsync("Optional");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-notification-bell");

        // An invitation is real data: the step folds into the done line once the list is read again.
        using (var invited = await session.PostAsync("/v1/organization/invitations", new { email = ApiSession.NewEmail("invitee"), role = "Viewer" }, Ct))
        {
            invited.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await Page.ReloadAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Notifications, 2 need attention" }).ClickAsync();
        await Expect(setup.GetByRole(AriaRole.Link, new() { Name = "Invite your team" })).ToHaveCountAsync(0);
        await Expect(setup).ToContainTextAsync("1 step done");

        // Not needed takes the step off the list and the count for the organization, not just this browser.
        await setup.GetByRole(AriaRole.Button, new() { Name = "Not needed: Set your branding" }).ClickAsync();
        await Expect(setup.GetByRole(AriaRole.Link, new() { Name = "Set your branding" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Notifications, 1 need attention" })).ToBeVisibleAsync();

        // The same Owner in another browser, with none of this one's storage.
        await using var elsewhere = await Fixture.Browser.NewContextAsync();
        var other = await elsewhere.NewPageAsync();
        await other.SetViewportSizeAsync(1440, 900);
        await SignInAsync(other, owner);
        await other.GetByRole(AriaRole.Button, new() { Name = "Notifications, 1 need attention" }).ClickAsync();
        var otherSetup = other.GetByRole(AriaRole.Dialog, new() { Name = "Notifications" }).GetByRole(AriaRole.Region, new() { Name = "Finish setting up" });
        await Expect(otherSetup.GetByRole(AriaRole.Link)).ToHaveTextAsync(["Publish your first job", "Send email from your own domain"]);

        // Hiding the checklist there hides it here too, once this page reads it again.
        await otherSetup.GetByRole(AriaRole.Button, new() { Name = "Hide setup for everyone" }).ClickAsync();
        await Expect(otherSetup).ToHaveCountAsync(0);
        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Notifications", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_bell_fits_a_phone_and_opens_within_the_screen()
    {
        var owner = ApiSession.NewEmail("e2e-bell-phone");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await session.OnboardAsync(Ct);

        await Page.SetViewportSizeAsync(390, 844);
        await SignInAsync(Page, owner);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Notifications, 3 need attention" }).ClickAsync();

        var panel = Page.GetByRole(AriaRole.Dialog, new() { Name = "Notifications" });
        await Expect(panel).ToBeVisibleAsync();
        var box = await panel.BoundingBoxAsync();
        box.Should().NotBeNull();
        (box!.X >= 0 && box.X + box.Width <= 390).Should().BeTrue("the panel stays inside a 390 px screen");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-notification-bell-phone");
    }

    private async Task SignInAsync(IPage page, string email)
    {
        var response = await page.GotoAsync(new Uri(Fixture.GetEndpoint("dashboard"), "/sign-in").ToString());
        response!.Ok.Should().BeTrue();
        await page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await page.GetByLabel("Any email address").FillAsync(email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }
}
