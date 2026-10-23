using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Applications;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests.Automations;

/// <summary>
/// The workflow engine end to end, through the running API and worker: an automation on a stage sends its email when an
/// application enters the stage, the run is audited, and a replayed event sends nothing twice.
/// </summary>
public sealed class AutomationEmailTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Entering_a_stage_sends_the_configured_email_once_even_when_the_event_is_replayed()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await AutomationSamples.JobWithAutomationsAsync(recruiter, "Automation email", AutomationSamples.EmailOnStage("screening", "stage-change"));
        var candidateEmail = ApiSession.NewEmail("candidate");
        var application = await recruiter.CreateApplicationAsync(job, candidateEmail, "Robin Ellis");

        (await recruiter.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);

        var sent = await fixture.Mailpit.WaitForMessagesAsync(candidateEmail, 1, Ct);
        sent.Should().ContainSingle();
        sent[0].Subject.Should().Be("An update on your application for Automation email");
        sent[0].Text.Should().Contain("Hi Robin Ellis").And.Contain("has moved to the next step: Screening");

        var run = await AutomationSamples.WaitForRunAsync(recruiter, application.Id(), "email-on-stage", "Completed");
        run.GetProperty("trigger").GetString().Should().Be("StageEntered");
        run.GetProperty("actions")[0].GetProperty("status").GetString().Should().Be("Completed");

        // Replay: the same ApplicationMoved (same timeline event id) delivered again in a new envelope, as a republish
        // or a recovered outbox would. The worker handles it, and the run that already exists stops a second email.
        var moved = (await recruiter.GetAsync($"/v1/applications/{application.Id()}/timeline")).At("1");
        var eventId = moved.GetProperty("id").GetGuid();
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        var handledBefore = await store.CountHandledAsync(eventId.ToString(), Ct);

        await using (var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct))
        {
            await publisher.Services.GetRequiredService<IMessageBus>().PublishAsync(new ApplicationMoved(
                recruiter.OrganizationId, application.Id(), job, "applied", "screening", eventId, moved.GetProperty("occurredAt").GetDateTimeOffset()));
            await WolverineStore.WaitUntilAsync(
                async () => await store.CountHandledAsync(eventId.ToString(), Ct) > handledBefore, "the worker to handle the replayed move", Ct);
        }

        (await fixture.Mailpit.MessagesAsync(candidateEmail, Ct)).Should().ContainSingle("the replay found the run and sent nothing");
        (await recruiter.GetAsync($"/v1/applications/{application.Id()}/automation-runs")).Expect(HttpStatusCode.OK)
            .Count().Should().Be(1, "each automation runs once per event");
    }

    // The stage-change email names the stage the application is in when it sends. A person rejecting an application
    // fires StageExited on the stage it leaves; an email there would read "moved to the next step: Rejected".
    [Fact]
    public async Task Leaving_a_stage_for_the_rejected_stage_sends_no_stage_change_email()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await AutomationSamples.JobWithAutomationsAsync(recruiter, "Exit email", new JsonObject
        {
            ["id"] = "email-on-exit",
            ["name"] = "Email on leaving screening",
            ["trigger"] = new JsonObject { ["type"] = "StageExited", ["stageId"] = "screening" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "SendEmail", ["emailTemplate"] = "stage-change" }),
        });
        var candidateEmail = ApiSession.NewEmail("candidate");
        var application = await recruiter.CreateApplicationAsync(job, candidateEmail);

        (await recruiter.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        (await recruiter.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "rejected" })).Expect(HttpStatusCode.OK);

        var run = await AutomationSamples.WaitForRunAsync(recruiter, application.Id(), "email-on-exit", "Completed");
        run.GetProperty("actions")[0].GetProperty("status").GetString().Should().Be("Skipped");
        (await fixture.Mailpit.MessagesAsync(candidateEmail, Ct)).Should().BeEmpty("a skipped action queues no email");
    }

    [Fact]
    public async Task A_new_application_gets_the_application_received_email()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await AutomationSamples.JobWithAutomationsAsync(recruiter, "Received email", new JsonObject
        {
            ["id"] = "received",
            ["name"] = "Confirm the application",
            ["trigger"] = new JsonObject { ["type"] = "ApplicationSubmitted" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "SendEmail", ["emailTemplate"] = "application-received" }),
        });
        var candidateEmail = ApiSession.NewEmail("candidate");

        await recruiter.CreateApplicationAsync(job, candidateEmail);

        var sent = await fixture.Mailpit.WaitForMessagesAsync(candidateEmail, 1, Ct);
        sent[0].Subject.Should().Be("We received your application for Received email");
        sent[0].Html.Should().Contain("<p>Thank you for applying for Received email at");
    }
}
