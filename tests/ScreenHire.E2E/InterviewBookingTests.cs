using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// A candidate's interview page end to end (docs/architecture/interview-scheduling.md): two members connect a sandbox
/// calendar, a recruiter offers an interview, and the candidate opens the emailed link with JavaScript off, changes the
/// time zone, books a time, moves it and cancels it. Each test signs in as a new person with a new organization and sets
/// everything up through the API, the way the dashboard does.
/// </summary>
public sealed partial class InterviewBookingTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] Britain = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_candidate_changes_the_zone_books_moves_and_cancels_an_interview_without_javascript()
    {
        using var scene = await SceneAsync();

        await WithoutJavaScriptAsync();
        var response = await Page.GotoAsync(scene.Link);
        response!.Status.Should().Be(200);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Pick a time for your interview");
        await Expect(Page.Locator("main")).ToContainTextAsync($"{scene.JobTitle}. The interview takes 30 minutes.");
        (await Page.Locator("script:not([type='application/ld+json'])").CountAsync()).Should().Be(0, "the page ships no JavaScript");
        (await Page.Locator("[style]").CountAsync()).Should().Be(0, "the content security policy forbids inline styles");
        await Expect(Page.Locator("#tz")).ToHaveValueAsync("UTC");
        await ExpectNoAccessibilityViolationsAsync();

        // Pressing Book without a time is refused by the browser; nothing is posted.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Book this time" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Pick a time for your interview");

        // The zone is a plain GET form: the times come back written in the chosen zone, on the half hour for Kolkata.
        await Page.Locator("#tz").SelectOptionAsync("Asia/Kolkata");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Show times in this zone" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("tz=Asia%2FKolkata"));
        await Expect(Page.Locator("#tz")).ToHaveValueAsync("Asia/Kolkata");
        await Expect(Page.GetByText("All times are in India Standard Time.")).ToBeVisibleAsync();
        var morning = Page.GetByLabel("14:30 to 15:00").First; // 09:00 UTC, the first hour of the working day
        await Expect(morning).ToBeVisibleAsync();
        var firstDay = await DayOfAsync(morning);
        await ExpectNoAccessibilityViolationsAsync();
        await ShotsAsync(scene.Link + "?tz=Asia%2FKolkata", "interview-open");

        morning = Page.GetByLabel("14:30 to 15:00").First; // the shots reopened the page in a new browser context
        await morning.CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Book this time" }).ClickAsync();

        // Back on the same address without the time that was posted, now booked, in the same zone.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Your interview is booked");
        Page.Url.Should().Contain("tz=Asia%2FKolkata").And.Contain("outcome=booked").And.NotContain("start=").And.NotContain("14%3A30");
        ILocator Booked() => Page.GetByRole(AriaRole.Region, new() { Name = "Your time" });
        await Expect(Booked()).ToContainTextAsync(firstDay);
        await Expect(Booked()).ToContainTextAsync("14:30 to 15:00, India Standard Time");
        await Expect(Booked()).ToContainTextAsync("The video link is in your calendar invitation.");
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("We are sending a confirmation to your email.");
        (await Page.Locator("script:not([type='application/ld+json'])").CountAsync()).Should().Be(0);
        await ExpectNoAccessibilityViolationsAsync();
        await ShotsAsync(scene.Link + "?tz=Asia%2FKolkata", "interview-booked");

        // The confirmation email carries a link to manage the booking (a newly signed token for the same interview).
        var confirmation = await WaitForEmailAsync(scene.Candidate, "is set");
        InterviewLink().IsMatch(confirmation.Text).Should().BeTrue($"the confirmation carries the manage link: {confirmation.Text}");

        // Pick another time: the picker opens on the same zone, and the current time is one click away.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Pick another time" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Pick another time");
        Page.Url.Should().Contain("change=1").And.Contain("tz=Asia%2FKolkata");
        await Expect(Page.Locator("main")).ToContainTextAsync("Choose a new time and we will move it.");
        await ExpectNoAccessibilityViolationsAsync();
        var last = Page.Locator("fieldset").Last;
        var lastDay = (await last.Locator("legend").InnerTextAsync()).Trim();
        var lastTime = last.GetByRole(AriaRole.Radio).Last;
        var lastRange = (await lastTime.Locator("xpath=ancestor::label[1]").InnerTextAsync()).Trim();
        await lastTime.CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Move to this time" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Your interview is booked");
        await Expect(Booked()).ToContainTextAsync(lastDay);
        await Expect(Booked()).ToContainTextAsync(lastRange);
        await Expect(Booked()).Not.ToContainTextAsync("14:30 to 15:00");
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Moved to the new time.");
        Page.Url.Should().NotContain("change=1");
        await ExpectNoAccessibilityViolationsAsync();

        // Cancelling closes the link: the page says so, and so does the next visit.
        await Page.Locator("summary", new() { HasText = "Cancel this interview" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Yes, cancel it" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("This interview is cancelled");
        await Expect(Page.Locator("main")).ToContainTextAsync("reply to the email you received");
        await ExpectNoAccessibilityViolationsAsync();
        var again = await Page.GotoAsync(scene.Link);
        again!.Status.Should().Be(410);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("This interview is cancelled");
        await ExpectNoAccessibilityViolationsAsync();

        // The recruiter sees the cancellation too.
        (await scene.Interview(Ct)).GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task A_link_that_was_never_issued_or_belongs_to_nobody_says_so_plainly()
    {
        using var scene = await SceneAsync();
        await WithoutJavaScriptAsync();

        var forged = await Page.GotoAsync(scene.Link.Replace(scene.Token, "CfDJ8NeverIssuedToken000000000", StringComparison.Ordinal));
        forged!.Status.Should().Be(404);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("We could not find this interview");
        await Expect(Page.Locator("main")).ToContainTextAsync("Open it again from the email you received.");
        await ExpectNoAccessibilityViolationsAsync();

        var damaged = await Page.GotoAsync(scene.Link.Replace(scene.Token, "short", StringComparison.Ordinal));
        damaged!.Status.Should().Be(404);

        var other = await OpenAsync("careers", $"/no-such-employer/interviews/{scene.Token}");
        other.Status.Should().Be(404);
    }

    [Fact]
    public async Task A_team_cancellation_closes_the_candidates_link_with_a_calm_page()
    {
        using var scene = await SceneAsync();
        var interview = await scene.Interview(Ct);
        using (var cancelled = await scene.Owner.PostAsync($"/v1/interviews/{interview.GetProperty("id").GetGuid()}/cancel", new { }, Ct))
        {
            cancelled.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        }

        await WithoutJavaScriptAsync();
        var response = await Page.GotoAsync(scene.Link);
        response!.Status.Should().Be(410);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("This interview is cancelled");
        await Expect(Page.Locator("main")).ToContainTextAsync("This link is closed.");
        await Expect(Page.GetByRole(AriaRole.Button)).ToHaveCountAsync(0);
        (await Page.Locator("script:not([type='application/ld+json'])").CountAsync()).Should().Be(0);
        await ExpectNoAccessibilityViolationsAsync();
    }

    // The page at a phone's width and a desktop's, with its pictures kept for the design review. Scripts are switched on
    // only to measure and to take the pictures: the page has none of its own, so it renders the same.
    private async Task ShotsAsync(string link, string name)
    {
        await WithJavaScriptAsync();
        foreach (var (width, height) in new[] { (390, 844), (1280, 900) })
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync(link);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
            var overflow = await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
            overflow.Should().BeFalse($"the page must not scroll sideways at {width}px");
            var smallest = await Page.EvaluateAsync<double>(
                "() => Math.min(...[...document.querySelectorAll('button, a.button-primary, a.button-secondary, label.choice, select')].map(e => e.getBoundingClientRect().height))");
            smallest.Should().BeGreaterThanOrEqualTo(44, $"touch targets are at least 44px tall at {width}px");
            await SaveScreenshotAsync(Page, $"{name}-{width}");
        }

        await WithoutJavaScriptAsync();
        await Page.GotoAsync(link);
    }

    private static async Task<string> DayOfAsync(ILocator radioLabelled) =>
        (await radioLabelled.Locator("xpath=ancestor::fieldset[1]/legend").InnerTextAsync()).Trim();

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

    // A new organization with a job, an application, two members who each connected a sandbox calendar, and an interview
    // the owner offered for the next two weeks. Returns the link the candidate's email carries.
    private async Task<Scene> SceneAsync()
    {
        var ownerEmail = ApiSession.NewEmail("e2e-interview");
        var recruiterEmail = ApiSession.NewEmail("e2e-interviewer");
        var candidate = ApiSession.NewEmail("candidate");
        var jobTitle = $"Support lead {Guid.NewGuid().ToString("N")[..4]}";
        var slug = $"test-{Guid.NewGuid():N}";
        var owner = new ApiSession(Fixture);
        ApiSession? recruiter = null;
        try
        {
            await owner.SignInWithDevSsoAsync(ownerEmail, Ct);
            await ReadAsync(await owner.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);
            await owner.ConnectSandboxCalendarAsync(Ct);

            using (var invited = await owner.PostAsync("/v1/organization/invitations", new { email = recruiterEmail, role = "Recruiter" }, Ct))
            {
                invited.StatusCode.Should().Be(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(Ct));
            }

            recruiter = new ApiSession(Fixture);
            var token = await Fixture.Mailpit.WaitForInvitationTokenAsync(recruiterEmail, Ct);
            using (var accepted = await recruiter.PostAsync("/v1/auth/invitations/accept", new { token }, Ct))
            {
                accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync(Ct));
            }

            await recruiter.ConnectSandboxCalendarAsync(Ct);

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
                $"/v1/jobs/{job.GetProperty("id").GetGuid()}/applications", new { candidate = new { fullName = "Riley Chen", email = candidate } }, Ct), HttpStatusCode.Created);
            var applicationId = application.GetProperty("id").GetGuid();

            var now = DateTimeOffset.UtcNow;
            var interviewers = new[] { await UserIdAsync(owner), await UserIdAsync(recruiter) };
            await ReadAsync(await owner.PostAsync(
                $"/v1/applications/{applicationId}/interviews",
                new { interviewerUserIds = interviewers, durationMinutes = 30, windowStart = now, windowEnd = now.AddDays(14), timeZone = "UTC" },
                Ct), HttpStatusCode.Created);

            var invitation = await WaitForEmailAsync(candidate, "Pick a time");
            var link = InterviewLink().Match(invitation.Text);
            link.Success.Should().BeTrue($"the invitation carries the link: {invitation.Text}");
            return new Scene(owner, recruiter, applicationId, candidate, slug, jobTitle, link.Value, link.Groups["token"].Value);
        }
        catch
        {
            recruiter?.Dispose();
            owner.Dispose();
            throw;
        }
    }

    private static async Task<Guid> UserIdAsync(ApiSession session) =>
        (await session.CurrentUserAsync(Ct))!.Value.GetProperty("id").GetGuid();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    private sealed record Scene(ApiSession Owner, ApiSession Recruiter, Guid ApplicationId, string Candidate, string Slug, string JobTitle, string Link, string Token) : IDisposable
    {
        /// <summary>The application's newest interview, as the recruiter sees it.</summary>
        public async Task<JsonElement> Interview(CancellationToken cancellationToken)
        {
            var interviews = await ReadAsync(await Owner.Client.GetAsync($"/v1/applications/{ApplicationId}/interviews", cancellationToken), HttpStatusCode.OK);
            return interviews.EnumerateArray().First();
        }

        public void Dispose()
        {
            Recruiter.Dispose();
            Owner.Dispose();
        }
    }

    [GeneratedRegex(@"https?://\S+/interviews/(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex InterviewLink();
}
