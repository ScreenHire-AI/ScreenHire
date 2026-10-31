using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// Rich text in a real browser (docs/architecture/rich-text.md): a job's description formatted from the editor's toolbar
/// reaches the careers page and its Google for Jobs JSON-LD as headings, bold and a list; an email template's message
/// takes a subheading and a variable chip that the preview fills in; and typed formatting stops while single-key
/// shortcuts are off. Each test signs in as a new person with a new organization.
/// </summary>
public sealed class RichTextTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] UkAndIreland = ["GB", "IE"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_description_formatted_in_the_editor_reaches_the_careers_page_and_google_for_jobs()
    {
        var scene = await SceneAsync();
        await SignInAsync(scene.Email);

        await Page.GotoAsync(DashboardUrl($"/jobs/{scene.JobId}/edit"));
        var editor = Page.GetByRole(AriaRole.Textbox, new() { Name = "Description" });
        var toolbar = Page.GetByRole(AriaRole.Toolbar, new() { Name = "Formatting" });
        await editor.ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+A");
        await Page.Keyboard.TypeAsync("About the role");
        await toolbar.GetByRole(AriaRole.Button, new() { Name = "Heading", Exact = true }).ClickAsync();
        await Page.Keyboard.PressAsync("End");
        await Page.Keyboard.PressAsync("Enter");
        await Page.Keyboard.TypeAsync("We build ");
        await toolbar.GetByRole(AriaRole.Button, new() { Name = "Bold" }).ClickAsync();
        await Page.Keyboard.TypeAsync("tools");
        await toolbar.GetByRole(AriaRole.Button, new() { Name = "Bold" }).ClickAsync();
        await Page.Keyboard.TypeAsync(" for small hiring teams, from research to release, with the whole product team.");
        await Page.Keyboard.PressAsync("Enter");
        await toolbar.GetByRole(AriaRole.Button, new() { Name = "Bulleted list" }).ClickAsync();
        await Page.Keyboard.TypeAsync("Remote first");
        await Expect(toolbar.GetByRole(AriaRole.Button, new() { Name = "Bulleted list" })).ToHaveAttributeAsync("aria-pressed", "true");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save details" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl($"/jobs/{scene.JobId}"));

        await Page.GotoAsync(new Uri(Fixture.GetEndpoint("careers"), $"/{scene.Slug}/jobs/{scene.JobSlug}").ToString());
        var about = Page.GetByRole(AriaRole.Region, new() { Name = "About the role" });
        await Expect(about.Locator("h2")).ToHaveTextAsync("About the role");
        await Expect(about.Locator("strong")).ToHaveTextAsync("tools");
        await Expect(about.Locator("ul > li")).ToHaveTextAsync("Remote first");

        using var document = JsonDocument.Parse(await Page.Locator("script[type='application/ld+json']").TextContentAsync() ?? "");
        document.RootElement.GetProperty("description").GetString().Should()
            .StartWith("<h2>About the role</h2><p>We build <strong>tools</strong> for small hiring teams")
            .And.EndWith("<ul><li>Remote first</li></ul>");
    }

    [Fact]
    public async Task An_email_message_takes_a_subheading_and_a_variable_chip_that_the_preview_fills_in()
    {
        var scene = await SceneAsync();
        await Page.SetViewportSizeAsync(1700, 1000);
        await SignInAsync(scene.Email);

        await Page.GotoAsync(DashboardUrl("/templates/emails/stage-change"));
        var message = Page.GetByRole(AriaRole.Textbox, new() { Name = "Message" });
        var preview = Page.FrameLocator("iframe[title='Email preview']");
        // The page is settled once the preview shows the sample candidate.
        await Expect(preview.Locator("body")).ToContainTextAsync("Hi Jordan");
        await Expect(message.Locator("[data-liquid]").First).ToContainTextAsync("First name");
        // The wording ends on a chip; Ctrl or Cmd End still reaches the end of the message.
        await message.ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+End");
        await Page.Keyboard.PressAsync("Enter");
        await Page.GetByRole(AriaRole.Toolbar, new() { Name = "Formatting" }).GetByRole(AriaRole.Button, new() { Name = "Subheading" }).ClickAsync();
        await Page.Keyboard.TypeAsync("Next steps for ");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Insert variable" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "First name" }).ClickAsync();

        await Expect(message.Locator("h3")).ToHaveTextAsync("Next steps for variable First name");
        await Expect(preview.Locator("h3")).ToHaveTextAsync("Next steps for Jordan");
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task Typed_formatting_stops_while_single_key_shortcuts_are_off()
    {
        var scene = await SceneAsync();
        await SignInAsync(scene.Email);
        await Page.EvaluateAsync("() => localStorage.setItem('screenhire:shortcuts', 'off')");

        await Page.GotoAsync(DashboardUrl($"/jobs/{scene.JobId}/edit"));
        var editor = Page.GetByRole(AriaRole.Textbox, new() { Name = "Description" });
        await editor.ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+A");
        await Page.Keyboard.TypeAsync("- not a list ## nor a heading");

        await Expect(editor).ToContainTextAsync("- not a list");
        await Expect(editor.Locator("ul")).ToHaveCountAsync(0);

        // With them on, the same keys start a list.
        await Page.EvaluateAsync("() => localStorage.setItem('screenhire:shortcuts', 'on')");
        await Page.ReloadAsync();
        await editor.ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+A");
        await Page.Keyboard.TypeAsync("- a list item");
        await Expect(editor.Locator("ul > li")).ToHaveTextAsync("a list item");
    }

    private sealed record Scene(string Email, string Slug, Guid JobId, string JobSlug);

    // A new person and organization with one published remote job, created through the API as that person.
    private async Task<Scene> SceneAsync()
    {
        var email = ApiSession.NewEmail("e2e-rich-text");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        var slug = $"test-{Guid.NewGuid():N}";
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);
        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title = $"Product Designer {Guid.NewGuid().ToString("N")[..4]}",
                description = "<p>Shape how small hiring teams review applicants, from research to release, with engineering.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = UkAndIreland,
                salary = new { min = 60_000, max = 75_000, currency = "GBP", period = "Year" },
            },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct), HttpStatusCode.OK);
        return new Scene(email, slug, jobId, job.GetProperty("slug").GetString()!);
    }

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
        var response = await Page.GotoAsync(DashboardUrl("/sign-in"));
        response!.Ok.Should().BeTrue();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
