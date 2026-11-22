using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// A follow-up questionnaire end to end (workflow-engine.md §8): moving a candidate into Screening makes an automation
/// email them a link, the candidate answers on the careers site with JavaScript off, and a QuestionnaireCompleted
/// automation tags the application, which the recruiter sees on the candidate's page with the answers. The test signs
/// in as a new person with a new organization and sets the job up through the API as that person.
/// </summary>
public sealed partial class FollowUpQuestionnaireTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] Britain = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_automation_sends_a_questionnaire_the_candidate_answers_without_javascript_and_the_recruiter_sees_the_tag()
    {
        var owner = ApiSession.NewEmail("e2e-followup");
        var candidate = ApiSession.NewEmail("candidate");
        var (applicationId, title) = await SceneAsync(owner, candidate);

        var sent = await Fixture.Mailpit.WaitForMessagesAsync(candidate, 1, Ct);
        sent[0].Subject.Should().Be($"A few more questions about your application for {title}");
        var link = QuestionnaireLink().Match(sent[0].Text);
        link.Success.Should().BeTrue($"the email carries the link: {sent[0].Text}");

        // The candidate's browser runs no JavaScript: the page is a plain form that posts.
        await WithoutJavaScriptAsync();
        var response = await Page.GotoAsync(link.Value);
        response!.Status.Should().Be(200);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Availability");
        (await Page.Locator("script:not([type='application/ld+json'])").CountAsync()).Should().Be(0, "the page ships no JavaScript");
        await ExpectNoAccessibilityViolationsAsync();

        // A required question left blank comes back with the problem, and what was typed is kept.
        await Page.GetByLabel("Yes").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send answers" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Answer this question.");
        await Expect(Page.GetByLabel("Yes")).ToBeCheckedAsync();
        await ExpectNoAccessibilityViolationsAsync();

        await Page.GetByLabel("When could you start?").FillAsync("In two weeks");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send answers" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Answers sent");
        Page.Url.Should().NotContain(link.Groups["token"].Value, "the confirmation's address carries no token");
        await ExpectNoAccessibilityViolationsAsync();

        // The link answers once, and a link that was never issued says so plainly.
        await Page.GotoAsync(link.Value);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("You have answered these questions");
        var unknown = await Page.GotoAsync(link.Value.Replace(link.Groups["token"].Value, "CfDJ8NeverIssuedToken000000000", StringComparison.Ordinal));
        unknown!.Status.Should().Be(404);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("We could not find these questions");
        await ExpectNoAccessibilityViolationsAsync();

        await WithJavaScriptAsync();
        await SignInAsync(owner);
        await Page.GotoAsync(DashboardUrl($"/applications/{applicationId}"));
        var tags = Page.Locator("[data-slot='header-band']").GetByRole(AriaRole.List, new() { Name = "Tags", Exact = true });
        await Expect(tags).ToContainTextAsync("answered", new() { Timeout = 30_000 });
        // The tag is a pill in the candidate's header; the form and its answers are on the Application tab, in a row that opens.
        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Application") }).ClickAsync();
        var followUp = Page.GetByRole(AriaRole.Region, new() { Name = "Forms", Exact = true });
        await followUp.GetByRole(AriaRole.Button, new() { NameRegex = new("^Availability") }).ClickAsync();
        await Expect(followUp).ToContainTextAsync("In two weeks");
        await Expect(followUp).ToContainTextAsync("Do you hold a forklift licence?");
        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Activity") }).ClickAsync();
        var activity = Page.GetByRole(AriaRole.Region, new() { Name = "Activity" });
        await Expect(activity).ToContainTextAsync("Answered the \"Availability\" questionnaire");
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task A_questionnaire_in_sections_goes_a_page_at_a_time_and_saved_answers_come_back_through_the_link()
    {
        var owner = ApiSession.NewEmail("e2e-followup");
        var candidate = ApiSession.NewEmail("candidate");
        await SceneAsync(owner, candidate, sectioned: true);
        var link = QuestionnaireLink().Match((await Fixture.Mailpit.WaitForMessagesAsync(candidate, 1, Ct))[0].Text);

        await WithoutJavaScriptAsync();
        await Page.GotoAsync(link.Value);
        await Expect(Page.Locator("[data-page-progress]")).ToContainTextAsync("Step 1 of 2");
        await Expect(Page.Locator("[data-page-progress]")).ToContainTextAsync("0 of 2 answered");
        await Expect(Page.Locator("#questions-heading")).ToHaveTextAsync("When you can start");
        await Expect(Page.GetByLabel("Do you hold a forklift licence?")).ToHaveCountAsync(0);
        await Page.GetByLabel("When could you start?").FillAsync("In two weeks");

        // Saved to finish later: the same link opens the answers again.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save and finish later" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).First).ToContainTextAsync("Saved. Open the link in your email to finish.");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GotoAsync(link.Value);
        await Expect(Page.GetByText("Welcome back. Your saved answers are filled in.")).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("When could you start?")).ToHaveValueAsync("In two weeks");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Expect(Page.Locator("#questions-heading")).ToHaveTextAsync("Your licence");
        await Expect(Page.Locator("[data-page-progress]")).ToContainTextAsync("Step 2 of 2");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send answers" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Answer this question.");
        await Page.GetByLabel("Yes").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send answers" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Answers sent");
    }

    [Fact]
    public async Task A_form_built_in_the_library_and_sent_at_Screening_reaches_the_candidate_and_its_answers_show_on_the_Application_tab()
    {
        var owner = ApiSession.NewEmail("e2e-forms");
        var candidate = ApiSession.NewEmail("candidate");
        var title = $"Line cook {Guid.NewGuid().ToString("N")[..4]}";
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { name = "Riley Harper", organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);
        var template = await ReadAsync(await session.PostAsync("/v1/templates", new { name = $"{title} template" }, Ct), HttpStatusCode.Created);
        var templateId = template.GetProperty("id").GetGuid();

        // The form is built once, in the library, and published.
        await SignInAsync(owner);
        await Page.GotoAsync(DashboardUrl("/templates?tab=forms"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "New template" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Form", Exact = true }).ClickAsync();
        var create = Page.GetByRole(AriaRole.Dialog, new() { Name = "New form" });
        await create.GetByLabel("Name").FillAsync("Screening questionnaire");
        await create.GetByRole(AriaRole.Button, new() { Name = "Create form" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Screening questionnaire");
        var questions = Page.GetByRole(AriaRole.Region, new() { Name = "Questions", Exact = true });
        await questions.GetByRole(AriaRole.Button, new() { NameRegex = new("^Edit question 1") }).ClickAsync();
        await questions.GetByRole(AriaRole.Textbox, new() { Name = "Question 1", Exact = true }).FillAsync("Which shifts can you work?");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved as a draft of version 1." })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Published version 1." })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // The hiring template takes it from the library and sends it when a candidate enters Screening.
        await Page.GotoAsync(DashboardUrl($"/templates/{templateId}?tab=forms"));
        var forms = Page.GetByRole(AriaRole.Group, new() { Name = "Forms", Exact = true });
        await forms.GetByRole(AriaRole.Button, new() { Name = "New form" }).ClickAsync();
        var add = Page.GetByRole(AriaRole.Dialog, new() { Name = "Add a form" });
        await add.GetByRole(AriaRole.Button, new() { Name = "Add Screening questionnaire" }).ClickAsync();
        var card = forms.GetByRole(AriaRole.Button, new() { NameRegex = new("^Screening questionnaire") });
        await Expect(card).ToContainTextAsync("Only when sent by hand");
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "When it's sent" }).SelectOptionAsync("stage");
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Stage", Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Screening" });
        await Expect(card).ToContainTextAsync("Sent when they enter Screening");
        await Expect(card).ToContainTextAsync("1 question");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Pipeline" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Stages" })).ToContainTextAsync("Sends \"Screening questionnaire\"");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved version 1." })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Published version 1." })).ToBeVisibleAsync();

        // A job from the template, a candidate moved into Screening: the form's email arrives, and the candidate answers.
        var version = await ReadAsync(await session.Client.GetAsync($"/v1/templates/{templateId}/versions/1", Ct), HttpStatusCode.OK);
        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title,
                templateVersionId = version.GetProperty("id").GetGuid(),
                description = "<p>Cook on the line.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Britain,
            },
            Ct), HttpStatusCode.Created);
        var application = await ReadAsync(await session.PostAsync(
            $"/v1/jobs/{job.GetProperty("id").GetGuid()}/applications", new { candidate = new { fullName = "Riley Chen", email = candidate } }, Ct), HttpStatusCode.Created);
        var applicationId = application.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/applications/{applicationId}/move", new { toStageId = "screening" }, Ct), HttpStatusCode.OK);
        var link = QuestionnaireLink().Match((await Fixture.Mailpit.WaitForMessagesAsync(candidate, 1, Ct))[0].Text);
        link.Success.Should().BeTrue();
        await Page.GotoAsync(link.Value);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Screening questionnaire");
        await Page.GetByLabel("Which shifts can you work?").FillAsync("Weekends and evenings");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send answers" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Answers sent");

        // The recruiter reads the answers under the form's own row on the Application tab.
        await Page.GotoAsync(DashboardUrl($"/applications/{applicationId}"));
        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Application") }).ClickAsync();
        var sent = Page.GetByRole(AriaRole.Region, new() { Name = "Forms", Exact = true });
        var row = sent.GetByRole(AriaRole.Button, new() { NameRegex = new("^Screening questionnaire") });
        await Expect(row).ToContainTextAsync("Answered");
        await row.ClickAsync();
        await Expect(sent).ToContainTextAsync("Weekends and evenings");
        await ExpectNoAccessibilityViolationsAsync();
    }

    // A new organization whose job sends "Availability" on entering Screening and tags the application when it is
    // answered; one candidate added and moved into Screening through the API, which sends the email. `sectioned` puts
    // each of its two questions on a page of its own.
    private async Task<(Guid ApplicationId, string Title)> SceneAsync(string owner, string candidate, bool sectioned = false)
    {
        var title = $"Warehouse lead {Guid.NewGuid().ToString("N")[..4]}";
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { name = "Riley Harper", organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);

        // A new template's draft is the default workflow; add the follow-up and its automations, then publish it.
        var template = await ReadAsync(await session.PostAsync("/v1/templates", new { name = $"{title} template" }, Ct), HttpStatusCode.Created);
        var templateId = template.GetProperty("id").GetGuid();
        var draft = await ReadAsync(await session.Client.GetAsync($"/v1/templates/{templateId}/versions/1", Ct), HttpStatusCode.OK);
        await ReadAsync(await session.PutAsync($"/v1/templates/{templateId}/versions/1", new { definition = Definition(draft.GetProperty("definition"), sectioned) }, Ct), HttpStatusCode.OK);
        var version = await ReadAsync(await session.PostAsync($"/v1/templates/{templateId}/versions/1/publish", new { }, Ct), HttpStatusCode.OK);

        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title,
                templateVersionId = version.GetProperty("id").GetGuid(),
                description = "<p>Run the warehouse floor and the team on it.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Britain,
            },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();
        var application = await ReadAsync(await session.PostAsync(
            $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = "Riley Chen", email = candidate } }, Ct), HttpStatusCode.Created);
        var applicationId = application.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/applications/{applicationId}/move", new { toStageId = "screening" }, Ct), HttpStatusCode.OK);
        return (applicationId, title);
    }

    // The definition with one follow-up questionnaire and the two automations around it.
    private static System.Text.Json.Nodes.JsonObject Definition(JsonElement draft, bool sectioned)
    {
        var definition = System.Text.Json.Nodes.JsonNode.Parse(draft.GetRawText())!.AsObject();
        var workflow = definition["workflow"]!.AsObject();
        workflow["followUpQuestionnaires"] = System.Text.Json.Nodes.JsonNode.Parse("""
            [{
              "id": "availability",
              "name": "Availability",
              "questions": [
                { "id": "start_date", "type": "ShortText", "label": "When could you start?", "required": true },
                { "id": "forklift_licence", "type": "YesNo", "label": "Do you hold a forklift licence?", "required": true }
              ]
            }]
            """);
        if (sectioned)
        {
            workflow["followUpQuestionnaires"]![0]!["sections"] = System.Text.Json.Nodes.JsonNode.Parse("""
                [
                  { "id": "when", "name": "When you can start", "questionIds": ["start_date"] },
                  { "id": "licence", "name": "Your licence", "questionIds": ["forklift_licence"] }
                ]
                """);
        }

        workflow["automations"] = System.Text.Json.Nodes.JsonNode.Parse("""
            [
              { "id": "send-on-screening", "name": "Ask about availability",
                "trigger": { "type": "StageEntered", "stageId": "screening" },
                "actions": [{ "type": "SendQuestionnaire", "questionnaireId": "availability" }] },
              { "id": "tag-on-answers", "name": "Tag when answered",
                "trigger": { "type": "QuestionnaireCompleted", "questionnaireId": "availability" },
                "actions": [{ "type": "AddTag", "tag": "answered" }] }
            ]
            """);
        return definition;
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

    [GeneratedRegex(@"https?://\S+/questionnaires/(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex QuestionnaireLink();
}
