using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests.Automations;

/// <summary>
/// The Reject action end to end, through the running worker and Mailpit: after an organization rewords the rejection
/// email, a knockout automation that auto-rejects sends it with the reason, and ScreenHire adds the automated-decision
/// sentence no wording can remove.
/// </summary>
public sealed class RejectionEmailTests(AppHostFixture fixture)
{
    private const string Reason = "This role needs the right to work in the country it is based in.";

    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The engine does not yet build answer facts (AutomationFacts.FromTimeline leaves them empty, and nothing raises
    // QuestionnaireCompleted), so a knockout cannot match through the apply flow today. The test records the matched run
    // as the engine would have, with the answer the candidate gave, and publishes the Reject action's message; everything
    // from there is the real worker, store, renderer and mail transport.
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

        var definition = Samples.DefinitionWith(RejectOnNo());
        definition["workflow"]!["questionnaire"] = KnockoutQuestionnaire();
        var (_, versionId) = await recruiter.CreatePublishedTemplateAsync("Rejection email template", definition);
        var jobBody = Samples.RemoteJob("Rejection email");
        jobBody["templateVersionId"] = versionId;
        var job = (await recruiter.CreateJobAsync(jobBody)).Id();
        var candidateEmail = ApiSession.NewEmail("candidate");
        var applicationId = (await recruiter.CreateApplicationAsync(job, candidateEmail, "Robin Ellis")).Id();

        var run = await SeedMatchedRunAsync(recruiter.OrganizationId, job, applicationId);
        await using (var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct))
        {
            await publisher.Services.GetRequiredService<IMessageBus>()
                .PublishAsync(new ExecuteAutomationAction(recruiter.OrganizationId, run.ActionRunId, run.Run.ActionKey(0)));
        }

        var sent = await fixture.Mailpit.WaitForMessagesAsync(candidateEmail, 1, Ct);
        await AutomationSamples.WaitForRunAsync(recruiter, applicationId, "knockout-reject", "Completed");

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
        (await recruiter.GetAsync($"/v1/applications/{applicationId}")).Text("stage.id").Should().Be("rejected");
    }

    private async Task<(AutomationRun Run, Guid ActionRunId)> SeedMatchedRunAsync(Guid organizationId, Guid jobId, Guid applicationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId, Ct);
        var automation = job.Workflow.Automations.Single(a => a.Id == "knockout-reject");
        var facts = new AutomationFacts([new AnswerFact("work_authorization", ["no"])], []);
        var trigger = new AutomationTriggerEvent(organizationId, applicationId, jobId, TriggerType.QuestionnaireCompleted, null, Guid.CreateVersion7(), Now);
        var evaluation = AutomationEvaluator.Evaluate(automation, facts, accommodationRequested: false, []);
        evaluation.Outcome.Should().Be(EvaluationOutcome.Matched, "the candidate gave the knockout's disqualifying answer");

        var (run, actions) = AutomationRun.Record(trigger, automation, job.WorkflowVersion, facts, evaluation, [], Now);
        db.AutomationRuns.Add(run);
        db.AutomationActionRuns.AddRange(actions);
        await db.SaveChangesAsync(Ct);
        return (run, actions.Single().Id);
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
        ["trigger"] = new JsonObject { ["type"] = "QuestionnaireCompleted" },
        ["conditions"] = new JsonObject
        {
            ["match"] = "All",
            ["rules"] = new JsonArray(new JsonObject { ["fact"] = "answers.work_authorization", ["operator"] = "EqualTo", ["value"] = "no" }),
        },
        ["actions"] = new JsonArray(new JsonObject { ["type"] = "Reject", ["reason"] = Reason }),
        ["allowAutoReject"] = true,
    };
}
