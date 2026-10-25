using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// Email templates and email to candidates in a real browser: an owner rewords a built-in template in Settings, sees the
/// preview follow the wording, then emails three candidates from a selection on the job's pipeline, and each email
/// arrives in Mailpit in the new wording. The test signs in as a new person with a new organization and adds its job and
/// applications through the API as that person.
/// </summary>
public sealed class EmailTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] Candidates = ["Casey Morgan", "Riley Chen", "Sam Taylor"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_owner_edits_a_template_previews_it_and_emails_three_candidates_from_a_selection()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var (owner, jobId, title, addresses) = await SceneAsync();
        await SignInAsync(owner);

        // Settings: reword the built-in "Application received" email.
        await Page.GotoAsync(DashboardUrl("/settings/email-templates"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Email templates");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Application received" }).First.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Application received");
        var subject = Page.GetByLabel("Subject");
        await subject.FillAsync($"Welcome {marker}, {{{{ candidateName }}}}");

        // The preview follows the wording, with the sample candidate.
        var preview = Page.GetByRole(AriaRole.Region, new() { Name = "Preview" });
        await Expect(preview.Locator("[data-email-subject]")).ToHaveTextAsync($"Welcome {marker}, Jordan Lee");
        await Expect(preview.FrameLocator("iframe[title='Email preview']").Locator("body")).ToContainTextAsync("Jordan Lee");
        await preview.GetByRole(AriaRole.Button, new() { Name = "Plain text" }).ClickAsync();
        await Expect(preview.Locator("[data-email-text]")).ToContainTextAsync("Hi Jordan Lee,");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/email-templates"));
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "Application received" })).ToContainTextAsync("Built-in, edited");

        // The job's pipeline: select every candidate and email them.
        await Page.GotoAsync(DashboardUrl($"/jobs/{jobId}"));
        var table = Page.GetByRole(AriaRole.Table, new() { Name = $"Candidates for {title}" });
        await Expect(table.GetByRole(AriaRole.Link, new() { Name = Candidates[0] })).ToBeVisibleAsync();
        await table.GetByRole(AriaRole.Checkbox, new() { Name = "Select all candidates" }).CheckAsync();
        var pipeline = Page.GetByRole(AriaRole.Region, new() { Name = "Pipeline" });
        await pipeline.GetByRole(AriaRole.Button, new() { Name = "Email 3" }).ClickAsync();

        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Email 3 candidates" });
        await sheet.GetByLabel("Template").SelectOptionAsync(new SelectOptionValue { Label = "Application received" });
        await Expect(sheet.Locator("[data-email-subject]")).ToContainTextAsync($"Welcome {marker}, ");
        await ExpectNoAccessibilityViolationsAsync();
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Send 3 emails" }).ClickAsync();
        await Expect(sheet).ToBeHiddenAsync();
        await Expect(pipeline.GetByRole(AriaRole.Status).Filter(new() { HasText = "Sending" })).ToHaveTextAsync("Sending 3 emails.");

        for (var i = 0; i < addresses.Count; i++)
        {
            var sent = await Fixture.Mailpit.WaitForMessagesAsync(addresses[i], 1, Ct);
            sent.Should().ContainSingle();
            sent[0].Subject.Should().Be($"Welcome {marker}, {Candidates[i]}");
        }
    }

    [Fact]
    public async Task An_owner_edits_a_template_then_sees_the_change_in_its_history()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var (owner, _, _, _) = await SceneAsync();
        await SignInAsync(owner);

        await Page.GotoAsync(DashboardUrl("/settings/email-templates"));
        await Page.GetByRole(AriaRole.Link, new() { Name = "Moved to the next step" }).First.ClickAsync();
        var band = Page.Locator("[data-slot=header-band]");
        await Expect(band).ToContainTextAsync("By ScreenHire");
        var subject = Page.GetByLabel("Subject");
        var original = await subject.InputValueAsync();
        await subject.FillAsync($"Next steps {marker} for {{{{ jobTitle }}}}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/email-templates"));

        // Back on the template: Last updated names the person, and History says what they changed.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Moved to the next step" }).First.ClickAsync();
        await Expect(band).ToContainTextAsync("by you");
        await band.GetByRole(AriaRole.Button, new() { Name = "History" }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "History" });
        await Expect(sheet.GetByRole(AriaRole.Heading, new() { Name = "Today" })).ToBeVisibleAsync();
        await Expect(sheet.GetByRole(AriaRole.Listitem).First).ToContainTextAsync("You changed the subject");
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Show the change" }).ClickAsync();
        await Expect(sheet.GetByRole(AriaRole.Definition).First).ToContainTextAsync(original);
        await Expect(sheet.GetByRole(AriaRole.Definition).First).ToContainTextAsync($"Next steps {marker} for {{{{ jobTitle }}}}");
        await ExpectNoAccessibilityViolationsAsync();
    }

    // A new person and organization with one draft job and an application per name in Candidates, created through the API
    // as that person, so the browser signs in to exactly this data.
    private async Task<(string Owner, Guid JobId, string Title, IReadOnlyList<string> Addresses)> SceneAsync()
    {
        var email = ApiSession.NewEmail("e2e-email");
        var title = $"Warehouse lead {Guid.NewGuid().ToString("N")[..4]}";
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        using (var onboarded = await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct))
        {
            onboarded.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title,
                description = "<p>Run the warehouse floor and the team on it.</p>",
                employmentType = "FullTime",
                workplaceType = "OnSite",
            },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();

        var addresses = Candidates.Select(name => ApiSession.NewEmail(name.Split(' ')[0].ToLowerInvariant())).ToList();
        for (var i = 0; i < Candidates.Length; i++)
        {
            await ReadAsync(await session.PostAsync(
                $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = Candidates[i], email = addresses[i] } }, Ct), HttpStatusCode.Created);
        }

        return (email, jobId, title, addresses);
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
