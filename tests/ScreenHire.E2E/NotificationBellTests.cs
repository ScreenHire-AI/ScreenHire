using System.Net;

namespace ScreenHire.E2E;

/// <summary>
/// The notification bell in the top bar (design system section 11): the setup a new organization still has to do lives in the
/// bell, not on Jobs. A new Owner sees the count and the steps in the panel, a step folds into the done line once the
/// organization's own data says it is done, and hiding setup lasts a reload in that browser.
/// </summary>
public sealed class NotificationBellTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_owner_sees_setup_in_the_bell_and_not_on_Jobs_and_dismissing_it_lasts_a_reload()
    {
        var owner = ApiSession.NewEmail("e2e-bell");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await session.OnboardAsync(Ct);

        await Page.SetViewportSizeAsync(1440, 900);
        await SignInAsync(owner);

        // Jobs shows the jobs and four numbers, no setup panel.
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Set up your organization" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Group, new() { Name = "Overview" }).GetByRole(AriaRole.Link)).ToHaveCountAsync(4);

        // Five steps open, one badge on the bell. The team is only the Owner, nothing is set, and no job is published.
        var bell = Page.GetByRole(AriaRole.Button, new() { Name = "Notifications, 5 need attention" });
        await Expect(bell).ToBeVisibleAsync();
        await bell.ClickAsync();

        var panel = Page.GetByRole(AriaRole.Dialog, new() { Name = "Needs attention" });
        var setup = panel.GetByRole(AriaRole.Region, new() { Name = "Finish setting up" });
        await Expect(setup).ToBeVisibleAsync();
        await Expect(setup.GetByRole(AriaRole.Link)).ToHaveTextAsync(
            ["Invite your team", "Set your branding", "Set up your careers site", "Add your email domain", "Publish your first job"]);
        await Expect(setup.GetByRole(AriaRole.Link, new() { Name = "Set your branding" })).ToHaveAttributeAsync("href", "/settings/branding");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-notification-bell");

        // An invitation is real data: the step folds into the done line once the list is read again.
        using (var invited = await session.PostAsync("/v1/organization/invitations", new { email = ApiSession.NewEmail("invitee"), role = "Viewer" }, Ct))
        {
            invited.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await Page.ReloadAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Notifications, 4 need attention" }).ClickAsync();
        await Expect(setup.GetByRole(AriaRole.Link, new() { Name = "Invite your team" })).ToHaveCountAsync(0);
        await Expect(setup).ToContainTextAsync("1 step done");

        await setup.GetByRole(AriaRole.Button, new() { Name = "Hide setup in this browser" }).ClickAsync();
        await Expect(setup).ToHaveCountAsync(0);
        await Expect(panel.GetByText("Nothing needs attention.")).ToBeVisibleAsync();

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
        await SignInAsync(owner);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Notifications, 5 need attention" }).ClickAsync();

        var panel = Page.GetByRole(AriaRole.Dialog, new() { Name = "Needs attention" });
        await Expect(panel).ToBeVisibleAsync();
        var box = await panel.BoundingBoxAsync();
        box.Should().NotBeNull();
        (box!.X >= 0 && box.X + box.Width <= 390).Should().BeTrue("the panel stays inside a 390 px screen");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-notification-bell-phone");
    }

    private async Task SignInAsync(string email)
    {
        var response = await Page.GotoAsync(new Uri(Fixture.GetEndpoint("dashboard"), "/sign-in").ToString());
        response!.Ok.Should().BeTrue();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }
}
