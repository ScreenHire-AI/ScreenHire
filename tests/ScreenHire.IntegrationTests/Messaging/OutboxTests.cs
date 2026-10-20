using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Jobs;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.IntegrationTests.Messaging;

/// <summary>
/// The EF Core transactional outbox as the API configures it: an entity change and the message published with it
/// commit together or not at all.
/// </summary>
public sealed class OutboxTests(AppHostFixture fixture)
{
    private readonly MessagingSettings settings = new();

    [Fact]
    public async Task A_message_published_with_a_change_is_sent_when_the_change_commits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: cancellationToken);
        await using var publisher = await MessagingHost.StartPublisherAsync(fixture, cancellationToken);
        var job = TestData.NewJob(organization, "committed");

        await using (var scope = publisher.Services.CreateAsyncScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<ScreenHireDbContext>>();
            outbox.DbContext.Jobs.Add(job);
            await outbox.PublishAsync(new JobPublished(organization.Id, job.Id, DateTimeOffset.UtcNow));
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }

        (await JobExistsAsync(organization.Id, job.Id, cancellationToken)).Should().BeTrue();
        await WolverineStore.WaitUntilAsync(
            async () => await publisher.Store.CountQueuedAsync(settings.WorkerQueue, job.Id.ToString(), cancellationToken) == 1,
            "JobPublished to reach the worker queue", cancellationToken);
        (await publisher.Store.CountOutgoingAsync(job.Id.ToString(), cancellationToken)).Should().Be(0, "the outbox is flushed");
    }

    [Fact]
    public async Task A_message_published_with_a_change_that_fails_to_commit_is_never_sent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var organization = await fixture.CreateOrganizationAsync(org => [TestData.NewJob(org, "taken")], cancellationToken);
        await using var publisher = await MessagingHost.StartPublisherAsync(fixture, cancellationToken);
        var duplicate = TestData.NewJob(organization, "taken");

        await using (var scope = publisher.Services.CreateAsyncScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<ScreenHireDbContext>>();
            outbox.DbContext.Jobs.Add(duplicate);
            await outbox.PublishAsync(new JobPublished(organization.Id, duplicate.Id, DateTimeOffset.UtcNow));

            var save = () => outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

            await save.Should().ThrowAsync<DbUpdateException>("job slugs are unique within an organization");
        }

        (await JobExistsAsync(organization.Id, duplicate.Id, cancellationToken)).Should().BeFalse();
        (await publisher.Store.CountOutgoingAsync(duplicate.Id.ToString(), cancellationToken)).Should().Be(0, "the outbox row rolled back with the job");
        (await publisher.Store.CountQueuedAsync(settings.WorkerQueue, duplicate.Id.ToString(), cancellationToken)).Should().Be(0, "nothing was sent");
    }

    private async Task<bool> JobExistsAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, cancellationToken);
        return await db.Jobs.AnyAsync(j => j.Id == id, cancellationToken);
    }
}
