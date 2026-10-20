using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// <c>POST /v1/jobs/{jobId}/close</c>: the job closes and <c>JobClosed</c> commits with it, once; another organization's
/// job is a 404 like a missing one.
/// </summary>
public sealed class JobCloseTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Closing_an_open_job_closes_it_and_raises_JobClosed_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, organizationId) = await SignedInAsync(cancellationToken);
        using var _ = session;
        var job = await AddJobAsync(organizationId, open: true, cancellationToken);
        var store = await WolverineStore.ForAppHostAsync(fixture, cancellationToken);
        var jobId = job.Id.ToString();

        using var response = await CloseAsync(session, job.Id, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("status").GetString().Should().Be("Closed");
        var stored = await LoadJobAsync(job, cancellationToken);
        stored.Status.Should().Be(JobStatus.Closed);
        stored.ClosedAt.Should().NotBeNull();
        // No worker handler wants JobClosed yet; the worker queue still delivers it once and marks it handled.
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(jobId, cancellationToken) == 1, "JobClosed to reach the worker", cancellationToken);

        using var again = await CloseAsync(session, job.Id, cancellationToken);

        again.StatusCode.Should().Be(HttpStatusCode.OK, "closing a closed job changes nothing");
        (await LoadJobAsync(job, cancellationToken)).ClosedAt.Should().Be(stored.ClosedAt);
        (await store.CountQueuedAsync("worker", jobId, cancellationToken)).Should().Be(0);
        (await store.CountIncomingAsync(jobId, cancellationToken)).Should().Be(1, "the second close raised no event");
    }

    [Fact]
    public async Task Closing_a_draft_closes_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, organizationId) = await SignedInAsync(cancellationToken);
        using var _ = session;
        var job = await AddJobAsync(organizationId, open: false, cancellationToken);

        using var response = await CloseAsync(session, job.Id, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadJobAsync(job, cancellationToken)).Status.Should().Be(JobStatus.Closed);
    }

    [Fact]
    public async Task Closing_another_organizations_job_is_not_found_and_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, _) = await SignedInAsync(cancellationToken);
        using var __ = session;
        Job? theirs = null;
        await fixture.CreateOrganizationAsync(org =>
        {
            theirs = TestData.NewJob(org, "theirs");
            theirs.Publish(DateTimeOffset.UtcNow);
            return [theirs];
        }, cancellationToken);

        using var response = await CloseAsync(session, theirs!.Id, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LoadJobAsync(theirs, cancellationToken)).Status.Should().Be(JobStatus.Open);
    }

    [Fact]
    public async Task Closing_without_the_antiforgery_header_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, organizationId) = await SignedInAsync(cancellationToken);
        using var _ = session;
        var job = await AddJobAsync(organizationId, open: true, cancellationToken);

        using var response = await session.Client.PostAsync($"/v1/jobs/{job.Id}/close", content: null, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LoadJobAsync(job, cancellationToken)).Status.Should().Be(JobStatus.Open);
    }

    [Fact]
    public async Task Closing_signed_out_is_unauthorized()
    {
        using var session = new ApiSession(fixture);

        using var response = await CloseAsync(session, Guid.NewGuid(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static Task<HttpResponseMessage> CloseAsync(ApiSession session, Guid jobId, CancellationToken cancellationToken) =>
        session.PostAsync($"/v1/jobs/{jobId}/close", new { }, cancellationToken);

    private async Task<(ApiSession Session, Guid OrganizationId)> SignedInAsync(CancellationToken cancellationToken)
    {
        var session = new ApiSession(fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail("closer"), cancellationToken);
        return (session, await session.OnboardAsync(cancellationToken));
    }

    private async Task<Job> AddJobAsync(Guid organizationId, bool open, CancellationToken cancellationToken)
    {
        var job = TestData.NewJob(organizationId, open ? "open" : "draft");
        if (open)
        {
            job.Publish(DateTimeOffset.UtcNow);
        }

        await fixture.AddJobsAsync(organizationId, [job], cancellationToken);
        return job;
    }

    private async Task<Job> LoadJobAsync(Job job, CancellationToken cancellationToken)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(job.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, cancellationToken);
        return await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id, cancellationToken);
    }
}
