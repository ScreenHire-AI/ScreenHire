using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests.Automations;

/// <summary>
/// The workflow engine's endpoints and its behaviour through the running stack: validate and simulate, manual runs and
/// the tasks they create, loop detection surfacing as an automation issue, and a failed action reaching the issues
/// list. Each test uses its own organization.
/// </summary>
public sealed class WorkflowEngineTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Validate_reports_guardrail_errors_with_paths_and_accepts_a_valid_workflow()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var workflow = Samples.Definition()["workflow"]!.DeepClone().AsObject();

        var valid = (await recruiter.PostAsync("/v1/workflows/validate", new JsonObject { ["workflow"] = workflow.DeepClone() })).Expect(HttpStatusCode.OK);
        valid.At("valid").GetBoolean().Should().BeTrue();

        workflow["automations"] = new JsonArray(Samples.IntegrityAutomation(new JsonObject { ["type"] = "Reject", ["reason"] = "Looks automated" }));
        var refused = (await recruiter.PostAsync("/v1/workflows/validate", new JsonObject { ["workflow"] = workflow })).Expect(HttpStatusCode.OK);
        refused.At("valid").GetBoolean().Should().BeFalse();
        refused.Text("errors.0.path").Should().Be("automations[0].actions[0]");
        refused.Text("errors.0.message").Should().Contain("Integrity signals never drive rejection");
    }

    [Fact]
    public async Task Simulate_shows_which_automations_a_sample_application_would_fire()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var definition = await Samples.DefinitionOfAsync(recruiter, SystemTemplates.HourlyRole, 1);
        var workflow = definition["workflow"]!.AsObject();

        var result = (await recruiter.PostAsync("/v1/workflows/simulate", new JsonObject
        {
            ["workflow"] = workflow.DeepClone(),
            ["trigger"] = new JsonObject { ["type"] = "QuestionnaireCompleted" },
            ["facts"] = new JsonObject
            {
                ["answers"] = new JsonArray(new JsonObject { ["questionId"] = "work_authorization", ["values"] = new JsonArray("no") }),
                ["tags"] = new JsonArray(),
            },
        })).Expect(HttpStatusCode.OK);

        result.Count("automations").Should().Be(1);
        result.Text("automations.0.automationId").Should().Be("right_to_work_review");
        result.Text("automations.0.outcome").Should().Be("Matched");
        result.Text("automations.0.actions.0.type").Should().Be("QueueForRejectionReview", "the knockout default queues for a person");

        // The same workflow with a TimeElapsed nudge, for an application with an accommodation: skipped, not run.
        workflow["automations"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "nudge",
            ["name"] = "Nudge",
            ["trigger"] = new JsonObject { ["type"] = "TimeElapsed", ["stageId"] = "screening", ["afterHours"] = 48 },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "NotifyTeam", ["note"] = "Waiting two days" }),
        });
        var skipped = (await recruiter.PostAsync("/v1/workflows/simulate", new JsonObject
        {
            ["workflow"] = workflow.DeepClone(),
            ["trigger"] = new JsonObject { ["type"] = "TimeElapsed", ["stageId"] = "screening" },
            ["accommodationRequested"] = true,
        })).Expect(HttpStatusCode.OK);
        skipped.Text("automations.0.outcome").Should().Be("Skipped");

        workflow["automations"] = new JsonArray(Samples.IntegrityAutomation(new JsonObject { ["type"] = "Reject", ["reason"] = "No" }));
        var invalid = await recruiter.PostAsync("/v1/workflows/simulate", new JsonObject
        {
            ["workflow"] = workflow.DeepClone(),
            ["trigger"] = new JsonObject { ["type"] = "IntegrityAssessed" },
        });
        invalid.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("workflow.automations[0].actions[0]");
    }

    [Fact]
    public async Task A_manual_automation_tags_the_application_and_queues_a_rejection_review_without_moving_it()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await AutomationSamples.JobWithAutomationsAsync(recruiter, "Manual automation", new JsonObject
        {
            ["id"] = "flag",
            ["name"] = "Flag for review",
            ["trigger"] = new JsonObject { ["type"] = "Manual" },
            ["actions"] = new JsonArray(
                new JsonObject { ["type"] = "AddTag", ["tag"] = "needs-decision" },
                new JsonObject { ["type"] = "QueueForRejectionReview", ["reason"] = "Missing a required licence." }),
        });
        var application = await recruiter.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"));

        var queued = (await recruiter.PostAsync($"/v1/applications/{application.Id()}/automations/flag/run")).Expect(HttpStatusCode.Accepted);
        queued.Text("automationId").Should().Be("flag");

        var run = await AutomationSamples.WaitForRunAsync(recruiter, application.Id(), "flag", "Completed");
        run.GetProperty("trigger").GetString().Should().Be("Manual");
        run.GetProperty("actorUserId").GetGuid().Should().Be(recruiter.UserId);
        run.GetProperty("eventId").GetGuid().Should().Be(queued.Id("eventId"));

        var detail = (await recruiter.GetAsync($"/v1/applications/{application.Id()}")).Expect(HttpStatusCode.OK);
        detail.Text("stage.id").Should().Be("applied", "queueing for rejection review never moves the application");
        detail.Text("tags.0").Should().Be("needs-decision");
        var tasks = (await recruiter.GetAsync($"/v1/applications/{application.Id()}/tasks")).Expect(HttpStatusCode.OK);
        tasks.Text("0.kind").Should().Be("RejectionReview");
        tasks.Text("0.note").Should().Be("Missing a required licence.");
        var timeline = (await recruiter.GetAsync($"/v1/applications/{application.Id()}/timeline")).Body;
        timeline.EnumerateArray().Should().Contain(e => e.GetProperty("type").GetString() == "TagAdded"
            && e.GetProperty("automationRunId").GetGuid() == run.GetProperty("id").GetGuid());

        // Only a Manual automation can be run by hand, and another organization cannot run anything on this application.
        (await recruiter.PostAsync($"/v1/applications/{application.Id()}/automations/nowhere/run")).Expect(HttpStatusCode.NotFound);
        using var other = await fixture.SignedInWithOrganizationAsync();
        (await other.PostAsync($"/v1/applications/{application.Id()}/automations/flag/run")).Expect(HttpStatusCode.NotFound);
        (await other.GetAsync($"/v1/applications/{application.Id()}/automation-runs")).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Two_automations_that_move_an_application_back_and_forth_are_stopped_as_a_loop()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        JsonObject MoveOnEnter(string id, string from, string to) => new()
        {
            ["id"] = id,
            ["name"] = $"From {from} to {to}",
            ["trigger"] = new JsonObject { ["type"] = "StageEntered", ["stageId"] = from },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "MoveToStage", ["stageId"] = to }),
        };
        var job = await AutomationSamples.JobWithAutomationsAsync(
            recruiter, "Looping workflow", MoveOnEnter("to-interview", "screening", "interview"), MoveOnEnter("to-screening", "interview", "screening"));
        var application = await recruiter.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"));

        (await recruiter.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);

        var blocked = await AutomationSamples.WaitForRunAsync(recruiter, application.Id(), "to-interview", "Blocked");
        blocked.GetProperty("detail").GetString().Should().Contain("loop");
        blocked.GetProperty("chain").EnumerateArray().Select(e => e.GetString()).Should().Equal("to-screening", "to-interview");

        var issues = (await recruiter.GetAsync("/v1/automation-issues")).Expect(HttpStatusCode.OK);
        issues.Body.EnumerateArray().Should().ContainSingle(i => i.GetProperty("runId").GetGuid() == blocked.GetProperty("id").GetGuid());
        (await recruiter.GetAsync($"/v1/applications/{application.Id()}")).Text("stage.id").Should().Be("screening");

        using var other = await fixture.SignedInWithOrganizationAsync();
        (await other.GetAsync("/v1/automation-issues")).Expect(HttpStatusCode.OK).Count().Should().Be(0);
    }

    // The error policy's last step: an action that fails every retry lands in the dead-letter table and, through
    // AutomationFailures, marks its run failed so the issues list shows it. A private-schema worker with no retries runs
    // the action; its webhook URL points at a private address, which the webhook client refuses without any network.
    [Fact]
    public async Task An_action_that_fails_every_retry_becomes_an_automation_issue()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Failing webhook"));
        var application = await recruiter.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"));
        var (run, action) = await RecordWebhookRunAsync(recruiter, job.Id(), application.Id(), "https://10.0.0.1/hook");

        var settings = new MessagingSettings { Schema = MessagingHost.NewSchema(), AutoProvision = true, RetryDelays = [], ScheduledRetryDelays = [] };
        await using (var worker = await MessagingHost.StartHandlerAsync(fixture, settings, [], Ct))
        {
            await worker.Services.GetRequiredService<IMessageBus>().SendAsync(
                new ExecuteAutomationAction(recruiter.OrganizationId, action.Id, run.ActionKey(0)));

            await WolverineStore.WaitUntilAsync(async () =>
                (await recruiter.GetAsync("/v1/automation-issues")).Body.EnumerateArray()
                    .Any(i => i.GetProperty("runId").GetGuid() == run.Id && i.GetProperty("status").GetString() == "Failed"),
                "the failed action to reach the automation issues list", Ct);
            (await worker.Store.GetDeadLettersAsync(action.Id.ToString(), Ct)).Should().ContainSingle()
                .Which.ExceptionType.Should().Contain("HttpRequestException");
        }

        var runs = (await recruiter.GetAsync($"/v1/applications/{application.Id()}/automation-runs")).Body;
        var recorded = runs.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == run.Id);
        recorded.GetProperty("actions")[0].GetProperty("status").GetString().Should().Be("Failed");
        recorded.GetProperty("detail").GetString().Should().Contain("CallWebhook failed after every retry");
    }

    // A matched run with one CallWebhook action, written as the organization through row-level security, as the engine
    // would record it. The real worker never sees its action message, so only the test's private worker runs it.
    private async Task<(AutomationRun Run, AutomationActionRun Action)> RecordWebhookRunAsync(Tenant recruiter, Guid jobId, Guid applicationId, string url)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(recruiter.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        var automation = new Automation(
            "hook", "Call the webhook", new AutomationTrigger(TriggerType.Manual), null, [new AutomationAction(ActionType.CallWebhook, Url: url)]);
        var now = DateTimeOffset.UtcNow;
        var trigger = new AutomationTriggerEvent(recruiter.OrganizationId, applicationId, jobId, TriggerType.Manual, null, Guid.CreateVersion7(), now);
        var evaluation = AutomationEvaluator.Evaluate(automation, AutomationFacts.None, false, []);
        var (run, actions) = AutomationRun.Record(trigger, automation, 1, AutomationFacts.None, evaluation, [], now);
        db.AutomationRuns.Add(run);
        db.AutomationActionRuns.AddRange(actions);
        await db.SaveChangesAsync(Ct);
        return (run, actions[0]);
    }
}
