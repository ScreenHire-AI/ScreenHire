using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests.Automations;

/// <summary>
/// Follow-up questionnaires through the running stack (workflow-engine.md §8): a SendQuestionnaire automation emails a
/// signed link through the worker and Mailpit, the careers routes read and answer it, one request per application and
/// questionnaire, and completing it fires its QuestionnaireCompleted trigger once.
/// </summary>
public sealed partial class FollowUpQuestionnaireTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sent_questionnaire_is_one_request_answered_once_and_its_completion_fires_the_trigger_once()
    {
        var (recruiter, slug, job) = await JobWithFollowUpAsync("Follow-up journey");
        using var _ = recruiter;
        using var careers = fixture.CreateHttpClient("api");
        var email = ApiSession.NewEmail("followup");
        var applicationId = await ApplyAsync(careers, slug, job, email);

        // Entering Screening sends the questionnaire; entering it again sends the same request's link again.
        await MoveAsync(recruiter, applicationId, "screening");
        var first = await fixture.Mailpit.WaitForMessagesAsync(email, 1, Ct);
        first[0].Subject.Should().Be("A few more questions about your application for Follow-up journey");
        var token = TokenIn(first[0].Text, slug);
        await MoveAsync(recruiter, applicationId, "interview");
        await MoveAsync(recruiter, applicationId, "screening");
        var second = await fixture.Mailpit.WaitForMessagesAsync(email, 2, Ct);
        var secondToken = TokenIn(second.Single(m => !m.Text.Contains(token, StringComparison.Ordinal)).Text, slug);

        var detail = await WaitForAsync(recruiter, applicationId, d => d.At("followUps.0.timesSent").GetInt32() == 2, "the second send");
        detail.Count("followUps").Should().Be(1, "one request per application and questionnaire");
        detail.Text("followUps.0.questionnaireId").Should().Be("availability");
        detail.At("followUps.0.completedAt").ValueKind.Should().Be(JsonValueKind.Null);

        var page = await careers.GetFromJsonAsync<JsonElement>(QuestionnairePath(slug, token), Ct);
        page.At("status").GetString().Should().Be("Open");
        page.At("name").GetString().Should().Be("Availability");
        page.At("jobTitle").GetString().Should().Be("Follow-up journey");
        page.At("organization.slug").GetString().Should().Be(slug);
        page.At("questions").EnumerateArray().Select(q => q.GetProperty("id").GetString()).Should().Equal("start_date", "forklift_licence");
        page.GetRawText().Should().NotContainEquivalentOf("knockout");
        (await careers.GetFromJsonAsync<JsonElement>(QuestionnairePath(slug, secondToken), Ct)).At("status").GetString()
            .Should().Be("Open", "every link to an unanswered request works while it is unexpired");

        var missing = await careers.PostAsJsonAsync(QuestionnairePath(slug, token), Answers(("forklift_licence", "yes")), Ct);
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missing.Content.ReadFromJsonAsync<JsonElement>(Ct)).At("errors").EnumerateObject().Select(p => p.Name).Should().Equal("answers.start_date");

        (await careers.PostAsJsonAsync(QuestionnairePath(slug, token), Answers(("start_date", "Next month"), ("forklift_licence", "yes")), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await careers.PostAsJsonAsync(QuestionnairePath(slug, secondToken), Answers(("start_date", "Later"), ("forklift_licence", "no")), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "a link answers once, whichever send it came from");
        (await careers.GetFromJsonAsync<JsonElement>(QuestionnairePath(slug, token), Ct)).At("status").GetString().Should().Be("Answered");

        // Completion fires the QuestionnaireCompleted automation, which tags the application; redelivering its message
        // (same idempotency key) runs nothing twice.
        var run = await AutomationSamples.WaitForRunAsync(recruiter, applicationId, "tag-on-answers", "Completed");
        await using (var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct))
        {
            var completedEntry = run.GetProperty("eventId").GetGuid();
            await publisher.Services.GetRequiredService<IMessageBus>().PublishAsync(new RunAutomations(
                recruiter.OrganizationId, applicationId, TriggerType.QuestionnaireCompleted, null, completedEntry, DateTimeOffset.UtcNow,
                QuestionnaireId: "availability"));
        }

        detail = await WaitForAsync(recruiter, applicationId, d => d.At("tags").EnumerateArray().Any(t => t.GetString() == "answered"), "the tag");
        detail.At("followUps.0.completedAt").ValueKind.Should().Be(JsonValueKind.String);
        detail.At("followUps.0.questions").EnumerateArray().Select(q => q.GetProperty("label").GetString())
            .Should().Equal("When could you start?", "Do you hold a forklift licence?");
        detail.At("followUps.0.answers").EnumerateArray().Select(a => (a.GetProperty("questionId").GetString(), a.GetProperty("value").GetString()))
            .Should().Equal(("start_date", "Next month"), ("forklift_licence", "yes"));

        // Answered: entering Screening again sends nothing.
        await MoveAsync(recruiter, applicationId, "interview");
        await MoveAsync(recruiter, applicationId, "screening");
        await WolverineStore.WaitUntilAsync(async () =>
            (await recruiter.GetAsync($"/v1/applications/{applicationId}/automation-runs")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
                .Count(r => r.GetProperty("automationId").GetString() == "send-on-screening" && r.GetProperty("status").GetString() == "Completed") == 3,
            "the third send to finish", Ct);
        (await fixture.Mailpit.MessagesAsync(email, Ct)).Should().HaveCount(2, "an answered questionnaire is not sent again");

        var runs = (await recruiter.GetAsync($"/v1/applications/{applicationId}/automation-runs")).Expect(HttpStatusCode.OK).Body;
        runs.EnumerateArray().Count(r => r.GetProperty("automationId").GetString() == "tag-on-answers").Should().Be(1, "the trigger fires once");
        var timeline = (await recruiter.GetAsync($"/v1/applications/{applicationId}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray().ToList();
        timeline.Count(e => e.GetProperty("type").GetString() == "QuestionnaireSent").Should().Be(2);
        var completed = timeline.Single(e => e.GetProperty("type").GetString() == "QuestionnaireCompleted");
        completed.GetProperty("questionnaireId").GetString().Should().Be("availability");
        completed.GetProperty("questionnaireRequestId").GetGuid().Should().Be(detail.At("followUps.0.id").GetGuid());
        completed.GetProperty("id").GetGuid().Should().Be(run.GetProperty("eventId").GetGuid(), "the run is keyed on the completion entry");
    }

    [Fact]
    public async Task A_forged_expired_or_other_organizations_link_is_refused()
    {
        var (recruiter, slug, job) = await JobWithFollowUpAsync("Follow-up refusals");
        using var _ = recruiter;
        using var careers = fixture.CreateHttpClient("api");
        var email = ApiSession.NewEmail("refused");
        var applicationId = await ApplyAsync(careers, slug, job, email);
        await MoveAsync(recruiter, applicationId, "screening");
        var token = TokenIn((await fixture.Mailpit.WaitForMessagesAsync(email, 1, Ct))[0].Text, slug);
        var answers = Answers(("start_date", "Now"), ("forklift_licence", "yes"));

        // Forged: a changed character, or a token never issued.
        var forged = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');
        foreach (var bad in new[] { forged, "not-a-token" })
        {
            (await careers.GetAsync(QuestionnairePath(slug, bad), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await careers.PostAsJsonAsync(QuestionnairePath(slug, bad), answers, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // Another organization's careers route: its tenant cannot see this request, and the token names another organization.
        using var other = await fixture.SignedInWithOrganizationAsync();
        var otherSlug = (await other.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("slug");
        (await careers.GetAsync(QuestionnairePath(otherSlug, token), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await careers.PostAsJsonAsync(QuestionnairePath(otherSlug, token), answers, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/v1/applications/{applicationId}")).Expect(HttpStatusCode.NotFound);

        // Expired: the request's expiry is the authority, so the page can say so. Backdated as the table owner.
        await using (var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct)))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand("UPDATE questionnaire_requests SET expires_at = now() - interval '1 minute' WHERE application_id = @id", connection);
            command.Parameters.AddWithValue("id", applicationId);
            (await command.ExecuteNonQueryAsync(Ct)).Should().Be(1);
        }

        (await careers.GetFromJsonAsync<JsonElement>(QuestionnairePath(slug, token), Ct)).At("status").GetString().Should().Be("Expired");
        (await careers.PostAsJsonAsync(QuestionnairePath(slug, token), answers, Ct)).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await recruiter.GetAsync($"/v1/applications/{applicationId}")).At("followUps.0.completedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // The tenant's follow-up: a job whose workflow sends "Availability" on entering Screening and tags the application
    // when it is answered.
    private async Task<(Tenant Recruiter, string Slug, Guid Job)> JobWithFollowUpAsync(string title)
    {
        var recruiter = await fixture.SignedInWithOrganizationAsync();
        var definition = Samples.DefinitionWith(
            new JsonObject
            {
                ["id"] = "send-on-screening",
                ["name"] = "Ask about availability",
                ["trigger"] = new JsonObject { ["type"] = "StageEntered", ["stageId"] = "screening" },
                ["actions"] = new JsonArray(new JsonObject { ["type"] = "SendQuestionnaire", ["questionnaireId"] = "availability" }),
            },
            new JsonObject
            {
                ["id"] = "tag-on-answers",
                ["name"] = "Tag when answered",
                ["trigger"] = new JsonObject { ["type"] = "QuestionnaireCompleted", ["questionnaireId"] = "availability" },
                ["actions"] = new JsonArray(new JsonObject { ["type"] = "AddTag", ["tag"] = "answered" }),
            });
        definition["workflow"]!["followUpQuestionnaires"] = new JsonArray(new JsonObject
        {
            ["id"] = "availability",
            ["name"] = "Availability",
            ["questions"] = new JsonArray(
                new JsonObject { ["id"] = "start_date", ["type"] = "ShortText", ["label"] = "When could you start?", ["required"] = true },
                new JsonObject
                {
                    ["id"] = "forklift_licence",
                    ["type"] = "YesNo",
                    ["label"] = "Do you hold a forklift licence?",
                    ["required"] = true,
                    ["knockout"] = new JsonObject { ["disqualifyingAnswer"] = false },
                }),
        });
        var (_, versionId) = await recruiter.CreatePublishedTemplateAsync($"{title} template", definition);
        var body = Samples.RemoteJob(title);
        body["templateVersionId"] = versionId;
        var job = (await recruiter.CreateJobAsync(body)).Id();
        await recruiter.PublishJobAsync(job);
        var slug = (await recruiter.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("slug");
        return (recruiter, slug, job);
    }

    private static async Task<Guid> ApplyAsync(HttpClient careers, string slug, Guid job, string email)
    {
        var response = await careers.PostAsJsonAsync($"/v1/public/organizations/{slug}/jobs/{job}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["fullName"] = "Robin Ellis", ["email"] = email },
            ["privacyNoticeVersion"] = "2026-10-20",
            ["answers"] = new JsonArray(),
            ["aiUseDeclaration"] = "DidNotUse",
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("applicationId").GetGuid();
    }

    private static async Task MoveAsync(Tenant recruiter, Guid applicationId, string stageId) =>
        (await recruiter.PostAsync($"/v1/applications/{applicationId}/move", new JsonObject { ["toStageId"] = stageId })).Expect(HttpStatusCode.OK);

    private static async Task<ApiResult> WaitForAsync(Tenant recruiter, Guid applicationId, Func<ApiResult, bool> done, string what)
    {
        ApiResult detail = null!;
        await WolverineStore.WaitUntilAsync(async () =>
        {
            detail = (await recruiter.GetAsync($"/v1/applications/{applicationId}")).Expect(HttpStatusCode.OK);
            return detail.Count("followUps") > 0 && done(detail);
        }, what, Ct);
        return detail;
    }

    private static string QuestionnairePath(string slug, string token) => $"/v1/public/organizations/{slug}/questionnaires/{token}";

    private static JsonObject Answers(params (string QuestionId, string Value)[] answers) => new()
    {
        ["answers"] = new JsonArray([.. answers.Select(a => (JsonNode)new JsonObject { ["questionId"] = a.QuestionId, ["value"] = a.Value })]),
    };

    // The link in the email is the organization's careers board followed by /questionnaires/{token}.
    private static string TokenIn(string text, string slug)
    {
        var match = QuestionnaireLink().Match(text);
        match.Success.Should().BeTrue($"the email carries the questionnaire link: {text}");
        match.Groups["board"].Value.Should().Contain(slug);
        return match.Groups["token"].Value;
    }

    [GeneratedRegex(@"(?<board>https?://\S+)/questionnaires/(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex QuestionnaireLink();
}
