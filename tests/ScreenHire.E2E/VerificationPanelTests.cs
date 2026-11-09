using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Verification;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.E2E;

/// <summary>
/// The verification UI in a real browser (ADR-0004, docs/architecture/verification-engine.md section 7): the synthetic
/// applicants apply through the careers submission path while the recruiter watches the job, their chips and the job's
/// overview arrive live, the evidence panel shows the reasons, evidence, next step and AI policy, and a dismissal with a
/// reason lands in the audit log and takes the candidate out of "To review". Each test signs in as a new person with
/// a new organization.
/// </summary>
public sealed class VerificationPanelTests(E2EFixture fixture) : BrowserTest(fixture)
{
    // The worker assesses asynchronously after each submission; the first run after the stack starts is the slowest.
    private static readonly LocatorAssertionsToContainTextOptions Assessed = new() { Timeout = 60_000 };

    private static readonly LocatorAssertionsToBeVisibleOptions Visible = new() { Timeout = 60_000 };

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
        foreach (var applicant in new[] { genuine, bot })
        {
            var id = await Fixture.ApplyAsync(scene.Slug, scene.JobId, applicant, Ct);
            if (applicant == bot)
            {
                botId = id;
            }
        }

        // The later member carries the duplicate signal from its own first check, and the earlier one gets it from a
        // re-check (RecheckApplicationDuplicates). A re-check that reaches the earlier application before its own first
        // check has nothing to add, so the farm's second member applies only once every check has run on the first.
        await SettledAsync(scene.Slug, await Fixture.ApplyAsync(scene.Slug, scene.JobId, farmFirst, Ct));
        await Fixture.ApplyAsync(scene.Slug, scene.JobId, farmSecond, Ct);

