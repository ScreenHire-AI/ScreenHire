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

    // The validation badge beside the title is busy while the check of the latest change is on its way, and until then
    // it still shows the previous change's result. Assert on the settled result, never on an earlier one that happens to
    // match.
    private async Task ExpectValidationAsync(string text)
    {
        var validation = Page.GetByRole(AriaRole.Region, new() { Name = "Validation" });
        await Expect(validation).ToHaveAttributeAsync("aria-busy", "false");
        await Expect(validation).ToContainTextAsync(text);
    }

    // A settled check that found a problem: the badge counts it, and the builder's "Problems to fix" panel says it.
    private async Task ExpectProblemAsync(string text)
    {
        await ExpectValidationAsync("to fix");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Problems to fix" })).ToContainTextAsync(text);
    }

    // The page's header band: the title, the validation badge and the version's facts as pills.
    private ILocator HeaderBand => Page.Locator("[data-slot=header-band]");

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
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Hiring templates" })).ToContainTextAsync("Software engineer");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "New template" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Hiring template" }).ClickAsync();
        var newTemplate = Page.GetByRole(AriaRole.Dialog, new() { Name = "New hiring template" });
        await newTemplate.GetByLabel("Name").FillAsync(templateName);
        await newTemplate.GetByRole(AriaRole.Button, new() { Name = "Create template" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(templateName);
        await Expect(HeaderBand).ToContainTextAsync("Draft");
        await ExpectValidationAsync("No problems found");

        // A knockout question in the form builder, which the preview shows as the careers page would.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Questionnaire" }).ClickAsync();
        var questions = Page.GetByRole(AriaRole.Region, new() { Name = "Screening questions", Exact = true });
        await questions.GetByRole(AriaRole.Button, new() { Name = "Add a question" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Yes or no", Exact = true }).ClickAsync();
        await questions.GetByRole(AriaRole.Textbox, new() { Name = "Question 1", Exact = true }).FillAsync(RightToWork);
        await questions.GetByRole(AriaRole.Checkbox, new() { Name = "Knockout question" }).CheckAsync();
        await Expect(Page.GetByText("A knockout is an objective eligibility check only")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-questionnaire-builder-light");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview", Exact = true }).ClickAsync();
        var preview = Page.Locator("[data-preview-device]");
        var previewQuestion = preview.GetByRole(AriaRole.Group, new() { Name = RightToWork });
        await Expect(previewQuestion.GetByRole(AriaRole.Radio)).ToHaveCountAsync(2);
        await previewQuestion.GetByRole(AriaRole.Radio, new() { Name = "No" }).CheckAsync();
        await Expect(previewQuestion.GetByRole(AriaRole.Radio, new() { Name = "No" })).ToBeCheckedAsync();
        await Expect(preview.GetByLabel("Resume or CV")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).ClickAsync();

        // An automation on the integrity checks that tries to reject: the API's guardrail refuses it.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Pipeline" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Edit automations for the whole workflow") }).ClickAsync();
        var drawer = Page.GetByRole(AriaRole.Dialog, new() { Name = "Automations: Whole workflow" });
        await drawer.GetByRole(AriaRole.Button, new() { Name = "Add automation" }).ClickAsync();
        var firstAction = drawer.GetByRole(AriaRole.Combobox, new() { NameRegex = new Regex("^Action 1") });
        await drawer.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true }).FillAsync("Route for review");
        await drawer.GetByLabel("Trigger").SelectOptionAsync(new SelectOptionValue { Label = "The integrity checks finish" });
        await drawer.GetByRole(AriaRole.Button, new() { Name = "Add condition" }).ClickAsync();
        await drawer.GetByLabel("Fact for condition 1").SelectOptionAsync(new SelectOptionValue { Label = "Integrity: authenticity" });
        await drawer.GetByLabel("Value for condition 1").SelectOptionAsync(new SelectOptionValue { Label = "Verification suggested" });
        await firstAction.SelectOptionAsync(new SelectOptionValue { Label = "Reject" });
        await drawer.GetByRole(AriaRole.Textbox, new() { Name = "Reason", Exact = true }).FillAsync("The role needs a verified identity.");
        await Expect(drawer.GetByRole(AriaRole.Article)).ToContainTextAsync(Guardrail);
        await Expect(firstAction).ToHaveAttributeAsync("aria-invalid", "true");
        await ExpectProblemAsync(Guardrail);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-automation-guardrail-light");

        // Routing for review instead is allowed.
        await firstAction.SelectOptionAsync(new SelectOptionValue { Label = "Add a tag" });
        await drawer.GetByRole(AriaRole.Textbox, new() { Name = "Tag", Exact = true }).FillAsync("needs-review");
        await ExpectValidationAsync("No problems found");
        await Expect(drawer).Not.ToContainTextAsync(Guardrail);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(drawer).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Whole workflow" }))
            .ToContainTextAsync("When the integrity checks finish, if 1 condition holds: add the tag \"needs-review\".");

        // The graph is the same workflow.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Graph" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Edit Route for review" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".svelte-flow")).ToContainTextAsync("Add the tag \"needs-review\"");
        // Framed in the pane, not just present: the view starts at the top left, where this automation's row begins.
        var pane = (await Page.Locator(".svelte-flow").BoundingBoxAsync())!;
        var edit = (await Page.GetByRole(AriaRole.Button, new() { Name = "Edit Route for review" }).BoundingBoxAsync())!;
        edit.X.Should().BeInRange(pane.X, pane.X + pane.Width - edit.Width);
        edit.Y.Should().BeInRange(pane.Y, pane.Y + pane.Height - edit.Height);
        // The zoom toolbar floats over the canvas's bottom left; the first column starts right of it, so it covers no node.
        var toolbar = (await Page.Locator("[data-graph-controls]").BoundingBoxAsync())!;
        foreach (var node in await Page.Locator(".svelte-flow__node").AllAsync())
        {
            var box = (await node.BoundingBoxAsync())!;
            var overlaps = box.X < toolbar.X + toolbar.Width && toolbar.X < box.X + box.Width && box.Y < toolbar.Y + toolbar.Height && toolbar.Y < box.Y + box.Height;
            overlaps.Should().BeFalse($"the zoom toolbar should not cover the node \"{await node.GetAttributeAsync("aria-label")}\"");
        }
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
        await Expect(HeaderBand).ToContainTextAsync("Published");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Edit as version 2" })).ToBeVisibleAsync();

        // A published version reads as content: no inputs, the stages on a line and the automations as rows.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Pipeline" }).ClickAsync();
        var pipeline = Page.GetByRole(AriaRole.Tabpanel, new() { Name = "Pipeline" });
        await Expect(pipeline.Locator("input, select, textarea")).ToHaveCountAsync(0);
        await Expect(pipeline.GetByRole(AriaRole.List, new() { Name = "Stages in order" })).ToBeVisibleAsync();
        await Expect(pipeline.GetByRole(AriaRole.Region, new() { Name = "Automations" })).ToContainTextAsync("Route for review");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-template-read-only-light");

        // A job from the template, then a sample application through the job's copy of the workflow.
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^Jobs") }).ClickAsync();
        // The header band's New job; a list with no jobs offers it in its empty state too.
        await Page.Locator("[data-slot='header-band']").GetByRole(AriaRole.Link, new() { Name = "New job" }).ClickAsync();
        var newJob = Page.GetByRole(AriaRole.Form, new() { Name = "New job" });
        await newJob.GetByLabel("Title").FillAsync("Warehouse associate, nights");
        await newJob.GetByLabel("Hiring template").SelectOptionAsync(new SelectOptionValue { Label = $"{templateName} (v1)" });
        await newJob.GetByLabel("Workplace").SelectOptionAsync(new SelectOptionValue { Label = "On-site" });
        await newJob.GetByLabel("City").FillAsync("Leeds");
        await newJob.GetByLabel("Country").FillAsync("GB");
        await newJob.GetByRole(AriaRole.Button, new() { Name = "Create job" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Warehouse associate, nights");
        await Page.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^Edit workflow") }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Workflow: Warehouse associate, nights");
        await Expect(HeaderBand).ToContainTextAsync($"From {templateName} v1");

        await Page.GetByRole(AriaRole.Tab, new() { Name = "Simulate" }).ClickAsync();
        var sample = Page.GetByRole(AriaRole.Form, new() { Name = "Sample application" });
        await sample.GetByLabel("Trigger").SelectOptionAsync(new SelectOptionValue { Label = "The integrity checks finish" });
        await sample.GetByLabel("Authenticity").SelectOptionAsync(new SelectOptionValue { Label = "Verification suggested" });
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
        // The jobs list marks the job with a pill, and the job's facts say so beside the template's version.
        var row = Page.GetByRole(AriaRole.Table, new() { Name = "Jobs" }).GetByRole(AriaRole.Row).Filter(new() { HasText = "Retail assistant, weekends" });
        await Expect(row).ToContainTextAsync("Template update");
        await row.GetByRole(AriaRole.Link, new() { Name = "Retail assistant, weekends" }).ClickAsync();
        await Expect(Page.Locator("[data-slot='header-band']")).ToContainTextAsync("Retail assistant v1");
        await Expect(Page.Locator("[data-slot='header-band']")).ToContainTextAsync("Update available");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Edit workflow" }).ClickAsync();
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
        await Expect(HeaderBand).ToContainTextAsync("Workflow version 2");
        await Expect(update).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Spinbutton, new() { Name = "Time limit (days)" }).Nth(1)).ToHaveValueAsync("2");
    }

    [Fact]
    public async Task A_ScreenHire_template_reads_as_content_and_every_template_screen_fits_a_phone()
    {
        var email = await OnboardAsync();
        await Page.SetViewportSizeAsync(1440, 1040);
        await SignInAsync(email);

        // A ScreenHire template cannot be edited: it reads as content, says why, with Copy template as the one action (E3).
        await Page.GotoAsync(DashboardUrl("/templates"));
        await Page.GetByRole(AriaRole.Region, new() { Name = "Hiring templates" }).GetByRole(AriaRole.Link, new() { Name = "Software engineer", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Software engineer");
        await Expect(HeaderBand).ToContainTextAsync("Built in");
        await Expect(Page.GetByText("Copy this ScreenHire template to change it.")).ToBeVisibleAsync();
        await ExpectValidationAsync("No problems found");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Copy template" })).ToBeVisibleAsync();
        var pipeline = Page.GetByRole(AriaRole.Tabpanel, new() { Name = "Pipeline" });
        await Expect(pipeline.GetByRole(AriaRole.List, new() { Name = "Stages in order" })).ToBeVisibleAsync();
        await Expect(pipeline.Locator("input, select, textarea")).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-template-screenhire-light");

        // Its earlier version, from the versions menu.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Versions" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { NameRegex = new Regex("^Version 1, published") }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?version=1$"));
        await Expect(HeaderBand).ToContainTextAsync("Version 1");

        // On a phone: the read-only view, the list and a draft in the builder, none scrolling sideways.
        await Page.SetViewportSizeAsync(390, 844);
        await ExpectNoSidewaysScrollAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-template-screenhire-390");

        await Page.GotoAsync(DashboardUrl("/templates"));
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Hiring templates" }).GetByRole(AriaRole.Link, new() { Name = "Software engineer", Exact = true })).ToBeVisibleAsync();
        await ExpectNoSidewaysScrollAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-templates-390");

        await Page.GetByRole(AriaRole.Region, new() { Name = "Hiring templates" }).GetByRole(AriaRole.Link, new() { Name = "Software engineer", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Copy template" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Software engineer (copy)");
        // A phone has the primary action in the action bar and the rest in More (design system section 8).
        await Expect(Page.Locator("[data-slot='action-bar']").GetByRole(AriaRole.Button, new() { Name = "Publish version 1" })).ToBeVisibleAsync();
        await HeaderBand.GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Save draft" })).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await ExpectValidationAsync("No problems found");
        await ExpectNoSidewaysScrollAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-template-builder-390");
    }

    [Fact]
    public async Task A_recruiter_builds_an_automation_in_the_graph_with_the_keyboard_and_finds_it_in_the_pipeline()
    {
        const string Blank = "Leave this out rather than sending it blank.";
        var email = await OnboardAsync();
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(email, Ct);
            var template = await ReadAsync(await session.PostAsync("/v1/templates", new { name = "Support agent" }, Ct), HttpStatusCode.Created);
            await Page.SetViewportSizeAsync(1440, 1040);
            await SignInAsync(email);
            await Page.GotoAsync(DashboardUrl($"/templates/{template.GetProperty("id").GetGuid()}"));
        }
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Support agent");
        await ExpectValidationAsync("No problems found");
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Graph" }).ClickAsync();
        var graph = Page.GetByRole(AriaRole.Tabpanel, new() { Name = "Graph" });
        ILocator Node(string name) => graph.GetByRole(AriaRole.Group, new() { Name = name, Exact = true });

        // From here on, the keyboard only: Tab into the graph, the arrow keys from node to node.
        await TabUntilFocusedAsync(Node("Whole workflow"));
        await Page.Keyboard.PressAsync("ArrowRight");
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(Node("Stage 2: Screening")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(graph.GetByRole(AriaRole.Button, new() { Name = "Add automation to Screening" })).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Enter");

        // The Pipeline view's own form, opened at the new automation's name, its trigger set to entering Screening.
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Automations: Screening" });
        var name = sheet.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true });
        await Expect(name).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Control+A");
        await Page.Keyboard.TypeAsync("Fast-track referrals");
        await Expect(sheet.GetByRole(AriaRole.Combobox, new() { Name = "Trigger" })).ToHaveValueAsync("StageEntered");
        await TabUntilFocusedAsync(sheet.GetByRole(AriaRole.Button, new() { Name = "Add condition" }));
        await Page.Keyboard.PressAsync("Enter");
        await Expect(sheet.GetByLabel("Fact for condition 1")).ToHaveValueAsync("application.tags");
        await TabUntilFocusedAsync(sheet.GetByRole(AriaRole.Textbox, new() { NameRegex = new Regex("^Value") }), backwards: true);
        await Page.Keyboard.TypeAsync("referral");
        // The action it starts with adds a tag, and the tag is still blank.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(sheet).ToBeHiddenAsync();

        // The focus is back on the new automation, and the problem shows on the node it is about.
        await Expect(Node("When: Fast-track referrals")).ToBeFocusedAsync();
        await ExpectProblemAsync(Blank);
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(Node("If: All of 1 condition, Fast-track referrals")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        var action = Node("Then, action 1: Add the tag \"\", Fast-track referrals, 1 problem to fix");
        await Expect(action).ToBeFocusedAsync();
        await Expect(action).ToContainTextAsync(Blank);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-workflow-graph-problem-1440");

        // Its Edit opens the same form at that action.
        await Page.Keyboard.PressAsync("Tab");
        await Page.Keyboard.PressAsync("Enter");
        await Expect(sheet.GetByRole(AriaRole.Combobox, new() { Name = "Action 1" })).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(sheet.GetByRole(AriaRole.Textbox, new() { Name = "Tag", Exact = true })).ToBeFocusedAsync();
        await Page.Keyboard.TypeAsync("fast-track");
        await ExpectValidationAsync("No problems found");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(sheet).ToBeHiddenAsync();
        await Expect(Node("Then, action 1: Add the tag \"fast-track\", Fast-track referrals")).ToBeVisibleAsync();
        await Expect(graph.GetByRole(AriaRole.Button, new() { Name = "Edit Add the tag \"fast-track\" in Fast-track referrals" })).ToBeFocusedAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-workflow-graph-edit-1440");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved version 1." })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Pipeline" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Stages" }).GetByRole(AriaRole.Listitem, new() { Name = "Stage 2: Screening" }))
            .ToContainTextAsync("When a candidate enters Screening, if 1 condition holds: add the tag \"fast-track\".");

        // The graph at a laptop's and a tablet's width, and on a phone, where it is for reading. It opens at full size.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Graph" }).ClickAsync();
        await Page.Mouse.MoveAsync(0, 0);
        foreach (var (width, height) in new[] { (1024, 900), (768, 1024) })
        {
            await Page.SetViewportSizeAsync(width, height);
            await Expect(graph.GetByRole(AriaRole.Button, new() { Name = "Add automation to Screening" })).ToBeVisibleAsync();
            await ExpectNoSidewaysScrollAsync();
            await SaveScreenshotAsync(Page, $"dashboard-workflow-graph-{width}");
        }
        await Page.SetViewportSizeAsync(390, 844);
        await Expect(graph).ToContainTextAsync("On a phone the graph is for reading. Edit in Pipeline.");
        await Expect(graph.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Edit|Add automation|Remove) ") })).ToHaveCountAsync(0);
        await Expect(Node("When: Fast-track referrals")).ToBeAttachedAsync();
        await ExpectNoSidewaysScrollAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-workflow-graph-390");
    }

    // Presses Tab (or Shift+Tab) until `target` has the focus, as a keyboard user would, and fails if it never does.
    private async Task TabUntilFocusedAsync(ILocator target, bool backwards = false)
    {
        for (var i = 0; i < 30; i++)
        {
            if (await target.EvaluateAsync<bool>("element => element === document.activeElement"))
            {
                return;
            }
            await Page.Keyboard.PressAsync(backwards ? "Shift+Tab" : "Tab");
        }
        await Expect(target).ToBeFocusedAsync();
    }

    private async Task ExpectNoSidewaysScrollAsync() =>
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the page at {0} scrolls sideways", Page.Url);

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
