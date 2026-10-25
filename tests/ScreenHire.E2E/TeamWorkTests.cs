using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.E2E;

/// <summary>
/// A hiring team organizing candidates in the dashboard: a note in Activity, a tag, the candidates list filtered by that
/// tag, a manual automation run from the application with the team notified, and completing the task it made on My
/// tasks. Signs in as a new person with a new organization, whose job has a Manual automation that creates a to-do and
/// notifies the team.
/// </summary>
public sealed class TeamWorkTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Tagged = "Ellis Moreno";
    private const string Untagged = "Robin Lake";
    private static readonly string[] Gb = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_note_a_tag_the_tag_filter_a_manual_run_and_its_task_completed()
    {
        var (email, applicationId) = await ArrangeAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(email);

        // A note, in a sheet, lands in Activity with its author.
        await Page.GotoAsync(DashboardUrl($"/applications/{applicationId}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(Tagged);
        var activity = Region("Activity");
        await activity.GetByRole(AriaRole.Button, new() { Name = "Add note", Exact = true }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Add a note" });
        await sheet.GetByLabel("Note").FillAsync("Strong portfolio. Ask about the notice period.");
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Add note", Exact = true }).ClickAsync();
        await Expect(sheet).ToBeHiddenAsync();
        await Expect(activity).ToContainTextAsync("Note by you");
        await Expect(activity).ToContainTextAsync("\"Strong portfolio. Ask about the notice period.\"");

        // A tag, as a pill with its own Remove button.
        var tags = Region("Tags");
        await tags.GetByLabel("New tag").FillAsync("Referral");
        await tags.GetByRole(AriaRole.Button, new() { Name = "Add tag" }).ClickAsync();
        await Expect(tags.GetByRole(AriaRole.List, new() { Name = "Tags" })).ToContainTextAsync("Referral");
        await Expect(tags.GetByRole(AriaRole.Button, new() { Name = "Remove tag Referral" })).ToBeVisibleAsync();
        await Expect(activity).ToContainTextAsync("Tagged \"Referral\" by you");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-team-work-application");

        // The candidates list, filtered by the tag, shows only the tagged candidate.
        await Page.GotoAsync(DashboardUrl("/candidates"));
        var applications = Region("Applications");
        await Expect(applications.GetByRole(AriaRole.Link, new() { Name = Untagged, Exact = true })).ToBeVisibleAsync();
        await applications.GetByRole(AriaRole.Button, new() { Name = "Tag: all" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitemradio, new() { Name = "Referral" }).ClickAsync();
        // A filter menu stays open for another choice, as the integrity filter does.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.GetByRole(AriaRole.Menu)).ToHaveCountAsync(0);
        await Expect(applications.GetByRole(AriaRole.Link, new() { Name = Untagged, Exact = true })).ToHaveCountAsync(0);
        await Expect(applications.GetByRole(AriaRole.Link, new() { Name = Tagged, Exact = true })).ToBeVisibleAsync();

        // A manual automation runs from the application and makes a task, which appears live.
        await applications.GetByRole(AriaRole.Link, new() { Name = Tagged, Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(Tagged);
        await Region("Automations").GetByRole(AriaRole.Button, new() { Name = "Run, Prepare the interview" }).ClickAsync();
        await Expect(Region("Automations")).ToContainTextAsync("Asked to run \"Prepare the interview\"");
        await Expect(Region("Tasks")).ToContainTextAsync("Call the references.", new() { Timeout = 30_000 });
        // Its NotifyTeam action reaches the open dashboard's notification tray, with the action's note.
        var notifications = Region("Notifications");
        await Expect(notifications).ToContainTextAsync("Interview prep has started.", new() { Timeout = 30_000 });
        await Expect(notifications.GetByRole(AriaRole.Link, new() { Name = $"{Tagged}, Platform Engineer" })).ToBeVisibleAsync();
        await notifications.GetByRole(AriaRole.Button, new() { Name = "Dismiss notification" }).ClickAsync();
        await Expect(notifications.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(0);

        // My tasks lists it for the team; completing it empties the open list and puts it under Done.
        await Page.GotoAsync(DashboardUrl("/tasks"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("My tasks");
        var open = Region("Open tasks");
        await Expect(open.GetByRole(AriaRole.Link, new() { Name = Tagged, Exact = true })).ToBeVisibleAsync();
        await Expect(open).ToContainTextAsync("For the team");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-my-tasks");
        await Page.SetViewportSizeAsync(390, 844);
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the page never scrolls sideways (F1)");
        await SaveScreenshotAsync(Page, "dashboard-my-tasks-phone");
        await Page.SetViewportSizeAsync(1440, 1000);
        await open.GetByRole(AriaRole.Button, new() { Name = $"Mark done for {Tagged}: Call the references." }).ClickAsync();
        await Expect(open.GetByText("Nothing to do")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true }).ClickAsync();
        await Expect(Region("Done")).ToContainTextAsync("by you");
    }

    private ILocator Region(string name) => Page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true });

    // A new person and organization, a published job whose workflow gains a Manual automation that makes a to-do and
    // notifies the team, and two applications a recruiter added.
    private async Task<(string Email, Guid ApplicationId)> ArrangeAsync()
    {
        var email = ApiSession.NewEmail("e2e-team-work");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);
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
            ["id"] = "prepare",
            ["name"] = "Prepare the interview",
            ["trigger"] = new JsonObject { ["type"] = "Manual" },
            ["actions"] = new JsonArray(
                new JsonObject { ["type"] = "CreateTask", ["note"] = "Call the references." },
                new JsonObject { ["type"] = "NotifyTeam", ["note"] = "Interview prep has started." }),
        });
        await ReadAsync(await session.PutAsync($"/v1/jobs/{jobId}/workflow", new JsonObject { ["workflow"] = workflow, ["expectedWorkflowVersion"] = 1 }, Ct), HttpStatusCode.OK);
        await ReadAsync(await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct), HttpStatusCode.OK);

        var tagged = await ReadAsync(await session.PostAsync(
            $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = Tagged, email = ApiSession.NewEmail("ellis") } }, Ct), HttpStatusCode.Created);
        await ReadAsync(await session.PostAsync(
            $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = Untagged, email = ApiSession.NewEmail("robin") } }, Ct), HttpStatusCode.Created);
        return (email, tagged.GetProperty("id").GetGuid());
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
