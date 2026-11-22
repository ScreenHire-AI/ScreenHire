using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static ScreenHire.IntegrationTests.Forms.FormSupport;

namespace ScreenHire.IntegrationTests.Forms;

/// <summary>
/// Forms sent to a candidate (workflow-engine.md section 8): a library form sent when the candidate enters a stage, and
/// "Send a form" by hand for someone who may email candidates, both through the one request per application and form.
/// Equality monitoring forms only ever count toward totals: their answers are never on the application, never a fact for an
/// automation, and their totals hide every count under 5.
/// </summary>
public sealed partial class FormSendingTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_form_sent_by_hand_goes_once_per_send_in_the_senders_name_and_never_after_it_is_answered()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var formId = await CreatePublishedFormAsync(owner, "References", "Standard", Question("referee", "Who can we ask about your work?", required: true));
        var (slug, job) = await JobAsync(owner, "Sent by hand", DefinitionUsing("references", formId, 1));
        using var careers = fixture.CreateHttpClient("api");
        var email = ApiSession.NewEmail("byhand");
        var applicationId = await ApplyAsync(careers, slug, job, email);
        var path = $"/v1/applications/{applicationId}/forms/references/send";

        (await viewer.PostAsync(path)).Expect(HttpStatusCode.Forbidden);
        (await other.PostAsync(path)).Expect(HttpStatusCode.NotFound);
        (await owner.PostAsync($"/v1/applications/{applicationId}/forms/nothing-like-it/send")).Expect(HttpStatusCode.NotFound);

        var headers = new Dictionary<string, string> { ["Idempotency-Key"] = "click-1" };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await owner.Session.PostAsync(path, new JsonObject(), headers, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Accepted, "a retry of the same click is answered as the click was");
        }

        var detail = (await owner.GetAsync($"/v1/applications/{applicationId}")).Expect(HttpStatusCode.OK);
        detail.At("followUps.0.timesSent").GetInt32().Should().Be(1, "one click, one send");
        var first = await fixture.Mailpit.WaitForMessagesAsync(email, 1, Ct);
        first[0].Subject.Should().StartWith("A few more questions about your application");

        (await owner.PostAsync(path)).Expect(HttpStatusCode.Accepted).At("timesSent").GetInt32().Should().Be(2, "a new click is a new send");
        var both = await fixture.Mailpit.WaitForMessagesAsync(email, 2, Ct);
        var timeline = (await owner.GetAsync($"/v1/applications/{applicationId}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "QuestionnaireSent").ToList();
        timeline.Should().HaveCount(2).And.OnlyContain(e => e.GetProperty("actorUserId").GetGuid() == owner.UserId, "each send is in the sender's name");

        var token = TokenIn(both[0].Text);
        (await careers.PostAsJsonAsync($"/v1/public/organizations/{slug}/questionnaires/{token}", Answers(("referee", "Sam Ortiz, my last manager")), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.PostAsync(path)).Expect(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_equality_monitoring_form_sent_at_a_stage_is_only_ever_counted_and_its_totals_hide_counts_under_five()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var formId = await CreatePublishedFormAsync(owner, "Equality monitoring", "EqualityMonitoring",
            Question("disability", "Do you consider yourself to have a disability?", "YesNo"));
        // A completion automation that names no form fires on any answered form; it must not fire on this one.
        var anyForm = new JsonObject
        {
            ["id"] = "tag-any-form",
            ["name"] = "Tag any answered form",
            ["trigger"] = new JsonObject { ["type"] = "QuestionnaireCompleted" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "AddTag", ["tag"] = "answered" }),
        };
        var (slug, job) = await JobAsync(owner, "Equality journey", DefinitionUsing("equality", formId, 1, SendOnEntering("screening", "equality"), anyForm));
        using var careers = fixture.CreateHttpClient("api");
        var from = DateTimeOffset.UtcNow.AddMinutes(-5);

        var applications = new List<Guid>();
        string[] answers = ["yes", "yes", "yes", "yes", "yes", "no"];
        for (var i = 0; i < answers.Length; i++)
        {
            var email = ApiSession.NewEmail($"equality{i}");
            var applicationId = await ApplyAsync(careers, slug, job, email);
            applications.Add(applicationId);
            (await owner.PostAsync($"/v1/applications/{applicationId}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
            var token = TokenIn((await fixture.Mailpit.WaitForMessagesAsync(email, 1, Ct))[0].Text);
            var page = await careers.GetFromJsonAsync<JsonElement>($"/v1/public/organizations/{slug}/questionnaires/{token}", Ct);
            page.GetProperty("equalityMonitoring").GetBoolean().Should().BeTrue("the candidate's page says what it is for");
            (await careers.PostAsJsonAsync($"/v1/public/organizations/{slug}/questionnaires/{token}", Answers(("disability", answers[i])), Ct))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            if (i == 3)
            {
                // Four answers: too few to show anything.
                var early = await ReportAsync(owner, from);
                early.At("forms.0.responses").ValueKind.Should().Be(JsonValueKind.Null);
                early.Count("forms.0.questions").Should().Be(0);
            }
        }

        var detail = (await owner.GetAsync($"/v1/applications/{applications[0]}")).Expect(HttpStatusCode.OK);
        detail.At("followUps.0.kind").GetString().Should().Be("EqualityMonitoring");
        detail.At("followUps.0.completedAt").ValueKind.Should().Be(JsonValueKind.String, "whether it was answered is not an answer");
        detail.Count("followUps.0.answers").Should().Be(0, "the answers never show on the application");
        detail.Count("followUps.0.questions").Should().Be(0);
        detail.Body.GetRawText().Should().NotContain("disability");

        await using (var mcp = await McpSupport.ConnectAsync(fixture, (await owner.CreateKeyAsync("Assistant", ["Read"])).Text("token")))
        {
            var read = await mcp.CallAsync("get_application", new() { ["applicationId"] = applications[0] });
            read.GetRawText().Should().NotContain("disability", "an assistant reads what the application shows");
        }

        await Wait.ForNothingAsync(TimeSpan.FromSeconds(2), "an automation run for an answered equality form", Ct);
        (await owner.GetAsync($"/v1/applications/{applications[0]}/automation-runs")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Select(r => r.GetProperty("automationId").GetString()).Should().NotContain(
                "tag-any-form", "an answered equality form starts no automation, not even one that names no form");
        (await owner.GetAsync($"/v1/applications/{applications[0]}")).Expect(HttpStatusCode.OK).Count("tags").Should().Be(0);

        var report = await ReportAsync(owner, from);
        var form = report.At("forms").EnumerateArray().Single(f => f.GetProperty("formTemplateId").GetGuid() == formId);
        form.GetProperty("responses").GetInt32().Should().Be(6);
        form.GetProperty("questions")[0].GetProperty("answers").EnumerateArray()
            .Select(a => (a.GetProperty("label").GetString(), a.GetProperty("count").ValueKind == JsonValueKind.Null ? (int?)null : a.GetProperty("count").GetInt32()))
            .Should().Equal(("Yes", 5), ("No", null), ("No answer", null));
        report.Body.GetRawText().Should().NotContain(applications[0].ToString(), "totals name no candidate");

        using var otherOrganization = await fixture.SignedInWithOrganizationAsync();
        (await ReportAsync(otherOrganization, from)).Body.GetRawText().Should().NotContain(
            formId.ToString(), "another organization's totals hold neither this form nor its answers");

        (await recruiter.GetAsync($"/v1/reports/equality?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"))}"))
            .Expect(HttpStatusCode.Forbidden);
    }

    private static async Task<ApiResult> ReportAsync(Tenant owner, DateTimeOffset from) =>
        (await owner.GetAsync(
            $"/v1/reports/equality?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"))}"))
        .Expect(HttpStatusCode.OK);

    private static async Task<(string Slug, Guid Job)> JobAsync(Tenant owner, string title, JsonObject definition)
    {
        var (_, versionId) = await owner.CreatePublishedTemplateAsync($"{title} template", definition);
        var body = Samples.RemoteJob(title);
        body["templateVersionId"] = versionId;
        var job = (await owner.CreateJobAsync(body)).Id();
        await owner.PublishJobAsync(job);
        return ((await owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("slug"), job);
    }

    private static async Task<Guid> ApplyAsync(HttpClient careers, string slug, Guid job, string email)
    {
        var response = await careers.PostAsJsonAsync($"/v1/public/organizations/{slug}/jobs/{job}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["firstName"] = "Robin", ["lastName"] = "Ellis", ["email"] = email },
            ["privacyNoticeVersion"] = "2026-10-20",
            ["answers"] = new JsonArray(),
            ["aiUseDeclaration"] = "DidNotUse",
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("applicationId").GetGuid();
    }

    private static JsonObject Answers(params (string QuestionId, string Value)[] answers) => new()
    {
        ["answers"] = new JsonArray([.. answers.Select(a => (JsonNode)new JsonObject { ["questionId"] = a.QuestionId, ["value"] = a.Value })]),
    };

    private static string TokenIn(string text)
    {
        var match = QuestionnaireLink().Match(text);
        match.Success.Should().BeTrue($"the email carries the questionnaire link: {text}");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex(@"https?://\S+/questionnaires/(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex QuestionnaireLink();
}
