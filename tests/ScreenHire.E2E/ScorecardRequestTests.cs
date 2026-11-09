using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// Asking a teammate for a scorecard, and the task that is done where its work is done (ADR-0017 decision 11): the Owner
/// asks a Recruiter from the candidate's Scorecards tab, the Recruiter opens My tasks, follows the row to the candidate
/// with the scorecard open ("Asked by ... on ..."), fills it in, and the task is done without anyone ticking it. The sheet
/// then offers the next task, which opens on its own candidate's scorecard. Each person in a browser of their own, in one
/// new organization whose job uses the default workflow.
/// </summary>
public sealed class ScorecardRequestTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Candidate = "Jamie Rowe";
    private const string SecondCandidate = "Priya Reed";
    private static readonly string[] Gb = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_teammate_asked_for_a_scorecard_fills_it_in_from_my_tasks_and_the_task_is_done()
    {
        using var ownerSession = new ApiSession(Fixture);
        var ownerEmail = ApiSession.NewEmail("owner");
        await ownerSession.SignInWithDevSsoAsync(ownerEmail, Ct);
        await ReadAsync(await ownerSession.PostAsync("/v1/auth/onboarding", new { organizationName = "Fernhill Works", slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);
        var job = await ReadAsync(await ownerSession.PostAsync(
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
        await ReadAsync(await ownerSession.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct), HttpStatusCode.OK);
        var application = await ReadAsync(await ownerSession.PostAsync(
            $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = Candidate, email = ApiSession.NewEmail("jamie") } }, Ct), HttpStatusCode.Created);
        var applicationId = application.GetProperty("id").GetGuid();
        await ReadAsync(await ownerSession.PostAsync($"/v1/applications/{applicationId}/move", new { toStageId = "interview" }, Ct), HttpStatusCode.OK);
        var recruiterEmail = ApiSession.NewEmail("recruiter");
        using var recruiterSession = await InviteAsync(ownerSession, recruiterEmail, "Recruiter");
        var members = await ReadAsync(await ownerSession.Client.GetAsync("/v1/organization/members", Ct), HttpStatusCode.OK);
        var recruiter = members.EnumerateArray().Single(member => member.GetProperty("email").GetString() == recruiterEmail);
        var recruiterName = recruiter.GetProperty("displayName").GetString()!;
        var recruiterId = recruiter.GetProperty("userId").GetGuid();

        // The Owner has already given a scorecard on Jamie, so the Recruiter's tab has something to withhold.
        await ReadAsync(await ownerSession.PostAsync(
            $"/v1/applications/{applicationId}/scorecards",
            new
            {
                stageId = "interview",
                recommendation = "StrongYes",
                answers = new object[]
                {
                    new { questionId = "role_skills", rating = 4 },
                    new { questionId = "problem_solving", rating = 4 },
                    new { questionId = "work_knowledge", rating = 5 },
                },
            },
            Ct), HttpStatusCode.Created);

        // A second candidate the Owner will ask the Recruiter about too, so the first submit has a next task to offer.
        var second = await ReadAsync(await ownerSession.PostAsync(
            $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = SecondCandidate, email = ApiSession.NewEmail("priya") } }, Ct), HttpStatusCode.Created);
        var secondId = second.GetProperty("id").GetGuid();
        await ReadAsync(await ownerSession.PostAsync($"/v1/applications/{secondId}/move", new { toStageId = "interview" }, Ct), HttpStatusCode.OK);

        // The Owner asks the Recruiter for the Interview scorecard, the stage's own, which the sheet starts on.
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, ownerEmail);
        await Page.GotoAsync(DashboardUrl($"/applications/{applicationId}?tab=scorecards"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(Candidate);
        var scorecards = Page.GetByRole(AriaRole.Region, new() { Name = "Scorecards", Exact = true });
        await scorecards.GetByRole(AriaRole.Button, new() { Name = "Ask for one" }).ClickAsync();
        var ask = Page.GetByRole(AriaRole.Dialog, new() { Name = "Ask for a scorecard" });
        var teammate = ask.GetByRole(AriaRole.Combobox, new() { Name = "Teammate" });
        await Expect(teammate).ToHaveValueAsync(recruiter.GetProperty("userId").GetString()!);
        await Expect(ask.GetByRole(AriaRole.Combobox, new() { Name = "Scorecard" })).ToHaveValueAsync("interview");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-ask-for-a-scorecard");
        await ask.GetByRole(AriaRole.Button, new() { Name = "Ask", Exact = true }).ClickAsync();
        await Expect(ask).ToBeHiddenAsync();
        await Expect(scorecards).ToContainTextAsync($"Asked {recruiterName} for the Interview scorecard.");
        // The request is under "Asked for, not in yet" until they give it.
        await Expect(scorecards.GetByText("Asked for, not in yet")).ToBeVisibleAsync();
        await Expect(scorecards.GetByRole(AriaRole.Row).Filter(new() { HasText = "Waiting" })).ToContainTextAsync("by you");
        await ReadAsync(await ownerSession.PostAsync(
            $"/v1/applications/{secondId}/scorecard-requests", new { assigneeUserId = recruiterId, scorecardTemplateId = "interview" }, Ct), HttpStatusCode.Created);

        // The Recruiter's My tasks has it; the row has no checkbox and opens the candidate with the scorecard open.
        await using var recruiterContext = await Fixture.Browser.NewContextAsync(new() { Locale = "en-US" });
        var recruiterPage = await recruiterContext.NewPageAsync();
        await recruiterPage.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(recruiterPage, recruiterEmail);

        // Asked for a scorecard and not yet given it, the Recruiter sees who gave one and when, never what it said.
        await recruiterPage.GotoAsync(DashboardUrl($"/applications/{applicationId}?tab=scorecards"));
        var theirs = recruiterPage.GetByRole(AriaRole.Region, new() { Name = "Scorecards", Exact = true });
        await Expect(theirs).ToContainTextAsync("Submit yours to see the others.");
        var givenRow = theirs.GetByRole(AriaRole.Row).Filter(new() { HasNotText = "Waiting" }).Nth(1);
        await Expect(givenRow).Not.ToContainTextAsync("Strong yes");
        await Expect(givenRow.GetByRole(AriaRole.Button)).ToHaveCountAsync(0);
        await SaveScreenshotAsync(recruiterPage, "dashboard-scorecards-withheld");
        await recruiterPage.SetViewportSizeAsync(390, 844);
        await Expect(theirs).ToContainTextAsync("Submit yours to see the others.");
        (await recruiterPage.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the page never scrolls sideways (F1)");
        await SaveScreenshotAsync(recruiterPage, "dashboard-scorecards-withheld-phone");
        await recruiterPage.SetViewportSizeAsync(1440, 1000);

        await recruiterPage.GotoAsync(DashboardUrl("/tasks"));
        var tasks = recruiterPage.GetByRole(AriaRole.Region, new() { Name = "Tasks", Exact = true });
        var row = tasks.GetByRole(AriaRole.Row, new() { NameRegex = new(Candidate) }).GetByRole(AriaRole.Link, new() { Name = "Fill in the Interview scorecard." });
        await Expect(row).ToBeVisibleAsync();
        await Expect(tasks.GetByRole(AriaRole.Checkbox)).ToHaveCountAsync(0);
        await row.ClickAsync();

        var sheet = recruiterPage.GetByRole(AriaRole.Dialog, new() { Name = "Give your scorecard" });
        await Expect(sheet).ToContainTextAsync(new Regex(@"Asked by .+ on "));
        // The task asks for one scorecard, so the sheet is on it with no Scorecard select (redesign pick 6).
        await Expect(Question(sheet, "Skills the role needs")).ToBeVisibleAsync();
        await Expect(sheet.GetByRole(AriaRole.Combobox, new() { Name = "Scorecard" })).ToHaveCountAsync(0);
        await Question(sheet, "Skills the role needs").GetByRole(AriaRole.Radio, new() { Name = "4", Exact = true }).ClickAsync();
        await Question(sheet, "Problem solving").GetByRole(AriaRole.Radio, new() { Name = "3", Exact = true }).ClickAsync();
        await Question(sheet, "Overall recommendation").GetByRole(AriaRole.Radio, new() { Name = "Yes", Exact = true }).ClickAsync();
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Submit scorecard" }).ClickAsync();

        // The sheet says so and offers the next task; the one just done is not it.
        await Expect(sheet).ToContainTextAsync("Scorecard submitted.");
        await Expect(sheet.GetByRole(AriaRole.Button, new() { Name = "Submit scorecard" })).ToHaveCountAsync(0);
        await SaveScreenshotAsync(recruiterPage, "dashboard-scorecard-submitted");
        await sheet.GetByRole(AriaRole.Link, new() { Name = "Open next task" }).ClickAsync();

        // The next task opens on its own candidate's scorecard, ready to fill in.
        await Expect(recruiterPage.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(SecondCandidate);
        var next = recruiterPage.GetByRole(AriaRole.Dialog, new() { Name = "Give your scorecard" });
        await Expect(next).ToContainTextAsync(new Regex(@"Asked by .+ on "));
        await Question(next, "Skills the role needs").GetByRole(AriaRole.Radio, new() { Name = "4", Exact = true }).ClickAsync();
        await Question(next, "Problem solving").GetByRole(AriaRole.Radio, new() { Name = "4", Exact = true }).ClickAsync();
        await Question(next, "Overall recommendation").GetByRole(AriaRole.Radio, new() { Name = "Strong yes", Exact = true }).ClickAsync();
        await next.GetByRole(AriaRole.Button, new() { Name = "Submit scorecard" }).ClickAsync();
        await Expect(next).ToContainTextAsync("Scorecard submitted.");
        await Expect(next.GetByRole(AriaRole.Link, new() { Name = "Open next task" })).ToHaveCountAsync(0);
        await recruiterPage.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        await Expect(recruiterPage.GetByRole(AriaRole.Region, new() { Name = "Scorecards", Exact = true })).ToContainTextAsync("Strong yes");

        // Submitting was the work, so both tasks are done: gone from the open list, under Done with who finished them.
        await recruiterPage.GotoAsync(DashboardUrl("/tasks"));
        await Expect(recruiterPage.GetByText("Nothing to do")).ToBeVisibleAsync();
        await recruiterPage.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true }).ClickAsync();
        var done = recruiterPage.GetByRole(AriaRole.Table, new() { Name = "Done tasks" });
        await Expect(done).ToContainTextAsync(Candidate);
        await Expect(done).ToContainTextAsync(SecondCandidate);
        await Expect(done.GetByRole(AriaRole.Cell, new() { Name = "You", Exact = true })).ToHaveCountAsync(2);
    }

    private static ILocator Question(ILocator sheet, string label) =>
        sheet.GetByRole(AriaRole.Group, new() { NameRegex = new($"^{System.Text.RegularExpressions.Regex.Escape(label)}") });

    private async Task<ApiSession> InviteAsync(ApiSession owner, string email, string role)
    {
        using (var invited = await owner.PostAsync("/v1/organization/invitations", new { email, role }, Ct))
        {
            invited.StatusCode.Should().Be(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(Ct));
        }

        var token = await Fixture.Mailpit.WaitForInvitationTokenAsync(email, Ct);
        var session = new ApiSession(Fixture);
        try
        {
            using var accepted = await session.PostAsync("/v1/auth/invitations/accept", new { token }, Ct);
            accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync(Ct));
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    // Dev SSO finds the account by its address, so an invited person signs in to their organization.
    private async Task SignInAsync(IPage page, string email)
    {
        await page.GotoAsync(DashboardUrl("/sign-in"));
        await page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await page.GetByLabel("Any email address").FillAsync(email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
