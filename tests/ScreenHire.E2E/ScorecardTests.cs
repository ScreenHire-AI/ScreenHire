using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.E2E;

/// <summary>
/// A reviewer fills a stage's scorecard on the candidate page, and an automation acts on the decision: the job's
/// workflow moves a candidate the interviewer recommends to Offer. Signs in as a new person with a new organization
/// whose job has the default workflow (an interview scorecard) and that one ReviewerDecision automation.
/// </summary>
public sealed class ScorecardTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Candidate = "Morgan Hale";
    private static readonly string[] Gb = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_scorecard_with_a_yes_moves_the_candidate_on_through_the_reviewer_decision()
    {
        var (email, applicationId) = await ArrangeAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(email);

        await Page.GotoAsync(DashboardUrl($"/applications/{applicationId}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(Candidate);
        var scorecards = Region("Scorecards");
        await Expect(scorecards).ToContainTextAsync("No scorecards yet.");
        await scorecards.GetByRole(AriaRole.Button, new() { Name = "Fill scorecard" }).ClickAsync();

        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Fill scorecard" });
        await Expect(sheet).ToContainTextAsync("Stage: Interview");
        await sheet.GetByRole(AriaRole.Group, new() { Name = "Skills the role needs" })
            .GetByRole(AriaRole.Radio, new() { Name = "Strongly demonstrated" }).CheckAsync();
        await sheet.GetByRole(AriaRole.Group, new() { Name = "Problem solving" })
            .GetByRole(AriaRole.Radio, new() { Name = "Demonstrated", Exact = true }).CheckAsync();
        await sheet.GetByRole(AriaRole.Group, new() { Name = "Recommendation" })
            .GetByRole(AriaRole.Radio, new() { Name = "Strong yes" }).CheckAsync();
        await sheet.GetByLabel("Note").FillAsync("Clear walk-through of the cache design.");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-scorecard-sheet");
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Submit scorecard" }).ClickAsync();
        await Expect(sheet).ToBeHiddenAsync();

        // The scorecard shows in words with its reviewer; the automation's move reaches the page live.
        await Expect(scorecards).ToContainTextAsync("Strong yes by you");
        await Expect(scorecards).ToContainTextAsync("Not rated");
        await Expect(scorecards.GetByRole(AriaRole.Button, new() { Name = "Fill scorecard" })).ToHaveCountAsync(0);
        var activity = Region("Activity");
        await Expect(activity).ToContainTextAsync("Scorecard for Interview by you");
        await Expect(activity).ToContainTextAsync("Moved from Interview to Offer by an automation", new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-slot=fact]", new() { Has = Page.GetByText("Stage", new() { Exact = true }) })).ToContainTextAsync("Offer");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-scorecards");

        await Page.SetViewportSizeAsync(390, 844);
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the page never scrolls sideways (F1)");
        await SaveScreenshotAsync(Page, "dashboard-scorecards-phone");
    }

    private ILocator Region(string name) => Page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true });

    // A new person and organization, a published job whose default workflow gains "a yes from the interview moves the
    // candidate to Offer", and an application a recruiter added and moved into Interview.
    private async Task<(string Email, Guid ApplicationId)> ArrangeAsync()
    {
        var email = ApiSession.NewEmail("e2e-scorecard");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Northgate Works", slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);
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

        var workflow = JsonNode.Parse(job.GetProperty("workflow").GetRawText())!.AsObject();
        workflow["automations"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "advance-on-yes",
            ["name"] = "Move a yes from the interview to Offer",
            ["trigger"] = new JsonObject { ["type"] = "ReviewerDecision", ["stageId"] = "interview" },
            ["conditions"] = new JsonObject
            {
                ["match"] = "All",
                ["rules"] = new JsonArray(new JsonObject
                {
                    ["fact"] = "reviewer.recommendation", ["operator"] = "In", ["values"] = new JsonArray("Yes", "StrongYes"),
                }),
            },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "MoveToStage", ["stageId"] = "offer" }),
        });
        await ReadAsync(await session.PutAsync($"/v1/jobs/{jobId}/workflow", new JsonObject { ["workflow"] = workflow, ["expectedWorkflowVersion"] = 1 }, Ct), HttpStatusCode.OK);
        await ReadAsync(await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct), HttpStatusCode.OK);

        var application = await ReadAsync(await session.PostAsync(
            $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = Candidate, email = ApiSession.NewEmail("morgan") } }, Ct), HttpStatusCode.Created);
        var applicationId = application.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/applications/{applicationId}/move", new { toStageId = "interview" }, Ct), HttpStatusCode.OK);
        return (email, applicationId);
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
