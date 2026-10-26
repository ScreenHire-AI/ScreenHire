using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// The email composer and the Messages tab in a real browser (design system section 21, Candidate): an owner starts from a template, changes
/// its subject and body for one candidate, previews it with the candidate's first name and sends it. Mailpit and the
/// candidate's Messages show the changed email, and the template stays as it was. A blank email goes from the Messages
/// box. A candidate added with one name, as every candidate from before the name was split, still shows it whole. The
/// test signs in as a new person with a new organization and adds its job, template and candidates through the API.
/// </summary>
public sealed class EmailComposerTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string TemplateSubject = "Interview for {{ jobTitle }}";
    private const string TemplateBody = "Hi {{ firstName }},\n\nWe would like to meet you.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_owner_changes_a_template_for_one_send_and_messages_shows_the_email_as_it_went()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var scene = await SceneAsync();
        await SignInAsync(scene.Owner);

        // A candidate with one name, the way every candidate from before the split is kept, shows all of it.
        await Page.GotoAsync(DashboardUrl($"/applications/{scene.Riley}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Riley Chen");

        // From the candidate's header: start from the template and change it for this email.
        await Page.GotoAsync(DashboardUrl($"/applications/{scene.Casey}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Casey Morgan");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Email", Exact = true }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Email Casey Morgan" });
        await sheet.GetByLabel("Start from").SelectOptionAsync(new SelectOptionValue { Label = "Interview invitation" });
        await Expect(sheet.GetByLabel("Subject")).ToHaveValueAsync(TemplateSubject);
        await sheet.GetByLabel("Subject").FillAsync($"Interview {marker} for {{{{ jobTitle }}}}");
        await sheet.GetByLabel("Message").FillAsync($"Hi {{{{ firstName }}}},\n\nYour portfolio stood out {marker}.");
        await Expect(sheet.GetByText("Changed for this email only. The Interview invitation template stays as it is.")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // The preview is the email with the candidate's own details.
        await sheet.GetByRole(AriaRole.Group, new() { Name = "View" }).GetByRole(AriaRole.Button, new() { Name = "Preview" }).ClickAsync();
        await Expect(sheet.Locator("[data-email-subject]")).ToHaveTextAsync($"Interview {marker} for {scene.Title}");
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Plain text" }).ClickAsync();
        await Expect(sheet.Locator("[data-email-text]")).ToContainTextAsync("Hi Casey,");
        await ExpectNoAccessibilityViolationsAsync();
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
        await Expect(sheet).ToBeHiddenAsync();

        var sent = await Fixture.Mailpit.WaitForMessagesAsync(scene.CaseyEmail, 1, Ct);
        sent.Should().ContainSingle();
        sent[0].Subject.Should().Be($"Interview {marker} for {scene.Title}");
        sent[0].Text.Should().Contain("Hi Casey,").And.Contain($"Your portfolio stood out {marker}.");

        // The template is as it was.
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(scene.Owner, Ct);
            var template = await ReadAsync(await session.Client.GetAsync($"/v1/email-templates/{scene.TemplateId}", Ct), HttpStatusCode.OK);
            template.GetProperty("subject").GetString().Should().Be(TemplateSubject);
            template.GetProperty("body").GetString().Should().Be(TemplateBody);
        }

        // Messages shows the email as it went, once the worker has sent it.
        await Page.GotoAsync(DashboardUrl($"/applications/{scene.Casey}?tab=messages"));
        var messages = Page.GetByRole(AriaRole.Region, new() { Name = "Messages", Exact = true });
        var row = messages.GetByRole(AriaRole.List, new() { Name = "Emails" }).GetByRole(AriaRole.Button).Filter(new() { HasText = $"Interview {marker}" });
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await row.ClickAsync();
        await Expect(row).ToHaveAttributeAsync("aria-expanded", "true");
        // The row names the subject; the opened email does not repeat it (W4).
        await Expect(row).ToContainTextAsync($"Interview {marker} for {scene.Title}");
        await Expect(messages.FrameLocator("iframe[title='Email preview']").Locator("body")).ToContainTextAsync($"Your portfolio stood out {marker}.");
        await Expect(messages).Not.ToContainTextAsync("repl", new() { IgnoreCase = true });
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task A_blank_email_goes_from_the_messages_box()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var scene = await SceneAsync();
        await SignInAsync(scene.Owner);

        await Page.GotoAsync(DashboardUrl($"/applications/{scene.Casey}?tab=messages"));
        var box = Page.GetByRole(AriaRole.Form, new() { Name = "Email Casey Morgan" });
        await Expect(box.GetByLabel("Start from")).ToHaveValueAsync("");
        await box.GetByLabel("Subject").FillAsync($"A quick question {marker}");
        await box.GetByLabel("Message").FillAsync("Hi {{ firstName }}, are you free on Tuesday?");
        await ExpectNoAccessibilityViolationsAsync();
        await box.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Sending" }).First).ToHaveTextAsync("Sending 1 email.");
        await Expect(box.GetByLabel("Subject")).ToHaveValueAsync("");

        var sent = await Fixture.Mailpit.WaitForMessagesAsync(scene.CaseyEmail, 1, Ct);
        sent.Should().ContainSingle();
        sent[0].Subject.Should().Be($"A quick question {marker}");
        sent[0].Text.Should().Contain("Hi Casey, are you free on Tuesday?");

        // The email appears in Messages without a reload, once the worker has sent it.
        var messages = Page.GetByRole(AriaRole.Region, new() { Name = "Messages", Exact = true });
        await Expect(messages.GetByRole(AriaRole.List, new() { Name = "Emails" })).ToContainTextAsync($"A quick question {marker}", new() { Timeout = 30_000 });
    }

    private sealed record Scene(string Owner, Guid JobId, string Title, string TemplateId, Guid Casey, string CaseyEmail, Guid Riley);

    // A new person and organization with one job, an "Interview invitation" template, Casey Morgan named in two parts and
    // Riley Chen added with the older single fullName, all through the API as that person.
    private async Task<Scene> SceneAsync()
    {
        var email = ApiSession.NewEmail("e2e-composer");
        var title = $"Studio lead {Guid.NewGuid().ToString("N")[..4]}";
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        await ReadAsync(
            await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct),
            HttpStatusCode.OK);

        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new { title, description = "<p>Lead the studio.</p>", employmentType = "FullTime", workplaceType = "OnSite" },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();
        var template = await ReadAsync(
            await session.PostAsync("/v1/email-templates", new { name = "Interview invitation", subject = TemplateSubject, body = TemplateBody }, Ct),
            HttpStatusCode.Created);

        var caseyEmail = ApiSession.NewEmail("casey");
        var casey = await ReadAsync(await session.PostAsync(
            $"/v1/jobs/{jobId}/applications", new { candidate = new { firstName = "Casey", lastName = "Morgan", email = caseyEmail } }, Ct), HttpStatusCode.Created);
        var riley = await ReadAsync(await session.PostAsync(
            $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = "Riley Chen", email = ApiSession.NewEmail("riley") } }, Ct), HttpStatusCode.Created);

        return new Scene(email, jobId, title, template.GetProperty("id").GetString()!, casey.GetProperty("id").GetGuid(), caseyEmail, riley.GetProperty("id").GetGuid());
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
