using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ScreenHire.Application.Integrity;

namespace ScreenHire.E2E;

/// <summary>
/// The job page on design system v0.3 (docs/design/design-system.md section 22) in a real browser: the authenticity
/// chips and the Stage filter, a sort that outlives a reload, a posting a board did not take as a notice with the
/// Distribution tab's count, and a board that fits a wide window without moving the page. Each test signs in as a new
/// person with a new organization, and adds its job and applications through the API.
/// </summary>
public sealed class JobPageTests(E2EFixture fixture) : BrowserTest(fixture)
{
    // The worker assesses asynchronously after each submission; the first run after the stack starts is the slowest.
    private static readonly LocatorAssertionsToContainTextOptions Assessed = new() { Timeout = 60_000 };

    private static readonly string[] UkAndIreland = ["GB", "IE"];

    // Over 100 characters of text, so every board takes the posting.
    private const string FullDescription =
        "<p>Shape how small hiring teams review applicants: research, prototype and ship the recruiter dashboard with engineering, end to end.</p>";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_authenticity_chip_and_the_stage_filter_narrow_the_candidates()
    {
        var scene = await SceneAsync(FullDescription);
        var genuine = SyntheticApplicants.GenuineHuman;
        var bot = SyntheticApplicants.Bot;
        // Both of the farm's applications, which together are what makes it a farm.
        var (farm, farmSecond) = (SyntheticApplicants.ApplicationFarm[0], SyntheticApplicants.ApplicationFarm[1]);
        var ids = new Dictionary<string, Guid>();
        foreach (var applicant in new[] { genuine, bot, farm, farmSecond })
        {
            ids[applicant.FullName] = await Fixture.ApplyAsync(scene.Slug, scene.JobId, applicant, Ct);
        }

        using (var session = await SessionAsync(scene.Email))
        {
            await ReadAsync(await session.PostAsync($"/v1/applications/{ids[farm.FullName]}/move", new { toStageId = "screening" }, Ct), HttpStatusCode.OK);
        }

        await Page.SetViewportSizeAsync(1440, 1040);
        await SignInAsync(Page, scene.Email);
        await GoToJobAsync(scene.JobId);
        var pipeline = Page.GetByRole(AriaRole.Region, new() { Name = "Pipeline" });
        var table = Page.GetByRole(AriaRole.Table, new() { Name = $"Candidates for {scene.Title}" });
        await Expect(Chip(bot.FullName)).ToContainTextAsync("Authenticity: Verification suggested", Assessed);
        await Expect(Chip(farm.FullName)).ToContainTextAsync("Authenticity: Verification suggested", Assessed);
        await Expect(Chip(farmSecond.FullName)).ToContainTextAsync("Authenticity: Verification suggested", Assessed);
        await Expect(Chip(genuine.FullName)).ToContainTextAsync("Authenticity: No concerns", Assessed);

        // The chips carry each level's count; one press narrows the table to that level.
        var chips = pipeline.GetByRole(AriaRole.Group, new() { Name = "Authenticity" });
        await chips.GetByRole(AriaRole.Button, new() { Name = "Verification suggested 3" }).ClickAsync();
        await Expect(table.Locator("[data-row-link]")).ToHaveCountAsync(3);
        await Expect(table.GetByRole(AriaRole.Link, new() { Name = genuine.FullName })).ToHaveCountAsync(0);

        // The Stage filter narrows it further, to the one in Screening.
        await pipeline.GetByRole(AriaRole.Button, new() { Name = "Stage: All" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitemradio, new() { NameRegex = new("^Screening") }).ClickAsync();
        await Expect(pipeline.GetByRole(AriaRole.Button, new() { Name = "Stage: Screening" })).ToBeVisibleAsync();
        await Expect(table.Locator("[data-row-link]")).ToHaveCountAsync(1);
        await Expect(table.GetByRole(AriaRole.Link, new() { Name = farm.FullName })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-job-filtered-light");

        // A ticked row gets the bar; under an authenticity filter it offers Move but no Email (ADR-0004).
        await pipeline.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {farm.FullName}" }).CheckAsync();
        var bar = pipeline.GetByRole(AriaRole.Region, new() { Name = "Selected candidates" });
        await Expect(bar).ToContainTextAsync("1 selected");
        await bar.GetByRole(AriaRole.Button, new() { Name = "Move 1 selected…" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Offer" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Rejected" })).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(bar.GetByRole(AriaRole.Button, new() { NameRegex = new("^Email") })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_sort_is_kept_after_a_reload()
    {
        var scene = await SceneAsync(FullDescription, candidates: ["Casey Morgan", "Alex Rivera", "Jordan Lee"]);
        await Page.SetViewportSizeAsync(1440, 1040);
        await SignInAsync(Page, scene.Email);
        await GoToJobAsync(scene.JobId);
        var table = Page.GetByRole(AriaRole.Table, new() { Name = $"Candidates for {scene.Title}" });

        // A job's candidates open with those needing attention first: the Stage column (L2).
        await Expect(table.GetByRole(AriaRole.Columnheader, new() { Name = "Stage" })).ToHaveAttributeAsync("aria-sort", "ascending");
        await table.GetByRole(AriaRole.Button, new() { Name = "Candidate", Exact = true }).ClickAsync();
        await Expect(table.GetByRole(AriaRole.Columnheader, new() { Name = "Candidate", Exact = true })).ToHaveAttributeAsync("aria-sort", "ascending");
        await Expect(table.Locator("[data-row-link]")).ToHaveTextAsync(["Alex Rivera", "Casey Morgan", "Jordan Lee"]);

        await Page.ReloadAsync();

        await Expect(table.GetByRole(AriaRole.Columnheader, new() { Name = "Candidate", Exact = true })).ToHaveAttributeAsync("aria-sort", "ascending");
        await Expect(table.Locator("[data-row-link]")).ToHaveTextAsync(["Alex Rivera", "Casey Morgan", "Jordan Lee"]);
        await Expect(table.GetByRole(AriaRole.Columnheader, new() { Name = "Stage" })).Not.ToHaveAttributeAsync("aria-sort", "ascending");
    }

    [Fact]
    public async Task A_posting_a_board_did_not_take_is_a_notice_and_the_Distribution_tab_counts_the_boards_the_job_is_on()
    {
        // Between 50 and 100 characters of text: enough for Talent.com, too short for Adzuna, whose checks keep the posting
        // back with an issue (docs/architecture/job-distribution.md, "What each state means").
        var scene = await SceneAsync("<p>Run the recruiter dashboard's release train with the platform team.</p>");
        int boards;
        using (var session = await SessionAsync(scene.Email))
        using (var response = await session.Client.GetAsync("/v1/boards", Ct))
        {
            boards = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength();
        }

        await Page.SetViewportSizeAsync(1440, 1040);
        await SignInAsync(Page, scene.Email);
        await GoToJobAsync(scene.JobId);

        var notice = Page.Locator("[data-slot='notice']");
        await Expect(notice).ToContainTextAsync("Adzuna did not take this job. Adzuna needs a description of at least 100 characters.");
        var posted = (boards - 1).ToString(CultureInfo.InvariantCulture);
        var all = boards.ToString(CultureInfo.InvariantCulture);
        // Chromium puts a space before the count badge when it names the tab from its content.
        var distribution = Page.GetByRole(AriaRole.Tab, new() { NameRegex = new($"^Distribution ?, posted on {posted} of {all} boards$") });
        await Expect(distribution).ToBeVisibleAsync();
        await Expect(distribution).ToContainTextAsync($"{posted}/{all}");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-job-refused-posting-light");

        // The notice links to the fix: the Distribution tab, where the board's row says what is wrong.
        await notice.GetByRole(AriaRole.Link, new() { Name = "Fix on Distribution" }).ClickAsync();
        await Expect(distribution).ToHaveAttributeAsync("aria-selected", "true");
        var adzuna = Page.GetByRole(AriaRole.Region, new() { Name = "Distribution" }).Locator("[data-board='adzuna']");
        await Expect(adzuna.Locator("[data-posting-problem]")).ToContainTextAsync("Adzuna needs a description of at least 100 characters.");
        await Expect(adzuna.GetByRole(AriaRole.Link, new() { Name = "Edit details" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // The tab is in the address, so a reload keeps it.
        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Distribution") })).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Fact]
    public async Task At_1920_every_stage_of_the_board_shows_without_the_page_scrolling()
    {
        var scene = await SceneAsync(FullDescription, candidates: ["Casey Morgan", "Alex Rivera", "Jordan Lee", "Riley Chen", "Sam Taylor", "Robin Ashby"]);
        using (var session = await SessionAsync(scene.Email))
        {
            await ReadAsync(await session.PostAsync($"/v1/applications/{scene.Applications[0]}/move", new { toStageId = "screening" }, Ct), HttpStatusCode.OK);
        }

        await Page.SetViewportSizeAsync(1920, 1080);
        await SignInAsync(Page, scene.Email);
        await GoToJobAsync(scene.JobId);
        await Page.GetByRole(AriaRole.Region, new() { Name = "Pipeline" }).GetByRole(AriaRole.Button, new() { Name = "Board", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Applied stage" })).ToBeVisibleAsync();

        var fit = await Page.EvaluateAsync<JsonElement>(
            """
            () => {
              const board = document.querySelector('[data-pipeline-board]');
              const edge = board.getBoundingClientRect();
              const columns = [...board.querySelectorAll('[data-stage-column]')].map((column) => {
                const box = column.getBoundingClientRect();
                return { id: column.dataset.stageColumn, width: Math.round(box.width), inside: box.left >= edge.left - 1 && box.right <= edge.right + 1 };
              });
              const page = document.documentElement;
              return {
                columns,
                sideways: board.scrollWidth - board.clientWidth,
                pageDown: page.scrollHeight - page.clientHeight,
                pageSideways: page.scrollWidth - page.clientWidth,
              };
            }
            """);

        var columns = fit.GetProperty("columns").EnumerateArray().ToList();
        columns.Select(column => column.GetProperty("id").GetString()).Should().Equal("applied", "screening", "interview", "offer", "closed");
        columns.Should().OnlyContain(column => column.GetProperty("inside").GetBoolean(), "every stage shows at 1920px");
        columns.Select(column => column.GetProperty("width").GetInt32()).Should().Equal(296, 296, 188, 188, 188);
        fit.GetProperty("sideways").GetInt32().Should().Be(0, "the board needs no sideways scroll at 1920px");
        fit.GetProperty("pageDown").GetInt32().Should().Be(0, "the board scrolls inside itself, never the page");
        fit.GetProperty("pageSideways").GetInt32().Should().Be(0, "the page never scrolls sideways (F1)");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-job-board-1920-light");
    }

    private sealed record Scene(string Email, string Slug, Guid JobId, string Title, IReadOnlyList<Guid> Applications);

    // A new person and organization with one published job and an application per name, created through the API as that
    // person, so the browser signs in to exactly this data.
    private async Task<Scene> SceneAsync(string description, string[]? candidates = null)
    {
        var email = ApiSession.NewEmail("e2e-job-page");
        var title = $"Release Engineer {Guid.NewGuid().ToString("N")[..4]}";
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        var slug = $"test-{Guid.NewGuid():N}";
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);
        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title,
                description,
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = UkAndIreland,
                salary = new { min = 60_000, max = 75_000, currency = "GBP", period = "Year" },
            },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct), HttpStatusCode.OK);

        var applications = new List<Guid>();
        foreach (var name in candidates ?? [])
        {
            var address = $"{name.ToLowerInvariant().Replace(' ', '.')}@example.com";
            var application = await ReadAsync(await session.PostAsync(
                $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = name, email = address } }, Ct), HttpStatusCode.Created);
            applications.Add(application.GetProperty("id").GetGuid());
        }

        return new Scene(email, slug, jobId, title, applications);
    }

    private async Task<ApiSession> SessionAsync(string email)
    {
        var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        return session;
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

    private async Task GoToJobAsync(Guid jobId)
    {
        await Page.GotoAsync(DashboardUrl($"/jobs/{jobId}"));
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Pipeline" })).ToBeVisibleAsync();
    }

    private ILocator Chip(string candidate) =>
        Page.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = candidate, Exact = true }) })
            .Locator("[data-integrity-chip='authenticity']");

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
