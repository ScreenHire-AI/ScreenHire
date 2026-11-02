using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// A candidate's reply in a real browser (docs/architecture/email.md, "Replies"), delivered through the sandbox route in place
/// of SendGrid: an owner emails a candidate, the email's Reply-To address takes the answer, and while the owner has the
/// candidate's page open the reply's card appears in the tray; Messages then lists it beside the email sent, opening to the
/// message, and Activity says the candidate replied. A bounce reported by the provider shows on the candidate's page and
/// hides the email box until the owner allows email again. The test signs in as a new person with a new organization and
/// adds its job and candidate through the API as that person.
/// </summary>
public sealed class CandidateRepliesTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reply_shows_as_a_card_in_Messages_and_in_Activity()
    {
        var scene = await SceneAsync();
        await SignInAsync(scene.Owner);
        await Page.GotoAsync(DashboardUrl($"/applications/{scene.ApplicationId}?tab=messages"));
        var messages = Page.GetByRole(AriaRole.Region, new() { Name = "Messages", Exact = true });
        await Expect(messages).ToBeVisibleAsync();

        // The owner emails the candidate through the API; the email in Mailpit holds the address replies go to.
        await SendEmailAsync(scene, "Next steps", "<p>Can you talk on Thursday?</p>");
        var sent = (await Fixture.Mailpit.WaitForMessagesAsync(scene.CandidateEmail, 1, Ct)).Single();
        sent.ReplyTo.Should().NotBeNull("every email about an application carries its reply address");
        sent.ReplyTo!.Address.Should().StartWith("reply+");
        // A reply is taken only for an email that is on the timeline, which the worker commits just after it sends.
        await LatestEmailIdAsync(scene);

        // The candidate answers. With the page open, the tray gets a card for the person who sent the email.
        using (var session = scene.Session)
        {
            using var delivered = await session.PostAsync(
                "/v1/dev/inbound-email",
                new { to = sent.ReplyTo.Address, from = scene.CandidateEmail, fromName = "Casey Morgan", subject = "Re: Next steps", text = "Thursday works for me." },
                Ct);
            delivered.StatusCode.Should().Be(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync(Ct));
            (await delivered.Content.ReadAsStringAsync(Ct)).Should().Contain("Stored");
        }

        await Expect(Page.GetByText("Replied to your email.")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // Messages lists the reply with the email sent, and opens to the message.
        var list = messages.GetByRole(AriaRole.List, new() { Name = "Emails" });
        var reply = list.GetByRole(AriaRole.Button).Filter(new() { HasText = "Re: Next steps" });
        await Expect(reply).ToContainTextAsync($"Reply from {scene.CandidateEmail}");
        await Expect(list.GetByRole(AriaRole.Button).Filter(new() { HasText = "Next steps" })).ToHaveCountAsync(2);
        await reply.ClickAsync();
        await Expect(messages.GetByText($"Casey Morgan <{scene.CandidateEmail}>")).ToBeVisibleAsync();
        await Expect(messages.FrameLocator("iframe[title='Email preview']").Locator("body")).ToContainTextAsync("Thursday works for me.");
        await SaveScreenshotAsync(Page, "dashboard-candidate-messages-reply-1440");

        // Activity records that the candidate replied, with nobody named.
        await Page.GotoAsync(DashboardUrl($"/applications/{scene.ApplicationId}?tab=activity"));
        await Expect(Page.GetByText("Candidate replied \"Re: Next steps\"")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_bounce_flags_the_address_hides_the_email_box_and_Allow_email_again_clears_it()
    {
        var scene = await SceneAsync();
        await SignInAsync(scene.Owner);
        await SendEmailAsync(scene, "Welcome", "<p>Welcome aboard.</p>");
        var sent = (await Fixture.Mailpit.WaitForMessagesAsync(scene.CandidateEmail, 1, Ct)).Single();
        sent.Subject.Should().Be("Welcome");

        // SendGrid's signed event webhook reports the bounce for that email.
        var emailId = await LatestEmailIdAsync(scene);
        var body = JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object>
            {
                ["event"] = "bounce", ["type"] = "bounce", ["email"] = scene.CandidateEmail, ["reason"] = "550 5.1.1 mailbox not found",
                ["organizationId"] = scene.OrganizationId, ["emailId"] = emailId,
            },
        });
        var (signature, timestamp) = EmailEvents.Sign(body, DateTimeOffset.UtcNow);
        using (var client = Fixture.CreateHttpClient("api"))
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/v1/webhooks/email/events") { Content = new StringContent(body) })
        {
            request.Headers.Add(EmailEvents.SignatureHeader, signature);
            request.Headers.Add(EmailEvents.TimestampHeader, timestamp);
            using var response = await client.SendAsync(request, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // The candidate's page says so beside the name, explains what it means, and offers no email box.
        await Page.GotoAsync(DashboardUrl($"/applications/{scene.ApplicationId}?tab=messages"));
        var band = Page.Locator("[data-slot=header-band]");
        await Expect(band).ToContainTextAsync("Email bounced");
        await Expect(Page.GetByText("ScreenHire does not email this address any more")).ToContainTextAsync("550 5.1.1 mailbox not found");
        await Expect(Page.GetByRole(AriaRole.Form, new() { NameRegex = new("^Email Casey Morgan") })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-candidate-email-bounced-1440");

        // Allowing email again takes the flag off, and the page offers the box again.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Allow email again" }).ClickAsync();
        await Expect(band).Not.ToContainTextAsync("Email bounced");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Allow email again" })).ToHaveCountAsync(0);
    }

    private sealed record Scene(string Owner, ApiSession Session, Guid OrganizationId, Guid ApplicationId, string CandidateEmail);

    // A new person and organization with one job and one candidate, created through the API as that person, so the browser
    // signs in to exactly this data. The session stays open for the test's own calls.
    private async Task<Scene> SceneAsync()
    {
        var email = ApiSession.NewEmail("e2e-replies");
        var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        var organizationId = await session.OnboardAsync(Ct);

        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title = $"Warehouse lead {Guid.NewGuid().ToString("N")[..4]}",
                description = "<p>Run the warehouse floor and the team on it.</p>",
                employmentType = "FullTime",
                workplaceType = "OnSite",
            },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();
        var candidateEmail = ApiSession.NewEmail("casey");
        var application = await ReadAsync(await session.PostAsync(
            $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = "Casey Morgan", email = candidateEmail } }, Ct), HttpStatusCode.Created);
        return new Scene(email, session, organizationId, application.GetProperty("id").GetGuid(), candidateEmail);
    }

    // A blank email to the candidate: the wording in the request, rendered for them in the API and sent by the worker.
    private static async Task SendEmailAsync(Scene scene, string subject, string body)
    {
        using var response = await scene.Session.PostAsync("/v1/emails", new { applicationIds = new[] { scene.ApplicationId }, subject, body }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
    }

    // The id of the one email to the candidate, as Messages lists it: the send the provider's event names.
    private static async Task<Guid> LatestEmailIdAsync(Scene scene)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var response = await scene.Session.Client.GetAsync($"/v1/applications/{scene.ApplicationId}", Ct);
            var application = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            var candidateId = application.GetProperty("candidate").GetProperty("id").GetGuid();
            using var listed = await scene.Session.Client.GetAsync($"/v1/candidates/{candidateId}/emails", Ct);
            var emails = await listed.Content.ReadFromJsonAsync<JsonElement>(Ct);
            if (emails.GetArrayLength() > 0)
            {
                return emails[0].GetProperty("id").GetGuid();
            }

            await Task.Delay(100, Ct);
        }

        throw new TimeoutException("The email never appeared in Messages.");
    }

    private async Task SignInAsync(string email)
    {
        var response = await Page.GotoAsync(DashboardUrl("/sign-in"));
        response!.Ok.Should().BeTrue();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }
}
