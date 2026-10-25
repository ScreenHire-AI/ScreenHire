using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.E2E;

/// <summary>
/// A reviewer fills a stage's scorecard on the candidate page, answering every kind of question, sees the candidate's
/// rating as stars, and an automation acts on the decision: the job's workflow moves a candidate the interviewer
/// recommends to Offer. Signs in as a new person with a new organization whose job's Interview stage uses the team's own
/// library scorecard with every answer kind, and that one ReviewerDecision automation.
/// </summary>
public sealed class ScorecardTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Candidate = "Morgan Hale";
    private static readonly string[] Gb = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_scorecard_with_every_kind_makes_the_stars_and_its_yes_moves_the_candidate_on()
    {
        var (email, jobId, applicationId) = await ArrangeAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(email);

        await Page.GotoAsync(DashboardUrl($"/applications/{applicationId}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(Candidate);
        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Scorecards") }).ClickAsync();
        var scorecards = Region("Scorecards");
        await Expect(scorecards).ToContainTextAsync("No scorecards yet.");
        await scorecards.GetByRole(AriaRole.Button, new() { Name = "Fill scorecard" }).ClickAsync();

        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Fill scorecard" });
        await Expect(sheet).ToContainTextAsync("Stage: Interview");
        // Ratings and yes-or-no answer on a row of joined buttons, with Low and High under a number scale.
        await Question(sheet, "Problem solving").GetByRole(AriaRole.Radio, new() { Name = "4", Exact = true }).ClickAsync();
        await Question(sheet, ".NET and C#").GetByRole(AriaRole.Radio, new() { Name = "10, High" }).ClickAsync();
        await Question(sheet, "Is the code readable?").GetByRole(AriaRole.Radio, new() { Name = "Yes" }).ClickAsync();
        await Question(sheet, "Did they finish in the time?").GetByRole(AriaRole.Radio, new() { Name = "No" }).ClickAsync();
        await Question(sheet, "Level").GetByRole(AriaRole.Radio, new() { Name = "Senior" }).CheckAsync();
        await Question(sheet, "What should the next interview cover?").GetByRole(AriaRole.Checkbox, new() { Name = "Testing" }).CheckAsync();
        await sheet.GetByLabel("In a sentence").FillAsync("Strong on design, light on tests.");
        await sheet.GetByLabel("Notes on the exercise").FillAsync("Finished the cache, skipped the eviction tests.");
        await Question(sheet, "Overall recommendation").GetByRole(AriaRole.Radio, new() { Name = "Strong yes" }).ClickAsync();
        await sheet.GetByLabel("Note for the team").FillAsync("Clear walk-through of the cache design.");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-scorecard-sheet");
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Submit scorecard" }).ClickAsync();
        await Expect(sheet).ToBeHiddenAsync();

        // The scorecard shows each answer with its reviewer, and its rating questions make the stars: 4, 10 (5) and a
        // rating Yes (5) average 4.7. The automation's move reaches the page live.
        await Expect(scorecards).ToContainTextAsync("Strong yes by you");
        await Expect(scorecards).ToContainTextAsync("10 of 10");
        await Expect(scorecards).ToContainTextAsync("Testing");
        var rating = Page.GetByRole(AriaRole.Button, new() { Name = "Rated 4.7 out of 5 from 1 scorecard" });
        await Expect(rating).ToBeVisibleAsync();
        await Expect(rating.GetByRole(AriaRole.Img, new() { Name = "4.7 of 5 stars" })).ToBeVisibleAsync();
        await rating.ClickAsync();
        await Expect(Page.GetByText("From 1 scorecard")).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(scorecards.GetByRole(AriaRole.Button, new() { Name = "Fill scorecard" })).ToHaveCountAsync(0);
        // The automation's move shows in the rail's Stage and in Activity.
        await Expect(Page.GetByRole(AriaRole.Complementary, new() { Name = "Details" })).ToContainTextAsync("Offer", new() { Timeout = 30_000 });
        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Activity") }).ClickAsync();
        var activity = Region("Activity");
        await Expect(activity).ToContainTextAsync("Scorecard for Interview by you");
        await Expect(activity).ToContainTextAsync("Moved from Interview to Offer by an automation", new() { Timeout = 30_000 });
        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Scorecards") }).ClickAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-scorecards");

        // The job's table has a Rating column that sorts, highest first.
        await Page.GotoAsync(DashboardUrl($"/jobs/{jobId}"));
        var table = Page.GetByRole(AriaRole.Table);
        await Expect(table.GetByRole(AriaRole.Row, new() { NameRegex = new(Candidate) })).ToContainTextAsync("4.7");
        await table.GetByRole(AriaRole.Button, new() { NameRegex = new("^Rating") }).ClickAsync();
        await Expect(table.GetByRole(AriaRole.Columnheader, new() { NameRegex = new("^Rating") })).ToHaveAttributeAsync("aria-sort", "descending");
        await SaveScreenshotAsync(Page, "dashboard-job-ratings");

        await Page.GotoAsync(DashboardUrl($"/applications/{applicationId}?tab=scorecards"));
        await Page.SetViewportSizeAsync(390, 844);
        await Expect(Region("Scorecards")).ToContainTextAsync("Strong yes by you");
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the page never scrolls sideways (F1)");
        await SaveScreenshotAsync(Page, "dashboard-scorecards-phone");
    }

    // A question's group: a rating's row of buttons or a choice's options, named by the question.
    private static ILocator Question(ILocator sheet, string label) =>
        sheet.GetByRole(AriaRole.Group, new() { NameRegex = new($"^{System.Text.RegularExpressions.Regex.Escape(label)}") });

    private ILocator Region(string name) => Page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true });

    // A new person and organization, the team's own scorecard with every answer kind, a published job whose Interview
    // stage uses it and whose workflow gains "a yes from the interview moves the candidate to Offer", and an application
    // a recruiter added and moved into Interview.
    private async Task<(string Email, Guid JobId, Guid ApplicationId)> ArrangeAsync()
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
        var scorecard = await ReadAsync(await session.PostAsync(
            "/v1/scorecard-templates",
            new
            {
                name = "Technical interview",
                questions = new object[]
                {
                    new { id = "problem", type = "Rating5", label = "Problem solving", required = true },
                    new { id = "dotnet", type = "Rating10", label = ".NET and C#" },
                    new { id = "readable", type = "RatingYesNo", label = "Is the code readable?" },
                    new { id = "finished", type = "YesNo", label = "Did they finish in the time?" },
                    new { id = "level", type = "SingleChoice", label = "Level", options = new[] { new { id = "mid", label = "Mid" }, new { id = "senior", label = "Senior" } } },
                    new
                    {
                        id = "next", type = "MultiChoice", label = "What should the next interview cover?",
                        options = new[] { new { id = "design", label = "System design" }, new { id = "testing", label = "Testing" } },
                    },
                    new { id = "summary", type = "ShortText", label = "In a sentence" },
                    new { id = "notes", type = "LongText", label = "Notes on the exercise" },
                },
            },
            Ct), HttpStatusCode.Created);

        var workflow = JsonNode.Parse(job.GetProperty("workflow").GetRawText())!.AsObject();
        // The Interview stage names the library scorecard; saving gives the job its own copy of the questions.
        var interview = workflow["stages"]!.AsArray().Single(stage => (string?)stage!["id"] == "interview")!.AsObject();
        interview.Remove("scorecard");
        interview["scorecardTemplateId"] = scorecard.GetProperty("id").GetString();
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
        return (email, jobId, applicationId);
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
