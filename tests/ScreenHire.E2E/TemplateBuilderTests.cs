using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// The workflow builder in a real browser (docs/architecture/workflow-engine.md §4): building a hiring template with a
/// knockout question and an automation, the guardrail that refuses an integrity-driven rejection, publishing, creating a
/// job from the template and simulating an application through it; and a job taking a template update. Each test signs
/// in as a new person with a new organization.
/// </summary>
public sealed class TemplateBuilderTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Guardrail = "Integrity signals never drive rejection";

    private const string RightToWork = "Do you have the right to work in the UK?";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The region is busy while the check of the latest change is on its way, and until then it still shows the previous
    // change's result. Assert on the settled result, never on an earlier one that happens to match.
    private async Task ExpectValidationAsync(string text)
    {
        var validation = Page.GetByRole(AriaRole.Region, new() { Name = "Validation" });
        await Expect(validation).ToHaveAttributeAsync("aria-busy", "false");
        await Expect(validation).ToContainTextAsync(text);
    }

    [Fact]
    public async Task A_recruiter_builds_and_publishes_a_template_creates_a_job_from_it_and_simulates_an_application()
    {
        var email = await OnboardAsync();
        var templateName = $"Warehouse associate {Guid.NewGuid().ToString("N")[..4]}";
        await Page.SetViewportSizeAsync(1440, 1040);
        await SignInAsync(email);

        // A new template from the templates list.
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^Templates") }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Templates");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Templates" })).ToContainTextAsync("Software engineer");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "New template" }).ClickAsync();
        var newTemplate = Page.GetByRole(AriaRole.Dialog, new() { Name = "New template" });
        await newTemplate.GetByLabel("Name").FillAsync(templateName);
        await newTemplate.GetByRole(AriaRole.Button, new() { Name = "Create template" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(templateName);
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Template" })).ToContainTextAsync("Draft");
        await ExpectValidationAsync("No problems found.");

        // A knockout question, which the candidate preview shows as the careers page would.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Questionnaire" }).ClickAsync();
        await Page.GetByLabel("New question's answer type").SelectOptionAsync(new SelectOptionValue { Label = "Yes or no" });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add question" }).ClickAsync();
        await Page.GetByRole(AriaRole.Textbox, new() { NameRegex = new Regex("^Question answers\\.") }).FillAsync(RightToWork);
        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Knockout question" }).CheckAsync();
        await Expect(Page.GetByText("A knockout is an objective eligibility check only")).ToBeVisibleAsync();
        var preview = Page.GetByRole(AriaRole.Region, new() { Name = "Candidate preview" });
        var previewQuestion = preview.GetByRole(AriaRole.Group, new() { Name = RightToWork });
        await Expect(previewQuestion.GetByRole(AriaRole.Radio)).ToHaveCountAsync(2);
        await previewQuestion.GetByRole(AriaRole.Radio, new() { Name = "No" }).CheckAsync();
        await Expect(previewQuestion.GetByRole(AriaRole.Radio, new() { Name = "No" })).ToBeCheckedAsync();
        await Expect(preview.GetByLabel("Resume or CV")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-questionnaire-builder-light");

        // An automation on the integrity checks that tries to reject: the API's guardrail refuses it.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Pipeline" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Automations for the whole workflow") }).ClickAsync();
        var drawer = Page.GetByRole(AriaRole.Dialog, new() { Name = "Automations: Whole workflow" });
        await drawer.GetByRole(AriaRole.Button, new() { Name = "Add automation" }).ClickAsync();
        var firstAction = drawer.GetByRole(AriaRole.Combobox, new() { NameRegex = new Regex("^Action 1") });
        await drawer.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true }).FillAsync("Route for review");
        await drawer.GetByLabel("Trigger").SelectOptionAsync(new SelectOptionValue { Label = "The integrity checks finish" });
        await drawer.GetByRole(AriaRole.Button, new() { Name = "Add condition" }).ClickAsync();
        await drawer.GetByLabel("Fact for condition 1").SelectOptionAsync(new SelectOptionValue { Label = "Integrity: authenticity" });
        await drawer.GetByLabel("Value for condition 1").SelectOptionAsync(new SelectOptionValue { Label = "Verification recommended" });
        await firstAction.SelectOptionAsync(new SelectOptionValue { Label = "Reject" });
        await drawer.GetByRole(AriaRole.Textbox, new() { Name = "Reason", Exact = true }).FillAsync("The role needs a verified identity.");
        await Expect(drawer.GetByRole(AriaRole.Article)).ToContainTextAsync(Guardrail);
        await Expect(firstAction).ToHaveAttributeAsync("aria-invalid", "true");
        await ExpectValidationAsync(Guardrail);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-automation-guardrail-light");

        // Routing for review instead is allowed.
        await firstAction.SelectOptionAsync(new SelectOptionValue { Label = "Add a tag" });
        await drawer.GetByRole(AriaRole.Textbox, new() { Name = "Tag", Exact = true }).FillAsync("needs-review");
        await ExpectValidationAsync("No problems found.");
        await Expect(drawer).Not.ToContainTextAsync(Guardrail);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(drawer).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Whole workflow" }))
            .ToContainTextAsync("When the integrity checks finish, if 1 condition holds: add the tag \"needs-review\".");

        // The graph is the same workflow.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Graph" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Edit Route for review" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".svelte-flow")).ToContainTextAsync("Add the tag \"needs-review\"");
        // Framed in the pane, not just present: the view fits the graph once it is shown.
        var pane = (await Page.Locator(".svelte-flow").BoundingBoxAsync())!;
        var edit = (await Page.GetByRole(AriaRole.Button, new() { Name = "Edit Route for review" }).BoundingBoxAsync())!;
        edit.X.Should().BeInRange(pane.X, pane.X + pane.Width - edit.Width);
        edit.Y.Should().BeInRange(pane.Y, pane.Y + pane.Height - edit.Height);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-workflow-graph-light");

        // The graph takes its colours from the tokens in the dark theme too.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Switch to dark theme" }).ClickAsync();
        await Expect(Page.Locator(".svelte-flow")).ToHaveClassAsync(new Regex(@"\bdark\b"));
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-workflow-graph-dark");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" }).ClickAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved version 1." })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish version 1" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Published version 1." })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Template" })).ToContainTextAsync("Published");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Edit as version 2" })).ToBeVisibleAsync();

        // A job from the template, then a sample application through the job's copy of the workflow.
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^Jobs") }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "New job" }).ClickAsync();
        var newJob = Page.GetByRole(AriaRole.Dialog, new() { Name = "New job" });
        await newJob.GetByLabel("Title").FillAsync("Warehouse associate, nights");
        await newJob.GetByLabel("Hiring template").SelectOptionAsync(new SelectOptionValue { Label = $"{templateName} (v1)" });
        await newJob.GetByLabel("Workplace").SelectOptionAsync(new SelectOptionValue { Label = "On-site" });
        await newJob.GetByLabel("City").FillAsync("Leeds");
        await newJob.GetByLabel("Country").FillAsync("GB");
        await newJob.GetByRole(AriaRole.Button, new() { Name = "Create job" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Warehouse associate, nights");
        await Page.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^Workflow") }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Workflow: Warehouse associate, nights");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Job workflow" })).ToContainTextAsync($"from {templateName} v1");

        await Page.GetByRole(AriaRole.Tab, new() { Name = "Simulate" }).ClickAsync();
        var sample = Page.GetByRole(AriaRole.Form, new() { Name = "Sample application" });
        await sample.GetByLabel("Trigger").SelectOptionAsync(new SelectOptionValue { Label = "The integrity checks finish" });
        await sample.GetByLabel("Authenticity").SelectOptionAsync(new SelectOptionValue { Label = "Verification recommended" });
        await sample.GetByLabel(RightToWork).SelectOptionAsync(new SelectOptionValue { Label = "Yes" });
        await sample.GetByRole(AriaRole.Button, new() { Name = "Run test" }).ClickAsync();
        var results = Page.GetByRole(AriaRole.Region, new() { Name = "What would happen" });
        var fired = results.GetByRole(AriaRole.Listitem, new() { Name = "Route for review: Would run" });
        await Expect(fired).ToContainTextAsync("integrity.authenticity is: holds");
        await Expect(fired).ToContainTextAsync("Add the tag \"needs-review\"");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-workflow-simulate-light");

        // A sample the conditions do not match runs nothing.
        await sample.GetByLabel("Authenticity").SelectOptionAsync(new SelectOptionValue { Label = "No concerns" });
        await sample.GetByRole(AriaRole.Button, new() { Name = "Run test" }).ClickAsync();
        await Expect(results.GetByRole(AriaRole.Listitem, new() { Name = "Route for review: Conditions do not hold" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_job_shows_a_template_update_with_its_differences_and_takes_it_when_applied()
    {
        var email = await OnboardAsync();
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        var template = await ReadAsync(await session.PostAsync("/v1/templates", new { name = "Retail assistant" }, Ct), HttpStatusCode.Created);
        var templateId = template.GetProperty("id").GetGuid();
        var first = await ReadAsync(await session.PostAsync($"/v1/templates/{templateId}/versions/1/publish", new { }, Ct), HttpStatusCode.OK);
        var job = await ReadAsync(
            await session.PostAsync("/v1/jobs", new { title = "Retail assistant, weekends", templateVersionId = first.GetProperty("id").GetGuid() }, Ct),
            HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();

        // Version 2 shortens the Screening SLA from three days to two.
        var definition = first.GetProperty("definition").Deserialize<Dictionary<string, JsonElement>>()!;
        var workflow = JsonSerializer.SerializeToNode(definition["workflow"])!.AsObject();
        workflow["stages"]![1]!["slaDays"] = 2;
        await ReadAsync(
            await session.PostAsync($"/v1/templates/{templateId}/versions", new { definition = new { workflow, postingDefaults = definition["postingDefaults"] } }, Ct),
            HttpStatusCode.Created);
        await ReadAsync(await session.PostAsync($"/v1/templates/{templateId}/versions/2/publish", new { }, Ct), HttpStatusCode.OK);

        await SignInAsync(email);
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Template update available" })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Template update available" }).ClickAsync();
        var update = Page.GetByRole(AriaRole.Region, new() { Name = "Template update available" });
        await Expect(update).ToContainTextAsync("Retail assistant has version 2; this job uses version 1.");
        await update.GetByRole(AriaRole.Button, new() { Name = "Review update" }).ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Apply version 2 of Retail assistant?" });
        var differences = dialog.GetByRole(AriaRole.List, new() { Name = "Differences" });
        await Expect(differences).ToContainTextAsync("stages[screening].slaDays");
        await Expect(differences).ToContainTextAsync("Before: 3");
        await Expect(differences).ToContainTextAsync("After: 2");
        await ExpectNoAccessibilityViolationsAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Apply update" }).ClickAsync();

        await Expect(dialog).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Applied version 2 of Retail assistant." })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Job workflow" })).ToContainTextAsync("Workflow version 2");
        await Expect(update).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Spinbutton, new() { Name = "SLA (days)" }).Nth(1)).ToHaveValueAsync("2");
    }

    private async Task<string> OnboardAsync()
    {
        var email = ApiSession.NewEmail("e2e-builder");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        using var onboarded = await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Northgate Depot", slug = $"test-{Guid.NewGuid():N}" }, Ct);
        onboarded.StatusCode.Should().Be(HttpStatusCode.OK);
        return email;
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

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
