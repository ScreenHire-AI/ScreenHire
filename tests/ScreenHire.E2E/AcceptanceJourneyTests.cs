using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.E2E;

/// <summary>
/// The MVP acceptance journey: the whole product on one machine, in the order a small hiring team meets it. A recruiter
/// signs in with Dev SSO, sets up an organization, creates a job from a template and publishes it to the careers page and
/// the sandbox board. A genuine candidate and a bot apply on the careers site while the recruiter watches the job: both
/// arrive live with their integrity levels and evidence. The recruiter moves the genuine candidate on, the stage-change
/// email lands in Mailpit, and closing the job takes it down everywhere. The other end-to-end classes go deep on each
/// step; this one proves the steps connect.
/// </summary>
public sealed class AcceptanceJourneyTests(E2EFixture fixture) : BrowserTest(fixture)
{
    // The worker assesses each application asynchronously; the first run after the stack starts is the slowest.
    private static readonly LocatorAssertionsToContainTextOptions Assessed = new() { Timeout = 60_000 };

    // The sandbox board accepts and removes after AppHostFixture.SandboxDelay (4 s), once the worker has sent the request.
    private static readonly LocatorAssertionsToContainTextOptions WaitForBoard = new() { Timeout = 45_000 };

    private const string JobTitle = "Backend Engineer (.NET)";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_recruiter_publishes_a_job_screens_a_genuine_candidate_and_a_bot_and_tears_the_job_down()
    {
        var recruiter = ApiSession.NewEmail("e2e-acceptance");
        var genuine = SyntheticApplicants.GenuineHuman;
        var bot = SyntheticApplicants.Bot;
        // Own addresses per run, so Mailpit's search by recipient sees only this journey's email.
        var genuineEmail = ApiSession.NewEmail("robin.ashby");
        var botEmail = $"jv7x92-{Guid.NewGuid().ToString("N")[..8]}@tempinbox.example.net";
        await Page.SetViewportSizeAsync(1440, 1040);

        // 1. Sign in with Dev SSO and set up the organization.
        await GoToAsync("dashboard", "/sign-in");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(recruiter);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/onboarding"));
        await Page.GetByLabel("Organization name").FillAsync($"Harbour Lane {Guid.NewGuid().ToString("N")[..8]}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create organization" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
        var organizationSlug = (await DashboardJsonAsync("/v1/auth/me")).GetProperty("organization").GetProperty("slug").GetString()!;

        // The team's own template: the "Software engineer" system template plus an email to the candidate when they reach
        // Screening. Arranged through the API as the same person; TemplateBuilderTests covers building one in the UI.
        var templateName = await ArrangeTemplateWithStageEmailAsync(recruiter);

        // 2. A job from the template, published to every board.
        await Page.GetByRole(AriaRole.Button, new() { Name = "New job" }).ClickAsync();
        var newJob = Page.GetByRole(AriaRole.Dialog, new() { Name = "New job" });
        await newJob.GetByLabel("Title").FillAsync(JobTitle);
        await newJob.GetByLabel("Hiring template").SelectOptionAsync(new SelectOptionValue { Label = $"{templateName} (v1)" });
        await newJob.GetByLabel("Workplace").SelectOptionAsync(new SelectOptionValue { Label = "Remote" });
        await newJob.GetByLabel("Countries candidates can work from").FillAsync("GB, IE");
        await ExpectNoAccessibilityViolationsAsync();
        await newJob.GetByRole(AriaRole.Button, new() { Name = "Create job" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(JobTitle);
        var jobId = Guid.Parse(new Uri(Page.Url).Segments[^1]);
        var jobSlug = (await DashboardJsonAsync($"/v1/jobs/{jobId}")).GetProperty("slug").GetString()!;

        // The details the template does not set: a salary range, before anyone sees the job.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Edit details" }).ClickAsync();
        var details = Page.GetByRole(AriaRole.Dialog, new() { Name = "Edit job details" });
        await details.GetByLabel("From", new() { Exact = true }).FillAsync("70000");
        await details.GetByLabel("To", new() { Exact = true }).FillAsync("90000");
        await ExpectNoAccessibilityViolationsAsync();
        await details.GetByRole(AriaRole.Button, new() { Name = "Save details" }).ClickAsync();
        await Expect(details).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Job details" })).ToContainTextAsync(new Regex("£70[Kk]-£90[Kk]")); // "K" or "k", by the browser's locale

        // Each board's answer reaches this page as a live event, so check the connection first: a lost one fails here
        // with its own name, not as a board that seems to stay Queued.
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Live updates" })).ToContainTextAsync("Live updates: Live");
        var distribution = Page.GetByRole(AriaRole.Region, new() { Name = "Distribution" });
        await distribution.GetByRole(AriaRole.Button, new() { NameRegex = new("^Publish to \\d+ boards?$") }).ClickAsync();
        await Expect(Board("careers")).ToContainTextAsync("Live: the board confirmed the job shows.");
        await Expect(Board("sandbox")).ToContainTextAsync("Live: the board confirmed the job shows.", WaitForBoard);
        (await SandboxListingsAsync(organizationSlug)).Should().Contain(jobId.ToString(), "the sandbox board lists the job");
        await ExpectNoAccessibilityViolationsAsync();

        // The recruiter keeps the job open while candidates apply; nothing below reloads it.
        var table = Page.GetByRole(AriaRole.Table, new() { Name = $"Candidates for {JobTitle}" });
        await Page.EvaluateAsync("() => { window.__screenhireNoReload = true; }");

        // 3. The candidates apply on the careers site, each in a browser of their own. The genuine candidate's browser runs
        // no JavaScript, so it reports no automation markers and no device check, as a person's plain browser would not
        // look like Playwright's. The bot's browser is Playwright's as it is (webdriver, headless), and it fills the
        // hidden honeypot field, gives a throwaway address and a number that is not a phone number, and sends at once.
        await ApplyAsGenuineCandidateAsync(organizationSlug, jobSlug, genuine, genuineEmail);
        await ApplyAsBotAsync(organizationSlug, jobSlug, bot, botEmail);

        // Both arrive in the open table, then their levels when the worker has assessed them.
        await Expect(table.GetByRole(AriaRole.Link, new() { Name = genuine.FullName, Exact = true })).ToBeVisibleAsync();
        await Expect(table.GetByRole(AriaRole.Link, new() { Name = bot.FullName, Exact = true })).ToBeVisibleAsync();
        await Expect(Chip(genuine.FullName, "authenticity")).ToContainTextAsync("Authenticity: No concerns", Assessed);
        await Expect(Chip(bot.FullName, "authenticity")).ToContainTextAsync("Authenticity: Verification recommended", Assessed);
        (await Page.EvaluateAsync<bool>("() => window.__screenhireNoReload === true")).Should().BeTrue("the table updated in place");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "acceptance-job-with-candidates-light");

        // The bot's evidence: what the form and the identity checks saw, the next step, and nothing that decides.
        await OpenApplicationAsync(table, bot.FullName);
        var panel = Page.GetByRole(AriaRole.Region, new() { Name = "Integrity" });
        var authenticity = panel.GetByRole(AriaRole.Region, new() { Name = "Authenticity" });
        await Expect(authenticity).ToContainTextAsync("Authenticity: Verification recommended", Assessed);
        foreach (var code in new[] { "AUTO_HONEYPOT", "AUTO_WEBDRIVER", "AUTO_SUBMIT_VELOCITY", "ID_EMAIL_DISPOSABLE", "ID_PHONE_INVALID" })
        {
            await Expect(authenticity.Locator($"[data-signal='{code}']")).ToBeVisibleAsync();
        }

        await Expect(authenticity).ToContainTextAsync("Suggested next step: Confirm the candidate is who they say they are");
        await Expect(panel.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("reject", RegexOptions.IgnoreCase) })).ToHaveCountAsync(0);
        (await panel.TextContentAsync()).Should().NotContain("%", "integrity output has no percentage (ADR-0004)");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "acceptance-bot-evidence-light");

