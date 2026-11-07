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
/// from the page, which the team then sees as a reply. The organization, its job and the application are made through the API.
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
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);
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
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 3 })).ToHaveTextAsync("Platform Engineer");
        var steps = Page.GetByRole(AriaRole.List, new() { Name = "Where your application is" }).GetByRole(AriaRole.Listitem);
        await Expect(steps).ToHaveCountAsync(4);
        await Expect(steps.First).ToHaveAttributeAsync("aria-current", "step");
        await Expect(Page.GetByText("No messages yet.")).ToBeVisibleAsync();
        (await Page.ContentAsync()).Should().NotContain("Screening").And.NotContain("Offer", "the employer's own stage names never reach the page");
        (await Page.Locator("script:not([type='application/ld+json'])").CountAsync()).Should().Be(0, "the page ships no JavaScript");
        await ExpectNoAccessibilityViolationsAsync();

        // The team writes. The email ends with the candidate's link, and the page now offers the messages.
        await ReadAsync(await session.PostAsync(
            "/v1/emails",
            new { subject = "Next steps", body = "Hello, can we talk on Friday?", templateId = (string?)null, applicationIds = new[] { applicationId } },
            Ct), HttpStatusCode.Accepted);
        var footerLink = await PortalLinkAsync(candidate, subjectStart: "Next steps");
        await Page.GotoAsync(footerLink.Value);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Read and answer your messages" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Platform Engineer");
        await Expect(Page.GetByText("Hello, can we talk on Friday?")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // The candidate answers from the page; a refresh after the redirect posts nothing again.
        await Page.GetByLabel("Your message").FillAsync("Friday works for me.");
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
        messages.EnumerateArray().Count(m => m.GetProperty("direction").GetString() == "Received").Should().Be(1);
    }

    [Fact]
    public async Task A_link_that_is_not_valid_says_so_and_offers_a_new_one()
    {
        var owner = ApiSession.NewEmail("e2e-portal-expired");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        var slug = $"test-{Guid.NewGuid():N}";
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);

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

    // The link in the email whose subject starts with the given text (the candidate may have others, such as the receipt for an
    // application). Polls the inbox until it is there.
    private async Task<Match> PortalLinkAsync(string email, string subjectStart)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        while (true)
        {
            var link = (await Fixture.Mailpit.MessagesAsync(email, Ct))
                .Where(message => message.Subject.StartsWith(subjectStart, StringComparison.Ordinal))
                .Select(message => PortalLink().Match(message.Text))
                .FirstOrDefault(match => match.Success);
            if (link is not null)
            {
                return link;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token);
            }
            catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
            {
                throw new TimeoutException("The portal link did not reach the candidate's inbox within 60 s.");
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

    [GeneratedRegex(@"https?://\S+/my-applications/(?<token>[A-Za-z0-9_-]{16,})")]
    private static partial Regex PortalLink();
}
