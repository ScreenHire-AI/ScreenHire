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
        await using (var context = await Fixture.Browser.NewContextAsync(new() { JavaScriptEnabled = false, Locale = "en-US" }))
        {
            var page = await context.NewPageAsync();
            var response = await page.GotoAsync(link.Value);
            response!.Status.Should().Be(200);
            await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Availability");
            (await page.Locator("script:not([type='application/ld+json'])").CountAsync()).Should().Be(0, "the page ships no JavaScript");

            // A required question left blank comes back with the problem, and what was typed is kept.
            await page.GetByLabel("Yes").CheckAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Send answers" }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Answer this question.");
            await Expect(page.GetByLabel("Yes")).ToBeCheckedAsync();

            await page.GetByLabel("When could you start?").FillAsync("In two weeks");
            await page.GetByRole(AriaRole.Button, new() { Name = "Send answers" }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Answers sent");
            page.Url.Should().NotContain(link.Groups["token"].Value, "the confirmation's address carries no token");

            // The link answers once.
            await page.GotoAsync(link.Value);
            await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("You have answered these questions");
        }

        await SignInAsync(owner);
        await Page.GotoAsync(DashboardUrl($"/applications/{applicationId}"));
        var tags = Page.GetByRole(AriaRole.Region, new() { Name = "Tags" });
        await Expect(tags).ToContainTextAsync("answered", new() { Timeout = 30_000 });
        var followUp = Page.GetByRole(AriaRole.Region, new() { Name = "Follow-up: Availability" });
        await Expect(followUp).ToContainTextAsync("In two weeks");
        await Expect(followUp).ToContainTextAsync("Do you hold a forklift licence?");
        var activity = Page.GetByRole(AriaRole.Region, new() { Name = "Activity" });
        await Expect(activity).ToContainTextAsync("Answered the \"Availability\" questionnaire");
        await ExpectNoAccessibilityViolationsAsync();
    }

    // A new organization whose job sends "Availability" on entering Screening and tags the application when it is
    // answered; one candidate added and moved into Screening through the API, which sends the email.
    private async Task<(Guid ApplicationId, string Title)> SceneAsync(string owner, string candidate)
    {
        var title = $"Warehouse lead {Guid.NewGuid().ToString("N")[..4]}";
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);

        // A new template's draft is the default workflow; add the follow-up and its automations, then publish it.
        var template = await ReadAsync(await session.PostAsync("/v1/templates", new { name = $"{title} template" }, Ct), HttpStatusCode.Created);
        var templateId = template.GetProperty("id").GetGuid();
        var draft = await ReadAsync(await session.Client.GetAsync($"/v1/templates/{templateId}/versions/1", Ct), HttpStatusCode.OK);
        await ReadAsync(await session.PutAsync($"/v1/templates/{templateId}/versions/1", new { definition = Definition(draft.GetProperty("definition")) }, Ct), HttpStatusCode.OK);
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
    private static System.Text.Json.Nodes.JsonObject Definition(JsonElement draft)
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