        // The chips arrive with VerificationAssessed, in the page that was already open. Levels as in the golden files
        // (tests/ScreenHire.UnitTests/Verification/Golden); the farm's reason counts differ from them because these
        // submissions answer only the resume question, which makes 20 seconds a plausible time to complete the form.
        await Expect(Chip(genuine.FullName)).ToContainTextAsync("Verification: No concerns", Assessed);
        await Expect(Chip(bot.FullName)).ToContainTextAsync("Verification: Verification suggested, 6 reasons", Assessed);
        await Expect(Chip(farmFirst.FullName)).ToContainTextAsync("Verification: Verification suggested", Assessed);
        await Expect(Chip(farmSecond.FullName)).ToContainTextAsync("Verification: Verification suggested", Assessed);
        // No signs of AI help draws no mark: the AI mark shows only for Some and Strong signs.
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = bot.FullName, Exact = true }) }).Locator("[data-ai-mark]")).ToHaveCountAsync(0);
        await Expect(Chip(bot.FullName)).ToContainTextAsync("Verify");
        (await Page.EvaluateAsync<bool>("() => window.__screenhireNoReload === true")).Should().BeTrue("the chips updated in place");

        var legend = Page.GetByRole(AriaRole.Group, new() { Name = "Verification" });
        await Expect(legend.GetByRole(AriaRole.Button, new() { Name = "Clear 1" })).ToBeVisibleAsync();
        await Expect(legend.GetByRole(AriaRole.Button, new() { Name = "Verify 3" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-verification-job-light");

        // The legend filters the table to one level, and a second press shows everyone again.
        var table = Page.GetByRole(AriaRole.Table, new() { Name = $"Candidates for {scene.Title}" });
        await legend.GetByRole(AriaRole.Button, new() { Name = "Verify 3" }).ClickAsync();
        await Expect(CandidateLinks(table)).ToHaveCountAsync(3);
        await Expect(table.GetByRole(AriaRole.Link, new() { Name = genuine.FullName })).ToHaveCountAsync(0);
        await legend.GetByRole(AriaRole.Button, new() { Name = "Verify 3" }).ClickAsync();
        await Expect(CandidateLinks(table)).ToHaveCountAsync(4);

        // The bot's Verification tab: reasons, evidence, the next step and the AI policy, and nothing that decides.
        await table.GetByRole(AriaRole.Link, new() { Name = bot.FullName }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(bot.FullName);
        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Verification") }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?tab=verification$"));
        var tab = Page.GetByRole(AriaRole.Tabpanel);
        var summary = Region("Verification");
        // The tab is organised by the six check groups: a panel for each that found something, named for the group.
        await Expect(Region("Network")).ToBeVisibleAsync(Visible);
        foreach (var code in new[] { "AUTO_HONEYPOT", "AUTO_SUBMIT_VELOCITY", "AUTO_WEBDRIVER", "ID_EMAIL_DISPOSABLE", "ID_PHONE_INVALID", "NET_DATACENTER_IP" })
        {
            await Expect(tab.Locator($"[data-signal='{code}']")).ToBeVisibleAsync();
        }

        await Expect(Region("Network").Locator("[data-signal='NET_DATACENTER_IP']")).ToBeVisibleAsync();
        await Expect(Region("Contact details").Locator("[data-signal='ID_PHONE_INVALID']")).ToBeVisibleAsync();
        await Expect(Region("Form use").Locator("[data-signal='AUTO_WEBDRIVER']")).ToBeVisibleAsync();
        await Expect(Region("Authenticity")).ToHaveCountAsync(0);
        await Expect(Region("Review log")).ToHaveCountAsync(0);
        var map = summary.GetByRole(AriaRole.List, new() { Name = "Check groups" });
        await Expect(map.Locator("[data-group='Network']")).ToContainTextAsync("1 found");
        await Expect(map.Locator("[data-group='Documents']")).ToContainTextAsync("information only");
        await Expect(map.Locator("[data-group='Duplicates']")).ToContainTextAsync("Nothing found");
        await Expect(summary.Locator("[data-next-step]")).ToContainTextAsync("Confirm the candidate is who they say they are");
        // A finding expands to its evidence.
        await OpenAllFindingsAsync();
        await Expect(tab.Locator("[data-signal='NET_DATACENTER_IP']")).ToContainTextAsync("Example Cloud Hosting (AS64497)");
        await Expect(tab.Locator("[data-signal='AUTO_WEBDRIVER'] [data-evidence-kind='List']")).ToContainTextAsync("webdriver");
        await Expect(tab.Locator("[data-signal='AUTO_SUBMIT_VELOCITY'] [data-evidence-kind='Metric']")).ToContainTextAsync("Plausible minimum for this form");
        // A calm AI row is its badge and the job's policy, and nothing marks a finding that counts as information only.
        var ai = summary.Locator("[data-ai-row]");
        await Expect(ai).ToContainTextAsync("No signs of AI help");
        await Expect(ai).ToContainTextAsync("AI allowed with disclosure");
        // AI help is marked as information only on its own findings (the resume's) and nowhere else.
        await Expect(Region("Documents").Locator("[data-information-only]")).Not.ToHaveCountAsync(0);
        await Expect(Region("Network").Locator("[data-information-only]")).ToHaveCountAsync(0);
        await Expect(Region("Form use").Locator("[data-information-only]")).ToHaveCountAsync(0);
        (await tab.TextContentAsync()).Should().NotContain("Declared AI use", "the declaration is on the Application tab, not repeated here");
        // A feature that does not exist yet is hidden, not shown disabled (design system A3).
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Request verification", RegexOptions.IgnoreCase) })).ToHaveCountAsync(0);
        await Expect(tab).Not.ToContainTextAsync("Phase 2");
        await Expect(tab.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("reject", RegexOptions.IgnoreCase) })).ToHaveCountAsync(0);
        await Expect(tab).ToContainTextAsync("No automated decision has been made. Every action is logged.");
        (await tab.TextContentAsync()).Should().NotContain("%", "verification output has no percentage (ADR-0004)");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-verification-panel-light");

        // Each finding is marked explained in turn: it closes with its badge, and the level stays as assessed.
        string[] counted = ["AUTO_HONEYPOT", "AUTO_SUBMIT_VELOCITY", "AUTO_WEBDRIVER", "ID_EMAIL_DISPOSABLE", "ID_PHONE_INVALID", "NET_DATACENTER_IP"];
        foreach (var code in counted)
        {
            await Expect(summary.GetByRole(AriaRole.Button, new() { Name = "Dismiss", Exact = true })).ToBeVisibleAsync();
            var reason = tab.Locator($"[data-signal='{code}']");
            await reason.GetByRole(AriaRole.Button, new() { Name = "Mark explained" }).ClickAsync();
            var explain = Page.GetByRole(AriaRole.Dialog, new() { NameRegex = new Regex("^Mark \".+\" explained\\?$") });
            if (code == "NET_DATACENTER_IP")
            {
                await explain.GetByRole(AriaRole.Radio, new() { Name = "Expected for this role" }).CheckAsync();
                await explain.GetByLabel("Note (optional)").FillAsync("Remote role; works through a hosted VPN.");
                await ExpectNoAccessibilityViolationsAsync();
            }
            else
            {
                await explain.GetByRole(AriaRole.Radio, new() { Name = "Confirmed with the candidate" }).CheckAsync();
            }

            await explain.GetByRole(AriaRole.Button, new() { Name = "Mark explained" }).ClickAsync();
            await Expect(explain).ToBeHiddenAsync();
            await Expect(reason).ToHaveAttributeAsync("data-explained", "");
            await Expect(reason.GetByRole(AriaRole.Button, new() { Name = "Undo" })).ToBeVisibleAsync();
        }

        await ExpectNoAccessibilityViolationsAsync();

        // Every reason explained, the one button reads Dismiss all; dismissing still takes a reason and is one action.
        await summary.GetByRole(AriaRole.Button, new() { Name = "Dismiss all", Exact = true }).ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Dismiss this verification result?" });
        var listed = dialog.GetByRole(AriaRole.List, new() { Name = "Explained findings" });
        await Expect(listed.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(counted.Length);
        await Expect(listed).ToContainTextAsync("Expected for this role, by you. \"Remote role; works through a hosted VPN.\"");
        await dialog.GetByRole(AriaRole.Radio, new() { Name = "Verified outside ScreenHire" }).CheckAsync();
        await dialog.GetByLabel("Note (optional)").FillAsync("Video call on Monday; ID checked.");
        await ExpectNoAccessibilityViolationsAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Dismiss with this reason" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();

        await Expect(summary).ToContainTextAsync("Dismissed by you");
        await Expect(summary).ToContainTextAsync("Verified outside ScreenHire");
        await Expect(tab.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Dismiss|Undo|Mark explained)") })).ToHaveCountAsync(0);

        // There is no Review log panel: every explanation, and the dismissal with its note, is a line in Activity, under the
        // Verification filter.
        await Tab("Activity").ClickAsync();
        var activity = Region("Activity");
        await activity.GetByRole(AriaRole.Group, new() { Name = "Show" }).GetByRole(AriaRole.Button, new() { NameRegex = new("^Verification") }).ClickAsync();
        await Expect(activity).ToContainTextAsync("Explained \"Sent from a hosting provider's network\": Expected for this role");
        await Expect(activity).ToContainTextAsync("Dismissed the verification result: Verified outside ScreenHire");
        await Expect(activity).ToContainTextAsync("\"Video call on Monday; ID checked.\"");
        await Tab("Verification").ClickAsync();

        // The audit entries are in the API's record too, with the reviewer: one per explained reason, then one dismissal.
        var record = await Page.APIRequest.GetAsync(DashboardUrl($"/v1/applications/{botId}/verification"));
        record.Ok.Should().BeTrue();
        using (var json = JsonDocument.Parse(await record.TextAsync()))
        {
            var entries = json.RootElement.GetProperty("reviewLog").EnumerateArray().ToList();
            entries.Select(e => e.GetProperty("type").GetString()).Should().Equal([.. counted.Select(_ => "ReasonExplained"), "Dismissed"]);
            var entry = entries[^1];
            entry.GetProperty("type").GetString().Should().Be("Dismissed");
            entry.GetProperty("dismissReason").GetString().Should().Be("VerifiedOutsideScreenHire");
            entry.GetProperty("note").GetString().Should().Be("Video call on Monday; ID checked.");
            json.RootElement.GetProperty("authenticity").GetProperty("level").GetString().Should().Be("VerificationRecommended");
        }

        await Page.GetByRole(AriaRole.Button, new() { Name = "Switch to dark theme" }).ClickAsync();
        await Expect(Page.Locator("html")).ToHaveClassAsync(new Regex("\\bdark\\b"));
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-verification-panel-dark");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" }).ClickAsync();

        // "To review" now holds both farm members, which carry the duplicate; the dismissed bot and the genuine
        // applicant are not in it.
        await Page.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^To review") }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("To review");
        var queue = Page.GetByRole(AriaRole.Table, new() { Name = "Candidates" });
        await Expect(CandidateLinks(queue)).ToHaveCountAsync(2);
        await Expect(queue.GetByRole(AriaRole.Link, new() { Name = farmFirst.FullName })).ToBeVisibleAsync();
        await Expect(queue.GetByRole(AriaRole.Link, new() { Name = farmSecond.FullName })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // A farm member's Verification tab shows what it shares with the others; the genuine applicant's has nothing to dismiss.
        await queue.GetByRole(AriaRole.Link, new() { Name = farmSecond.FullName }).ClickAsync();
        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Verification") }).ClickAsync();
        await Expect(Region("Duplicates")).ToBeVisibleAsync(Visible);
        var shared = Page.Locator("[data-signal='ID_DUPLICATE_IN_ORG']");
        await OpenAllFindingsAsync();
        await Expect(shared).ToContainTextAsync("Same device");
        await Expect(shared).ToContainTextAsync("Same resume file");
        await Expect(Page.Locator("[data-signal='NET_DATACENTER_IP']")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        await Page.GotoAsync(DashboardUrl($"/jobs/{scene.JobId}"));
        await Page.GetByRole(AriaRole.Table, new() { Name = $"Candidates for {scene.Title}" }).GetByRole(AriaRole.Link, new() { Name = genuine.FullName }).ClickAsync();
        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Verification") }).ClickAsync();
        var genuineTab = Page.GetByRole(AriaRole.Tabpanel);
        await Expect(Region("Verification").Locator("[data-next-step]")).ToHaveTextAsync("No action needed.", new() { Timeout = 60_000 });
        // A calm result is one line and a collapsed list of what was checked, with no map and no panel for any group.
        await Expect(genuineTab).ToContainTextAsync("Nothing unusual in the checks that ran.");
        await Expect(genuineTab.Locator("[data-checked]")).ToContainTextAsync("What was checked");
        await Expect(genuineTab.GetByRole(AriaRole.List, new() { Name = "Check groups" })).ToHaveCountAsync(0);
        await Expect(genuineTab.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Dismiss") })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
    }

    private sealed record Scene(string Email, string Slug, Guid JobId, string Title);

    // A new person and organization with one published job in Great Britain, as the verification integration tests use,
    // created through the API as that person.
    private async Task<Scene> SceneAsync()
    {
        var email = ApiSession.NewEmail("e2e-verification");
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

    // The row's one Verification state, in one word; its long name is the chip's screen-reader text ("Verification: Verification suggested").
    private ILocator Chip(string candidate) =>
        Page.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = candidate, Exact = true }) })
            .Locator("[data-verification-chip]");

    // Waits until every analyzer has run on the application, content forensics included, as the integration tests'
    // SettledAsync does: eight analyzers (VerificationRegistration).
    private async Task SettledAsync(string slug, Guid applicationId)
    {
        var reader = new TenantContext();
        reader.SetOrganization(Guid.NewGuid()); // organizations are public rows, readable as any tenant
        Guid organizationId;
        await using (var db = await Fixture.CreateDbContextAsync(reader, Ct))
        {
            organizationId = await db.Organizations.Where(o => o.Slug == slug).Select(o => o.Id).SingleAsync(Ct);
        }

        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        for (var attempt = 0; attempt < 600; attempt++)
        {
            await using var db = await Fixture.CreateDbContextAsync(tenant, Ct);
            if (await db.VerificationAnalyzerRuns.Where(r => r.ApplicationId == applicationId).Select(r => r.Analyzer).Distinct().CountAsync(Ct) >= 8)
            {
                return;
            }

            await Task.Delay(100, Ct);
        }

        throw new TimeoutException("Expected every verification analyzer to run on the farm's first application.");
    }

    private ILocator Region(string name) => Page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true });

    // Opens every finding on the tab. A group with one finding has no Expand all, so each closed finding is opened by its row.
    private async Task OpenAllFindingsAsync()
    {
        await Page.EvalOnSelectorAllAsync("[data-signal] details:not([open]) > summary", "rows => rows.forEach(row => row.click())");
        await Expect(Page.Locator("[data-signal] details:not([open])")).ToHaveCountAsync(0);
    }

    private ILocator Tab(string name) => Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^" + name) });

    private static ILocator CandidateLinks(ILocator table) => table.Locator("[data-row-link]");

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
