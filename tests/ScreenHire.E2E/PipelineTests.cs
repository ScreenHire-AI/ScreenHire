using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// The recruiter's job page in a real browser: the candidate table and board, moving a candidate by keyboard while a
/// second session watches it move live, and one-click publish and teardown on the local sandbox board. Each test signs
/// in as a new person with a new organization, and adds its job and applications through the API as that person.
/// </summary>
public sealed class PipelineTests(E2EFixture fixture) : BrowserTest(fixture)
{
    // The sandbox board accepts and removes after AppHostFixture.SandboxDelay (4 s), once the worker has sent the request.
    private static readonly LocatorAssertionsToContainTextOptions WaitForBoard = new() { Timeout = 45_000 };

    private static readonly string[] Candidates = ["Casey Morgan", "Jordan Lee", "Riley Chen", "Sam Taylor", "Alex Rivera"];

    private static readonly string[] UkAndIreland = ["GB", "IE"];

    // The flavor D recruiter mockup's frame, so the screenshots compare with it side by side.
    private const int DesignWidth = 1440;
    private const int DesignHeight = 1040;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Moving_a_candidate_by_keyboard_updates_the_board_and_a_second_session_without_a_reload()
    {
        var scene = await SceneAsync(publish: true);
        await Page.SetViewportSizeAsync(DesignWidth, DesignHeight);
        await SignInAsync(Page, scene.Email);
        await GoToJobAsync(Page, scene.JobId);

        var table = Page.GetByRole(AriaRole.Table, new() { Name = $"Candidates for {scene.Title}" });
        await Expect(table.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = "Casey Morgan" }) }))
            .ToContainTextAsync("Applied");
        // The pipeline panel counts every candidate after its title.
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Pipeline" }).Locator("header [data-slot='badge']"))
            .ToHaveTextAsync(Candidates.Length.ToString(CultureInfo.InvariantCulture));
        await Expect(Page.GetByRole(AriaRole.Group, new() { Name = "Stage" }).GetByRole(AriaRole.Button, new() { Name = "All" }))
            .ToHaveAttributeAsync("aria-pressed", "true");
        // J moves the cursor to the next row and shows it in the side panel.
        await table.GetByRole(AriaRole.Link, new() { Name = Candidates[0] }).FocusAsync();
        await Page.Keyboard.PressAsync("j");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Selected candidate" })).ToContainTextAsync(Candidates[1]);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-job-table-light");

        // The second session: the same person in another browser, watching the same job's table.
        await using var otherContext = await Fixture.Browser.NewContextAsync();
        var other = await otherContext.NewPageAsync();
        await SignInAsync(other, scene.Email);
        await GoToJobAsync(other, scene.JobId);
        await Expect(LiveIndicator(other)).ToContainTextAsync("Live updates: Live");
        await Expect(StageCellOf(other, "Casey Morgan")).ToHaveTextAsync("Applied");
        await other.EvaluateAsync("() => { window.__screenhireNoReload = true; }");

        // V switches to the board; Shift + Right moves the focused card to the next stage.
        await Page.Keyboard.PressAsync("v");
        var card = Page.GetByRole(AriaRole.Listitem, new() { Name = "Casey Morgan, Applied" });
        await card.FocusAsync();
        await Page.Keyboard.PressAsync("Shift+ArrowRight");

        var screening = Page.GetByRole(AriaRole.List, new() { Name = "Screening stage" });
        await Expect(screening.GetByRole(AriaRole.Listitem, new() { Name = "Casey Morgan, Screening" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Moved Casey Morgan to Screening." })).ToBeVisibleAsync();
        await Expect(screening.GetByRole(AriaRole.Listitem, new() { Name = "Casey Morgan, Screening" })).ToBeFocusedAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-job-board-light");

        await Expect(StageCellOf(other, "Casey Morgan")).ToHaveTextAsync("Screening");
        (await other.EvaluateAsync<bool>("() => window.__screenhireNoReload === true")).Should().BeTrue("the table updated in place");

        // The move is recorded on the application's timeline.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Casey Morgan" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Casey Morgan");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Timeline" })).ToContainTextAsync("Moved from Applied to Screening by you");
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task Publishing_to_the_sandbox_board_and_taking_it_down_shows_each_state_the_board_confirms()
    {
        var scene = await SceneAsync(publish: false);
        await Page.SetViewportSizeAsync(DesignWidth, DesignHeight);
        await SignInAsync(Page, scene.Email);
        await GoToJobAsync(Page, scene.JobId);
        var distribution = Page.GetByRole(AriaRole.Region, new() { Name = "Distribution" });
        await Expect(Board(Page, "sandbox")).ToContainTextAsync("Sandbox board");
        await Expect(Board(Page, "sandbox")).ToContainTextAsync("Not posted to this board.");
        await ExpectNoAccessibilityViolationsAsync();

        // Publishing a draft is the page's one primary action, in its header band.
        await Page.Locator("[data-slot='header-band']").GetByRole(AriaRole.Button, new() { NameRegex = new("^Publish to \\d+ boards?$") }).ClickAsync();

        await Expect(Board(Page, "careers")).ToContainTextAsync("Live: the board confirmed the job shows.");
        await Expect(Board(Page, "sandbox")).ToContainTextAsync("Live: the board confirmed the job shows.", WaitForBoard);
        (await SandboxListingsAsync(scene.Slug)).Should().Contain(scene.JobId.ToString(), "the sandbox board lists the job");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-distribution-live-light");

        await distribution.GetByRole(AriaRole.Button, new() { Name = "Take down from Sandbox board" }).ClickAsync();

        await Expect(Board(Page, "sandbox")).ToContainTextAsync("Removed:", WaitForBoard);
        (await SandboxListingsAsync(scene.Slug)).Should().NotContain(scene.JobId.ToString(), "the sandbox board dropped the job");
        await Expect(Board(Page, "careers")).ToContainTextAsync("Live:"); // the other boards stay up
        await Expect(distribution.GetByRole(AriaRole.Button, new() { Name = "Post to Sandbox board" })).ToBeVisibleAsync();

        // Closing takes it down everywhere: a destructive item in the More menu, confirmed in a dialog (A2).
        var band = Page.Locator("[data-slot='header-band']");
        var more = band.GetByRole(AriaRole.Button, new() { Name = "More actions" });
        var closeItem = Page.GetByRole(AriaRole.Menuitem, new() { Name = "Close & remove everywhere" });
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Close this job and remove it everywhere?" });

        // Backing out leaves the job open and the page usable.
        await more.ClickAsync();
        await closeItem.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.Locator(":focus")).ToHaveCountAsync(1);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Keep it open" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(more).ToBeFocusedAsync();
        await Expect(band).ToContainTextAsync("Open");
        await Expect(Board(Page, "careers")).ToContainTextAsync("Live:");

        await more.ClickAsync();
        await closeItem.ClickAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Close & remove everywhere" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(Board(Page, "careers")).ToContainTextAsync("Removed:");
        await Expect(band).ToContainTextAsync("Closed");
        await Expect(more).ToBeHiddenAsync();

        // The dark theme, for comparing with the design.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Switch to dark theme" }).ClickAsync();
        await Expect(Page.Locator("html")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bdark\\b"));
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-distribution-closed-dark");
    }

    [Fact]
    public async Task The_shell_has_the_command_palette_go_to_shortcuts_and_saved_views()
    {
        var scene = await SceneAsync(publish: true);
        await SignInAsync(Page, scene.Email);

        await Page.Keyboard.PressAsync("g");
        await Page.Keyboard.PressAsync("c");
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/candidates"));
        var table = Page.GetByRole(AriaRole.Table, new() { Name = "Candidates" });
        await Expect(table.GetByRole(AriaRole.Link, new() { Name = scene.Title })).ToHaveCountAsync(Candidates.Length);
        await Expect(Page.GetByRole(AriaRole.Link, new() { NameRegex = new("^New today") })).ToContainTextAsync(Candidates.Length.ToString(CultureInfo.InvariantCulture));
        await ExpectNoAccessibilityViolationsAsync();

        await Page.Keyboard.PressAsync("Control+k");
        var palette = Page.GetByRole(AriaRole.Dialog, new() { Name = "Command palette" });
        await palette.GetByPlaceholder("Search jobs, candidates and actions").FillAsync("Riley");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.Keyboard.PressAsync("Enter");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Riley Chen");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Answers" })).ToContainTextAsync("No questionnaire answers.");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Timeline" })).ToContainTextAsync("Added to Applied by you");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-application-light");

        await Page.Keyboard.PressAsync("g");
        await Page.Keyboard.PressAsync("j");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");

        // From inside the candidate table, whose own J moves the cursor, G J still goes to the jobs.
        await Page.Keyboard.PressAsync("g");
        await Page.Keyboard.PressAsync("c");
        await table.GetByRole(AriaRole.Link, new() { Name = "Riley Chen" }).FocusAsync();
        await Page.Keyboard.PressAsync("g");
        await Page.Keyboard.PressAsync("j");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    [Fact]
    public async Task The_job_page_fits_a_phone_without_scrolling_sideways()
    {
        var scene = await SceneAsync(publish: false);
        await Page.SetViewportSizeAsync(390, 844);
        await SignInAsync(Page, scene.Email);
        await GoToJobAsync(Page, scene.JobId);
        // On a phone the candidate table is a list of two-line items (design system section 8).
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = $"Candidates for {scene.Title}" }).GetByRole(AriaRole.Link, new() { Name = Candidates[0] }))
            .ToBeVisibleAsync();

        // The chips' sr-only labels sit inside their list items and must not widen the page.
        var overflow = await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        overflow.Should().Be(0, "the page never scrolls sideways (design system F1)");
    }

    private sealed record Scene(string Email, string Slug, Guid JobId, string Title);

    // A new person and organization with one job (published or a draft) and an application per name in Candidates,
    // created through the API as that person, so the browser signs in to exactly this data.
    private async Task<Scene> SceneAsync(bool publish)
    {
        var email = ApiSession.NewEmail("e2e-pipeline");
        var title = $"Senior Product Designer {Guid.NewGuid().ToString("N")[..4]}";
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        var slug = $"test-{Guid.NewGuid():N}";
        using (var onboarded = await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug }, Ct))
        {
            onboarded.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title,
                department = "Design",
                description = "<p>Shape how small hiring teams review applicants: research, prototype and ship the recruiter dashboard with engineering, end to end.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = UkAndIreland,
                salary = new { min = 70_000, max = 85_000, currency = "GBP", period = "Year" },
            },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();
        if (publish)
        {
            await ReadAsync(await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct), HttpStatusCode.OK);
        }

        // Oldest first, so the list (newest first) starts with the first name.
        foreach (var name in Candidates.Reverse())
        {
            var address = $"{name.ToLowerInvariant().Replace(' ', '.')}@example.com";
            await ReadAsync(await session.PostAsync(
                $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = name, email = address } }, Ct), HttpStatusCode.Created);
        }

        return new Scene(email, slug, jobId, title);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    private async Task SignInAsync(IPage page, string email)
    {
        var response = await page.GotoAsync(DashboardUrl("/sign-in"));
        response!.Ok.Should().BeTrue();
        await page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await page.GetByLabel("Any email address").FillAsync(email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private async Task GoToJobAsync(IPage page, Guid jobId)
    {
        await page.GotoAsync(DashboardUrl($"/jobs/{jobId}"));
        await Expect(page.GetByRole(AriaRole.Region, new() { Name = "Distribution" })).ToBeVisibleAsync();
    }

    // The page's own /v1 proxy, with the page's cookies; the sandbox board's public listing needs no session.
    private async Task<string> SandboxListingsAsync(string slug)
    {
        var response = await Page.APIRequest.GetAsync(DashboardUrl($"/v1/public/sandbox-board/{slug}/listings"));
        response.Ok.Should().BeTrue();
        return await response.TextAsync();
    }

    private static ILocator Board(IPage page, string boardId) =>
        page.GetByRole(AriaRole.Region, new() { Name = "Distribution" }).Locator($"[data-board='{boardId}']");

    private static ILocator StageCellOf(IPage page, string candidate) =>
        page.GetByRole(AriaRole.Row).Filter(new() { Has = page.GetByRole(AriaRole.Link, new() { Name = candidate, Exact = true }) })
            .GetByRole(AriaRole.Cell).Nth(2);

    private static ILocator LiveIndicator(IPage page) =>
        page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Live updates" });

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
