using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// "Tell us when you're free" end to end (docs/architecture/interview-scheduling.md): a recruiter chooses it in the Schedule
/// interview sheet, the candidate opens the emailed link with JavaScript off and ticks times on more than one day, the
/// recruiter's Interview panel offers those times with a Book button, and the candidate's page then shows the booked time
/// while the confirmation email carries an invite.ics. What the endpoints do with the times is the integration tests' job;
/// this is what each person sees. One organization, set up through the API the way the dashboard does.
/// </summary>
public sealed partial class CandidateSuggestsTimesTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] Britain = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_candidate_sends_times_across_two_days_without_javascript_and_the_recruiter_books_one()
    {
        var ownerEmail = ApiSession.NewEmail("e2e-suggest");
        var candidate = ApiSession.NewEmail("candidate");
        using var owner = new ApiSession(Fixture);
        await owner.SignInWithDevSsoAsync(ownerEmail, Ct);
        await ReadAsync(await owner.PostAsync("/v1/auth/onboarding", new { name = "Riley Harper", organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);
        await owner.ConnectSandboxCalendarAsync(Ct);
        var job = await ReadAsync(await owner.PostAsync(
            "/v1/jobs",
            new
            {
                title = $"Support lead {Guid.NewGuid().ToString("N")[..4]}",
                description = "<p>Help customers and the team that looks after them.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Britain,
            },
            Ct), HttpStatusCode.Created);
        var application = await ReadAsync(await owner.PostAsync(
            $"/v1/jobs/{job.GetProperty("id").GetGuid()}/applications", new { candidate = new { firstName = "Riley", lastName = "Chen", email = candidate } }, Ct), HttpStatusCode.Created);
        var applicationUrl = new Uri(Fixture.GetEndpoint("dashboard"), $"/applications/{application.GetProperty("id").GetGuid()}").ToString();

        // The recruiter asks when the candidate is free: the way is chosen in the sheet and nothing else is needed.
        await Page.SetViewportSizeAsync(1280, 900);
        await SignInAsync(ownerEmail);
        await Page.GotoAsync(applicationUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Riley Chen");
        await Page.Locator("[data-slot=header-band]").GetByRole(AriaRole.Button, new() { Name = "Schedule interview" }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Schedule interview" });
        var choice = sheet.GetByRole(AriaRole.Radiogroup, new() { Name = "How should Riley pick a time?" });
        await choice.GetByRole(AriaRole.Radio, new() { Name = "Tell us when you're free" }).CheckAsync();
        await Expect(sheet.GetByText("Hours they may pick from")).ToBeVisibleAsync();
        await sheet.GetByLabel("Time zone").SelectOptionAsync("UTC");
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
        await Expect(sheet).ToHaveCountAsync(0);
        var panel = Page.GetByRole(AriaRole.Region, new() { Name = "Interview", Exact = true });
        await Expect(panel).ToContainTextAsync("Waiting for the candidate");
        await Expect(panel).ToContainTextAsync("Asked when they are free");

        // The candidate opens the emailed link with JavaScript off and ticks three times, the third on another day.
        var invitation = await WaitForEmailAsync(candidate, "When could you do your");
        var link = InterviewLink().Match(invitation.Text);
        link.Success.Should().BeTrue($"the invitation carries the link: {invitation.Text}");
        await WithoutJavaScriptAsync();
        var response = await Page.GotoAsync(link.Value);
        response!.Status.Should().Be(200);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(new Regex("^When are you free for your "));
        (await Page.Locator("script:not([type='application/ld+json'])").CountAsync()).Should().Be(0, "the page ships no JavaScript");
        var days = Page.Locator("form[data-suggest] details");
        (await days.CountAsync()).Should().BeGreaterThan(1);
        await Expect(days.First).ToHaveAttributeAsync("open", "");
        foreach (var (width, height) in new[] { (390, 844), (1280, 900) })
        {
            await Page.SetViewportSizeAsync(width, height);
            var form = await Page.Locator("form[data-suggest]").BoundingBoxAsync();
            (form!.X + form.Width).Should().BeLessThanOrEqualTo(width, $"the form fits {width}px without sideways scrolling");
            foreach (var chip in await days.First.Locator("label").AllAsync())
            {
                var box = await chip.BoundingBoxAsync();
                (box!.X + box.Width).Should().BeLessThanOrEqualTo(width, $"a time chip fits {width}px");
                box.Height.Should().BeGreaterThanOrEqualTo(44, "touch targets are at least 44px tall");
            }

            await ExpectNoAccessibilityViolationsAsync();
            await SaveScreenshotAsync(Page, $"candidate-suggests-times-{width}");
        }

        var picked = new List<string>();
        var firstDay = days.First.Locator("label");
        foreach (var index in new[] { 0, 2 })
        {
            picked.Add((await firstDay.Nth(index).InnerTextAsync()).Trim());
            await firstDay.Nth(index).ClickAsync();
        }

        var other = days.Nth(1);
        await other.Locator("summary").ClickAsync(); // only the first day starts open
        var otherDay = (await other.Locator("legend").TextContentAsync())!.Trim();
        var otherSlot = other.Locator("label").Nth(1);
        var thirdLabel = (await otherSlot.InnerTextAsync()).Trim();
        await otherSlot.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send these times" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("We've got your times");
        var sent = Page.GetByRole(AriaRole.Region, new() { Name = "Your times" });
        await Expect(sent.Locator("li")).ToHaveCountAsync(3);
        await Expect(sent).ToContainTextAsync($"{otherDay} at {thirdLabel}");
        foreach (var label in picked)
        {
            await Expect(sent).ToContainTextAsync($" at {label}");
        }

        await ExpectNoAccessibilityViolationsAsync();

        // The recruiter's Interview panel now offers the three times, each with a Book button, and books the second.
        await WithJavaScriptAsync();
        await Page.SetViewportSizeAsync(1280, 900);
        await SignInAsync(ownerEmail);
        await Page.GotoAsync(applicationUrl);
        panel = Page.GetByRole(AriaRole.Region, new() { Name = "Interview", Exact = true });
        await Expect(panel).ToContainTextAsync("Times to book", new() { Timeout = 30_000 });
        var offered = panel.GetByRole(AriaRole.List, new() { Name = "Times Riley is free" });
        await Expect(offered.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(3);
        await Expect(offered.GetByRole(AriaRole.Button, new() { Name = "Book" })).ToHaveCountAsync(3);
        await Expect(panel.GetByRole(AriaRole.Button, new() { Name = "Ask for more times" })).ToBeVisibleAsync();
        await Page.Mouse.MoveAsync(0, 0);
        await ExpectNoAccessibilityViolationsAsync();
        var booking = offered.GetByRole(AriaRole.Listitem).Nth(1);
        var bookedTime = (await booking.Locator("time").InnerTextAsync()).Trim();
        await booking.GetByRole(AriaRole.Button, new() { Name = "Book" }).ClickAsync();
        await Expect(panel).ToContainTextAsync("Booked", new() { Timeout = 30_000 });
        await Expect(panel.Locator("time")).ToHaveCountAsync(1);
        await Expect(panel.Locator("time")).ToHaveTextAsync(bookedTime);
        await Expect(panel).Not.ToContainTextAsync("Times to book");

        // The candidate's page now shows the booked time, and the confirmation carries a calendar invitation.
        await WithoutJavaScriptAsync();
        await Page.GotoAsync(link.Value);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Your interview is booked");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Your time" })).ToContainTextAsync(picked[1]);
        await ExpectNoAccessibilityViolationsAsync();
        var confirmation = await WaitForEmailAsync(candidate, "is set");
        confirmation.Files.Should().Contain(file => file.FileName == "invite.ics" && file.Content.Contains("BEGIN:VCALENDAR", StringComparison.Ordinal));
    }

    private Task<MailpitMessage> WaitForEmailAsync(string address, string subject) =>
        Wait.ForAsync(
            async () => (await Fixture.Mailpit.MessagesAsync(address, Ct)).FirstOrDefault(m => m.Subject.Contains(subject, StringComparison.Ordinal)),
            $"an email about \"{subject}\" to reach the candidate",
            Ct);

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

    [GeneratedRegex(@"https?://\S+/interviews/(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex InterviewLink();
}
