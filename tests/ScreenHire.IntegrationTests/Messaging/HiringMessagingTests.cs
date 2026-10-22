using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.Messaging;

/// <summary>
/// Each hiring endpoint that raises an event commits it with the data (the EF Core outbox), and the worker handles it once
/// as the event's tenant. The worker keeps running here, so these tests run in parallel with the others. A message is found
/// by an id only it carries (a version id, a timeline entry id), because several events of one job carry the job's id.
/// </summary>
public sealed class HiringMessagingTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Publishing_a_job_raises_one_event_the_worker_handles()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Messaging publish"));
        await recruiter.PublishJobAsync(job.Id());
        var versionId = (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/versions")).Text("0.id");

        await AssertHandledOnceAsync(versionId, "the worker to handle JobPublished", ".JobPublished");
    }

    [Fact]
    public async Task Closing_a_job_raises_one_event_and_closing_it_again_raises_none()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Messaging close"));

        // A draft was never published, so JobClosed is the only event that carries the job's id (its webhook fan-out
        // carries it too, hence the type).
        await recruiter.CloseJobAsync(job.Id());
        await AssertHandledOnceAsync(job.Text("id"), "the worker to handle JobClosed", ".JobClosed");
        await recruiter.CloseJobAsync(job.Id());

        var store = await WolverineStore.ForAppHostAsync(fixture, TestContext.Current.CancellationToken);
        (await store.CountIncomingAsync(job.Text("id"), TestContext.Current.CancellationToken, ".JobClosed")).Should().Be(1);
        (await store.CountOutgoingAsync(job.Text("id"), TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Adding_an_application_raises_one_event_the_worker_handles()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Messaging apply"));
        var application = await recruiter.CreateApplicationAsync(job.Id(), "casey.morgan@example.com");
        var submittedEntryId = (await recruiter.GetAsync($"/v1/applications/{application.Id()}/timeline")).Text("0.id");

        await AssertHandledOnceAsync(submittedEntryId, "the worker to handle ApplicationSubmitted");
    }

    [Fact]
    public async Task Moving_an_application_raises_one_event_the_worker_handles()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Messaging move"));
        var application = await recruiter.CreateApplicationAsync(job.Id(), "casey.morgan@example.com");
        (await recruiter.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        var movedEntryId = (await recruiter.GetAsync($"/v1/applications/{application.Id()}/timeline")).Text("1.id");

        await AssertHandledOnceAsync(movedEntryId, "the worker to handle ApplicationMoved");
    }

    [Fact]
    public async Task Publishing_a_template_version_raises_one_event_the_worker_handles()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        var (_, versionId) = await recruiter.CreatePublishedTemplateAsync("Messaging template");

        await AssertHandledOnceAsync(versionId.ToString(), "the worker to handle HiringTemplateVersionPublished");
    }

    // messageType narrows the count to the event when the worker's own follow-up messages carry the same id, such as a
    // job event's webhook fan-out (FanOutWebhook).
    private async Task AssertHandledOnceAsync(string needle, string because, string? messageType = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = await WolverineStore.ForAppHostAsync(fixture, cancellationToken);

        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(needle, cancellationToken, messageType) == 1, because, cancellationToken);

        (await store.CountIncomingAsync(needle, cancellationToken, messageType)).Should().Be(1, "the event is delivered to the worker exactly once");
        (await store.CountOutgoingAsync(needle, cancellationToken)).Should().Be(0, "the outbox is flushed");
        (await store.GetDeadLettersAsync(needle, cancellationToken)).Should().BeEmpty("the handler found the row as the event's tenant");
    }
}
