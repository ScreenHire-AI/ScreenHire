using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// Asking a teammate for a scorecard, and the task that is done where its work is done (ADR-0017 decision 11): the Owner
/// asks a Recruiter from the candidate's Scorecards tab, the Recruiter opens My tasks, follows the row to the candidate
/// with the scorecard open, fills it in, and the task is done without anyone ticking it. Each person in a browser of
/// their own, in one new organization whose job uses the default workflow.
/// </summary>
public sealed class ScorecardRequestTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Candidate = "Jamie Rowe";
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
        await ask.GetByLabel("Note").FillAsync("Focus on the pairing exercise.");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-ask-for-a-scorecard");
        await ask.GetByRole(AriaRole.Button, new() { Name = "Ask", Exact = true }).ClickAsync();
        await Expect(ask).ToBeHiddenAsync();
        await Expect(scorecards).ToContainTextAsync($"Asked {recruiterName} for the Interview scorecard.");

        // The Recruiter's My tasks has it; the row has no checkbox and opens the candidate with the scorecard open.
        await using var recruiterContext = await Fixture.Browser.NewContextAsync(new() { Locale = "en-US" });
        var recruiterPage = await recruiterContext.NewPageAsync();
        await recruiterPage.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(recruiterPage, recruiterEmail);
        await recruiterPage.GotoAsync(DashboardUrl("/tasks"));
        var tasks = recruiterPage.GetByRole(AriaRole.Region, new() { Name = "Tasks", Exact = true });
        var row = tasks.GetByRole(AriaRole.Link, new() { Name = "Fill in the Interview scorecard. Focus on the pairing exercise." });
        await Expect(row).ToBeVisibleAsync();
        await Expect(tasks.GetByRole(AriaRole.Checkbox)).ToHaveCountAsync(0);
        await row.ClickAsync();

        var sheet = recruiterPage.GetByRole(AriaRole.Dialog, new() { Name = "Fill in a scorecard" });
        await Expect(sheet.GetByRole(AriaRole.Combobox, new() { Name = "Scorecard" })).ToHaveValueAsync("stage:interview");
        await Question(sheet, "Skills the role needs").GetByRole(AriaRole.Radio, new() { Name = "4", Exact = true }).ClickAsync();
        await Question(sheet, "Problem solving").GetByRole(AriaRole.Radio, new() { Name = "3", Exact = true }).ClickAsync();
        await Question(sheet, "Overall recommendation").GetByRole(AriaRole.Radio, new() { Name = "Yes", Exact = true }).ClickAsync();
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Submit scorecard" }).ClickAsync();
        await Expect(sheet).ToBeHiddenAsync();
        await Expect(recruiterPage.GetByRole(AriaRole.Region, new() { Name = "Scorecards", Exact = true })).ToContainTextAsync("Yes by you");

        // Submitting it was the work, so the task is done: gone from the open list, under Done with who finished it.
        await recruiterPage.GotoAsync(DashboardUrl("/tasks"));
        await Expect(recruiterPage.GetByText("Nothing to do")).ToBeVisibleAsync();
        await recruiterPage.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true }).ClickAsync();
        var done = recruiterPage.GetByRole(AriaRole.List, new() { Name = "Done tasks" });
        await Expect(done).ToContainTextAsync("Fill in the Interview scorecard.");
        await Expect(done).ToContainTextAsync("by you");
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
