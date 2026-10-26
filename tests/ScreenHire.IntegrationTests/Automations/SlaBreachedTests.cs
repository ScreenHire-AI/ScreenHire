using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests.Automations;

/// <summary>
/// Stage time limits (SLAs) through the running stack. Entering a stage with a limit schedules a durable SlaBreached
/// timer due when the limit passes, and the application shows when it falls overdue. The timer runs its automation
/// once per stage entry: never for an entry the application has left, never twice for a redelivered message, and for a
/// candidate with an accommodation too. A limit waits days, so the test fires the timer by publishing the message the
/// schedule would release.
/// </summary>
public sealed class SlaBreachedTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Screening has a three-day limit in the default workflow.
    private static JsonObject Reminder() => new()
    {
        ["id"] = "reply-reminder",
        ["name"] = "Remind the team to reply",
        ["trigger"] = new JsonObject { ["type"] = "SlaBreached", ["stageId"] = "screening" },
        ["actions"] = new JsonArray(new JsonObject { ["type"] = "CreateTask", ["note"] = "Reply to this candidate." }),
    };

    [Fact]
    public async Task A_breach_fires_once_per_stage_entry_and_never_after_the_application_left_the_stage()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await AutomationSamples.JobWithAutomationsAsync(recruiter, "Time limit workflow", Reminder());
        var application = (await recruiter.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        (await recruiter.GetAsync($"/v1/applications/{application}")).At("slaDueAt").ValueKind.Should().Be(JsonValueKind.Null, "applied has no time limit");

        (await recruiter.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        var timeline = (await recruiter.GetAsync($"/v1/applications/{application}/timeline")).Body;
        var submittedId = timeline[0].GetProperty("id").GetGuid();
        var entryId = timeline[1].GetProperty("id").GetGuid();
        var due = timeline[1].GetProperty("occurredAt").GetDateTimeOffset().AddDays(3);

        // The application and the lists say when it falls overdue; the timer waits for the same instant.
        (await recruiter.GetAsync($"/v1/applications/{application}")).At("slaDueAt").GetDateTimeOffset().Should().Be(due);
        (await recruiter.GetAsync("/v1/applications")).Body.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == application)
            .GetProperty("slaDueAt").GetDateTimeOffset().Should().Be(due);
        (await recruiter.GetAsync($"/v1/jobs/{job}/applications")).At("0.slaDueAt").GetDateTimeOffset().Should().Be(due);
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await WolverineStore.WaitUntilAsync(
            async () => (await store.ScheduledTimesAsync(application.ToString(), Ct)).Count == 1, "the stage entry to schedule its timer", Ct);
        (await store.ScheduledTimesAsync(application.ToString(), Ct)).Should().Equal(due);

        await using var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        var bus = publisher.Services.GetRequiredService<IMessageBus>();
        var breach = new RunAutomations(recruiter.OrganizationId, application, TriggerType.SlaBreached, "screening", entryId, due, "reply-reminder");
        await bus.PublishAsync(breach);

        var run = await AutomationSamples.WaitForRunAsync(recruiter, application, "reply-reminder", "Completed");
        run.GetProperty("eventId").GetGuid().Should().Be(entryId, "the run is keyed on the stage entry");
        run.GetProperty("trigger").GetString().Should().Be("SlaBreached");
        var tasks = (await recruiter.GetAsync($"/v1/applications/{application}/tasks")).Expect(HttpStatusCode.OK);
        tasks.Count().Should().Be(1);
        tasks.Text("0.note").Should().Be("Reply to this candidate.");

        // The same message again (a redelivery) and a timer from the entry the application left both run nothing.
        var handledBefore = await store.CountHandledAsync(application.ToString(), Ct, ".RunAutomations");
        await bus.PublishAsync(breach);
        await bus.PublishAsync(breach with { EventId = submittedId, StageId = "applied" });
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(application.ToString(), Ct, ".RunAutomations") >= handledBefore + 2,
            "the worker to handle the redelivery and the stale timer", Ct);

        (await recruiter.GetAsync($"/v1/applications/{application}/automation-runs")).Body.EnumerateArray()
            .Should().ContainSingle(r => r.GetProperty("automationId").GetString() == "reply-reminder");
        (await recruiter.GetAsync($"/v1/applications/{application}/tasks")).Count().Should().Be(1, "one breach per stage entry");

        // Another organization cannot see the application or its due time.
        using var other = await fixture.SignedInWithOrganizationAsync();
        (await other.GetAsync($"/v1/applications/{application}")).Expect(HttpStatusCode.NotFound);
        (await other.GetAsync($"/v1/jobs/{job}/applications")).Expect(HttpStatusCode.NotFound);
        (await other.GetAsync("/v1/applications")).Body.EnumerateArray().Should().NotContain(a => a.GetProperty("id").GetGuid() == application);
    }

    // Leaving a stage and coming back is a new entry with its own timer; the first entry's timer runs nothing. A timer
    // scheduled under a shorter limit than the stage has now is dropped (workflow-engine.md section 8).
    [Fact]
    public async Task A_breach_runs_only_for_the_current_entry_and_its_current_limit()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await AutomationSamples.JobWithAutomationsAsync(recruiter, "Time limit re-entry", Reminder());
        var application = (await recruiter.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        foreach (var stage in new[] { "screening", "applied", "screening" })
        {
            (await recruiter.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = stage })).Expect(HttpStatusCode.OK);
        }

        var entries = (await recruiter.GetAsync($"/v1/applications/{application}/timeline")).Body.EnumerateArray()
            .Where(e => e.TryGetProperty("toStageId", out var to) && to.GetString() == "screening")
            .Select(e => (Id: e.GetProperty("id").GetGuid(), Due: e.GetProperty("occurredAt").GetDateTimeOffset().AddDays(3)))
            .ToList();
        entries.Should().HaveCount(2);
        var (left, current) = (entries[0], entries[1]);
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await WolverineStore.WaitUntilAsync(
            async () => (await store.ScheduledTimesAsync(application.ToString(), Ct)).Count == 2, "each screening entry to schedule its own timer", Ct);

        await using var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        var bus = publisher.Services.GetRequiredService<IMessageBus>();
        var handledBefore = await store.CountHandledAsync(application.ToString(), Ct, ".RunAutomations");
        await bus.PublishAsync(new RunAutomations(recruiter.OrganizationId, application, TriggerType.SlaBreached, "screening", left.Id, left.Due, "reply-reminder"));
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(application.ToString(), Ct, ".RunAutomations") >= handledBefore + 1,
            "the worker to handle the timer of the entry the application left", Ct);
        (await recruiter.GetAsync($"/v1/applications/{application}/automation-runs")).Body.EnumerateArray()
            .Should().NotContain(r => r.GetProperty("automationId").GetString() == "reply-reminder");

        await bus.PublishAsync(new RunAutomations(recruiter.OrganizationId, application, TriggerType.SlaBreached, "screening", current.Id, current.Due, "reply-reminder"));
        var run = await AutomationSamples.WaitForRunAsync(recruiter, application, "reply-reminder", "Completed");
        run.GetProperty("eventId").GetGuid().Should().Be(current.Id);
        (await recruiter.GetAsync($"/v1/applications/{application}/tasks")).Count().Should().Be(1);

        // A second application whose timer was scheduled when screening's limit was two days: it is three now, so the
        // timer is early and runs nothing.
        var early = (await recruiter.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        (await recruiter.PostAsync($"/v1/applications/{early}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        var entry = (await recruiter.GetAsync($"/v1/applications/{early}/timeline")).Body[1];
        var earlyHandled = await store.CountHandledAsync(early.ToString(), Ct, ".RunAutomations");
        await bus.PublishAsync(new RunAutomations(
            recruiter.OrganizationId, early, TriggerType.SlaBreached, "screening", entry.GetProperty("id").GetGuid(),
            entry.GetProperty("occurredAt").GetDateTimeOffset().AddDays(2), "reply-reminder"));
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(early.ToString(), Ct, ".RunAutomations") >= earlyHandled + 1,
            "the worker to handle the early timer", Ct);
        (await recruiter.GetAsync($"/v1/applications/{early}/automation-runs")).Body.EnumerateArray()
            .Should().NotContain(r => r.GetProperty("automationId").GetString() == "reply-reminder");
        (await recruiter.GetAsync($"/v1/applications/{early}/tasks")).Count().Should().Be(0);
    }

    // A time limit measures the team's delay, so an accommodation never skips the reminder.
    [Fact]
    public async Task A_breach_reminds_the_team_about_a_candidate_with_an_accommodation()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await AutomationSamples.JobWithAutomationsAsync(recruiter, "Time limit accommodation", Reminder());
        var application = (await recruiter.CreateApplicationAsync(
            job, ApiSession.NewEmail("candidate"), extra: new JsonObject { ["accommodationRequested"] = true })).Id();
        (await recruiter.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        var entry = (await recruiter.GetAsync($"/v1/applications/{application}/timeline")).Body[1];
        var due = entry.GetProperty("occurredAt").GetDateTimeOffset().AddDays(3);

        await using var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        await publisher.Services.GetRequiredService<IMessageBus>().PublishAsync(new RunAutomations(
            recruiter.OrganizationId, application, TriggerType.SlaBreached, "screening", entry.GetProperty("id").GetGuid(), due, "reply-reminder"));

        await AutomationSamples.WaitForRunAsync(recruiter, application, "reply-reminder", "Completed");
        (await recruiter.GetAsync($"/v1/applications/{application}/tasks")).Text("0.note").Should().Be("Reply to this candidate.");
    }

    [Fact]
    public async Task Saving_refuses_a_breach_automation_that_could_count_against_the_candidate()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var queue = Reminder();
        queue["actions"] = new JsonArray(new JsonObject { ["type"] = "QueueForRejectionReview", ["reason"] = "Waited too long" });

        var result = (await recruiter.PostAsync("/v1/workflows/validate", new JsonObject
        {
            ["workflow"] = Samples.DefinitionWith(queue)["workflow"]!.DeepClone(),
        })).Expect(HttpStatusCode.OK);

        result.At("valid").GetBoolean().Should().BeFalse();
        result.Count("errors").Should().Be(1);
        result.Text("errors.0.path").Should().Be("automations[0].actions[0]");
        result.Text("errors.0.message").Should().Contain("hiring team's delay, not the candidate");
        (await recruiter.PostAsync("/v1/templates", new JsonObject { ["name"] = "Refused", ["definition"] = Samples.DefinitionWith(queue.DeepClone().AsObject()) }))
            .Expect(HttpStatusCode.BadRequest);
    }
}
