using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.E2E;

/// <summary>
/// Teammates' changes as toasts (design system section 11, roadmap item 10), in one new organization with an Owner and a
/// Recruiter on one job's hiring team. The Owner's dashboard is open and live: the Recruiter's move reaches it as a toast
/// that names the Recruiter, the candidate (a link to their page) and the stage; the Owner's own move makes none; the
/// Owner turns Moves off in their notification grid on Your account and the next move is silent; and on a phone the toast sits above the
/// page's action bar. A candidate applying through the careers site toasts the members on the job's hiring team only, and
/// several applications within a minute fold into one toast that links to the job's Not screened candidates.
/// </summary>
public sealed class TeammateToastsTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Candidate = "Taylor Brook";
    private static readonly string[] Gb = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_teammates_move_toasts_the_owner_until_they_turn_moves_off_and_on_a_phone_clears_the_action_bar()
    {
        using var ownerSession = new ApiSession(Fixture);
        var ownerEmail = ApiSession.NewEmail("owner");
        await ownerSession.SignInWithDevSsoAsync(ownerEmail, Ct);
        await ReadAsync(await ownerSession.PostAsync("/v1/auth/onboarding", new { name = "Riley Harper", organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);
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
            $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = Candidate, email = ApiSession.NewEmail("taylor") } }, Ct), HttpStatusCode.Created);
        var applicationId = application.GetProperty("id").GetGuid();

        var recruiterEmail = ApiSession.NewEmail("recruiter");
        using var recruiterSession = await InviteAsync(ownerSession, recruiterEmail, "Recruiter");
        var members = await ReadAsync(await ownerSession.Client.GetAsync("/v1/organization/members", Ct), HttpStatusCode.OK);
        JsonElement MemberOf(string email) => members.EnumerateArray().Single(member => member.GetProperty("email").GetString() == email);
        var ownerName = MemberOf(ownerEmail).GetProperty("displayName").GetString()!;
        var recruiterName = MemberOf(recruiterEmail).GetProperty("displayName").GetString()!;
        // A toast is for a job whose hiring team the person is on, so both are on this one.
        await ReadAsync(await ownerSession.PutAsync(
            $"/v1/jobs/{jobId}/hiring-team",
            new { memberIds = new[] { MemberOf(ownerEmail).GetProperty("id").GetGuid(), MemberOf(recruiterEmail).GetProperty("id").GetGuid() } },
            Ct), HttpStatusCode.OK);

        // The Owner has the dashboard open and live.
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, ownerEmail);
        await Expect(LiveIndicator(Page)).ToContainTextAsync("Live updates: Connected");
        var toasts = Page.GetByRole(AriaRole.Region, new() { Name = "Notifications" });

        // The Recruiter moves the candidate: the toast names who, whom and where, and the name is a link to the candidate.
        await MoveAsync(recruiterSession, applicationId, "screening");
        await Expect(toasts).ToContainTextAsync($"{recruiterName} moved {Candidate} to Screening", new() { Timeout = 30_000 });
        var link = toasts.GetByRole(AriaRole.Link, new() { Name = Candidate });
        await Expect(link).ToHaveAttributeAsync("href", new Regex($"/applications/{applicationId}$"));

        // The Owner's own move makes no toast. The Recruiter's note after it is the marker: its toast arrives after
        // any the Owner's move would have raised, so once it shows, a missing one is missing for good.
        await MoveAsync(ownerSession, applicationId, "interview");
        await AddNoteAsync(recruiterSession, applicationId);
        await Expect(toasts).ToContainTextAsync($"{recruiterName} added a note on {Candidate}", new() { Timeout = 30_000 });
        await Expect(toasts).Not.ToContainTextAsync("to Interview");

        // Moves off in the notification grid: the person's menu, Your account, Notifications, Choose kinds on Teammates' moves and
        // notes, the Moves switch, Save. Then back to the job, whose page shows the toasts.
        var jobPage = Page.Url;
        await Page.GetByRole(AriaRole.Button, new() { Name = ownerName }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Your account" }).ClickAsync();
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Your account" }).GetByRole(AriaRole.Link, new() { Name = "Notifications" }).ClickAsync();
        var grid = Page.GetByRole(AriaRole.Table);
        await grid.GetByRole(AriaRole.Button, new() { Name = "Choose kinds" }).ClickAsync();
        var moves = grid.GetByRole(AriaRole.Switch, new() { Name = "Moves", Exact = true });
        await Expect(moves).ToBeCheckedAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await moves.ClickAsync();
        await Expect(moves).Not.ToBeCheckedAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved." })).ToBeVisibleAsync();
        var saved = await ReadAsync(await ownerSession.Client.GetAsync("/v1/account/notifications", Ct), HttpStatusCode.OK);
        saved.GetProperty("toastsOff").EnumerateArray().Select(kind => kind.GetString()).Should().Equal("Moves");
        await Page.GotoAsync(jobPage);
        await Expect(LiveIndicator(Page)).ToContainTextAsync("Live updates: Connected");

        // The next move is silent. The Recruiter's tag change after it is the marker, as the note was.
        await MoveAsync(recruiterSession, applicationId, "offer");
        await AddTagAsync(recruiterSession, applicationId);
        await Expect(toasts).ToContainTextAsync($"{recruiterName} changed the tags on {Candidate}", new() { Timeout = 30_000 });
        await Expect(toasts).Not.ToContainTextAsync("to Offer");

        // Moves back on, and on a phone the toast clears the Jobs page's action bar (the job page has one only for a draft).
        var choices = await ReadAsync(await ownerSession.Client.GetAsync("/v1/account/notifications", Ct), HttpStatusCode.OK);
        var rows = choices.GetProperty("rows").EnumerateArray()
            .Select(row => new { kind = row.GetProperty("kind").GetString(), email = row.GetProperty("email").GetString(), slack = (bool?)null })
            .ToArray();
        await ReadAsync(await ownerSession.PutAsync(
            "/v1/account/notifications", new { scope = "MyJobs", pauseToasts = false, closeToasts = true, toastsOff = Array.Empty<string>(), rows }, Ct), HttpStatusCode.OK);
        await Page.SetViewportSizeAsync(360, 740);
        await Page.GotoAsync(DashboardUrl("/"));
        await Expect(LiveIndicator(Page)).ToContainTextAsync("Live updates: Connected");
        var actionBar = Page.Locator("[data-slot=action-bar]");
        await Expect(actionBar).ToBeVisibleAsync();

        await MoveAsync(recruiterSession, applicationId, "hired");
        var toast = toasts.GetByRole(AriaRole.Listitem).Filter(new() { HasText = $"{recruiterName} moved {Candidate} to Hired" });
        await Expect(toast).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var toastBox = await toast.BoundingBoxAsync();
        var barBox = await actionBar.BoundingBoxAsync();
        toastBox.Should().NotBeNull();
        barBox.Should().NotBeNull();
        (toastBox!.Y + toastBox.Height).Should().BeLessThanOrEqualTo(barBox!.Y, "the toast sits above the action bar, never over it");
    }

    [Fact]
    public async Task A_candidate_applying_toasts_the_jobs_hiring_team_and_not_a_member_who_is_not_on_it()
    {
        using var ownerSession = new ApiSession(Fixture);
        var ownerEmail = ApiSession.NewEmail("owner");
        await ownerSession.SignInWithDevSsoAsync(ownerEmail, Ct);
        var slug = $"test-{Guid.NewGuid():N}";
        await ReadAsync(await ownerSession.PostAsync("/v1/auth/onboarding", new { name = "Riley Harper", organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);
        var quietJobId = await PublishJobAsync(ownerSession, "Data Engineer");
        var teamJobId = await PublishJobAsync(ownerSession, "Platform Engineer");

        var recruiterEmail = ApiSession.NewEmail("recruiter");
        using var recruiterSession = await InviteAsync(ownerSession, recruiterEmail, "Recruiter");
        var members = await ReadAsync(await ownerSession.Client.GetAsync("/v1/organization/members", Ct), HttpStatusCode.OK);
        Guid MemberId(string email) => members.EnumerateArray().Single(member => member.GetProperty("email").GetString() == email).GetProperty("id").GetGuid();
        // The Owner sees every job but is on the hiring team of only one of them.
        await ReadAsync(await ownerSession.PutAsync($"/v1/jobs/{quietJobId}/hiring-team", new { memberIds = new[] { MemberId(recruiterEmail) } }, Ct), HttpStatusCode.OK);
        await ReadAsync(await ownerSession.PutAsync($"/v1/jobs/{teamJobId}/hiring-team", new { memberIds = new[] { MemberId(ownerEmail) } }, Ct), HttpStatusCode.OK);

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, ownerEmail);
        await Expect(LiveIndicator(Page)).ToContainTextAsync("Live updates: Connected");
        var toasts = Page.GetByRole(AriaRole.Region, new() { Name = "Notifications" });

        // An application to the job the Owner is not on the team of makes no toast. The application to their own job
        // after it is the marker: its toast arrives after any this one would have raised.
        await ApplyAsync(slug, quietJobId, "Jamie Rowe");
        var first = await ApplyAsync(slug, teamJobId, "Casey Morgan");
        await Expect(toasts).ToContainTextAsync("Casey Morgan applied to Platform Engineer", new() { Timeout = 30_000 });
        await Expect(toasts).Not.ToContainTextAsync("Data Engineer");
        await Expect(toasts.GetByRole(AriaRole.Link, new() { Name = "Casey Morgan" })).ToHaveAttributeAsync("href", new Regex($"/applications/{first}$"));
        await Expect(toasts).Not.ToContainTextAsync("verif", new() { IgnoreCase = true });

        // A second one inside the minute takes the first's place, and links to the job's Not screened candidates. The Owner
        // is on the Candidates page by then (moved there in the app, so the live connection stays), where the link must
        // still narrow the list to the job.
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Candidates", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Candidates");
        await ApplyAsync(slug, teamJobId, "Noa Reyes");
        var count = toasts.GetByRole(AriaRole.Link, new() { Name = "2 new applications" });
        await Expect(count).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(toasts).ToContainTextAsync("2 new applications to Platform Engineer");
        await Expect(toasts).Not.ToContainTextAsync("Casey Morgan applied");
        // Long after the quiet job's application would have raised a toast: still none.
        await Expect(toasts).Not.ToContainTextAsync("Data Engineer");
        await Expect(count).ToHaveAttributeAsync("href", new Regex($"/candidates[?]view=not-screened&job={teamJobId}$"));
        await count.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/candidates[?]view=not-screened&job={teamJobId}$"));
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Job: Platform Engineer" })).ToBeVisibleAsync();
        var table = Page.GetByRole(AriaRole.Table, new() { Name = "Candidates" });
        await Expect(table.GetByRole(AriaRole.Link, new() { Name = "Noa Reyes" })).ToBeVisibleAsync();
        await Expect(table.GetByRole(AriaRole.Link, new() { Name = "Casey Morgan" })).ToBeVisibleAsync();
        await Expect(table.GetByRole(AriaRole.Link, new() { Name = "Jamie Rowe" })).ToHaveCountAsync(0);
    }

    // The careers site's own submission: the public route its apply form posts to.
    private async Task<Guid> ApplyAsync(string slug, Guid jobId, string fullName)
    {
        var parts = fullName.Split(' ');
        using var anonymous = Fixture.CreateHttpClient("api");
        using var applied = await anonymous.PostAsJsonAsync($"/v1/public/organizations/{slug}/jobs/{jobId}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["firstName"] = parts[0], ["lastName"] = parts[1], ["email"] = ApiSession.NewEmail(parts[0].ToLowerInvariant()) },
            ["privacyNoticeVersion"] = "2026-11-07.1",
            ["answers"] = new JsonArray(),
            ["aiUseDeclaration"] = "DidNotUse",
        }, Ct);
        applied.StatusCode.Should().Be(HttpStatusCode.Created, await applied.Content.ReadAsStringAsync(Ct));
        return (await applied.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("applicationId").GetGuid();
    }

    private static async Task<Guid> PublishJobAsync(ApiSession session, string title)
    {
        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title,
                description = "<p>Build and run the things people rely on.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Gb,
            },
            Ct), HttpStatusCode.Created);
        var id = job.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/jobs/{id}/publish", new { }, Ct), HttpStatusCode.OK);
        return id;
    }

    private static async Task MoveAsync(ApiSession session, Guid applicationId, string toStageId)
    {
        using var response = await session.PostAsync($"/v1/applications/{applicationId}/move", new { toStageId }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task AddNoteAsync(ApiSession session, Guid applicationId)
    {
        using var response = await session.PostAsync($"/v1/applications/{applicationId}/notes", new { note = "Strong portfolio." }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task AddTagAsync(ApiSession session, Guid applicationId)
    {
        using var response = await session.PostAsync($"/v1/applications/{applicationId}/tags", new { tag = "referral" }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
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

    // Dev SSO finds the account by its address, so the Owner signs in to their organization.
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
