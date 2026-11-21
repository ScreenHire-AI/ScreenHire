using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// A candidate's own page end to end (docs/architecture/candidate-portal.md), with JavaScript off: from the footer of a careers
/// page to the form, an address that applied gets a link by email and an address that did not gets the same answer on the page,
/// the link opens the application on its four steps, and after the team writes the candidate reads the message and answers it
/// from the page, with two files in the plain input and then, with JavaScript on, with the files the list left after one was
/// removed, which the team then sees as two replies with their files. The organization, its job and the application are made through the API.
/// The stack has no bot check configured, so the form posts without one.
/// </summary>
public sealed partial class CandidatePortalTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string SentText = "If we have an application from that address, we have emailed a link to your page.";

    private static readonly string[] Britain = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_candidate_gets_a_link_sees_where_their_application_stands_and_answers_the_team_from_the_page()
    {
        var owner = ApiSession.NewEmail("e2e-portal");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        var slug = $"test-{Guid.NewGuid():N}";
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { name = "Riley Harper", organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);
        var jobId = await PublishedJobAsync(session);
        var candidate = ApiSession.NewEmail("candidate");
        var applicationId = await ApplyAsync(slug, jobId, candidate);

        await WithoutJavaScriptAsync();

        // Every careers page links to the form from its footer.
        await GoToAsync("careers", $"/{slug}/privacy");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Your applications", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/my-applications$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Your applications");
        (await Page.Locator("script:not([type='application/ld+json'])").CountAsync()).Should().Be(0, "the page ships no JavaScript without a bot check");
        await ExpectNoAccessibilityViolationsAsync();

        // An address nobody applied with gets the very same answer, and nothing is sent to it.
        var stranger = ApiSession.NewEmail("nobody");
        await Page.GetByLabel("Email address").FillAsync(stranger);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Email me a link" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/my-applications/sent$"));
        await Expect(Page.GetByText(SentText)).ToBeVisibleAsync();
        Page.Url.Should().NotContain(Uri.EscapeDataString(stranger), "the confirmation's address carries no email address");

        // The address that applied gets that answer too, and then an email.
        await GoToAsync("careers", $"/{slug}/my-applications");
        await Page.GetByLabel("Email address").FillAsync(candidate);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Email me a link" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/my-applications/sent$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Check your email");
        await Expect(Page.GetByText(SentText)).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        (await Fixture.Mailpit.MessagesAsync(stranger, Ct)).Should().BeEmpty("an address that never applied gets nothing");

        var link = await PortalLinkAsync(candidate, subjectStart: "Your link to your applications at ");
        await Page.GotoAsync(link.Value);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Your applications");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 2 })).ToHaveTextAsync("Platform Engineer");
        var steps = Page.GetByRole(AriaRole.List, new() { Name = "Where your application is" }).GetByRole(AriaRole.Listitem);
        await Expect(steps).ToHaveCountAsync(4);
        await Expect(steps.First).ToHaveAttributeAsync("aria-current", "step");
        await Expect(Page.GetByText("No messages yet.")).ToBeVisibleAsync();
        (await Page.ContentAsync()).Should().NotContain("Screening", "the employer's own stage names never reach the page");
        (await Page.Locator("script:not([type='application/ld+json'])").CountAsync()).Should().Be(0, "the page ships no JavaScript");
        await ExpectNoAccessibilityViolationsAsync();

        // The team writes. The email ends with the candidate's link, and the page now offers the messages.
        var sentBefore = await SendsRecordedAsync(session, applicationId);
        await ReadAsync(await session.PostAsync(
            "/v1/emails",
            new { subject = "Next steps", body = "Hello, can we talk on Friday?", templateId = (string?)null, applicationIds = new[] { applicationId } },
            Ct), HttpStatusCode.Accepted);
        var footerLink = await PortalLinkAsync(candidate, subjectStart: "Next steps");
        await WaitForAnotherSendRecordAsync(session, applicationId, sentBefore);
        await Page.GotoAsync(footerLink.Value);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Read and answer your messages" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Platform Engineer");
        await Expect(Page.GetByText("Hello, can we talk on Friday?")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        var thread = Page.Url;

        // The candidate answers from the page, with two files in the plain input; a refresh after the redirect posts nothing again.
        await Page.GetByLabel("Your message").FillAsync("Friday works for me.");
        await Page.GetByLabel("Files").SetInputFilesAsync(
        [
            new FilePayload { Name = "availability.pdf", MimeType = "application/pdf", Buffer = ApplySteps.Pdf },
            new FilePayload { Name = "portfolio.pdf", MimeType = "application/pdf", Buffer = ApplySteps.Pdf },
        ]);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send message" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?sent=1$"));
        await Expect(Page.GetByText("Your message was sent.")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Friday works for me.")).ToBeVisibleAsync();
        await Page.ReloadAsync();
        await Expect(Page.GetByText("Friday works for me.")).ToHaveCountAsync(1);
        await ExpectNoAccessibilityViolationsAsync();

        // The team sees one reply, as it sees an emailed one.
        var detail = await ReadAsync(await session.Client.GetAsync($"/v1/applications/{applicationId}", Ct), HttpStatusCode.OK);
        var candidateId = detail.GetProperty("candidate").GetProperty("id").GetGuid();
        var messages = await ReadAsync(await session.Client.GetAsync($"/v1/candidates/{candidateId}/emails", Ct), HttpStatusCode.OK);
        var replies = messages.EnumerateArray().Where(m => m.GetProperty("direction").GetString() == "Received").ToList();
        replies.Should().ContainSingle();
        (await AttachmentNamesAsync(session, replies[0].GetProperty("id").GetGuid())).Should().BeEquivalentTo("availability.pdf", "portfolio.pdf");

        // With JavaScript on, the files field lists each file with Remove. Three are chosen, one is taken off, and the two left are sent.
        await WithJavaScriptAsync();
        await ApplySteps.RecordBeaconsAsync(Page);
        await Page.GotoAsync(thread);
        await Page.Locator("astro-island:not([ssr])").WaitForAsync();
        await Page.GetByLabel("Your message").FillAsync("Here are the documents.");
        await Page.GetByLabel("Files").SetInputFilesAsync(
        [
            new FilePayload { Name = "references.pdf", MimeType = "application/pdf", Buffer = ApplySteps.Pdf },
            new FilePayload { Name = "draft.pdf", MimeType = "application/pdf", Buffer = ApplySteps.Pdf },
            new FilePayload { Name = "plan.pdf", MimeType = "application/pdf", Buffer = ApplySteps.Pdf },
        ]);
        var chosen = Page.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "Remove" });
        await Expect(chosen).ToHaveCountAsync(3);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove draft.pdf" }).ClickAsync();
        await Expect(chosen).ToHaveCountAsync(2);
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send message" }).ClickAsync();
        await Expect(Page.GetByText("Your message was sent.")).ToBeVisibleAsync();
        (await ApplySteps.TelemetryBeaconsAsync(Page.MainFrame)).Should().BeEmpty("the island only lists files: the page runs no telemetry or device code");

        // Both replies are on the dashboard, each with the files that were sent: the second has exactly the two the list kept.
        await SignInToDashboardAsync(owner);
        await Page.GotoAsync(new Uri(Fixture.GetEndpoint("dashboard"), $"/applications/{applicationId}?tab=messages").ToString());
        var emails = Page.GetByRole(AriaRole.Region, new() { Name = "Messages", Exact = true }).GetByRole(AriaRole.List, new() { Name = "Emails" });
        var replyRows = emails.GetByRole(AriaRole.Button).Filter(new() { HasText = "Reply from" });
        await Expect(replyRows).ToHaveCountAsync(2);
        var shown = new List<string>();
        for (var index = 0; index < 2; index++)
        {
            await replyRows.Nth(index).ClickAsync();
            var attachments = Page.GetByRole(AriaRole.List, new() { Name = "Attachments" });
            await Expect(attachments.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(2);
            shown.Add(await attachments.InnerTextAsync());
        }

        shown.Should().ContainSingle(text => text.Contains("availability.pdf") && text.Contains("portfolio.pdf"));
        shown.Should().ContainSingle(text => text.Contains("references.pdf") && text.Contains("plan.pdf") && !text.Contains("draft.pdf"));
    }

    private async Task SignInToDashboardAsync(string email)
    {
        await Page.GotoAsync(new Uri(Fixture.GetEndpoint("dashboard"), "/sign-in").ToString());
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private static async Task<string[]> AttachmentNamesAsync(ApiSession session, Guid replyId)
    {
        var reply = await ReadAsync(await session.Client.GetAsync($"/v1/replies/{replyId}", Ct), HttpStatusCode.OK);
        return [.. reply.GetProperty("attachments").EnumerateArray().Select(file => file.GetProperty("fileName").GetString()!)];
    }

    [Fact]
    public async Task A_link_that_is_not_valid_says_so_and_offers_a_new_one()
    {
        var owner = ApiSession.NewEmail("e2e-portal-expired");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        var slug = $"test-{Guid.NewGuid():N}";
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { name = "Riley Harper", organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);

        await WithoutJavaScriptAsync();
        var response = await OpenAsync("careers", $"/{slug}/my-applications/{new string('a', 40)}");
        response.Status.Should().Be(404);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("This link has expired or is not valid");
        await ExpectNoAccessibilityViolationsAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Ask for a new link" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/my-applications$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Your applications");
    }

    private async Task<Guid> ApplyAsync(string slug, Guid jobId, string email)
    {
        using var anonymous = Fixture.CreateHttpClient("api");
        using var applied = await anonymous.PostAsJsonAsync($"/v1/public/organizations/{slug}/jobs/{jobId}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["firstName"] = "Casey", ["lastName"] = "Morgan", ["email"] = email },
            ["privacyNoticeVersion"] = "2026-11-07.1",
            ["answers"] = new JsonArray(),
            ["aiUseDeclaration"] = "DidNotUse",
        }, Ct);
        applied.StatusCode.Should().Be(HttpStatusCode.Created, await applied.Content.ReadAsStringAsync(Ct));
        return (await applied.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("applicationId").GetGuid();
    }

    private static async Task<Guid> PublishedJobAsync(ApiSession session)
    {
        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title = "Platform Engineer",
                description = "<p>Build and run the things people rely on.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Britain,
            },
            Ct), HttpStatusCode.Created);
        var id = job.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/jobs/{id}/publish", new { }, Ct), HttpStatusCode.OK);
        return id;
    }

    // The worker records a send after the mail goes out, and the page offers the messages from that record, so the inbox can
    // be a moment ahead of the page, which has no script to catch up.
    private static Task WaitForAnotherSendRecordAsync(ApiSession session, Guid applicationId, int sentBefore) =>
        Wait.UntilAsync(async () => await SendsRecordedAsync(session, applicationId) > sentBefore, "the send to be recorded", Ct);

    private static async Task<int> SendsRecordedAsync(ApiSession session, Guid applicationId)
    {
        var timeline = await ReadAsync(await session.Client.GetAsync($"/v1/applications/{applicationId}/timeline", Ct), HttpStatusCode.OK);
        return timeline.EnumerateArray().Count(entry => entry.GetProperty("type").GetString() == "EmailSent");
    }

    // The link in the email whose subject starts with the given text (the candidate may have others, such as the receipt for an
    // application). Polls the inbox until it is there.
    private Task<Match> PortalLinkAsync(string email, string subjectStart) =>
        Wait.ForAsync(
            async () => (await Fixture.Mailpit.MessagesAsync(email, Ct))
                .Where(message => message.Subject.StartsWith(subjectStart, StringComparison.Ordinal))
                .Select(message => PortalLink().Match(message.Text))
                .FirstOrDefault(match => match.Success),
            "the portal link to reach the candidate's inbox",
            Ct);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    [GeneratedRegex(@"https?://\S+/my-applications/(?<token>[A-Za-z0-9_-]{16,})")]
    private static partial Regex PortalLink();
}
