using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// A hiring team writing notes on a candidate (design system sections 11 and 22), in one new organization with an Owner,
/// an Admin and a Recruiter, each in a browser of their own: the Owner mentions the Recruiter through the @ list, the
/// Recruiter is emailed without the note's words and sees a card if the dashboard is open, the Owner edits the note and it
/// says Edited, the Recruiter has no menu on the Owner's note and the API refuses their edit and delete, and the Admin
/// deletes it through the menu and the dialog, which leaves Activity saying who deleted it and not what it said.
/// </summary>
public sealed class CandidateNotesTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Candidate = "Taylor Brook";
    private const string Organization = "Harbour Lane Studio";
    private static readonly string[] Gb = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_mention_emails_the_person_and_only_the_author_edits_while_an_admin_deletes_a_note()
    {
        using var ownerSession = new ApiSession(Fixture);
        var ownerEmail = ApiSession.NewEmail("owner");
        await ownerSession.SignInWithDevSsoAsync(ownerEmail, Ct);
        await ReadAsync(await ownerSession.PostAsync("/v1/auth/onboarding", new { organizationName = Organization, slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);
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
        var adminEmail = ApiSession.NewEmail("admin");
        using var recruiterSession = await InviteAsync(ownerSession, recruiterEmail, "Recruiter");
        using var adminSession = await InviteAsync(ownerSession, adminEmail, "Admin");

        // Names as the team sees them: the page and the emails use the account's display name.
        var members = await ReadAsync(await ownerSession.Client.GetAsync("/v1/organization/members", Ct), HttpStatusCode.OK);
        string NameOf(string email) => members.EnumerateArray().Single(member => member.GetProperty("email").GetString() == email).GetProperty("displayName").GetString()!;
        var (ownerName, recruiterName, adminName) = (NameOf(ownerEmail), NameOf(recruiterEmail), NameOf(adminEmail));
        var activityUrl = DashboardUrl($"/applications/{applicationId}?tab=activity");

        // The Recruiter has the dashboard open on the candidate, so a mention reaches them live.
        await using var recruiterContext = await Fixture.Browser.NewContextAsync(new() { Locale = "en-US" });
        var recruiterPage = await recruiterContext.NewPageAsync();
        await recruiterPage.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(recruiterPage, recruiterEmail);
        await recruiterPage.GotoAsync(activityUrl);
        await Expect(recruiterPage.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(Candidate);
        await Expect(recruiterPage.GetByRole(AriaRole.Status).Filter(new() { HasText = "Live updates" })).ToContainTextAsync("Live updates: Live");

        // The Owner writes a note on Activity and mentions the Recruiter from the @ list.
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, ownerEmail);
        await Page.GotoAsync(activityUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(Candidate);
        var box = Page.GetByLabel("Note for the hiring team");
        await box.ClickAsync();
        await box.PressSequentiallyAsync("Ask @recr");
        var teammates = Page.GetByRole(AriaRole.Listbox, new() { Name = "Teammates" });
        await Expect(teammates.GetByRole(AriaRole.Option)).ToHaveCountAsync(1);
        await Expect(teammates).ToContainTextAsync(recruiterName);
        await Expect(teammates).ToContainTextAsync("Recruiter");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(teammates).ToHaveCountAsync(0);
        await Expect(box).ToHaveValueAsync($"Ask @{recruiterName} ");
        await box.PressSequentiallyAsync("about the heliotrope lantern.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save note", Exact = true }).ClickAsync();
        await Expect(box).ToHaveValueAsync("");

        // The note reads "Ask @Name about ..." and has an id the mention email links to.
        var written = Page.Locator("li[id^='note-']").Filter(new() { HasText = "heliotrope lantern" });
        await Expect(written).ToContainTextAsync($"Ask @{recruiterName} about the heliotrope lantern.");
        await Expect(written).ToContainTextAsync("Note by you");
        var noteId = (await written.GetAttributeAsync("id"))!["note-".Length..];
        var note = Page.Locator($"#note-{noteId}");

        // The Recruiter is emailed who mentioned them and where, never what the note says, and sees a card if open.
        var mail = (await Fixture.Mailpit.WaitForMessagesAsync(recruiterEmail, 2, Ct)).Single(message => message.Subject.Contains("mentioned you", StringComparison.Ordinal));
        mail.Subject.Should().Be($"{ownerName} mentioned you on {Candidate}");
        mail.Text.Should().Contain($"{ownerName} mentioned you in a note on {Candidate}, Platform Engineer, at {Organization}.")
            .And.Contain($"/applications/{applicationId}?tab=activity#note-{noteId}");
        foreach (var words in new[] { "heliotrope", "lantern" })
        {
            mail.Text.Should().NotContain(words, "the email never quotes the note");
            mail.Html.Should().NotContain(words, "the email never quotes the note");
        }

        var card = recruiterPage.GetByRole(AriaRole.Region, new() { Name = "Notifications" });
        await Expect(card).ToContainTextAsync($"{ownerName} mentioned you in a note.", new() { Timeout = 30_000 });
        await Expect(card.GetByRole(AriaRole.Link, new() { Name = $"{Candidate}, Platform Engineer" })).ToBeVisibleAsync();

        // The author edits it through the note's menu, and it says Edited.
        await note.GetByRole(AriaRole.Button, new() { Name = "More actions for this note" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Edit" }).ClickAsync();
        var edit = note.GetByLabel("Edit your note");
        await Expect(edit).ToBeFocusedAsync();
        await edit.PressAsync("Control+End");
        await edit.PressSequentiallyAsync(" Checked with the team.");
        await note.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(note.GetByText("Edited", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(note).ToContainTextAsync($"Ask @{recruiterName} about the heliotrope lantern. Checked with the team.");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-candidate-notes");

        // The Recruiter reads the Owner's edited note and has no menu on it; the API says the same.
        await recruiterPage.GotoAsync($"{activityUrl}#note-{noteId}");
        var theirView = recruiterPage.Locator($"#note-{noteId}");
        await Expect(theirView).ToContainTextAsync("Checked with the team.");
        await Expect(theirView).ToContainTextAsync("Edited");
        await Expect(theirView.GetByRole(AriaRole.Button, new() { Name = "More actions for this note" })).ToHaveCountAsync(0);
        var path = $"/v1/applications/{applicationId}/notes/{noteId}";
        using (var edited = await recruiterSession.PutAsync(path, new { note = "Taken over." }, Ct))
        {
            edited.StatusCode.Should().Be(HttpStatusCode.Forbidden, "only the author edits a note");
        }

        using (var deleted = await recruiterSession.DeleteAsync(path, Ct))
        {
            deleted.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a Recruiter deletes only their own notes");
        }

        // An Admin deletes it through the menu and the dialog. The menu offers Delete and not Edit.
        await using var adminContext = await Fixture.Browser.NewContextAsync(new() { Locale = "en-US" });
        var adminPage = await adminContext.NewPageAsync();
        await adminPage.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(adminPage, adminEmail);
        await adminPage.GotoAsync(activityUrl);
        var adminsView = adminPage.Locator($"#note-{noteId}");
        await Expect(adminsView).ToContainTextAsync("Checked with the team.");
        await adminsView.GetByRole(AriaRole.Button, new() { Name = "More actions for this note" }).ClickAsync();
        await Expect(adminPage.GetByRole(AriaRole.Menuitem, new() { Name = "Edit" })).ToHaveCountAsync(0);
        await adminPage.GetByRole(AriaRole.Menuitem, new() { Name = "Delete" }).ClickAsync();
        var dialog = adminPage.GetByRole(AriaRole.Dialog, new() { Name = "Delete this note?" });
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Delete note" }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(adminsView).ToHaveCountAsync(0);
        await Expect(adminPage.GetByRole(AriaRole.Region, new() { Name = "Activity", Exact = true })).ToContainTextAsync("Deleted a note by you");

        // The Owner sees who deleted it, and nothing of what it said.
        await Page.ReloadAsync();
        var activity = Page.GetByRole(AriaRole.Region, new() { Name = "Activity", Exact = true });
        await Expect(activity).ToContainTextAsync($"Deleted a note by {adminName}");
        await Expect(note).ToHaveCountAsync(0);
        await Expect(activity).Not.ToContainTextAsync("heliotrope");
        await ExpectNoAccessibilityViolationsAsync();
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