        // 4. The genuine candidate: nothing to act on, so the recruiter moves them to Screening.
        await Page.GoBackAsync();
        await OpenApplicationAsync(table, genuine.FullName);
        var genuinePanel = Page.GetByRole(AriaRole.Region, new() { Name = "Integrity" });
        await Expect(genuinePanel.GetByRole(AriaRole.Region, new() { Name = "Authenticity" })).ToContainTextAsync("Authenticity: No concerns", Assessed);
        await Expect(genuinePanel).ToContainTextAsync("Suggested next step: No action needed.");
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Move to stage, {genuine.FullName}", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Screening" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Activity" })).ToContainTextAsync("Moved from Applied to Screening by you");

        // The template's automation emails the candidate, through the outbox, the worker and Mailpit.
        var sent = await Fixture.Mailpit.WaitForMessagesAsync(genuineEmail, 1, Ct);
        sent.Should().ContainSingle();
        sent[0].Subject.Should().Be($"An update on your application for {JobTitle}");
        sent[0].Text.Should().Contain($"Hi {genuine.FullName}").And.Contain("has moved to the next step: Screening");
        (await Fixture.Mailpit.MessagesAsync(botEmail, Ct)).Should().BeEmpty("nobody moved the bot, and no integrity result sends anything");

        // 5. Close the job: it comes down from every board, and the careers page answers 410 Gone.
        await Page.GotoAsync(DashboardUrl($"/jobs/{jobId}"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Close & remove everywhere" }).ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Close this job and remove it everywhere?" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Close & remove everywhere" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(Board("careers")).ToContainTextAsync("Removed:");
        await Expect(Board("sandbox")).ToContainTextAsync("Removed:", WaitForBoard);
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Job details" })).ToContainTextAsync("Closed");
        (await SandboxListingsAsync(organizationSlug)).Should().NotContain(jobId.ToString(), "the sandbox board dropped the job");

        await using var visitor = await Fixture.Browser.NewContextAsync();
        var careers = await visitor.NewPageAsync();
        var gone = await careers.GotoAsync(CareersUrl($"/{organizationSlug}/jobs/{jobSlug}"));
        gone!.Status.Should().Be(410);
        await Expect(careers.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("This job has closed");
    }

    // Copies the "Software engineer" system template's newest version and adds an automation that sends the stage-change
    // email when a candidate enters Screening, then publishes version 1.
    private async Task<string> ArrangeTemplateWithStageEmailAsync(string email)
    {
        var name = $"Engineering hire {Guid.NewGuid().ToString("N")[..4]}";
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        var template = await ReadAsync(
            await session.PostAsync("/v1/templates", new { name, copyFromVersionId = SystemTemplates.SoftwareEngineerVersion2 }, Ct), HttpStatusCode.Created);
        var templateId = template.GetProperty("id").GetGuid();
        var draft = await ReadAsync(await session.Client.GetAsync($"/v1/templates/{templateId}/versions/1", Ct), HttpStatusCode.OK);

        var definition = JsonNode.Parse(draft.GetProperty("definition").GetRawText())!.AsObject();
        definition["workflow"]!["automations"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "screening_email",
            ["name"] = "Tell the candidate they reached Screening",
            ["trigger"] = new JsonObject { ["type"] = "StageEntered", ["stageId"] = "screening" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "SendEmail", ["emailTemplate"] = EmailTemplateIds.StageChange }),
            ["enabled"] = true,
        });
        await ReadAsync(await session.PutAsync($"/v1/templates/{templateId}/versions/1", new { definition }, Ct), HttpStatusCode.OK);
        await ReadAsync(await session.PostAsync($"/v1/templates/{templateId}/versions/1/publish", new { }, Ct), HttpStatusCode.OK);
        return name;
    }

