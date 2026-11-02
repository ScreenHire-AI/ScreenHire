using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Automations;
using ScreenHire.IntegrationTests.Automations;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// The ScheduleInterview workflow action and the InterviewScheduled trigger through the running stack
/// (docs/architecture/workflow-engine.md section 8, docs/architecture/interview-scheduling.md): the save-time checks, an
/// automation that offers the candidate an interview when they enter a stage, and what the action does when it cannot.
/// </summary>
public sealed class InterviewWorkflowActionTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_workflow_naming_someone_outside_the_organization_as_an_interviewer_is_refused_at_every_save_site()
    {
        using var world = await InterviewWorld.CreateAsync(fixture, connectCalendars: false);
        var stranger = Guid.CreateVersion7();
        var path = "automations[0].actions[0].interviewerUserIds";
        var offending = ScheduleOnStage("screening", stranger);
        var fine = ScheduleOnStage("screening", world.Owner.UserId, world.Second.UserId);

        // Validate, as the builder does while editing.
        var validated = (await world.Owner.PostAsync("/v1/workflows/validate", new JsonObject { ["workflow"] = Samples.DefinitionWith(Copy(offending))["workflow"]!.DeepClone() }))
            .Expect(HttpStatusCode.OK);
        validated.At("valid").GetBoolean().Should().BeFalse();
        validated.Text("errors.0.path").Should().Be(path);
        (await world.Owner.PostAsync("/v1/workflows/validate", new JsonObject { ["workflow"] = Samples.DefinitionWith(Copy(fine))["workflow"]!.DeepClone() }))
            .Expect(HttpStatusCode.OK).At("valid").GetBoolean().Should().BeTrue();

        // A template's draft and its publish.
        var refused = await world.Owner.PostAsync("/v1/templates", new JsonObject { ["name"] = "Stranger", ["definition"] = Samples.DefinitionWith(Copy(offending)) });
        refused.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain($"definition.workflow.{path}");
        var (_, versionId) = await world.Owner.CreatePublishedTemplateAsync("Members", Samples.DefinitionWith(Copy(fine)));
        versionId.Should().NotBeEmpty("members are accepted");

        // A job's own workflow.
        var jobId = (await world.Owner.CreateJobAsync(Samples.RemoteJob("Job save"))).Id();
        var job = (await world.Owner.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK);
        var workflow = JsonNode.Parse(job.At("workflow").GetRawText())!.AsObject();
        workflow["automations"] = new JsonArray(offending.DeepClone());
        var update = await world.Owner.PutAsync($"/v1/jobs/{jobId}/workflow", new JsonObject { ["workflow"] = workflow.DeepClone(), ["expectedWorkflowVersion"] = 1 });
        update.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain($"workflow.{path}");

        workflow["automations"] = new JsonArray(fine.DeepClone());
        (await world.Owner.PutAsync($"/v1/jobs/{jobId}/workflow", new JsonObject { ["workflow"] = workflow.DeepClone(), ["expectedWorkflowVersion"] = 1 })).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_verification_signal_automation_that_schedules_an_interview_is_refused_at_save()
    {
        using var world = await InterviewWorld.CreateAsync(fixture, connectCalendars: false);
        var automation = ScheduleOnStage("screening", world.Owner.UserId);
        automation["trigger"] = new JsonObject { ["type"] = "VerificationAssessed" };

        var refused = await world.Owner.PostAsync("/v1/templates", new JsonObject { ["name"] = "Signal", ["definition"] = Samples.DefinitionWith(automation) });

        refused.Expect(HttpStatusCode.BadRequest).At("errors").GetProperty("definition.workflow.automations[0].actions[0]")[0].GetString()
            .Should().Contain("Verification signals never drive rejection");
    }

    [Fact]
    public async Task Entering_a_stage_offers_the_candidate_an_interview_once_and_it_books_like_a_scheduled_one()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (job, application, email) = await JobAndApplicationAsync(world, "Action journey",
            ScheduleOnStage("screening", world.Owner.UserId, world.Second.UserId), TagWhenBooked());

        await MoveAsync(world, application, "screening");

        // The invitation arrives with a working link, and the Open request has the action's interviewers and window.
        var invitation = await world.WaitForEmailAsync(email, "Pick a time");
        var token = InterviewWorld.TokenIn(invitation.Text);
        var run = await AutomationSamples.WaitForRunAsync(world.Owner, application, "schedule", "Completed");
        var detail = run.GetProperty("actions")[0];
        detail.GetProperty("status").GetString().Should().Be("Completed");
        detail.GetProperty("detail").GetString().Should().Contain("2 interviewers").And.Contain("14 days");
        var interview = (await world.InterviewsAsync(application)).Should().ContainSingle().Which;
        interview.GetProperty("status").GetString().Should().Be("Open");
        interview.GetProperty("durationMinutes").GetInt32().Should().Be(30);
        interview.GetProperty("timeZone").GetString().Should().Be("UTC");
        interview.GetProperty("createdByUserId").ValueKind.Should().Be(JsonValueKind.Null, "an automation made it, not a person");
        (interview.GetProperty("windowEnd").GetDateTimeOffset() - interview.GetProperty("windowStart").GetDateTimeOffset()).Should().Be(TimeSpan.FromDays(14));
        interview.GetProperty("interviewers").EnumerateArray().Select(i => i.GetProperty("userId").GetGuid())
            .Should().Equal(world.Owner.UserId, world.Second.UserId);
        interview.GetProperty("interviewers")[0].GetProperty("isOrganizer").GetBoolean().Should().BeTrue();

        await world.WaitForTimelineAsync("InterviewInvited", application);
        var timeline = (await world.Owner.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray().ToList();
        timeline.Single(e => e.GetProperty("type").GetString() == "InterviewInvited").GetProperty("automationRunId").GetGuid()
            .Should().Be(run.GetProperty("id").GetGuid());

        // Delivered again (a redelivered or retried message): the action has finished, so nothing more is made or sent.
        await RedeliverAsync(world, run);
        await Task.Delay(TimeSpan.FromSeconds(3), Ct);
        (await world.InterviewsAsync(application)).Should().ContainSingle();
        (await fixture.Mailpit.MessagesAsync(email, Ct)).Count(m => m.Subject.Contains("Pick a time", StringComparison.Ordinal)).Should().Be(1);

        // Leaving and entering again: the interview is still open, so the second run skips and sends nothing.
        await MoveAsync(world, application, "interview");
        await MoveAsync(world, application, "screening");
        var second = await WaitForRunsAsync(world, application, "schedule", 2);
        second.Select(r => r.GetProperty("actions")[0].GetProperty("status").GetString()).Should().BeEquivalentTo("Completed", "Skipped");
        second.Single(r => r.GetProperty("actions")[0].GetProperty("status").GetString() == "Skipped").GetProperty("actions")[0].GetProperty("detail").GetString()
            .Should().Contain("already has an interview");
        (await world.InterviewsAsync(application)).Should().ContainSingle();
        (await fixture.Mailpit.MessagesAsync(email, Ct)).Count(m => m.Subject.Contains("Pick a time", StringComparison.Ordinal)).Should().Be(1);

        // The candidate books: the InterviewScheduled automation fires.
        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        var start = InterviewWorld.SlotsOf(page).First();
        (await world.BookAsync(token, start)).Expect(HttpStatusCode.NoContent);
        var tagged = await WaitForAsync(world, application, d => d.At("tags").EnumerateArray().Any(t => t.GetString() == "interview-booked"), "the tag");
        tagged.At("tags").EnumerateArray().Select(t => t.GetString()).Should().Contain("interview-booked");
        var booked = await AutomationSamples.WaitForRunAsync(world.Owner, application, "tag-when-booked", "Completed");
        booked.GetProperty("trigger").GetString().Should().Be("InterviewScheduled");
        job.Should().NotBeEmpty();
    }

    [Fact]
    public async Task An_action_runs_again_over_the_request_it_made_without_a_second_request_or_email()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (_, application, email) = await JobAndApplicationAsync(world, "Action twice", ScheduleOnStage("screening", world.Owner.UserId));
        await MoveAsync(world, application, "screening");
        await world.WaitForEmailAsync(email, "Pick a time");
        var run = await AutomationSamples.WaitForRunAsync(world.Owner, application, "schedule", "Completed");

        // Put the action back to Pending, as if the first attempt's commit had not been recorded but the request had.
        await using (var db = await fixture.ContextAsync(world.OrganizationId, Ct))
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE automation_action_runs SET status = 'Pending' WHERE run_id = {run.GetProperty("id").GetGuid()}", Ct);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE automation_runs SET status = 'Running' WHERE id = {run.GetProperty("id").GetGuid()}", Ct);
        }

        await RedeliverAsync(world, run, key: "again");
        var runs = await WaitForRunsAsync(world, application, "schedule", 1, status: "Completed", actionStatus: "Skipped");
        runs.Single().GetProperty("actions")[0].GetProperty("detail").GetString().Should().Contain("offered already");
        (await world.InterviewsAsync(application)).Should().ContainSingle();
        (await fixture.Mailpit.MessagesAsync(email, Ct)).Count(m => m.Subject.Contains("Pick a time", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task An_application_with_an_interview_already_booked_or_offered_skips_the_action()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (_, application, email) = await JobAndApplicationAsync(world, "Action skips", ScheduleOnStage("screening", world.Owner.UserId));
        (await world.ScheduleAsync(applicationId: application, interviewers: [world.Owner.UserId])).Expect(HttpStatusCode.Created);
        await world.WaitForEmailAsync(email, "Pick a time");

        await MoveAsync(world, application, "screening");

        var run = await AutomationSamples.WaitForRunAsync(world.Owner, application, "schedule", "Completed");
        run.GetProperty("actions")[0].GetProperty("status").GetString().Should().Be("Skipped");
        (await world.InterviewsAsync(application)).Should().ContainSingle("the person's offer stands and no second one was made");
    }

    [Fact]
    public async Task An_expired_offer_does_not_block_the_action()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (_, application, email) = await JobAndApplicationAsync(world, "Action expired", ScheduleOnStage("screening", world.Owner.UserId));
        var offered = (await world.ScheduleAsync(applicationId: application, interviewers: [world.Owner.UserId])).Expect(HttpStatusCode.Created).Id();
        await world.WaitForEmailAsync(email, "Pick a time");
        await world.PassWindowAsync(offered);

        await MoveAsync(world, application, "screening");

        var run = await AutomationSamples.WaitForRunAsync(world.Owner, application, "schedule", "Completed");
        run.GetProperty("actions")[0].GetProperty("status").GetString().Should().Be("Completed");
        var interviews = await world.InterviewsAsync(application);
        interviews.Select(i => i.GetProperty("status").GetString()).Should().BeEquivalentTo("Open", "Expired");
        await world.WaitForEmailAsync(email, "Pick a time", count: 2);
    }

    [Fact]
    public async Task An_interviewer_without_a_calendar_fails_the_action_with_a_reason_a_person_can_read_and_sends_nothing()
    {
        using var world = await InterviewWorld.CreateAsync(fixture, connectCalendars: false);
        await InterviewWorld.ConnectCalendarAsync(world.Owner);
        var (_, application, email) = await JobAndApplicationAsync(world, "Action no calendar", ScheduleOnStage("screening", world.Owner.UserId, world.Second.UserId));

        await MoveAsync(world, application, "screening");

        var run = await AutomationSamples.WaitForRunAsync(world.Owner, application, "schedule", "Failed");
        var action = run.GetProperty("actions")[0];
        action.GetProperty("status").GetString().Should().Be("Failed");
        action.GetProperty("detail").GetString().Should().Contain("no working calendar connected").And.Contain("connect one from the account menu");
        (await world.InterviewsAsync(application)).Should().BeEmpty();
        (await fixture.Mailpit.MessagesAsync(email, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_interviewer_who_has_left_the_organization_fails_the_action()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (_, application, email) = await JobAndApplicationAsync(world, "Action left", ScheduleOnStage("screening", world.Owner.UserId, world.Second.UserId));
        var memberId = await world.Owner.MemberIdAsync(world.Second.UserId);
        (await world.Owner.DeleteAsync($"/v1/organization/members/{memberId}")).Expect(HttpStatusCode.NoContent);

        await MoveAsync(world, application, "screening");

        var run = await AutomationSamples.WaitForRunAsync(world.Owner, application, "schedule", "Failed");
        run.GetProperty("actions")[0].GetProperty("detail").GetString().Should().Contain("not a member");
        (await world.InterviewsAsync(application)).Should().BeEmpty();
        (await fixture.Mailpit.MessagesAsync(email, Ct)).Should().BeEmpty();
    }

    private static JsonObject Copy(JsonObject node) => (JsonObject)node.DeepClone();

    private static JsonObject ScheduleOnStage(string stageId, params Guid[] interviewers) => new()
    {
        ["id"] = "schedule",
        ["name"] = "Offer an interview",
        ["trigger"] = new JsonObject { ["type"] = "StageEntered", ["stageId"] = stageId },
        ["actions"] = new JsonArray(new JsonObject
        {
            ["type"] = "ScheduleInterview",
            ["interviewerUserIds"] = new JsonArray([.. interviewers.Select(id => JsonValue.Create(id))]),
            ["interviewMinutes"] = 30,
            ["interviewWindowDays"] = 14,
            ["interviewTimeZone"] = "UTC",
        }),
    };

    private static JsonObject TagWhenBooked() => new()
    {
        ["id"] = "tag-when-booked",
        ["name"] = "Tag when booked",
        ["trigger"] = new JsonObject { ["type"] = "InterviewScheduled" },
        ["actions"] = new JsonArray(new JsonObject { ["type"] = "AddTag", ["tag"] = "interview-booked" }),
    };

    private async Task<(Guid Job, Guid Application, string Email)> JobAndApplicationAsync(InterviewWorld world, string title, params JsonObject[] automations)
    {
        var job = await AutomationSamples.JobWithAutomationsAsync(world.Owner, title, automations);
        var email = ApiSession.NewEmail("candidate");
        return (job, (await world.Owner.CreateApplicationAsync(job, email)).Id(), email);
    }

    private static async Task MoveAsync(InterviewWorld world, Guid application, string stage) =>
        (await world.Owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = stage })).Expect(HttpStatusCode.OK);

    private static async Task<IReadOnlyList<JsonElement>> WaitForRunsAsync(
        InterviewWorld world, Guid application, string automationId, int count, string status = "Completed", string? actionStatus = null)
    {
        IReadOnlyList<JsonElement> found = [];
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                found =
                [
                    .. (await world.Owner.GetAsync($"/v1/applications/{application}/automation-runs")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
                        .Where(r => r.GetProperty("automationId").GetString() == automationId && r.GetProperty("status").GetString() == status
                            && (actionStatus is null || r.GetProperty("actions")[0].GetProperty("status").GetString() == actionStatus))
                        .Select(r => r.Clone()),
                ];
                return found.Count >= count;
            },
            $"{count} run(s) of {automationId}",
            Ct);
        return found;
    }

    private static async Task<ApiResult> WaitForAsync(InterviewWorld world, Guid application, Func<ApiResult, bool> done, string what)
    {
        var detail = default(ApiResult)!;
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                detail = (await world.Owner.GetAsync($"/v1/applications/{application}")).Expect(HttpStatusCode.OK);
                return done(detail);
            },
            what,
            Ct);
        return detail;
    }

    // Sends the first action's message again to the stack's worker.
    private async Task RedeliverAsync(InterviewWorld world, JsonElement run, string key = "")
    {
        var runId = run.GetProperty("id").GetGuid();
        Guid actionId;
        string actionKey;
        await using (var db = await fixture.ContextAsync(world.OrganizationId, Ct))
        {
            var stored = await db.AutomationRuns.AsNoTracking().SingleAsync(r => r.Id == runId, Ct);
            actionId = (await db.AutomationActionRuns.AsNoTracking().SingleAsync(a => a.RunId == runId && a.Index == 0, Ct)).Id;
            actionKey = stored.ActionKey(0) + key;
        }

        await using var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        await publisher.Services.GetRequiredService<IMessageBus>().PublishAsync(new ExecuteAutomationAction(world.OrganizationId, actionId, actionKey));
    }
}
