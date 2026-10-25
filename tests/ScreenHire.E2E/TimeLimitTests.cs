using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.E2E;

/// <summary>
/// A stage's time limit (SLA) in the dashboard: an application past it shows Overdue in the candidates list, the job's
/// table and board and its own page, the Overdue view finds it, and an SlaBreached automation reminds the team with a
/// task. The job's Screening stage has the default three-day limit. The worker's timer is released early (its due time
/// moved to now), and the browser's clock runs four days ahead, so nothing waits.
/// </summary>
public sealed class TimeLimitTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Waiting = "Rowan Hale";
    private const string Fresh = "Kit Navarro";
    private const string Title = "Support Specialist";
    private const string Reminder = "Reply to this candidate about next steps.";
    private static readonly string[] Gb = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_application_past_its_time_limit_shows_overdue_and_its_automation_creates_a_task()
    {
        var (email, jobId, applicationId) = await ArrangeAsync();
        (await Fixture.ReleaseAsync(applicationId.ToString(), Ct)).Should().Be(1, "entering Screening scheduled one SlaBreached timer");

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(email);
        await Page.Clock.SetFixedTimeAsync(DateTime.UtcNow.AddDays(4));

        // The Overdue view lists the waiting candidate only, with the badge before the stage.
        await Page.GotoAsync(DashboardUrl("/candidates?view=overdue"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Overdue");
        var applications = Page.GetByRole(AriaRole.Region, new() { Name = "Applications", Exact = true });
        var row = applications.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = Waiting, Exact = true }) });
        await Expect(row).ToContainTextAsync(new System.Text.RegularExpressions.Regex("Overdue by 1 day\\s*Screening"));
        await Expect(applications.GetByRole(AriaRole.Link, new() { Name = Fresh, Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Overdue 1" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-overdue-view");

        // A phone lists the rows as items, the badge first among the facts, and never scrolls sideways (F1).
        await Page.SetViewportSizeAsync(390, 844);
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Candidates" })).ToContainTextAsync("Overdue by 1 day");
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the page never scrolls sideways (F1)");
        await SaveScreenshotAsync(Page, "dashboard-overdue-view-phone");
        await Page.SetViewportSizeAsync(1440, 1000);

        // The job's table and board show it too.
        await Page.GotoAsync(DashboardUrl($"/jobs/{jobId}"));
        var table = Page.GetByRole(AriaRole.Table, new() { Name = $"Candidates for {Title}" });
        await Expect(table.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = Waiting, Exact = true }) }))
            .ToContainTextAsync("Overdue by 1 day");
        await Expect(table.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = Fresh, Exact = true }) }))
            .Not.ToContainTextAsync("Overdue");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Board", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Listitem, new() { Name = $"{Waiting}, Screening" })).ToContainTextAsync("Overdue by 1 day");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-overdue-board");

        // The application's page has the badge beside the name, and the task the SlaBreached automation created.
        await Page.GotoAsync(DashboardUrl($"/applications/{applicationId}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(Waiting);
        await Expect(Page.Locator("[data-slot='header-band']")).ToContainTextAsync("Overdue by 1 day");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Next steps", Exact = true })).ToContainTextAsync(Reminder, new() { Timeout = 30_000 });
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-overdue-application");
    }

    // A new person and organization, and a published job whose workflow reminds the team when Screening's time limit
    // passes. One candidate moves into Screening; the other stays in Applied, which has no limit.
    private async Task<(string Email, Guid JobId, Guid ApplicationId)> ArrangeAsync()
    {
        var email = ApiSession.NewEmail("e2e-time-limit");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Fernbrook Supply", slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);
        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title = Title,
                description = "<p>Help customers get the most from their orders.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Gb,
            },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();

        var workflow = JsonNode.Parse(job.GetProperty("workflow").GetRawText())!.AsObject();
        workflow["automations"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "reply-reminder",
            ["name"] = "Remind the team to reply",
            ["trigger"] = new JsonObject { ["type"] = "SlaBreached", ["stageId"] = "screening" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "CreateTask", ["note"] = Reminder }),
        });
        await ReadAsync(await session.PutAsync($"/v1/jobs/{jobId}/workflow", new JsonObject { ["workflow"] = workflow, ["expectedWorkflowVersion"] = 1 }, Ct), HttpStatusCode.OK);
        await ReadAsync(await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct), HttpStatusCode.OK);

        var waiting = await ReadAsync(await session.PostAsync(
            $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = Waiting, email = ApiSession.NewEmail("rowan") } }, Ct), HttpStatusCode.Created);
        await ReadAsync(await session.PostAsync(
            $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = Fresh, email = ApiSession.NewEmail("kit") } }, Ct), HttpStatusCode.Created);
        var applicationId = waiting.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/applications/{applicationId}/move", new { toStageId = "screening" }, Ct), HttpStatusCode.OK);
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
