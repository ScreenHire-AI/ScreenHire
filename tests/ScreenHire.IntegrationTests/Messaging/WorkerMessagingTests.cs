using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Messaging;

/// <summary>
/// The API publishes through the EF Core outbox and the worker handles the message, against the shared AppHost. Both
/// connect as <c>screenhire_app</c>, so this is the production shape: row-level security on the job, the outbox write
/// and the worker's handler, which sets the tenant from the message.
/// These tests stop and start the worker, so their collection runs on its own after the parallel tests.
/// </summary>
[Collection<WorkerLifecycle>]
public sealed class WorkerMessagingTests(AppHostFixture fixture)
{
    private const string WorkerQueue = "worker";

    // A published job's id is also in its postings' commands and events (job distribution), so counts name the type.
    private const string JobPublishedType = ".JobPublished";

    [Fact]
    public async Task Publishing_a_job_commits_it_with_its_event_and_the_worker_handles_the_event_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, job) = await SignedInWithJobAsync(cancellationToken);
        using var _ = session;
        var store = await WolverineStore.ForAppHostAsync(fixture, cancellationToken);

        using var response = await PublishAsync(session, job.Id, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        summary.GetProperty("status").GetString().Should().Be("Open");
        (await LoadJobAsync(job, cancellationToken)).Status.Should().Be(JobStatus.Open);

        var jobId = job.Id.ToString();
        // The handler reads the job back, which works only when the worker set the message's tenant (JobPublishedHandler).
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(jobId, cancellationToken, JobPublishedType) == 1, "the worker to handle JobPublished", cancellationToken);
        (await store.CountIncomingAsync(jobId, cancellationToken, JobPublishedType)).Should().Be(1, "the event is delivered to the worker exactly once");
        // The API's outbox sends to the worker queue and the real-time queue separately; both rows go once sent.
        await WolverineStore.WaitUntilAsync(
            async () => await CountApiOutboxAsync(jobId, cancellationToken) == 0, "the API's outbox to be flushed", cancellationToken);
        (await store.CountQueuedAsync(WorkerQueue, jobId, cancellationToken, JobPublishedType)).Should().Be(0, "the worker took the event off the queue");
        (await store.GetDeadLettersAsync(jobId, cancellationToken)).Should().BeEmpty("the handler found the job as its tenant");
    }

    [Fact]
    public async Task Publishing_an_open_job_again_raises_no_second_event()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, job) = await SignedInWithJobAsync(cancellationToken);
        using var _ = session;
        var store = await WolverineStore.ForAppHostAsync(fixture, cancellationToken);
        var jobId = job.Id.ToString();

        (await PublishAsync(session, job.Id, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(jobId, cancellationToken, JobPublishedType) == 1, "the worker to handle JobPublished", cancellationToken);
        await WolverineStore.WaitUntilAsync(
            async () => await CountApiOutboxAsync(jobId, cancellationToken) == 0, "the API's outbox to be flushed", cancellationToken);

        (await PublishAsync(session, job.Id, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The second call commits nothing, so there is no outbox row, queue row or delivery to wait for.
        (await CountApiOutboxAsync(jobId, cancellationToken)).Should().Be(0);
        (await store.CountQueuedAsync(WorkerQueue, jobId, cancellationToken, JobPublishedType)).Should().Be(0);
        (await store.CountIncomingAsync(jobId, cancellationToken, JobPublishedType)).Should().Be(1);
    }

    [Fact]
    public async Task A_message_published_while_the_worker_is_stopped_is_handled_after_it_restarts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, job) = await SignedInWithJobAsync(cancellationToken);
        using var _ = session;
        var store = await WolverineStore.ForAppHostAsync(fixture, cancellationToken);
        var jobId = job.Id.ToString();

        await StopWorkerAsync(cancellationToken);
        try
        {
            (await PublishAsync(session, job.Id, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

            // Committed and sent: the event waits in the Postgres queue, and nothing has received it.
            await WolverineStore.WaitUntilAsync(
                async () => await store.CountQueuedAsync(WorkerQueue, jobId, cancellationToken, JobPublishedType) == 1, "JobPublished to reach the worker queue", cancellationToken);
            (await store.CountIncomingAsync(jobId, cancellationToken, JobPublishedType)).Should().Be(0, "no process handles messages while the worker is stopped");
        }
        finally
        {
            await StartWorkerAsync(cancellationToken);
        }

        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(jobId, cancellationToken, JobPublishedType) == 1, "the restarted worker to handle JobPublished", cancellationToken);
        (await store.CountQueuedAsync(WorkerQueue, jobId, cancellationToken, JobPublishedType)).Should().Be(0);
    }

    [Fact]
    public async Task Publishing_an_unknown_job_returns_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, _) = await SignedInWithJobAsync(cancellationToken);
        using var __ = session;

        using var response = await PublishAsync(session, Guid.NewGuid(), cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Publishing_another_organizations_job_is_not_found_and_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, _) = await SignedInWithJobAsync(cancellationToken);
        using var __ = session;
        Job? theirs = null;
        await fixture.CreateOrganizationAsync(org => [theirs = TestData.NewJob(org, "theirs")], cancellationToken);
        var store = await WolverineStore.ForAppHostAsync(fixture, cancellationToken);

        using var response = await PublishAsync(session, theirs!.Id, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "another tenant's job looks exactly like a missing one");
        (await LoadJobAsync(theirs, cancellationToken)).Status.Should().Be(JobStatus.Draft);
        (await CountApiOutboxAsync(theirs.Id.ToString(), cancellationToken)).Should().Be(0);
        (await store.CountQueuedAsync(WorkerQueue, theirs.Id.ToString(), cancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Publishing_without_the_antiforgery_header_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, job) = await SignedInWithJobAsync(cancellationToken);
        using var _ = session;

        using var response = await session.Client.PostAsync($"/v1/jobs/{job.Id}/publish", content: null, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LoadJobAsync(job, cancellationToken)).Status.Should().Be(JobStatus.Draft);
    }

    [Fact]
    public async Task Publishing_signed_out_is_unauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);

        using var response = await PublishAsync(session, Guid.NewGuid(), cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Publishing_a_closed_job_is_a_conflict_and_raises_no_event()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, organizationId) = await SignedInAsync(cancellationToken);
        using var _ = session;
        var closed = TestData.NewJob(organizationId, "closed");
        closed.Publish(DateTimeOffset.UtcNow);
        closed.Close(DateTimeOffset.UtcNow);
        await fixture.AddJobsAsync(organizationId, [closed], cancellationToken);
        var store = await WolverineStore.ForAppHostAsync(fixture, cancellationToken);

        using var response = await PublishAsync(session, closed.Id, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await LoadJobAsync(closed, cancellationToken)).Status.Should().Be(JobStatus.Closed);
        (await CountApiOutboxAsync(closed.Id.ToString(), cancellationToken)).Should().Be(0);
        (await store.CountQueuedAsync(WorkerQueue, closed.Id.ToString(), cancellationToken)).Should().Be(0);
    }

    // The API keeps its outbox in its own schema (docs/architecture/realtime.md).
    private async Task<long> CountApiOutboxAsync(string bodyContains, CancellationToken cancellationToken) =>
        await (await WolverineStore.ForAppHostApiAsync(fixture, cancellationToken)).CountOutgoingAsync(bodyContains, cancellationToken);

    private static Task<HttpResponseMessage> PublishAsync(ApiSession session, Guid jobId, CancellationToken cancellationToken) =>
        session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, cancellationToken);

    private async Task<(ApiSession Session, Guid OrganizationId)> SignedInAsync(CancellationToken cancellationToken)
    {
        var session = new ApiSession(fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail("publisher"), cancellationToken);
        return (session, await session.OnboardAsync(cancellationToken));
    }

    // A signed-in member of a new organization, and a draft job in it.
    private async Task<(ApiSession Session, Job Job)> SignedInWithJobAsync(CancellationToken cancellationToken)
    {
        var (session, organizationId) = await SignedInAsync(cancellationToken);
        var draft = TestData.NewJob(organizationId, "draft");
        await fixture.AddJobsAsync(organizationId, [draft], cancellationToken);
        return (session, draft);
    }

    private async Task<Job> LoadJobAsync(Job job, CancellationToken cancellationToken)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(job.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, cancellationToken);
        return await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id, cancellationToken);
    }

    private async Task StopWorkerAsync(CancellationToken cancellationToken)
    {
        var result = await fixture.App.ResourceCommands.ExecuteCommandAsync("worker", KnownResourceCommands.StopCommand, cancellationToken);
        result.Success.Should().BeTrue(result.Message);
        await fixture.App.ResourceNotifications.WaitForResourceAsync("worker", KnownResourceStates.TerminalStates, cancellationToken);
    }

    // Running is only the process; the worker handles messages once its Wolverine node leads again, which can take a
    // minute on a busy machine, so the tests' waits start from there.
    private async Task StartWorkerAsync(CancellationToken cancellationToken)
    {
        var stopped = await fixture.GetDatabaseTimeAsync(cancellationToken);
        var result = await fixture.App.ResourceCommands.ExecuteCommandAsync("worker", KnownResourceCommands.StartCommand, cancellationToken);
        result.Success.Should().BeTrue(result.Message);
        await fixture.App.ResourceNotifications.WaitForResourceAsync("worker", KnownResourceStates.Running, cancellationToken);
        await fixture.WaitUntilWorkerRunsAgentsAsync(stopped, cancellationToken);
    }
}

/// <summary>Tests that stop and start the worker. xUnit runs this collection alone, after the parallel ones.</summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class WorkerLifecycle;
