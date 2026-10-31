using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ScreenHire.Application.Verification;

namespace ScreenHire.E2E;

/// <summary>
/// The candidate page on the detail template (design system sections 7, 11 and 21) at the sizes F1 names and the review
/// canvas's 820 and 390: who they are and where they stand in the header band, with no rail at the window's edge (S7);
/// Overview's two columns of panels where they fit, one column below that; and Move to stage in a bar at the bottom on a
/// phone. The same six tabs for every candidate, and a header that does not move between them. Signs in as a new person
/// with a new organization.
/// </summary>
public sealed class CandidatePageTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] Gb = ["GB"];

    private static readonly string[] TabNames = ["Overview", "Application", "Messages", "Scorecards", "Verification", "Activity"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_candidate_page_follows_the_detail_template_at_every_size()
    {
        var applicant = SyntheticApplicants.GenuineHuman;
        var (email, applicationIds) = await ApplicationsAsync(applicant);

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(email);
        await Page.GotoAsync(DashboardUrl($"/applications/{applicationIds[0]}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(applicant.FullName);
        var move = Page.GetByRole(AriaRole.Button, new() { Name = $"Move to stage, {applicant.FullName}", Exact = true });
        await Expect(move).ToBeVisibleAsync();
        await WaitForTabCountsAsync();

        // Wide: Overview's two columns side by side, Files beside Next steps. Narrower: one column, Files after Next steps.
        foreach (var (width, height, name) in new[] { (1920, 1080, "wide"), (1440, 1000, "desktop"), (1024, 900, "laptop"), (820, 1180, "tablet"), (768, 1024, "small-tablet"), (390, 844, "phone"), (360, 780, "small-phone") })
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.EvaluateAsync("() => window.scrollTo(0, 0)");
            await SettledAsync();

            (await Page.GetByRole(AriaRole.Complementary, new() { Name = "Details" }).CountAsync()).Should().Be(0, "a detail page has no rail (S7)");
            var files = await BoxAsync(Region("Files"));
            var content = await BoxAsync(Region("Next steps"));
            var action = await BoxAsync(move);
            if (width >= 1440)
            {
                files.X.Should().BeGreaterThan(content.X + content.Width, $"Files is beside Next steps at {width}px");
                files.Y.Should().BeApproximately(content.Y, 1, "panels in a row share a top edge (S4)");
            }
            else
            {
                files.Y.Should().BeGreaterThan(content.Y + content.Height, $"one column at {width}px, Files after Next steps");
                files.X.Should().BeApproximately(content.X, 1);
            }

            if (width < 768)
            {
                (action.Y + action.Height).Should().BeGreaterThan(height - 80, "phones keep the primary action in a bar at the bottom");
            }
            else
            {
                action.Y.Should().BeLessThan(content.Y, "the primary action is in the header band");
            }

            await ExpectNoSideScrollAsync();
            await ExpectNoAccessibilityViolationsAsync();
            await SaveScreenshotAsync(Page, $"dashboard-candidate-{name}");
        }

        // The bar's menu still moves the candidate.
        await move.ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Screening" }).ClickAsync();
        await Expect(Region("Activity")).ToContainTextAsync("Moved from Applied to Screening by you");
    }

    [Fact]
    public async Task Two_candidates_show_the_same_six_tabs_in_the_same_order()
    {
        var genuine = SyntheticApplicants.GenuineHuman;
        var bot = SyntheticApplicants.Bot;
        var (email, applicationIds) = await ApplicationsAsync(genuine, bot);

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(email);

        await Page.GotoAsync(DashboardUrl($"/applications/{applicationIds[0]}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(genuine.FullName);
        var genuineTabs = await TabLabelsAsync();

        // The bot's level needs a look, so its Verification tab counts the signals behind it; the genuine candidate's has
        // no count.
        await Page.GotoAsync(DashboardUrl($"/applications/{applicationIds[1]}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(bot.FullName);
        await Expect(Tab("Verification")).ToHaveAccessibleNameAsync(new Regex(@"^Verification \d+$"), new() { Timeout = 60_000 });
        var botTabs = await TabLabelsAsync();

        genuineTabs.Should().Equal(TabNames);
        botTabs.Should().Equal(TabNames);
        await Page.GotoAsync(DashboardUrl($"/applications/{applicationIds[0]}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(genuine.FullName);
        await Expect(Tab("Verification")).ToHaveAccessibleNameAsync("Verification", new() { Timeout = 60_000 });
    }

    [Fact]
    public async Task The_header_does_not_move_when_the_tab_changes()
    {
        var applicant = SyntheticApplicants.GenuineHuman;
        var (email, applicationIds) = await ApplicationsAsync(applicant);

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(email);
        await Page.GotoAsync(DashboardUrl($"/applications/{applicationIds[0]}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(applicant.FullName);
        // The tabs' counts arrive with the timeline; measure once the header band has taken its final shape.
        await WaitForTabCountsAsync();
        await SettledAsync();
        await Page.EvaluateAsync("() => { document.querySelector('[data-slot=\"header-band\"]').dataset.probe = 'same element'; }");
        var before = await BoxAsync(Band);
        await ExpectNoAccessibilityViolationsAsync();

        // Each tab shows its own panel; the header band, with the candidate's facts, is the same element in the same place.
        var tabs = new (string Tab, string Url, ILocator Content)[]
        {
            ("Verification", "tab=verification", Region("Review log")),
            ("Activity", "tab=activity", Page.GetByRole(AriaRole.Group, new() { Name = "Show" })),
            ("Application", "tab=application", Region("Declarations")),
            ("Messages", "tab=messages", Region("Messages")),
            ("Scorecards", "tab=scorecards", Region("Scorecards")),
            ("Overview", "", Region("Next steps")),
        };
        foreach (var (tab, url, content) in tabs)
        {
            await Tab(tab).ClickAsync();
            await Expect(Tab(tab)).ToHaveAttributeAsync("aria-selected", "true");
            await Expect(Page).ToHaveURLAsync(new Regex(url.Length == 0 ? @"/applications/[0-9a-f-]+$" : Regex.Escape(url) + "$"));
            await Expect(content).ToBeVisibleAsync();

            var after = await BoxAsync(Band);
            after.Y.Should().BeApproximately(before.Y, 1, $"the header stays put on {tab}");
            after.Height.Should().BeApproximately(before.Height, 1, $"the header keeps its height on {tab} (S8)");
            (await Page.EvaluateAsync<string?>("() => document.querySelector('[data-slot=\"header-band\"]')?.dataset.probe ?? null"))
                .Should().Be("same element", $"the header is not rebuilt on {tab}");
            await Expect(Band.GetByRole(AriaRole.Link, new() { Name = "Platform Engineer" })).ToBeVisibleAsync();
        }
    }

    private ILocator Band => Page.Locator("[data-slot='header-band']");

    private ILocator Region(string name) => Page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true });

    private ILocator Tab(string name) =>
        Page.GetByRole(AriaRole.Tablist, new() { Name = "Candidate sections" }).GetByRole(AriaRole.Tab, new() { NameRegex = new Regex($"^{name}") });

    // The leading word of each tab's name, in order: the name goes on with a count or ", Verification suggested".
    private async Task<IReadOnlyList<string>> TabLabelsAsync() =>
        [.. (await Page.GetByRole(AriaRole.Tablist, new() { Name = "Candidate sections" }).GetByRole(AriaRole.Tab).AllTextContentsAsync())
            .Select(text => Regex.Match(text.Trim(), "^[A-Za-z]+").Value)];

    // Activity's count joins its tab when the timeline has loaded.
    private async Task WaitForTabCountsAsync() =>
        await Expect(Tab("Activity")).ToHaveAccessibleNameAsync(new Regex(@"^Activity \d+$"));

    // The sidebar switches between full, rail and sheet a frame after a resize and then slides; measure once the content
    // has held still for 20 frames.
    private async Task SettledAsync() => await Page.EvaluateAsync(
        """
        () => new Promise((resolve) => {
          let last = '', still = 0;
          const step = () => {
            const box = document.querySelector('[data-slot="header-band"]').getBoundingClientRect();
            const key = `${box.left},${box.width}`;
            still = key === last ? still + 1 : 0;
            last = key;
            if (still >= 20) resolve(); else requestAnimationFrame(step);
          };
          requestAnimationFrame(step);
        })
        """);

    private static async Task<LocatorBoundingBoxResult> BoxAsync(ILocator locator)
    {
        await Expect(locator).ToBeVisibleAsync();
        return (await locator.BoundingBoxAsync())!;
    }

    private async Task ExpectNoSideScrollAsync() =>
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the page never scrolls sideways (F1)");

    // A new person and organization with one published job, and the applicants through the careers submission path, in order.
    private async Task<(string Email, IReadOnlyList<Guid> ApplicationIds)> ApplicationsAsync(params SyntheticApplicant[] applicants)
    {
        var email = ApiSession.NewEmail("e2e-candidate");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        var slug = $"test-{Guid.NewGuid():N}";
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);
        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title = "Platform Engineer",
                description = "<p>Build and run the things people rely on.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Gb,
            },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct), HttpStatusCode.OK);
        var ids = new List<Guid>();
        foreach (var applicant in applicants)
        {
            ids.Add(await Fixture.ApplyAsync(slug, jobId, applicant, Ct));
        }

        return (email, ids);
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
        await GoToAsync("dashboard", "/sign-in");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
