using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.IntegrationTests.Messaging;

/// <summary>
/// The API publishes through the EF Core outbox and the worker handles the message, against the shared AppHost.
/// These tests stop and start the worker, so their collection runs on its own after the parallel tests.
/// </summary>
[Collection(nameof(WorkerLifecycle))]
public sealed class WorkerMessagingTests(AppHostFixture fixture)
{
    private const string WorkerQueue = "worker";

    [Fact]
    public async Task Publishing_a_job_commits_it_with_its_event_and_the_worker_handles_the_event_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await CreateDraftJobAsync(cancellationToken);
        var store = await WolverineStore.ForAppHostAsync(fixture, cancellationToken);
        using var client = fixture.CreateHttpClient("api");

        var response = await client.PostAsync($"/v1/jobs/{job.Id}/publish", content: null, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        summary.GetProperty("status").GetString().Should().Be("Open");
        (await LoadJobAsync(job.Id, cancellationToken)).Status.Should().Be(JobStatus.Open);

        var jobId = job.Id.ToString();
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(jobId, cancellationToken) == 1, "the worker to handle JobPublished", cancellationToken);
        (await store.CountIncomingAsync(jobId, cancellationToken)).Should().Be(1, "the event is delivered to the worker exactly once");
        (await store.CountOutgoingAsync(jobId, cancellationToken)).Should().Be(0, "the outbox is flushed");
        (await store.CountQueuedAsync(WorkerQueue, jobId, cancellationToken)).Should().Be(0, "the worker took the event off the queue");
    }

    [Fact]
    public async Task Publishing_an_open_job_again_raises_no_second_event()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await CreateDraftJobAsync(cancellationToken);
        var store = await WolverineStore.ForAppHostAsync(fixture, cancellationToken);
        using var client = fixture.CreateHttpClient("api");
        var jobId = job.Id.ToString();

        (await client.PostAsync($"/v1/jobs/{job.Id}/publish", content: null, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(jobId, cancellationToken) == 1, "the worker to handle JobPublished", cancellationToken);

        (await client.PostAsync($"/v1/jobs/{job.Id}/publish", content: null, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The second call commits nothing, so there is no outbox row, queue row or delivery to wait for.
        (await store.CountOutgoingAsync(jobId, cancellationToken)).Should().Be(0);
        (await store.CountQueuedAsync(WorkerQueue, jobId, cancellationToken)).Should().Be(0);
        (await store.CountIncomingAsync(jobId, cancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task A_message_published_while_the_worker_is_stopped_is_handled_after_it_restarts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await CreateDraftJobAsync(cancellationToken);
        var store = await WolverineStore.ForAppHostAsync(fixture, cancellationToken);
        using var client = fixture.CreateHttpClient("api");
        var jobId = job.Id.ToString();

        await StopWorkerAsync(cancellationToken);
        try
        {
            (await client.PostAsync($"/v1/jobs/{job.Id}/publish", content: null, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

            // Committed and sent: the event waits in the Postgres queue, and nothing has received it.
            await WolverineStore.WaitUntilAsync(
                async () => await store.CountQueuedAsync(WorkerQueue, jobId, cancellationToken) == 1, "JobPublished to reach the worker queue", cancellationToken);
            (await store.CountIncomingAsync(jobId, cancellationToken)).Should().Be(0, "no process handles messages while the worker is stopped");
        }
        finally
        {
            await StartWorkerAsync(cancellationToken);
        }

        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(jobId, cancellationToken) == 1, "the restarted worker to handle JobPublished", cancellationToken);
        (await store.CountQueuedAsync(WorkerQueue, jobId, cancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Publishing_an_unknown_job_returns_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateHttpClient("api");

        var response = await client.PostAsync($"/v1/jobs/{Guid.NewGuid()}/publish", content: null, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Publishing_a_closed_job_is_a_conflict_and_raises_no_event()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Job? closed = null;
        await fixture.CreateOrganizationAsync(org =>
        {
            closed = TestData.NewJob(org, "closed");
            closed.Publish(DateTimeOffset.UtcNow);
            closed.Close(DateTimeOffset.UtcNow);
            return [closed];
        }, cancellationToken);
        var store = await WolverineStore.ForAppHostAsync(fixture, cancellationToken);
        using var client = fixture.CreateHttpClient("api");

        var response = await client.PostAsync($"/v1/jobs/{closed!.Id}/publish", content: null, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await LoadJobAsync(closed.Id, cancellationToken)).Status.Should().Be(JobStatus.Closed);
        (await store.CountOutgoingAsync(closed.Id.ToString(), cancellationToken)).Should().Be(0);
        (await store.CountQueuedAsync(WorkerQueue, closed.Id.ToString(), cancellationToken)).Should().Be(0);
    }

    private async Task<Job> CreateDraftJobAsync(CancellationToken cancellationToken)
    {
        Job? draft = null;
        await fixture.CreateOrganizationAsync(org => [draft = TestData.NewJob(org, "draft")], cancellationToken);
        return draft!;
    }

    private async Task<Job> LoadJobAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await fixture.CreateDbContextAsync(cancellationToken);
        return await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == id, cancellationToken);
    }

    private async Task StopWorkerAsync(CancellationToken cancellationToken)
    {
        var result = await fixture.App.ResourceCommands.ExecuteCommandAsync("worker", KnownResourceCommands.StopCommand, cancellationToken);
        result.Success.Should().BeTrue(result.Message);
        await fixture.App.ResourceNotifications.WaitForResourceAsync("worker", KnownResourceStates.TerminalStates, cancellationToken);
    }

    private async Task StartWorkerAsync(CancellationToken cancellationToken)
    {
        var result = await fixture.App.ResourceCommands.ExecuteCommandAsync("worker", KnownResourceCommands.StartCommand, cancellationToken);
        result.Success.Should().BeTrue(result.Message);
        await fixture.App.ResourceNotifications.WaitForResourceAsync("worker", KnownResourceStates.Running, cancellationToken);
    }
}

/// <summary>Tests that stop and start the worker. xUnit runs this collection alone, after the parallel ones.</summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class WorkerLifecycle;
