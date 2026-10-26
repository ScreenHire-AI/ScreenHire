using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// The frame every signed-in dashboard page sits in, at the design system's size classes (section 8): the icon rail at
/// 768px, the sheet behind the menu button and Back on a detail page at 390px, and the shortcuts dialog from the top
/// bar. Each test signs in as a new person with a new organization holding one job and one application.
/// </summary>
public sealed class ShellTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string CandidateName = "Jordan Lee";

    private const string JobTitle = "Senior Product Designer";

    private static readonly string[] UkAndIreland = ["GB", "IE"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task At_768px_the_sidebar_is_an_icon_rail_with_labels_in_tooltips()
    {
        var scene = await SceneAsync();
        await Page.SetViewportSizeAsync(768, 1024);
        await SignInAsync(scene.Email);

        var rail = await Page.Locator("[data-slot='sidebar-container']").BoundingBoxAsync();
        rail.Should().NotBeNull();
        rail!.Width.Should().Be(64, "the icon rail is 64px wide (design system section 5)");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Open navigation" })).ToHaveCountAsync(0);
        await ExpectNoSidewaysScrollAsync();

        var candidates = Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Candidates" });
        await candidates.HoverAsync();
        await Expect(Page.Locator("[data-slot='tooltip-content']")).ToContainTextAsync("Candidates");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-shell-768");

        await candidates.ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/candidates"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Candidates");
    }

    [Fact]
    public async Task At_390px_the_sidebar_is_a_sheet_behind_the_menu_button_and_a_detail_page_shows_Back()
    {
        var scene = await SceneAsync();
        await Page.SetViewportSizeAsync(390, 844);
        await SignInAsync(scene.Email);

        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" })).ToHaveCountAsync(0);
        await ExpectNoSidewaysScrollAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Open navigation" }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Navigation" });
        await Expect(sheet.GetByRole(AriaRole.Link, new() { NameRegex = new("^To review") })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-shell-390-sheet");

        await sheet.GetByRole(AriaRole.Link, new() { Name = "Candidates" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/candidates"));
        await Expect(sheet).ToHaveCountAsync(0);

        await Page.GotoAsync(DashboardUrl($"/applications/{scene.ApplicationId}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(CandidateName);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Open navigation" })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        // Back goes a level up the breadcrumb (Candidates / job / candidate), to the job.
        await Page.GetByRole(AriaRole.Link, new() { Name = $"Back to {JobTitle}" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl($"/jobs/{scene.JobId}"));
    }

    [Fact]
    public async Task The_top_bar_keyboard_button_and_question_mark_open_the_shortcuts_dialog()
    {
        var scene = await SceneAsync();
        await SignInAsync(scene.Email);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Keyboard shortcuts" }).ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Keyboard shortcuts" });
        await Expect(dialog.GetByRole(AriaRole.Switch, new() { Name = "Single-key shortcuts" })).ToBeCheckedAsync();
        await Expect(dialog).ToContainTextAsync("Go to Candidates");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0);

        await Page.Keyboard.PressAsync("Shift+Slash");
        await Expect(dialog).ToBeVisibleAsync();
    }

    private sealed record Scene(string Email, Guid JobId, Guid ApplicationId);

    // A new person and organization with one draft job and one application, created through the API as that person.
    private async Task<Scene> SceneAsync()
    {
        var email = ApiSession.NewEmail("e2e-shell");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        using (var onboarded = await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct))
        {
            onboarded.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title = JobTitle,
                department = "Design",
                description = "<p>Shape how small hiring teams review applicants: research, prototype and ship the recruiter dashboard with engineering, end to end.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = UkAndIreland,
            },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();
        var application = await ReadAsync(await session.PostAsync(
            $"/v1/jobs/{jobId}/applications",
            new { candidate = new { fullName = CandidateName, email = "jordan.lee@example.com" } },
            Ct), HttpStatusCode.Created);

        return new Scene(email, jobId, application.GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
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

    // The page never scrolls sideways (design system F1).
    private async Task ExpectNoSidewaysScrollAsync() =>
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the page at {0} scrolls sideways", Page.Url);

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
