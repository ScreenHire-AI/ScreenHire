using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests.Automations;

/// <summary>
/// The Reject action end to end, from the careers apply route through the running worker and Mailpit: the candidate's
/// answers and the application's source reach the facts, an opted-in knockout automation rejects, the organization's
/// reworded rejection email carries the reason, and ScreenHire adds the automated-decision sentence no wording can remove.
/// </summary>
public sealed class RejectionEmailTests(AppHostFixture fixture)
{
    private const string Reason = "This role needs the right to work in the country it is based in.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_knockout_reject_sends_the_organizations_reworded_email_with_the_reason_and_the_automated_decision_notice()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var marker = $"rj{Guid.NewGuid():N}"[..14];
        (await recruiter.PutAsync("/v1/email-templates/application-rejected", new JsonObject
        {
            ["subject"] = $"Update {marker} on {{{{ jobTitle }}}}",
            ["body"] = $"Dear {{{{ candidateName }}}}, {marker} we have decided not to go ahead.\n\nWhy: {{{{ reason }}}}",
        })).Expect(HttpStatusCode.OK);
        var organizationName = (await recruiter.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("name");

        var definition = Samples.DefinitionWith(RejectOnNo(), TagCareersApplications());
        definition["workflow"]!["questionnaire"] = KnockoutQuestionnaire();
        var (_, versionId) = await recruiter.CreatePublishedTemplateAsync("Rejection email template", definition);
        var jobBody = Samples.RemoteJob("Rejection email");
        jobBody["templateVersionId"] = versionId;
        var job = (await recruiter.CreateJobAsync(jobBody)).Id();
        await recruiter.PublishJobAsync(job);
        var slug = (await recruiter.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("slug");

        // The candidate answers "no" to the knockout on the careers apply route, as the careers server sends it.
        var candidateEmail = ApiSession.NewEmail("candidate");
        using var careers = fixture.CreateHttpClient("api");
        var response = await careers.PostAsJsonAsync($"/v1/public/organizations/{slug}/jobs/{job}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["fullName"] = "Robin Ellis", ["email"] = candidateEmail },
            ["privacyNoticeVersion"] = "2026-10-20",
            ["answers"] = new JsonArray(new JsonObject { ["questionId"] = "work_authorization", ["value"] = "no" }),
            ["aiUseDeclaration"] = "DidNotUse",
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var applicationId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("applicationId").GetGuid();

        var sent = await fixture.Mailpit.WaitForMessagesAsync(candidateEmail, 1, Ct);
        var run = await AutomationSamples.WaitForRunAsync(recruiter, applicationId, "knockout-reject", "Completed");
        await AutomationSamples.WaitForRunAsync(recruiter, applicationId, "tag-careers", "Completed");

        // The facts the conditions saw: the answer and the source, stored on the run.
        var facts = run.GetProperty("facts");
        facts.GetProperty("answers").EnumerateArray().Select(a => (a.GetProperty("questionId").GetString(), a.GetProperty("values")[0].GetString()))
            .Should().ContainSingle().Which.Should().Be(("work_authorization", "no"));
        facts.GetProperty("source").GetString().Should().Be("CareersSite");

        (await fixture.Mailpit.MessagesAsync(candidateEmail, Ct)).Should().ContainSingle("one Reject action sends one email");
        sent[0].Subject.Should().Be($"Update {marker} on Rejection email");
        sent[0].Text.Should().Contain($"Dear Robin Ellis, {marker} we have decided not to go ahead.")
            .And.Contain($"Why: {Reason}")
            .And.Contain("This decision was made automatically")
            .And.Contain($"contact the hiring team at {organizationName}");
        sent[0].Text.TrimEnd().Should().EndWith("a person will review your application.", "the notice follows the organization's wording");

        // The email reaches Mailpit just before the worker commits its timeline entry, so await the entry.
        JsonElement entry = default;
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                var timeline = (await recruiter.GetAsync($"/v1/applications/{applicationId}/timeline")).Expect(HttpStatusCode.OK).Body;
                var found = timeline.EnumerateArray().Where(e => e.GetProperty("type").GetString() == "EmailSent").ToList();
                if (found.Count != 1)
                {
                    return false;
                }

                entry = found[0].Clone();
                return true;
            },
            "the rejection email's timeline entry",
            Ct);
        entry.GetProperty("emailTemplateId").GetString().Should().Be("application-rejected");
        entry.GetProperty("emailSubject").GetString().Should().Be($"Update {marker} on Rejection email");
        var detail = await recruiter.GetAsync($"/v1/applications/{applicationId}");
        detail.Text("stage.id").Should().Be("rejected");
        detail.At("tags").EnumerateArray().Select(t => t.GetString()).Should().Contain("careers", "application.source reached the facts");
    }

    // Replaces the questionnaire with the one yes/no knockout question the rule reads. "No" disqualifies.
    private static JsonObject KnockoutQuestionnaire() => new()
    {
        ["questions"] = new JsonArray(new JsonObject
        {
            ["id"] = "work_authorization",
            ["type"] = "YesNo",
            ["label"] = "Do you have the right to work in the country this job is in?",
            ["required"] = true,
            ["knockout"] = new JsonObject { ["disqualifyingAnswer"] = false, ["reason"] = Reason },
        }),
    };

    private static JsonObject RejectOnNo() => new()
    {
        ["id"] = "knockout-reject",
        ["name"] = "Reject without the right to work",
        ["trigger"] = new JsonObject { ["type"] = "ApplicationSubmitted" },
        ["conditions"] = new JsonObject
        {
            ["match"] = "All",
            ["rules"] = new JsonArray(new JsonObject { ["fact"] = "answers.work_authorization", ["operator"] = "EqualTo", ["value"] = "no" }),
        },
        ["actions"] = new JsonArray(new JsonObject { ["type"] = "Reject", ["reason"] = Reason }),
        ["allowAutoReject"] = true,
    };

    // On the same trigger as the rejection, so its AddTag and the Reject change one application at once. One of them meets
    // the row's concurrency check and is retried; the rejection email still goes once, because it leaves only with the
    // action's commit (SendAutomationEmail).
    private static JsonObject TagCareersApplications() => new()
    {
        ["id"] = "tag-careers",
        ["name"] = "Tag careers-site applications",
        ["trigger"] = new JsonObject { ["type"] = "ApplicationSubmitted" },
        ["conditions"] = new JsonObject
        {
            ["match"] = "All",
            ["rules"] = new JsonArray(new JsonObject { ["fact"] = "application.source", ["operator"] = "EqualTo", ["value"] = "CareersSite" }),
        },
        ["actions"] = new JsonArray(new JsonObject { ["type"] = "AddTag", ["tag"] = "careers" }),
    };
}
