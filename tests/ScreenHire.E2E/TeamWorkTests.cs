using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// A hiring team organizing candidates in the dashboard: a note in Activity's box, a tag in the candidate's header, the
/// candidates list filtered by that tag, a manual automation run from Activity with the team notified, and completing the
/// task it made, in Overview's Needs you, from My tasks. Signs in as a new person with a new organization, whose job has a
/// Manual automation that creates a to-do and notifies the team.
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

        // A note, written in the box on the Activity tab, lands in the timeline below it with its author.
        await Page.GotoAsync(DashboardUrl($"/applications/{applicationId}?tab=activity"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(Tagged);
        var activity = Region("Activity");
        await activity.GetByLabel("Note for the hiring team").FillAsync("Strong portfolio. Ask about the notice period.");
        await activity.GetByRole(AriaRole.Button, new() { Name = "Save note", Exact = true }).ClickAsync();
        await Expect(activity.GetByLabel("Note for the hiring team")).ToHaveValueAsync("");
        await Expect(activity).ToContainTextAsync("Note by you");
        await Expect(activity).ToContainTextAsync("Strong portfolio. Ask about the notice period.");

        // A tag, in the candidate's header: "Add tag" is a text button that opens its field in place, and Escape closes it.
        var header = Page.Locator("[data-slot='header-band']");
        var addTag = header.GetByRole(AriaRole.Button, new() { Name = "Add tag", Exact = true });
        var newTag = header.GetByLabel("New tag");
        await Expect(newTag).ToHaveCountAsync(0);
        await addTag.ClickAsync();
        await Expect(newTag).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(newTag).ToHaveCountAsync(0);
        await addTag.ClickAsync();
        await newTag.FillAsync("Referral");
        await header.GetByRole(AriaRole.Button, new() { Name = "Add tag", Exact = true }).ClickAsync();
        // The tag is a muted pill, and its own Remove button is hidden until the pill is hovered (or focused, or tapped).
        var tags = header.GetByRole(AriaRole.List, new() { Name = "Tags", Exact = true });
        await Expect(tags).ToContainTextAsync("Referral");
        var remove = header.GetByRole(AriaRole.Button, new() { Name = "Remove tag Referral" });
        await Page.Mouse.MoveAsync(0, 0);
        await Expect(remove).ToHaveCSSAsync("opacity", "0");
        await tags.GetByRole(AriaRole.Listitem).First.HoverAsync();
        await Expect(remove).ToHaveCSSAsync("opacity", "1");
        await Expect(activity).ToContainTextAsync("Tagged \"Referral\" by you");
        // The Activity line can arrive before the tag's request has settled. Both buttons are disabled until it has, and
        // enabling one eases its colours back; axe measuring mid-ease reports a button at half contrast.
        await Expect(addTag).ToBeEnabledAsync();
        await Expect(remove).ToBeEnabledAsync();
        await Page.Mouse.MoveAsync(0, 0);
        await Expect(activity.GetByRole(AriaRole.Button, new() { Name = "Save note", Exact = true })).ToBeEnabledAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-team-work-application");

        // The candidates list, filtered by the tag, shows only the tagged candidate.
        await Page.GotoAsync(DashboardUrl("/candidates"));
        var applications = Region("Candidates");
        await Expect(applications.GetByRole(AriaRole.Link, new() { Name = Untagged, Exact = true })).ToBeVisibleAsync();
        await applications.GetByRole(AriaRole.Button, new() { Name = "Tag: all" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitemradio, new() { Name = "Referral" }).ClickAsync();
        // A filter menu stays open for another choice, as the verification filter does.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.GetByRole(AriaRole.Menu)).ToHaveCountAsync(0);
        await Expect(applications.GetByRole(AriaRole.Link, new() { Name = Untagged, Exact = true })).ToHaveCountAsync(0);
        await Expect(applications.GetByRole(AriaRole.Link, new() { Name = Tagged, Exact = true })).ToBeVisibleAsync();

        // A manual automation runs from the application's Activity tab and makes a task, which Overview's Needs you shows.
        await applications.GetByRole(AriaRole.Link, new() { Name = Tagged, Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(Tagged);
        await Expect(Region("Needs you")).ToBeVisibleAsync();
        await Expect(Region("Automations")).ToHaveCountAsync(0); // the manual automations are on Activity, not Overview
        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Activity") }).ClickAsync();
        await Region("Automations").GetByRole(AriaRole.Button, new() { Name = "Run, Prepare the interview" }).ClickAsync();
        await Expect(Region("Automations")).ToContainTextAsync("Asked to run \"Prepare the interview\"");
        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Overview") }).ClickAsync();
        await Expect(Region("Needs you")).ToContainTextAsync("Call the references.", new() { Timeout = 30_000 });
        // Its NotifyTeam action reaches the open dashboard's notification tray, with the action's note.
        var notifications = Region("Notifications");
        await Expect(notifications).ToContainTextAsync("Interview prep has started.", new() { Timeout = 30_000 });
        await Expect(notifications.GetByRole(AriaRole.Link, new() { Name = $"{Tagged}, Platform Engineer" })).ToBeVisibleAsync();
        await notifications.GetByRole(AriaRole.Button, new() { Name = "Dismiss notification" }).ClickAsync();
        await Expect(notifications.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(0);

        // My tasks lists it under For the team, and its row links to the candidate, where Needs you ticks it off: Undo
        // for 10 seconds, then the task is done, and it leaves My tasks' open list for Done.
        await Page.GotoAsync(DashboardUrl("/tasks"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("My tasks");
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^For the team") }).ClickAsync();
        var open = Region("Tasks");
        await Expect(open.GetByRole(AriaRole.Link, new() { Name = "Call the references.", Exact = true })).ToBeVisibleAsync();
        await Expect(open).ToContainTextAsync(Tagged);
        // For the team already says whose they are, so the table has no For column there.
        await Expect(open).ToContainTextAsync("To-do");
        await Expect(open.GetByRole(AriaRole.Checkbox)).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-my-tasks");
        await Page.SetViewportSizeAsync(390, 844);
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the page never scrolls sideways (F1)");
        await SaveScreenshotAsync(Page, "dashboard-my-tasks-phone");
        await Page.SetViewportSizeAsync(1440, 1000);
        await open.GetByRole(AriaRole.Link, new() { Name = "Call the references.", Exact = true }).ClickAsync();
        var steps = Region("Needs you");
        await steps.GetByRole(AriaRole.Checkbox, new() { Name = "Mark done for Call the references." }).ClickAsync();
        await Expect(steps.GetByRole(AriaRole.Button, new() { Name = "Undo, Call the references." })).ToBeVisibleAsync();
        await Expect(steps.GetByRole(AriaRole.Button, new() { Name = "Undo, Call the references." })).ToHaveCountAsync(0, new() { Timeout = 20_000 });
        await Expect(steps).ToContainTextAsync("by you");
        await Page.GotoAsync(DashboardUrl("/tasks"));
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^For the team") }).ClickAsync();
        await Expect(Region("Tasks").GetByText("Nothing to do")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true }).ClickAsync();
        await Expect(Region("Tasks").GetByRole(AriaRole.Cell, new() { Name = "You", Exact = true })).ToBeVisibleAsync();
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
