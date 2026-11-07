using System.Net;

namespace ScreenHire.E2E;

/// <summary>
/// The Set up your organization panel on Jobs (design system section 11): a new organization's Owner sees the steps open,
/// a step ticks from the organization's own data, and Dismiss keeps it away in that browser.
/// </summary>
public sealed class SetupChecklistTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_owner_sees_the_steps_open_one_ticks_from_real_data_and_dismissing_it_lasts_a_reload()
    {
        var owner = ApiSession.NewEmail("e2e-setup");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await session.OnboardAsync(Ct);

        await Page.SetViewportSizeAsync(1440, 900);
        await SignInAsync(owner);

        var panel = Page.GetByRole(AriaRole.Region, new() { Name = "Set up your organization" });
        await Expect(panel).ToBeVisibleAsync();
        var steps = panel.GetByRole(AriaRole.Listitem);
        await Expect(steps.GetByRole(AriaRole.Link)).ToHaveTextAsync(
            ["Invite your team", "Set your branding", "Set up your careers site", "Add your email domain", "Publish your first job"]);
        await Expect(panel.GetByText("Done", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(panel.GetByRole(AriaRole.Link, new() { Name = "Set your branding" })).ToHaveAttributeAsync("href", "/settings/branding");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-setup-checklist");

        // An invitation is real data: the step ticks once the list is read again.
        using (var invited = await session.PostAsync("/v1/organization/invitations", new { email = ApiSession.NewEmail("invitee"), role = "Viewer" }, Ct))
        {
            invited.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await Page.ReloadAsync();
        await Expect(steps.Filter(new() { HasText = "Invite your team" })).ToContainTextAsync("Done");
        await Expect(steps.Filter(new() { HasText = "Set your branding" })).Not.ToContainTextAsync("Done");

        await panel.GetByRole(AriaRole.Button, new() { Name = "Dismiss" }).ClickAsync();
        await Expect(panel).ToHaveCountAsync(0);

        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Set up your organization" })).ToHaveCountAsync(0);
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
