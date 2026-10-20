using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ScreenHire.Application.Integrity;

namespace ScreenHire.E2E;

/// <summary>
/// The integrity UI in a real browser (ADR-0004, docs/architecture/integrity-engine.md section 7): the synthetic
/// applicants apply through the careers submission path while the recruiter watches the job, their chips and the job's
/// overview arrive live, the evidence panel shows the reasons, evidence, next step and AI policy, and a dismissal with a
/// reason lands in the audit log and takes the candidate out of "Needs review". Each test signs in as a new person with
/// a new organization.
/// </summary>
public sealed class IntegrityPanelTests(E2EFixture fixture) : BrowserTest(fixture)
{
    // The worker assesses asynchronously after each submission; the first run after the stack starts is the slowest.
    private static readonly LocatorAssertionsToContainTextOptions Assessed = new() { Timeout = 60_000 };

    private static readonly string[] Gb = ["GB"];

    private const int DesignWidth = 1440;
    private const int DesignHeight = 1040;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Synthetic_applicants_show_their_levels_live_with_reasons_and_a_dismissal_is_logged()
    {
        var scene = await SceneAsync();
        await Page.SetViewportSizeAsync(DesignWidth, DesignHeight);
        await SignInAsync(scene.Email);
        await Page.GotoAsync(DashboardUrl($"/jobs/{scene.JobId}"));
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Live updates" })).ToContainTextAsync("Live updates: Live");
        await Page.EvaluateAsync("() => { window.__screenhireNoReload = true; }");

        var genuine = SyntheticApplicants.GenuineHuman;
        var bot = SyntheticApplicants.Bot;
        var (farmFirst, farmSecond) = (SyntheticApplicants.ApplicationFarm[0], SyntheticApplicants.ApplicationFarm[1]);
        var botId = Guid.Empty;
        foreach (var applicant in new[] { genuine, bot, farmFirst, farmSecond })
        {
            var id = await Fixture.ApplyAsync(scene.Slug, scene.JobId, applicant, Ct);
            if (applicant == bot)
            {
                botId = id;
            }
        }

        // The chips arrive with IntegrityAssessed, in the page that was already open. Levels as in the golden files
        // (tests/ScreenHire.UnitTests/Integrity/Golden); the farm's reason counts differ from them because these
        // submissions answer only the resume question, which makes 20 seconds a plausible time to complete the form.
        await Expect(Chip(genuine.FullName, "authenticity")).ToContainTextAsync("Authenticity: No concerns", Assessed);
        await Expect(Chip(bot.FullName, "authenticity")).ToContainTextAsync("Authenticity: Verification recommended, 6 reasons", Assessed);
        await Expect(Chip(farmFirst.FullName, "authenticity")).ToContainTextAsync("Authenticity: Verification recommended", Assessed);
        await Expect(Chip(farmSecond.FullName, "authenticity")).ToContainTextAsync("Authenticity: Verification recommended", Assessed);
        await Expect(Chip(bot.FullName, "aiAssistance")).ToContainTextAsync("AI assistance: Not indicated");
        (await Page.EvaluateAsync<bool>("() => window.__screenhireNoReload === true")).Should().BeTrue("the chips updated in place");

        var legend = Page.GetByRole(AriaRole.List, new() { Name = "Authenticity levels" });
        await Expect(legend.GetByRole(AriaRole.Button, new() { Name = "1 No concerns" })).ToBeVisibleAsync();
        await Expect(legend.GetByRole(AriaRole.Button, new() { Name = "3 Verification recommended" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-integrity-job-light");

        // The legend filters the table to one level, and a second press shows everyone again.
        var table = Page.GetByRole(AriaRole.Table, new() { Name = $"Candidates for {scene.Title}" });
        await legend.GetByRole(AriaRole.Button, new() { Name = "3 Verification recommended" }).ClickAsync();
        await Expect(CandidateLinks(table)).ToHaveCountAsync(3);
        await Expect(table.GetByRole(AriaRole.Link, new() { Name = genuine.FullName })).ToHaveCountAsync(0);
        await legend.GetByRole(AriaRole.Button, new() { Name = "3 Verification recommended" }).ClickAsync();
        await Expect(CandidateLinks(table)).ToHaveCountAsync(4);

        // The bot's evidence panel: reasons, evidence, the next step and the AI policy, and nothing that decides.
        await table.GetByRole(AriaRole.Link, new() { Name = bot.FullName }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(bot.FullName);
        var panel = Page.GetByRole(AriaRole.Region, new() { Name = "Integrity" });
        var authenticity = panel.GetByRole(AriaRole.Region, new() { Name = "Authenticity" });
        await Expect(authenticity).ToContainTextAsync("Authenticity: Verification recommended", Assessed);
        foreach (var code in new[] { "AUTO_HONEYPOT", "AUTO_SUBMIT_VELOCITY", "AUTO_WEBDRIVER", "ID_EMAIL_DISPOSABLE", "ID_PHONE_INVALID", "NET_DATACENTER_IP" })
        {
            await Expect(authenticity.Locator($"[data-signal='{code}']")).ToBeVisibleAsync();
        }

        await Expect(authenticity).ToContainTextAsync("Suggested next step: Confirm the candidate is who they say they are");
        await Expect(authenticity.Locator("[data-signal='NET_DATACENTER_IP']")).ToContainTextAsync("Example Cloud Hosting (AS64497)");
        await Expect(authenticity.Locator("[data-signal='AUTO_WEBDRIVER'] [data-evidence-kind='List']")).ToContainTextAsync("webdriver");
        await Expect(authenticity.Locator("[data-signal='AUTO_SUBMIT_VELOCITY'] [data-evidence-kind='Metric']")).ToContainTextAsync("Plausible minimum for this form");
        var ai = panel.GetByRole(AriaRole.Region, new() { Name = "AI assistance · informational" });
        await Expect(ai).ToContainTextAsync("AI assistance: Not indicated");
        await Expect(ai).ToContainTextAsync("Job policy: Allowed with disclosure · Declared AI use: No");
        await Expect(panel.GetByRole(AriaRole.Button, new() { Name = "Request verification" })).ToBeDisabledAsync();
        await Expect(panel).ToContainTextAsync("Phase 2");
        await Expect(panel.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("reject", RegexOptions.IgnoreCase) })).ToHaveCountAsync(0);
        await Expect(panel).ToContainTextAsync("No automated decision has been made. Every action is logged.");
        (await panel.TextContentAsync()).Should().NotContain("%", "integrity output has no percentage (ADR-0004)");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-integrity-panel-light");

        // Dismissing with a reason records the decision; the level stays as assessed.
        await authenticity.GetByRole(AriaRole.Button, new() { Name = "Dismiss authenticity with a reason" }).ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Dismiss this authenticity assessment?" });
        await dialog.GetByRole(AriaRole.Radio, new() { Name = "Verified outside ScreenHire" }).CheckAsync();
        await dialog.GetByLabel("Note (optional)").FillAsync("Video call on Monday; ID checked.");
        await ExpectNoAccessibilityViolationsAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Dismiss with this reason" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();

        var log = panel.GetByRole(AriaRole.Region, new() { Name = "Review log" });
        await Expect(log).ToContainTextAsync("Dismissed authenticity: Verified outside ScreenHire by you");
        await Expect(log).ToContainTextAsync("\"Video call on Monday; ID checked.\"");
        await Expect(authenticity).ToContainTextAsync("Authenticity: Verification recommended, dismissed after review");
        await Expect(authenticity.GetByRole(AriaRole.Button, new() { Name = "Dismiss authenticity with a reason" })).ToHaveCountAsync(0);

        // The audit entry is in the API's record too, with the reviewer.
        var record = await Page.APIRequest.GetAsync(DashboardUrl($"/v1/applications/{botId}/integrity"));
        record.Ok.Should().BeTrue();
        using (var json = JsonDocument.Parse(await record.TextAsync()))
        {
            var entry = json.RootElement.GetProperty("reviewLog").EnumerateArray().Single();
            entry.GetProperty("type").GetString().Should().Be("Dismissed");
            entry.GetProperty("dismissReason").GetString().Should().Be("VerifiedOutsideScreenHire");
            entry.GetProperty("note").GetString().Should().Be("Video call on Monday; ID checked.");
            json.RootElement.GetProperty("authenticity").GetProperty("level").GetString().Should().Be("VerificationRecommended");
        }

        await Page.GetByRole(AriaRole.Button, new() { Name = "Switch to dark theme" }).ClickAsync();
        await Expect(Page.Locator("html")).ToHaveClassAsync(new Regex("\\bdark\\b"));
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-integrity-panel-dark");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" }).ClickAsync();

        // "Needs review" now holds the two farm members; the dismissed bot and the genuine applicant are not in it.
        await Page.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^Needs review") }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Needs review");
        var queue = Page.GetByRole(AriaRole.Table, new() { Name = "Candidates" });
        await Expect(CandidateLinks(queue)).ToHaveCountAsync(2);
        await Expect(queue.GetByRole(AriaRole.Link, new() { Name = farmFirst.FullName })).ToBeVisibleAsync();
        await Expect(queue.GetByRole(AriaRole.Link, new() { Name = farmSecond.FullName })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // A farm member's panel shows what it shares with the others; the genuine applicant's has nothing to dismiss.
        await queue.GetByRole(AriaRole.Link, new() { Name = farmSecond.FullName }).ClickAsync();
        var farmPanel = Page.GetByRole(AriaRole.Region, new() { Name = "Integrity" });
        await Expect(farmPanel.GetByRole(AriaRole.Region, new() { Name = "Authenticity" })).ToContainTextAsync("Authenticity: Verification recommended", Assessed);
        var shared = farmPanel.Locator("[data-signal='ID_DUPLICATE_IN_ORG']");
        await Expect(shared).ToContainTextAsync("Same device");
        await Expect(shared).ToContainTextAsync("Same resume file");
        await Expect(farmPanel.Locator("[data-signal='NET_DATACENTER_IP']")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        await Page.GotoAsync(DashboardUrl($"/jobs/{scene.JobId}"));
        await Page.GetByRole(AriaRole.Table, new() { Name = $"Candidates for {scene.Title}" }).GetByRole(AriaRole.Link, new() { Name = genuine.FullName }).ClickAsync();
        var genuinePanel = Page.GetByRole(AriaRole.Region, new() { Name = "Integrity" });
        await Expect(genuinePanel.GetByRole(AriaRole.Region, new() { Name = "Authenticity" })).ToContainTextAsync("Authenticity: No concerns", Assessed);
        await Expect(genuinePanel).ToContainTextAsync("Suggested next step: No action needed.");
        await Expect(genuinePanel.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Dismiss") })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
    }

    private sealed record Scene(string Email, string Slug, Guid JobId, string Title);

    // A new person and organization with one published job in Great Britain, as the integrity integration tests use,
    // created through the API as that person.
    private async Task<Scene> SceneAsync()
    {
        var email = ApiSession.NewEmail("e2e-integrity");
        var title = $"Platform Engineer {Guid.NewGuid().ToString("N")[..4]}";
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        var slug = $"test-{Guid.NewGuid():N}";
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);
        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title,
                description = "<p>Build and run the things people rely on.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Gb,
                salary = new { min = 50_000, max = 60_000, currency = "GBP", period = "Year" },
            },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct), HttpStatusCode.OK);
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

    private async Task SignInAsync(string email)
    {
        await GoToAsync("dashboard", "/sign-in");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private ILocator Chip(string candidate, string kind) =>
        Page.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = candidate, Exact = true }) })
            .Locator($"[data-integrity-chip='{kind}']");

    private static ILocator CandidateLinks(ILocator table) => table.Locator("[data-row-link]");

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