    private async Task ApplyAsGenuineCandidateAsync(string organization, string job, SyntheticApplicant applicant, string email)
    {
        await using var context = await Fixture.Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        await page.GotoAsync(CareersUrl($"/{organization}/jobs/{job}"));
        await Expect(page.GetByRole(AriaRole.Main)).ToContainTextAsync("£70k–£90k"); // the salary set in the dashboard
        await page.GetByRole(AriaRole.Link, new() { Name = "Apply for this role" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Region, new() { Name = "How we keep this fair" })).ToBeVisibleAsync();
        await page.GetByLabel("Full name").FillAsync(applicant.FullName);
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Phone").FillAsync(applicant.Phone!);
        await page.GetByLabel("Your CV or resume").SetInputFilesAsync(
            new FilePayload { Name = applicant.ResumeFileName, MimeType = "application/pdf", Buffer = applicant.ResumeBytes() });
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();

        await AnswerQuestionsAsync(page, notice: "One month");
        await page.GetByLabel("No, I did not use AI tools").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Send application" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Application sent");
    }

    private async Task ApplyAsBotAsync(string organization, string job, SyntheticApplicant applicant, string email)
    {
        await using var context = await Fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(CareersUrl($"/{organization}/jobs/{job}/apply"));
        await page.Locator("astro-island:not([ssr])").WaitForAsync(); // hydrated: the upload island and the telemetry module

        await page.GetByLabel("Full name").FillAsync(applicant.FullName);
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Phone").FillAsync(applicant.Phone!);
        await page.GetByLabel("Your CV or resume").SetInputFilesAsync(
            new FilePayload { Name = applicant.ResumeFileName, MimeType = "application/pdf", Buffer = applicant.ResumeBytes() });
        await Expect(page.GetByText($"Uploaded: {applicant.ResumeFileName}")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();

        await AnswerQuestionsAsync(page, notice: "Immediately");
        await page.GetByLabel("No, I did not use AI tools").CheckAsync();
        // A person never sees or reaches this field; a script filling every input does.
        await page.Locator("#leave-this-empty").EvaluateAsync("field => { field.value = 'https://example.com'; }");
        await page.GetByRole(AriaRole.Button, new() { Name = "Send application" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Application sent");
    }

    // The "Software engineer" questionnaire, which the job copied from the template.
    private static async Task AnswerQuestionsAsync(IPage page, string notice)
    {
        await Expect(page.Locator("[aria-current='step']")).ToContainTextAsync("QUESTIONS");
        await page.GetByRole(AriaRole.Group, new() { Name = "Do you have the right to work in the country this job is in?" })
            .GetByLabel("Yes").CheckAsync();
        await page.GetByRole(AriaRole.Group, new() { Name = "How many years have you worked as a software engineer?" })
            .GetByLabel("6 to 9").CheckAsync();
        await page.GetByLabel("What is your notice period?").FillAsync(notice);
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue to review" }).ClickAsync();
        await Expect(page.Locator("[aria-current='step']")).ToContainTextAsync("REVIEW");
    }

    // A person's click on the name, the first one on the page included: the side panel keeps its place, so the row
    // does not move under the pointer while the click is in progress.
    private async Task OpenApplicationAsync(ILocator table, string candidate)
    {
        await table.GetByRole(AriaRole.Link, new() { Name = candidate, Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(candidate);
    }

    private ILocator Chip(string candidate, string kind) =>
        Page.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = candidate, Exact = true }) })
            .Locator($"[data-integrity-chip='{kind}']");

    private ILocator Board(string boardId) =>
        Page.GetByRole(AriaRole.Region, new() { Name = "Distribution" }).Locator($"[data-board='{boardId}']");

    // The dashboard's own /v1 proxy, with the recruiter's cookies.
    private async Task<JsonElement> DashboardJsonAsync(string path)
    {
        var response = await Page.APIRequest.GetAsync(DashboardUrl(path));
        response.Ok.Should().BeTrue("GET {0} returned HTTP {1}", path, response.Status);
        return (await response.JsonAsync())!.Value;
    }

    private async Task<string> SandboxListingsAsync(string organizationSlug)
    {
        var response = await Page.APIRequest.GetAsync(DashboardUrl($"/v1/public/sandbox-board/{organizationSlug}/listings"));
        response.Ok.Should().BeTrue();
        return await response.TextAsync();
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();

    private string CareersUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("careers"), pathAndQuery).ToString();
}
