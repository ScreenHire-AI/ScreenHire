using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// A candidate's data request end to end (docs/architecture/data-requests.md), with JavaScript off: from "Your choices" on the
/// privacy page to the data request page, an address that applied gets a link by email and an address that did not gets the
/// same answer on the page, the link opens the two choices, and choosing a copy lands on a confirmation that carries no link.
/// The organization, its job and the candidate's application are made through the API. The stack has no bot check
/// configured, so the form posts without one.
/// </summary>
public sealed partial class DataRequestTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string SentText = "If we have an application from that address, we have emailed a link.";

    private static readonly string[] Britain = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_candidate_asks_for_a_copy_of_their_data_from_the_emailed_link_and_an_unknown_address_gets_the_same_answer()
    {
        var owner = ApiSession.NewEmail("e2e-data-request");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        var slug = $"test-{Guid.NewGuid():N}";
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);
        var jobId = await PublishedJobAsync(session);
        var candidate = ApiSession.NewEmail("candidate");
        await ApplyAsync(slug, jobId, candidate);

        await WithoutJavaScriptAsync();

        // The privacy page's "Your choices" links to the form.
        await GoToAsync("careers", $"/{slug}/privacy");
        await Page.GetByRole(AriaRole.Link, new() { Name = "the page for data requests" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/data-request$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Ask about your data");
        (await Page.Locator("script:not([type='application/ld+json'])").CountAsync()).Should().Be(0, "the page ships no JavaScript without a bot check");
        await ExpectNoAccessibilityViolationsAsync();

        // The address that applied gets the fixed answer, and the address is not in the page's own address.
        await Page.GetByLabel("Email address").FillAsync(candidate);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Email me a link" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/data-request/sent$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Check your email");
        await Expect(Page.GetByText(SentText)).ToBeVisibleAsync();
        Page.Url.Should().NotContain(Uri.EscapeDataString(candidate), "the confirmation's address carries no email address");
        await ExpectNoAccessibilityViolationsAsync();

        // The link in the email opens the two choices.
        var link = await DataRequestLinkAsync(candidate);
        await Page.GotoAsync(link.Value);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Ask about your data");
        await Expect(Page.GetByLabel("A copy of my data")).Not.ToBeCheckedAsync();
        await Expect(Page.GetByText("Erasure cannot be undone", new() { Exact = false })).ToBeVisibleAsync();
        (await Page.Locator("script:not([type='application/ld+json'])").CountAsync()).Should().Be(0, "the page ships no JavaScript");
        await ExpectNoAccessibilityViolationsAsync();

        await Page.GetByLabel("A copy of my data").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send my request" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/data-request/received$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("We have your request");
        Page.Url.Should().NotContain(link.Groups["token"].Value, "the confirmation's address carries no token");
        await ExpectNoAccessibilityViolationsAsync();

        // The team sees one open request for a copy.
        var requests = await ReadAsync(await session.Client.GetAsync("/v1/data-requests", Ct), HttpStatusCode.OK);
        var items = requests.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("kind").GetString().Should().Be("Copy");
        items[0].GetProperty("status").GetString().Should().Be("Open");

        // An address nobody applied with gets the very same page.
        await GoToAsync("careers", $"/{slug}/data-request");
        await Page.GetByLabel("Email address").FillAsync(ApiSession.NewEmail("nobody"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Email me a link" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/data-request/sent$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Check your email");
        await Expect(Page.GetByText(SentText)).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task A_link_that_is_not_valid_says_so_and_offers_a_new_one()
    {
        var owner = ApiSession.NewEmail("e2e-data-request-expired");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        var slug = $"test-{Guid.NewGuid():N}";
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);

        await WithoutJavaScriptAsync();
        var response = await OpenAsync("careers", $"/{slug}/data-request/{new string('a', 40)}");
        response.Status.Should().Be(404);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("This link has expired or is not valid");
        await ExpectNoAccessibilityViolationsAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Ask for a new link" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/data-request$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Ask about your data");
    }

    private async Task ApplyAsync(string slug, Guid jobId, string email)
    {
        using var anonymous = Fixture.CreateHttpClient("api");
        using var applied = await anonymous.PostAsJsonAsync($"/v1/public/organizations/{slug}/jobs/{jobId}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["firstName"] = "Casey", ["lastName"] = "Morgan", ["email"] = email },
            ["privacyNoticeVersion"] = "2026-11-05.1",
            ["answers"] = new JsonArray(),
            ["aiUseDeclaration"] = "DidNotUse",
        }, Ct);
        applied.StatusCode.Should().Be(HttpStatusCode.Created, await applied.Content.ReadAsStringAsync(Ct));
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

    // The email that carries the link (the candidate may have others, such as the receipt for an application). Polls the
    // inbox until it is there.
    private async Task<Match> DataRequestLinkAsync(string email)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        while (true)
        {
            var link = (await Fixture.Mailpit.MessagesAsync(email, Ct)).Select(message => DataRequestLink().Match(message.Text)).FirstOrDefault(match => match.Success);
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
                throw new TimeoutException("The data request link did not reach the candidate's inbox within 60 s.");
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

    [GeneratedRegex(@"https?://\S+/data-request/(?<token>[A-Za-z0-9_-]{16,})")]
    private static partial Regex DataRequestLink();
}
