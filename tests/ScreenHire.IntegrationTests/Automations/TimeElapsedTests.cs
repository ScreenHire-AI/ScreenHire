using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests.Automations;

/// <summary>
/// TimeElapsed through the running stack. Entering a stage schedules a durable timer. A timer that fires runs its
/// automation, and one that counts from a stage entry the application has since left does nothing. A timer waits at
/// least an hour, so the test fires it by publishing the message the schedule would release.
/// </summary>
public sealed class TimeElapsedTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Entering_a_stage_schedules_a_timer_that_runs_when_due_and_does_nothing_once_the_stage_is_left()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await AutomationSamples.JobWithAutomationsAsync(recruiter, "Timer workflow", new JsonObject
        {
            ["id"] = "waiting",
            ["name"] = "Mark long waits",
            ["trigger"] = new JsonObject { ["type"] = "TimeElapsed", ["stageId"] = "screening", ["afterHours"] = 24 },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "AddTag", ["tag"] = "waiting" }),
        });
        var application = await recruiter.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"));
        (await recruiter.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);

        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountScheduledAsync(application.Id().ToString(), Ct) == 1, "the stage entry to schedule its timer", Ct);

        var timeline = (await recruiter.GetAsync($"/v1/applications/{application.Id()}/timeline")).Body;
        var submittedId = timeline[0].GetProperty("id").GetGuid();
        var entry = timeline[1];
        var entryId = entry.GetProperty("id").GetGuid();
        var due = entry.GetProperty("occurredAt").GetDateTimeOffset().AddHours(24);

        await using var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        var bus = publisher.Services.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new RunAutomations(recruiter.OrganizationId, application.Id(), TriggerType.TimeElapsed, "screening", entryId, due, "waiting"));

        var run = await AutomationSamples.WaitForRunAsync(recruiter, application.Id(), "waiting", "Completed");
        run.GetProperty("eventId").GetGuid().Should().Be(entryId, "the run is keyed on the stage entry the timer counts from");
        run.GetProperty("facts").GetProperty("hoursInStage").GetInt32().Should().Be(24);
        (await recruiter.GetAsync($"/v1/applications/{application.Id()}")).Text("tags.0").Should().Be("waiting");

        // A timer from an entry the application has left (its first stage) fires and is dropped: no run, no tag.
        var handledBefore = await store.CountHandledAsync(submittedId.ToString(), Ct);
        await bus.PublishAsync(new RunAutomations(
            recruiter.OrganizationId, application.Id(), TriggerType.TimeElapsed, "screening", submittedId, due, "waiting"));
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(submittedId.ToString(), Ct) > handledBefore, "the worker to handle the stale timer", Ct);

        var runs = (await recruiter.GetAsync($"/v1/applications/{application.Id()}/automation-runs")).Body;
        runs.EnumerateArray().Should().ContainSingle(r => r.GetProperty("automationId").GetString() == "waiting", "the stale timer ran nothing");
    }
}
