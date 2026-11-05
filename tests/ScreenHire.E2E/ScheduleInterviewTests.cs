using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// Scheduling an interview from the candidate's page (docs/architecture/interview-scheduling.md): a recruiter connects the
/// sandbox calendar from the account menu, offers the candidate times from the Schedule interview sheet with a teammate,
/// the candidate books one on the careers page, and the page shows the booked time once in its header and the interview
/// in the Overview panel, from which the recruiter cancels it. Each test signs in as a new person with a new
/// organization and sets up everything else through the API, the way the dashboard does.
/// </summary>
public sealed partial class ScheduleInterviewTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] Britain = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_recruiter_schedules_an_interview_the_candidate_books_it_and_the_recruiter_cancels_it()
    {
        var ownerEmail = ApiSession.NewEmail("e2e-schedule");
        var teammateEmail = ApiSession.NewEmail("e2e-schedule-peer");
        var otherEmail = ApiSession.NewEmail("e2e-schedule-nocal");
        var candidate = ApiSession.NewEmail("candidate");
        using var owner = new ApiSession(Fixture);
        await owner.SignInWithDevSsoAsync(ownerEmail, Ct);
        await ReadAsync(await owner.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);

        // A second member who has connected their calendar, and a third who has not.
        using var teammate = await InviteAsync(owner, teammateEmail);
        await teammate.ConnectSandboxCalendarAsync(Ct);
        using var other = await InviteAsync(owner, otherEmail);
        var members = await ReadAsync(await owner.Client.GetAsync("/v1/organization/members", Ct), HttpStatusCode.OK);
        string NameOf(string email) => members.EnumerateArray().Single(member => member.GetProperty("email").GetString() == email).GetProperty("displayName").GetString()!;
        var (ownerName, teammateName, otherName) = (NameOf(ownerEmail), NameOf(teammateEmail), NameOf(otherEmail));

        var jobTitle = $"Support lead {Guid.NewGuid().ToString("N")[..4]}";
        var job = await ReadAsync(await owner.PostAsync(
            "/v1/jobs",
            new
            {
                title = jobTitle,
                description = "<p>Help customers and the team that looks after them.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Britain,
            },
            Ct), HttpStatusCode.Created);
        var application = await ReadAsync(await owner.PostAsync(
            $"/v1/jobs/{job.GetProperty("id").GetGuid()}/applications", new { candidate = new { firstName = "Riley", lastName = "Chen", email = candidate } }, Ct), HttpStatusCode.Created);
        var applicationUrl = DashboardUrl($"/applications/{application.GetProperty("id").GetGuid()}");

        await Page.SetViewportSizeAsync(1280, 900);
        await SignInAsync(ownerEmail);
        await Page.GotoAsync(applicationUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Riley Chen");

        // The recruiter connects the sandbox calendar from the account menu, and comes back to this page with the sheet open.
        await Page.Locator("[data-sidebar=footer]").GetByRole(AriaRole.Button).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Calendar" }).ClickAsync();
        var calendar = Page.GetByRole(AriaRole.Dialog, new() { Name = "Calendar" });
        await Expect(calendar).ToBeVisibleAsync();
        await calendar.GetByRole(AriaRole.Button, new() { Name = "Connect Sandbox calendar" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(applicationUrl);
        await Expect(calendar).ToContainTextAsync("Connected");
        await Expect(calendar).ToContainTextAsync("Sandbox calendar");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(calendar).ToHaveCountAsync(0);

        // Schedule interview sits beside Email. The sheet lists the people who see the job: the recruiter is ticked
        // and is the organizer, the teammate can be ticked, and the member with no calendar is shown, in words, as unavailable.
        var header = Page.Locator("[data-slot=header-band]");
        await Expect(header.GetByRole(AriaRole.Button, new() { Name = "Email", Exact = true })).ToBeVisibleAsync();
        await header.GetByRole(AriaRole.Button, new() { Name = "Schedule interview" }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Schedule interview" });
        var interviewers = sheet.GetByRole(AriaRole.Group, new() { Name = "Interviewers" });
        await Expect(interviewers.GetByRole(AriaRole.Checkbox, new() { NameRegex = new($"^{Regex.Escape(ownerName)}") })).ToBeCheckedAsync();
        await Expect(sheet.GetByText($"{ownerName}'s calendar sends the invitation.")).ToBeVisibleAsync();
        var without = interviewers.GetByRole(AriaRole.Checkbox, new() { NameRegex = new($"^{Regex.Escape(otherName)}") });
        await Expect(without).ToBeDisabledAsync();
        await Expect(interviewers).ToContainTextAsync(otherName);
        await Expect(interviewers.GetByText("No calendar connected")).ToHaveCountAsync(1);
        await interviewers.GetByRole(AriaRole.Checkbox, new() { NameRegex = new($"^{Regex.Escape(teammateName)}") }).CheckAsync();
        await sheet.GetByLabel("Time zone").SelectOptionAsync("UTC");
        await Expect(sheet.GetByLabel("Length")).ToHaveValueAsync("30");
        await Expect(sheet.GetByLabel("From")).ToHaveValueAsync("09:00");
        await Expect(sheet.GetByLabel("To")).ToHaveValueAsync("17:00");
        await ExpectNoAccessibilityViolationsAsync();
        await ShotsAsync("schedule-interview-sheet", sheet: true);
        await OpenScheduleSheetAsync(header);
        await interviewers.GetByRole(AriaRole.Checkbox, new() { NameRegex = new($"^{Regex.Escape(teammateName)}") }).CheckAsync();
        await sheet.GetByLabel("Time zone").SelectOptionAsync("UTC");
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Send invitation" }).ClickAsync();
        await Expect(sheet).ToHaveCountAsync(0);

        // The Overview says it is waiting for the candidate, and nothing is in the header yet.
        var panel = Page.GetByRole(AriaRole.Region, new() { Name = "Interview", Exact = true });
        await Expect(panel).ToContainTextAsync("Waiting for the candidate");
        var length = header.GetByText("30 minutes with 2 interviewers");
        await Expect(length).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        await ShotsAsync("schedule-interview-open");

        // One interview at a time: the action is not offered while this one is open or booked.
        await Expect(header.GetByRole(AriaRole.Button, new() { Name = "Schedule interview" })).ToHaveCountAsync(0);

        // The candidate opens the emailed link in a browser of their own and books the first time offered.
        var invitation = await WaitForEmailAsync(candidate, "Pick a time");
        var link = InterviewLink().Match(invitation.Text);
        link.Success.Should().BeTrue($"the invitation carries the link: {invitation.Text}");
        await using (var candidateContext = await Fixture.Browser.NewContextAsync(new() { Locale = "en-US" }))
        {
            var candidatePage = await candidateContext.NewPageAsync();
            await candidatePage.GotoAsync(link.Value);
            await Expect(candidatePage.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Pick a time for your interview");
            await candidatePage.GetByRole(AriaRole.Radio).First.CheckAsync();
            await candidatePage.GetByRole(AriaRole.Button, new() { Name = "Book this time" }).ClickAsync();
            await Expect(candidatePage.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Your interview is booked");
        }

        // The page that was open learns of it live: the time is in the header's Interview card, said once, and the panel
        // lists the interviewers, with a link to the meeting once the organizer's calendar made it.
        await Expect(length).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var time = await length.Locator("xpath=preceding-sibling::p[1]").InnerTextAsync();
        var people = panel.GetByRole(AriaRole.List, new() { Name = "Interviewers" });
        await Expect(people).ToContainTextAsync(new Regex($"{Regex.Escape(ownerName)}\\s*Organizer"));
        await Expect(people).ToContainTextAsync(teammateName);
        await Expect(panel.GetByRole(AriaRole.Link, new() { NameRegex = new("^Join the meeting") })).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(panel).Not.ToContainTextAsync("calendar yet", new() { Timeout = 30_000 });
        await Expect(panel).Not.ToContainTextAsync(time);
        await ExpectNoAccessibilityViolationsAsync();
        await ShotsAsync("schedule-interview-booked");

        // Activity records it with its time.
        await Page.GotoAsync($"{applicationUrl}?tab=activity");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Activity", Exact = true })).ToContainTextAsync("Interview booked for", new() { Timeout = 30_000 });
        await Page.GotoAsync(applicationUrl);

        // Cancelling is in the panel's menu, behind a dialog; the header's time goes and the panel says how it ended.
        await panel.GetByRole(AriaRole.Button, new() { Name = "Interview actions" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Cancel interview" }).ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel this interview?" });
        await Expect(dialog).ToBeVisibleAsync();
        // The menu button's tooltip is still fading out behind the dialog, and has no colour contrast worth measuring.
        await Page.Mouse.MoveAsync(0, 0);
        await Expect(Page.Locator("[data-slot=tooltip-content]")).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel interview" }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(panel).ToContainTextAsync("was cancelled");
        await Expect(length).ToHaveCountAsync(0);
        await Expect(panel.GetByRole(AriaRole.Button, new() { Name = "Interview actions" })).ToHaveCountAsync(0);
        await Expect(header.GetByRole(AriaRole.Button, new() { Name = "Schedule interview" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task With_no_calendar_connected_the_sheet_says_so_and_opens_the_Calendar_sheet()
    {
        var ownerEmail = ApiSession.NewEmail("e2e-schedule-empty");
        using var owner = new ApiSession(Fixture);
        await owner.SignInWithDevSsoAsync(ownerEmail, Ct);
        await ReadAsync(await owner.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);
        var job = await ReadAsync(await owner.PostAsync(
            "/v1/jobs",
            new
            {
                title = "Office manager",
                description = "<p>Keep the studio running.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Britain,
            },
            Ct), HttpStatusCode.Created);
        var application = await ReadAsync(await owner.PostAsync(
            $"/v1/jobs/{job.GetProperty("id").GetGuid()}/applications", new { candidate = new { firstName = "Riley", lastName = "Chen", email = ApiSession.NewEmail("candidate") } }, Ct), HttpStatusCode.Created);

        await Page.SetViewportSizeAsync(1280, 900);
        await SignInAsync(ownerEmail);
        await Page.GotoAsync(DashboardUrl($"/applications/{application.GetProperty("id").GetGuid()}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Riley Chen");

        await OpenScheduleSheetAsync(Page.Locator("[data-slot=header-band]"));
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Schedule interview" });
        await Expect(sheet).ToContainTextAsync("Nobody who sees this job has a calendar connected.");
        await Expect(sheet.GetByRole(AriaRole.Button, new() { Name = "Send invitation" })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();

        await sheet.GetByRole(AriaRole.Button, new() { Name = "Connect your calendar" }).ClickAsync();
        await Expect(sheet).ToHaveCountAsync(0);
        var calendar = Page.GetByRole(AriaRole.Dialog, new() { Name = "Calendar" });
        await Expect(calendar.GetByRole(AriaRole.Button, new() { Name = "Connect Sandbox calendar" })).ToBeVisibleAsync();
    }

    // Opens the sheet from the header's button, or from the More menu on a phone. The caller says which, because the
    // header changes with the window's width a moment after the viewport does.
    private async Task OpenScheduleSheetAsync(ILocator header, bool phone = false)
    {
        if (phone)
        {
            await header.GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();
            await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Schedule interview" }).ClickAsync();
        }
        else
        {
            await header.GetByRole(AriaRole.Button, new() { Name = "Schedule interview" }).ClickAsync();
        }

        await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Schedule interview" })).ToBeVisibleAsync();
    }

    // The screen at a phone's width and a desktop's, with its pictures kept for the design review. With `sheet`, the
    // Schedule interview sheet is open for each picture, opened again from the More menu on the phone, and closed after.
    private async Task ShotsAsync(string name, bool sheet = false)
    {
        var header = Page.Locator("[data-slot=header-band]");
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Schedule interview" });
        foreach (var (width, height) in new[] { (390, 844), (1280, 900) })
        {
            await Page.SetViewportSizeAsync(width, height);
            if (sheet)
            {
                if (await dialog.CountAsync() > 0)
                {
                    await Page.Keyboard.PressAsync("Escape");
                    await Expect(dialog).ToHaveCountAsync(0);
                }

                await OpenScheduleSheetAsync(header, phone: width < 768);
            }

            await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
            // A tooltip that is still fading out has no colour contrast worth measuring.
            await Page.Mouse.MoveAsync(0, 0);
            await Expect(Page.Locator("[data-slot=tooltip-content]")).ToHaveCountAsync(0);
            var overflow = await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
            overflow.Should().BeFalse($"the page must not scroll sideways at {width}px");
            await ExpectNoAccessibilityViolationsAsync();
            await SaveScreenshotAsync(Page, $"{name}-{width}");
            if (sheet)
            {
                await Page.Keyboard.PressAsync("Escape");
                await Expect(dialog).ToHaveCountAsync(0);
            }
        }
    }

    // The Owner invites an address as a Recruiter; the invited person accepts the emailed link in a session of their own.
    private async Task<ApiSession> InviteAsync(ApiSession owner, string email)
    {
        using (var invited = await owner.PostAsync("/v1/organization/invitations", new { email, role = "Recruiter" }, Ct))
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

    private async Task<MailpitMessage> WaitForEmailAsync(string address, string subject)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        while (true)
        {
            var found = (await Fixture.Mailpit.MessagesAsync(address, Ct)).FirstOrDefault(m => m.Subject.Contains(subject, StringComparison.Ordinal));
            if (found is not null)
            {
                return found;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token);
            }
            catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
            {
                throw new TimeoutException($"No email about \"{subject}\" reached the candidate within 60 s.");
            }
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
    private async Task SignInAsync(string email)
    {
        await GoToAsync("dashboard", "/sign-in");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();

    [GeneratedRegex(@"https?://\S+/interviews/(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex InterviewLink();
}
