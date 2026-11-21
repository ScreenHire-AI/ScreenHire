using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// The workspace's Activity in the bell (design system section 11, Notification bell): with the Owner's dashboard open, A
/// opens the bell at Activity, a teammate's move and note arrive in it live, and each entry's link opens exactly where the
/// change is: the candidate for the move, the note on the candidate's Activity for the note.
/// </summary>
public sealed class ActivityFeedTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Candidate = "Taylor Brook";
    private static readonly string[] Gb = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_teammates_move_and_note_appear_live_in_another_sessions_Activity_and_each_link_opens_the_record()
    {
        using var ownerSession = new ApiSession(Fixture);
        var ownerEmail = ApiSession.NewEmail("owner");
        await ownerSession.SignInWithDevSsoAsync(ownerEmail, Ct);
        await ReadAsync(await ownerSession.PostAsync("/v1/auth/onboarding", new { name = "Riley Harper", organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);
        // The Owner makes the job, so it is on their hiring team: My jobs, where Activity starts, holds it.
        var job = await ReadAsync(await ownerSession.PostAsync(
            "/v1/jobs",
            new { title = "Platform Engineer", description = "<p>Build and run the things people rely on.</p>", employmentType = "FullTime", workplaceType = "Remote", applicantLocationRequirements = Gb },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();
        var application = await ReadAsync(await ownerSession.PostAsync(
            $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = Candidate, email = ApiSession.NewEmail("taylor") } }, Ct), HttpStatusCode.Created);
        var applicationId = application.GetProperty("id").GetGuid();
        var recruiterEmail = ApiSession.NewEmail("recruiter");
        using var recruiterSession = await InviteAsync(ownerSession, recruiterEmail, "Recruiter");
        var members = await ReadAsync(await ownerSession.Client.GetAsync("/v1/organization/members", Ct), HttpStatusCode.OK);
        var recruiterName = members.EnumerateArray().Single(member => member.GetProperty("email").GetString() == recruiterEmail).GetProperty("displayName").GetString()!;

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, ownerEmail);
        await Expect(LiveIndicator(Page)).ToContainTextAsync("Live updates: Connected");

        // A, anywhere outside a text field, opens the bell at Activity.
        await Page.Keyboard.PressAsync("a");
        var panel = Page.GetByRole(AriaRole.Dialog, new() { Name = "Notifications" });
        await Expect(panel.GetByRole(AriaRole.Tab, new() { Name = "Activity" })).ToHaveAttributeAsync("aria-selected", "true");
        var feed = panel.GetByRole(AriaRole.List, new() { Name = "Activity" });
        await Expect(feed).ToContainTextAsync($"added {Candidate}");

        // The Recruiter's move and note arrive while it is open.
        await PostAsync(recruiterSession, $"/v1/applications/{applicationId}/move", new { toStageId = "screening" }, HttpStatusCode.OK);
        var noteId = (await ReadAsync(
            await recruiterSession.PostAsync($"/v1/applications/{applicationId}/notes", new { note = "Strong portfolio." }, Ct), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var moveRow = feed.GetByRole(AriaRole.Listitem).Filter(new() { HasText = $"{recruiterName} moved {Candidate} to Screening" });
        var noteRow = feed.GetByRole(AriaRole.Listitem).Filter(new() { HasText = $"{recruiterName} added a note on {Candidate}" });
        await Expect(moveRow).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await Expect(noteRow).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await ExpectNoAccessibilityViolationsAsync();

        // The move opens the candidate; the panel closes behind the link.
        await moveRow.GetByRole(AriaRole.Link, new() { Name = Candidate }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/applications/{applicationId}$"));
        await Expect(panel).ToBeHiddenAsync();

        // The note opens on the candidate's Activity, at the note itself.
        await Page.Keyboard.PressAsync("a");
        await noteRow.GetByRole(AriaRole.Link, new() { Name = Candidate }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/applications/{applicationId}\\?tab=activity#note-{noteId}$"));
        await Expect(Page.Locator($"#note-{noteId}")).ToContainTextAsync("Strong portfolio.");
        await Expect(Page.Locator($"#note-{noteId}")).ToBeInViewportAsync();
    }

    private static async Task PostAsync(ApiSession session, string path, object body, HttpStatusCode expected)
    {
        using var response = await session.PostAsync(path, body, Ct);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
    }

    // The Owner invites an address; the invited person accepts the emailed link in a session of their own, which signs in
    // as them in the same organization.
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
            using var accepted = await session.PostAsync("/v1/auth/invitations/accept", new { token, name = ApiSession.NewName("Jordan") }, Ct);
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

    private async Task SignInAsync(IPage page, string email)
    {
        await page.GotoAsync(DashboardUrl("/sign-in"));
        await page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await page.GetByLabel("Any email address").FillAsync(email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private static ILocator LiveIndicator(IPage page) =>
        page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Live updates" });

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
