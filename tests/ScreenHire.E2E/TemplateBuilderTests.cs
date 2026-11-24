using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// The workflow builder in a real browser (docs/architecture/workflow-engine.md §4): building a hiring template with a
/// knockout question and an automation, the guardrail that refuses a verification-driven rejection, publishing, creating a
/// job from the template and simulating an application through it; and a job taking a template update. Each test signs
/// in as a new person with a new organization.
/// </summary>
public sealed class TemplateBuilderTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Guardrail = "never drives a rejection";

    private const string RightToWork = "Do you have the right to work in the UK?";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The validation badge beside the title is busy while the check of the latest change is on its way, and until then
    // it still shows the previous change's result. Assert on the settled result, never on an earlier one that happens to
    // match. A workflow with nothing wrong shows no badge at all.
    private async Task ExpectNoProblemsAsync()
    {
        var validation = Page.GetByRole(AriaRole.Region, new() { Name = "Validation" });
        await Expect(validation).ToHaveAttributeAsync("aria-busy", "false");
        await Expect(validation).Not.ToContainTextAsync("Needs fixes");
    }

    // A settled check that found a problem: the Needs fixes badge, and the builder's "Problems to fix" panel says it.
    private async Task ExpectProblemAsync(string text)
    {
        var validation = Page.GetByRole(AriaRole.Region, new() { Name = "Validation" });
        await Expect(validation).ToHaveAttributeAsync("aria-busy", "false");
        await Expect(validation).ToContainTextAsync("Needs fixes");
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
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("New hiring template");
        await Page.GetByLabel("Template name").FillAsync(templateName);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Start from nothing" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(templateName);
        await Expect(HeaderBand).ToContainTextAsync("Draft");
        await ExpectNoProblemsAsync();

        // A knockout question in the form builder, which the preview shows as the careers page would.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Forms" }).ClickAsync();
        var questions = Page.GetByRole(AriaRole.Region, new() { Name = "Questions", Exact = true });
        await questions.GetByRole(AriaRole.Button, new() { Name = "Add a question" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Yes or no", Exact = true }).ClickAsync();
        await questions.GetByRole(AriaRole.Textbox, new() { Name = "Question 1", Exact = true }).FillAsync(RightToWork);
        await questions.GetByRole(AriaRole.Checkbox, new() { Name = "Knockout question" }).CheckAsync();
        await Expect(Page.GetByText("A knockout is an objective eligibility check only")).ToBeVisibleAsync();

        // The forms are cards, each saying when it is sent, and the AI-use question is ScreenHire's: a locked row with the
        // organization's policy and a link to change it, not a setting of the template.
        var forms = Page.GetByRole(AriaRole.Group, new() { Name = "Forms", Exact = true });
        await Expect(forms.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Application form") })).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(forms).ToContainTextAsync("When they apply");
        var aiUse = Page.Locator("[data-ai-use-row]");
        await Expect(aiUse).ToContainTextAsync("AI use. Asked by ScreenHire. Policy: Allowed with disclosure.");
        await Expect(aiUse.GetByRole(AriaRole.Link, new() { Name = "Change policy" })).ToHaveAttributeAsync("href", "/verification/checks#answers");
        await Expect(Page.GetByRole(AriaRole.Combobox, new() { Name = "AI use while applying" })).ToHaveCountAsync(0);

        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-questionnaire-builder-light");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview", Exact = true }).ClickAsync();
        var preview = Page.Locator("[data-preview-device]");
        // The details and the resume come first, the questions after Next, as on the careers page.
        await Expect(preview.GetByLabel("Resume or CV")).ToBeVisibleAsync();
        await preview.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        var previewQuestion = preview.GetByRole(AriaRole.Group, new() { Name = RightToWork });
        await Expect(previewQuestion.GetByRole(AriaRole.Radio)).ToHaveCountAsync(2);
        await previewQuestion.GetByRole(AriaRole.Radio, new() { Name = "No" }).CheckAsync();
        await Expect(previewQuestion.GetByRole(AriaRole.Radio, new() { Name = "No" })).ToBeCheckedAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).ClickAsync();

        // An automation on the verification checks, written as a sentence. Once it reads a check, the choices never offer a
        // rejection or an email; the Rules tab is where the API's guardrail refuses one.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Automations" }).ClickAsync();
        await Page.GetByRole(AriaRole.Tabpanel, new() { Name = "Automations" }).GetByRole(AriaRole.Button, new() { Name = "Add an automation", Exact = true }).ClickAsync();
        var recipes = Page.GetByRole(AriaRole.Dialog, new() { Name = "Add an automation" });
        await Expect(recipes).ToContainTextAsync("Look closer when the checks suggest it");
        await recipes.GetByRole(AriaRole.Button, new() { Name = "Write your own" }).ClickAsync();
        var drawer = Page.GetByRole(AriaRole.Dialog);
        await drawer.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true }).FillAsync("Route for review");
        await ChooseAsync(drawer, "Trigger: someone applies", "The checks finish");
        await ChooseAsync(drawer, "Add a condition: and if", "Verification");
        await drawer.GetByRole(AriaRole.Button, new() { Name = "Condition 1: value: Clear" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Verify", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Clear", Exact = true }).ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(drawer.GetByRole(AriaRole.Button, new() { Name = "Condition 1: value: Verify" })).ToBeVisibleAsync();

        await drawer.GetByRole(AriaRole.Button, new() { Name = "Add another step: then also" }).ClickAsync();
        var steps = Page.Locator("[data-slot=popover-content]");
        await Expect(steps.GetByRole(AriaRole.Option)).ToHaveTextAsync(["Move to a stage", "Add a tag", "Ask a teammate to review"]);
        await Expect(steps).ToContainTextAsync("A check can only ask for a closer look. A person decides.");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-automation-guardrail-light");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(steps).ToBeHiddenAsync();

        await drawer.GetByRole(AriaRole.Tab, new() { Name = "Rules" }).ClickAsync();
        var firstAction = drawer.GetByRole(AriaRole.Combobox, new() { NameRegex = new Regex("^Action 1") });
        await firstAction.SelectOptionAsync(new SelectOptionValue { Label = "Reject" });
        await drawer.GetByRole(AriaRole.Textbox, new() { Name = "Reason", Exact = true }).FillAsync("The role needs a verified identity.");
        await Expect(drawer.GetByRole(AriaRole.Article)).ToContainTextAsync(Guardrail);
        await Expect(firstAction).ToHaveAttributeAsync("aria-invalid", "true");
        await ExpectProblemAsync(Guardrail);
        await ExpectNoAccessibilityViolationsAsync();

        // Routing for review instead is allowed.
        await firstAction.SelectOptionAsync(new SelectOptionValue { Label = "Add a tag" });
        await drawer.GetByRole(AriaRole.Textbox, new() { Name = "Tag name", Exact = true }).FillAsync("needs-review");
        await ExpectNoProblemsAsync();
        await Expect(drawer).Not.ToContainTextAsync(Guardrail);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(drawer).ToBeHiddenAsync();

        // The Pipeline tab counts it on the first stage, and its chip opens it on the Automations tab.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Pipeline" }).ClickAsync();
        await Page.GetByRole(AriaRole.Region, new() { Name = "Stages" }).GetByRole(AriaRole.Group, new() { Name = "Stage 1: Applied" })
            .GetByRole(AriaRole.Button, new() { Name = "1 automation at Applied" }).ClickAsync();
        var applied = Page.GetByRole(AriaRole.Dialog, new() { Name = "Route for review" });
        await Expect(applied).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(applied).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "Automations" })).ToHaveAttributeAsync("aria-selected", "true");

        // The graph is the same workflow.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Automations" }).ClickAsync();
        var automation = Page.GetByRole(AriaRole.Button, new() { Name = "When Route for review" });
        await Expect(automation).ToBeVisibleAsync();
        await Expect(Page.Locator(".svelte-flow")).ToContainTextAsync("Add the tag \"needs-review\"");
        // Framed in the pane, not just present: the first view starts at the lane, where this automation hangs.
        var pane = (await Page.Locator(".svelte-flow").BoundingBoxAsync())!;
        var edit = (await automation.BoundingBoxAsync())!;
        edit.X.Should().BeInRange(pane.X, pane.X + pane.Width - edit.Width);
        edit.Y.Should().BeInRange(pane.Y, pane.Y + pane.Height - edit.Height);
        // The zoom toolbar floats over the canvas's top left; the graph starts right of it, so it covers no node.
        var toolbar = (await Page.Locator("[data-graph-controls]").BoundingBoxAsync())!;
        foreach (var node in await Page.Locator(".svelte-flow__node").AllAsync())
        {
            var box = (await node.BoundingBoxAsync())!;
            var overlaps = box.X < toolbar.X + toolbar.Width && toolbar.X < box.X + box.Width && box.Y < toolbar.Y + toolbar.Height && toolbar.Y < box.Y + box.Height;
            overlaps.Should().BeFalse($"the zoom toolbar should not cover the node \"{await node.GetAttributeAsync("aria-label")}\"");
        }

        // Opened from the map, the automation's sheet starts at its trigger with no tooltip open, on the tab chosen last (Rules,
        // above). Switching to Sentence changes what is in it, never its width (the owner's walk-through, 2026-11-22).
        var tooltips = Page.Locator("[data-slot=tooltip-content]");
        var opened = Page.GetByRole(AriaRole.Dialog, new() { Name = "Route for review" });
        await automation.ClickAsync();
        await Expect(opened.GetByRole(AriaRole.Combobox, new() { Name = "Trigger" })).ToBeFocusedAsync();
        await Expect(opened.GetByRole(AriaRole.Tab, new() { Name = "Rules" })).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(tooltips).ToHaveCountAsync(0);
        var rulesWidth = (await opened.BoundingBoxAsync())!.Width;
        await opened.GetByRole(AriaRole.Tab, new() { Name = "Sentence" }).ClickAsync();
        await Expect(opened.Locator("[data-sentence]")).ToBeVisibleAsync();
        (await opened.BoundingBoxAsync())!.Width.Should().BeApproximately(rulesWidth, 0.5f);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(opened).ToBeHiddenAsync();
        // Enter on the node opens it the same way, now on Sentence: the focus moves after a key, and still no tooltip opens.
        await automation.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(opened.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Trigger: ") })).ToBeFocusedAsync();
        await Expect(tooltips).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(opened).ToBeHiddenAsync();
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
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
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
        await newJob.GetByLabel("Country", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "United Kingdom" });
        await newJob.GetByRole(AriaRole.Button, new() { Name = "Create job" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Warehouse associate, nights");
        await Page.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^Edit pipeline") }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Warehouse associate, nights pipeline");
        // The template the workflow came from is a header fact (W3).
        await Expect(HeaderBand).ToContainTextAsync($"{templateName} v1");

        // Try it on the job's copy: the checks flag something, the tag goes on, and the screen says only that a person
        // decides, never a level.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Try it" }).ClickAsync();
        var tryIt = Page.GetByRole(AriaRole.Tabpanel, new() { Name = "Try it" });
        await Expect(tryIt.GetByRole(AriaRole.Heading, new() { Level = 3 })).ToHaveTextAsync("Who is applying?");
        await Expect(tryIt).ToContainTextAsync("Nothing real is created and no one is emailed.");
        await tryIt.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Something to check") }).ClickAsync();
        await Expect(tryIt.GetByRole(AriaRole.Heading, new() { Level = 3 })).ToHaveTextAsync("Applied");
        await Expect(tryIt).ToContainTextAsync("The checks flagged something about Jordan. A person looks and decides what happens.");
        await Expect(tryIt).ToContainTextAsync("Adds the tag \"needs-review\"");
        await Expect(tryIt).Not.ToContainTextAsync("Verify");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-workflow-simulate-light");
    }

    [Fact]
    public async Task A_recruiter_starts_from_Trade_role_renames_a_stage_changes_a_time_limit_tries_a_good_fit_to_the_end_and_publishes()
    {
        var email = await OnboardAsync();
        await Page.SetViewportSizeAsync(1440, 1040);
        await SignInAsync(email);

        // Start from: four cards, each its name, one example line and its stages as text.
        await Page.GotoAsync(DashboardUrl("/templates/new"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("New hiring template");
        var cards = Page.GetByRole(AriaRole.Group, new() { Name = "Starting point" });
        await Expect(cards).ToContainTextAsync("Plumber, electrician, driver");
        await Expect(cards).ToContainTextAsync("Applied, Phone call, Trial shift, Offer");
        await Expect(cards).ToContainTextAsync("Hospitality");
        await Expect(cards).ToContainTextAsync("Barista, line cook, server");
        await Expect(cards).ToContainTextAsync("Applied, Screening, Trial shift, Offer");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-template-start-from-light");
        await cards.GetByText("Trade role with a trial shift").ClickAsync();
        await Page.GetByLabel("Template name").FillAsync("Service plumber");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Use this starting point" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Service plumber");
        await ExpectNoProblemsAsync();

        // The Pipeline tab, first, counts what happens at each stage in chips, never sentences.
        var stages = Page.GetByRole(AriaRole.Region, new() { Name = "Stages" });
        await Expect(stages).ToContainTextAsync("Trial shift");
        await Expect(stages).ToContainTextAsync("Respond within 5 days");
        await Expect(stages.GetByRole(AriaRole.Button, new() { Name = "1 email at Applied" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-template-pipeline-light");

        // Rename a stage and change its time limit in the stage's sheet.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Pipeline" }).ClickAsync();
        await Page.GetByRole(AriaRole.Tabpanel, new() { Name = "Pipeline" }).GetByRole(AriaRole.Button, new() { Name = "Trial shift", Exact = true }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Trial shift" });
        await sheet.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true }).FillAsync("Paid trial shift");
        await sheet.GetByRole(AriaRole.Radio, new() { Name = "3 days" }).ClickAsync();
        await sheet.GetByRole(AriaRole.Button, new() { Name = "About Respond within" }).ClickAsync();
        await Expect(Page.Locator("[data-info]")).ToContainTextAsync("After that they show as Overdue. Nothing happens to the candidate.");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("[data-info]")).ToHaveCountAsync(0);
        await Expect(sheet).ToBeVisibleAsync();
        await Expect(sheet.Locator("[data-when-overdue]")).ToContainTextAsync("Tell the team");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-template-stage-sheet-light");
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();
        await Expect(sheet).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Tabpanel, new() { Name = "Pipeline" })).ToContainTextAsync("Respond within 3 days");

        // A stage's sheet names its automations, and a name lands on that automation, on the tab where automations are edited.
        await Page.GetByRole(AriaRole.Tabpanel, new() { Name = "Pipeline" }).GetByRole(AriaRole.Button, new() { Name = "Applied", Exact = true }).ClickAsync();
        var listed = Page.GetByRole(AriaRole.Dialog, new() { Name = "Applied" }).GetByRole(AriaRole.Region, new() { Name = "Automations" }).GetByRole(AriaRole.Link);
        await Expect(listed.Last).ToHaveTextAsync("Edit in Automations");
        var automationName = (await listed.First.InnerTextAsync()).Trim();
        await listed.First.ClickAsync();
        var shown = Page.GetByRole(AriaRole.Dialog, new() { Name = automationName, Exact = true });
        await Expect(shown.GetByRole(AriaRole.Tab, new() { Name = "Sentence" })).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(shown).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "Automations" })).ToHaveAttributeAsync("aria-selected", "true");

        // An automation with a blank tag pauses the Try it tab, with the fix inline.
        await Page.GetByRole(AriaRole.Tabpanel, new() { Name = "Automations" }).GetByRole(AriaRole.Button, new() { Name = "Add an automation to Applied" }).ClickAsync();
        await Page.GetByRole(AriaRole.Dialog, new() { Name = "Add an automation" }).GetByRole(AriaRole.Button, new() { Name = "Write your own" }).ClickAsync();
        var drawer = Page.GetByRole(AriaRole.Dialog);
        await drawer.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true }).FillAsync("Welcome tag");
        await ChooseAsync(drawer, "Trigger: someone moves into", "Someone applies");
        // A new automation starts complete (it asks the team to review), so a blank tag is one the person chooses to add.
        await ChooseAsync(drawer, "Action 1: ask", "Add a tag");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(drawer).ToBeHiddenAsync();
        await ExpectProblemAsync("The tag in \"Welcome tag\" has no name. Type one, or remove that step.");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Fix 1 thing" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true })).ToBeDisabledAsync();
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Try it" }).ClickAsync();
        var drive = Page.GetByRole(AriaRole.Tabpanel, new() { Name = "Try it" });
        await Expect(drive).ToContainTextAsync("Paused until 1 thing is fixed");
        await Expect(drive).Not.ToContainTextAsync("Who is applying?");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Fix 1 thing" })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-template-test-paused-light");

        // Type the tag, and Try it is back: a good fit, clicked through one stage at a time to the end.
        await drive.GetByRole(AriaRole.Textbox, new() { Name = "Tag name" }).FillAsync("welcome");
        await drive.GetByRole(AriaRole.Button, new() { Name = "Add the tag" }).ClickAsync();
        await Expect(drive.GetByRole(AriaRole.Heading, new() { Level = 3 })).ToHaveTextAsync("Who is applying?");
        await ExpectNoProblemsAsync();
        await drive.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^A good fit") }).ClickAsync();
        await Expect(drive.GetByRole(AriaRole.Heading, new() { Level = 3 })).ToHaveTextAsync("Applied");
        await Expect(drive.GetByRole(AriaRole.List, new() { Name = "Pipeline" }).Locator("[aria-current=step]")).ToContainTextAsync("Applied");
        // The email as an inbox shows it, with Jordan's name filled in; a click opens it in full.
        var inbox = drive.Locator("[data-try-it-email]").First;
        await Expect(inbox.Locator("[data-try-it-subject]")).ToBeVisibleAsync();
        await inbox.ClickAsync();
        var letter = Page.GetByRole(AriaRole.Dialog);
        await Expect(letter).ToContainTextAsync("What the candidate gets. Nothing is sent.");
        await Expect(letter.Locator("[data-email-subject]")).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(letter).ToBeHiddenAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-template-try-it-light");

        // Each press moves Jordan one stage on, until the last offers the whole process. A disabled button waits for the
        // stage it is moving to, so each press lands on the next stage's button.
        var move = drive.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Move Jordan to ") });
        for (var pressed = 0; await move.CountAsync() > 0; pressed++)
        {
            pressed.Should().BeLessThan(10, "the pipeline has fewer stages than that");
            await move.ClickAsync();
            await Expect(drive.Locator("[aria-busy=true]")).ToHaveCountAsync(0);
        }
        await drive.GetByRole(AriaRole.Button, new() { Name = "See the whole process" }).ClickAsync();
        await Expect(drive.GetByRole(AriaRole.Heading, new() { Level = 3 })).ToHaveTextAsync("That's your hiring process");
        await Expect(drive.GetByRole(AriaRole.Button, new() { Name = "Looks good, publish" })).ToBeEnabledAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-template-try-it-end-light");

        // The three screens on a phone and in the dark theme: no sideways scroll, no accessibility violations.
        await Page.SetViewportSizeAsync(390, 844);
        foreach (var (tab, file) in new[] { ("Pipeline", "pipeline"), ("Try it", "test") })
        {
            await Page.GetByRole(AriaRole.Tab, new() { Name = tab }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Tabpanel, new() { Name = tab })).ToBeVisibleAsync();
            await ExpectNoSidewaysScrollAsync();
            await ExpectNoAccessibilityViolationsAsync();
            await SaveScreenshotAsync(Page, $"dashboard-template-{file}-390");
        }
        await Page.SetViewportSizeAsync(1440, 1040);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Switch to dark theme" }).ClickAsync();
        foreach (var (tab, file) in new[] { ("Pipeline", "pipeline"), ("Try it", "test") })
        {
            await Page.GetByRole(AriaRole.Tab, new() { Name = tab }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Tabpanel, new() { Name = tab })).ToBeVisibleAsync();
            await ExpectNoAccessibilityViolationsAsync();
            await SaveScreenshotAsync(Page, $"dashboard-template-{file}-dark");
        }
        await Page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" }).ClickAsync();

        // The end of Try it publishes, as the header's Publish does.
        await drive.GetByRole(AriaRole.Button, new() { Name = "Looks good, publish" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Published version 1." })).ToBeVisibleAsync();
        await Expect(HeaderBand).ToContainTextAsync("Published");
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Pipeline" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Stages" })).ToContainTextAsync("Paid trial shift");
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
        // The jobs list marks the job with a pill, and the job's header has the newer version as its own fact.
        var row = Page.GetByRole(AriaRole.Table, new() { Name = "Jobs" }).GetByRole(AriaRole.Row).Filter(new() { HasText = "Retail assistant, weekends" });
        await Expect(row).ToContainTextAsync("Template update");
        await row.GetByRole(AriaRole.Link, new() { Name = "Retail assistant, weekends" }).ClickAsync();
        await Expect(Page.Locator("[data-slot='header-band']")).ToContainTextAsync("Retail assistant v1");
        await Expect(Page.Locator("[data-slot='header-band']")).ToContainTextAsync("Retail assistant v2, See what changes");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Edit pipeline" }).ClickAsync();
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
        await Expect(HeaderBand.Locator("[data-slot=fact]").Filter(new() { Has = Page.GetByText("Version", new() { Exact = true }) }).Locator("dd")).ToHaveTextAsync("2");
        await Expect(update).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Stages" })).ToContainTextAsync("Respond within 2 days");
    }

    [Fact]
    public async Task Publishing_says_the_job_keeps_its_version_and_Review_updates_moves_it_to_the_new_one()
    {
        var email = await OnboardAsync();
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        var template = await ReadAsync(await session.PostAsync("/v1/templates", new { name = "Front of house" }, Ct), HttpStatusCode.Created);
        var templateId = template.GetProperty("id").GetGuid();
        var first = await ReadAsync(await session.PostAsync($"/v1/templates/{templateId}/versions/1/publish", new { }, Ct), HttpStatusCode.OK);
        var job = await ReadAsync(
            await session.PostAsync("/v1/jobs", new { title = "Host, evenings", templateVersionId = first.GetProperty("id").GetGuid() }, Ct),
            HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();

        // Version 2 is a draft that shortens Screening's time limit to two days.
        var definition = first.GetProperty("definition").Deserialize<Dictionary<string, JsonElement>>()!;
        var workflow = JsonSerializer.SerializeToNode(definition["workflow"])!.AsObject();
        workflow["stages"]![1]!["slaDays"] = 2;
        await ReadAsync(
            await session.PostAsync($"/v1/templates/{templateId}/versions", new { definition = new { workflow, postingDefaults = definition["postingDefaults"] } }, Ct),
            HttpStatusCode.Created);

        await SignInAsync(email);
        await Page.GotoAsync(DashboardUrl($"/templates/{templateId}"));
        await Expect(HeaderBand).ToContainTextAsync("Draft, unpublished changes");
        await Expect(HeaderBand).ToContainTextAsync(
            "Publishing makes version 2 the version new jobs get. Your 1 job stays on version 1 until you update it.");
        var publish = Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true });
        await Expect(publish).ToBeEnabledAsync();
        await publish.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Published version 2." })).ToBeVisibleAsync();

        // The job is on version 1 now that version 2 is live: Used by and the header line say so.
        await Expect(HeaderBand.GetByRole(AriaRole.Button, new() { Name = "1 job, 1 on an older version" })).ToBeVisibleAsync();
        await Expect(HeaderBand).ToContainTextAsync("1 job uses an older version.");
        await HeaderBand.GetByRole(AriaRole.Button, new() { Name = "Review updates" }).ClickAsync();
        var review = Page.GetByRole(AriaRole.Dialog, new() { Name = "Review updates" });
        var jobs = review.GetByRole(AriaRole.List, new() { Name = "Jobs on an older version" });
        await Expect(jobs).ToContainTextAsync("Host, evenings");
        await Expect(jobs.GetByRole(AriaRole.Link, new() { Name = "See what changes for Host, evenings" }))
            .ToHaveAttributeAsync("href", $"/jobs/{jobId}/workflow?review=update");
        await ExpectNoAccessibilityViolationsAsync();
        await jobs.GetByRole(AriaRole.Button, new() { Name = "Update Host, evenings" }).ClickAsync();

        // Update asks first, with what changes, because it replaces the job's own changes too.
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Apply version 2 of Front of house to Host, evenings?" });
        await Expect(dialog.GetByRole(AriaRole.List, new() { Name = "Differences" })).ToContainTextAsync("stages[screening].slaDays");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Apply update" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(review.GetByRole(AriaRole.Status)).ToContainTextAsync("Updated Host, evenings to version 2.");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(HeaderBand).Not.ToContainTextAsync("uses an older version");

        // The job's header has no update left to offer.
        await Page.GotoAsync(DashboardUrl($"/jobs/{jobId}"));
        await Expect(HeaderBand).ToContainTextAsync("Front of house v2");
        await Expect(HeaderBand).Not.ToContainTextAsync("Update available");
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
        await ExpectNoProblemsAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Copy template" })).ToBeVisibleAsync();
        var pipeline = Page.GetByRole(AriaRole.Tabpanel, new() { Name = "Pipeline" });
        await Expect(pipeline.GetByRole(AriaRole.List, new() { Name = "Stages in order" })).ToBeVisibleAsync();
        await Expect(pipeline.Locator("input, select, textarea")).ToHaveCountAsync(0);
        // Nothing edits: the only buttons are the counts that open their lists.
        await Expect(pipeline.Locator("button:not([data-chip])")).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-template-screenhire-light");

        // Its earlier version, from the Versions sheet; a ScreenHire template's sheet has no All changes.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Versions" }).ClickAsync();
        var versions = Page.GetByRole(AriaRole.Dialog, new() { Name = "Versions" });
        await Expect(versions.GetByRole(AriaRole.Tab, new() { Name = "All changes" })).ToHaveCountAsync(0);
        await versions.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^Version 1 ") }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?version=1$"));
        await Expect(HeaderBand).ToContainTextAsync("Published, version 1");

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
        await Expect(Page.Locator("[data-slot='action-bar']").GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true })).ToBeVisibleAsync();
        await HeaderBand.GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Save draft" })).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        // The pointer still rests on More actions, which shows its tooltip once the menu lets go of the page: a hover, as it
        // should be. The screenshot is of the page, so the pointer moves off to the title.
        await Page.GetByRole(AriaRole.Heading, new() { Level = 1 }).HoverAsync();
        await ExpectNoProblemsAsync();
        await ExpectNoSidewaysScrollAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-template-builder-390");
    }

    [Fact]
    public async Task A_recruiter_builds_an_automation_in_the_graph_with_the_keyboard_and_finds_it_in_the_overview()
    {
        const string Blank = "The tag in \"Fast-track referrals\" has no name. Type one, or remove that step.";
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
        await ExpectNoProblemsAsync();
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Automations" }).ClickAsync();
        var graph = Page.GetByRole(AriaRole.Tabpanel, new() { Name = "Automations" });
        ILocator Node(string name) => graph.GetByRole(AriaRole.Button, new() { Name = name });

        // From here on, the keyboard only: Tab into the graph, the arrow keys from node to node.
        await TabUntilFocusedAsync(Node("Stage Applied"));
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(Node("Stage Screening")).ToBeFocusedAsync();

        // A workflow with no automations says what to do, with one action: it opens the ready-made recipes.
        await Expect(graph).ToContainTextAsync("No automations yet");
        await TabUntilFocusedAsync(graph.GetByRole(AriaRole.Button, new() { Name = "Add an automation", Exact = true }));
        await Page.Keyboard.PressAsync("Enter");
        var recipes = Page.GetByRole(AriaRole.Dialog, new() { Name = "Add an automation" });
        await Expect(recipes).ToContainTextAsync("Thank people when they apply");
        await TabUntilFocusedAsync(recipes.GetByRole(AriaRole.Button, new() { Name = "Write your own" }));
        await Page.Keyboard.PressAsync("Enter");

        // The automation as a sentence, opened at its name, its trigger an arriving application.
        var sheet = Page.GetByRole(AriaRole.Dialog);
        var name = sheet.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true });
        await Expect(name).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Control+A");
        await Page.Keyboard.TypeAsync("Fast-track referrals");
        await Expect(sheet.GetByRole(AriaRole.Button, new() { Name = "Trigger: someone applies" })).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(sheet).ToBeHiddenAsync();

        // The focus is on the new automation. A plus on its line adds a condition, and opens the sentence at it.
        await Expect(Node("When Fast-track referrals")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(Node("Add a condition to Fast-track referrals")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Enter");
        var fact = sheet.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Condition 1: what to check") });
        await Expect(fact).ToBeFocusedAsync();
        // The blank for its value opens a field, which the keyboard fills. Escape closes the field first, then the sheet.
        var value = sheet.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Condition 1: value") });
        await TabUntilFocusedAsync(value);
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.GetByRole(AriaRole.Textbox, new() { Name = "Condition 1: value" })).ToBeFocusedAsync();
        await Page.Keyboard.TypeAsync("referral");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.GetByRole(AriaRole.Textbox, new() { Name = "Condition 1: value" })).ToBeHiddenAsync();
        // The action it starts with asks the team to review. Adding a tag instead leaves the tag blank.
        await ChooseAsync(sheet, "Action 1: ask", "Add a tag");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(sheet).ToBeHiddenAsync();

        // The focus is back on the automation, and the problem shows on the node it is about.
        await Expect(Node("When Fast-track referrals")).ToBeFocusedAsync();
        await ExpectProblemAsync(Blank);
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(Node("If in Fast-track referrals")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        var action = Node("Then action 1 in Fast-track referrals");
        await Expect(action).ToBeFocusedAsync();
        await Expect(action).ToContainTextAsync(Blank);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-workflow-graph-problem-1440");

        // A full-page screenshot can shrink the page for a moment, past the phone's width and back, which swaps the map for the list.
        // The node is drawn again once the page is wide; the focus goes back to it, as a keyboard user would.
        await action.FocusAsync();
        await Expect(action).ToBeFocusedAsync();
        // Pressing the action opens the same sentence at that step; its tag is the next blank, and typing it clears the problem.
        await Page.Keyboard.PressAsync("Enter");
        await Expect(sheet.GetByRole(AriaRole.Button, new() { Name = "Action 1: add the tag" })).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(sheet.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Action 1: tag name") })).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.GetByRole(AriaRole.Textbox, new() { Name = "Action 1: tag name" })).ToBeFocusedAsync();
        await Page.Keyboard.TypeAsync("fast-track");
        await ExpectNoProblemsAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-workflow-sentence-1440");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(sheet.GetByRole(AriaRole.Button, new() { Name = "Action 1: tag name: fast-track" })).ToBeVisibleAsync();
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();
        await Expect(sheet).ToBeHiddenAsync();
        await Expect(Node("Then action 1 in Fast-track referrals")).ToBeVisibleAsync();
        // The focus returns to the node that opened the sheet, or, when the page shrank during the screenshot and that node was drawn
        // again, to the automation's trigger: either way it is on a node of the map, not on the page.
        await Expect(Page.Locator(".svelte-flow__node :focus")).ToHaveCountAsync(1);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-workflow-graph-edit-1440");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved version 1." })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Pipeline" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Stages" }).GetByRole(AriaRole.Button, new() { Name = "1 automation at Applied" })).ToBeVisibleAsync();

        // The map at a laptop's and a tablet's width, and on a phone, which has the list in its place. The map opens at full size.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Automations" }).ClickAsync();
        await Page.Mouse.MoveAsync(0, 0);
        foreach (var (width, height) in new[] { (1024, 900), (768, 1024) })
        {
            await Page.SetViewportSizeAsync(width, height);
            await Expect(Node("Add an automation to Screening")).ToBeVisibleAsync();
            await ExpectNoSidewaysScrollAsync();
            await SaveScreenshotAsync(Page, $"dashboard-workflow-graph-{width}");
        }
        await Page.SetViewportSizeAsync(390, 844);
        await Expect(graph.GetByRole(AriaRole.Group, new() { Name = "View" })).ToHaveCountAsync(0);
        await Expect(graph.Locator(".svelte-flow")).ToHaveCountAsync(0);
        var row = graph.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Fast-track referrals") });
        await Expect(row).ToContainTextAsync("When someone applies");
        await Expect(graph.GetByRole(AriaRole.Button, new() { Name = "Add automation" })).ToBeVisibleAsync();
        await ExpectNoSidewaysScrollAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-workflow-graph-390");

        // The same sentence on a phone: it opens from the row, wraps inside the sheet and closes with Done.
        await row.ClickAsync();
        await Expect(sheet.GetByRole(AriaRole.Button, new() { Name = "Trigger: someone applies" })).ToBeVisibleAsync();
        await ExpectNoSidewaysScrollAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-workflow-sentence-390");
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();
        await Expect(sheet).ToBeHiddenAsync();
    }

    // Opens a blank of the automation's sentence and presses one of its choices.
    private async Task ChooseAsync(ILocator sheet, string blank, string choice)
    {
        await sheet.GetByRole(AriaRole.Button, new() { Name = blank }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = choice, Exact = true }).ClickAsync();
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
        using var onboarded = await session.PostAsync("/v1/auth/onboarding", new { name = "Riley Harper", organizationName = "Northgate Depot", slug = $"test-{Guid.NewGuid():N}" }, Ct);
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
