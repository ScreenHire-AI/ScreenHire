using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Jobs;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.Runtime.Recurring;

namespace ScreenHire.IntegrationTests.Messaging;

/// <summary>The worker's cron schedules (<see cref="ScreenHireMessaging.ScheduleRecurringMessages"/>), in a private schema.</summary>
public sealed class RecurringMessageTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Report_open_jobs_is_scheduled_every_five_minutes_and_its_handler_runs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = new MessagingSettings { Schema = MessagingHost.NewSchema(), AutoProvision = true };
        await using var worker = await MessagingHost.StartHandlerAsync(fixture, settings, [], cancellationToken, withRecurringSchedules: true);
        var schedules = worker.Services.GetRequiredService<IRecurringScheduleControl>();

        // The scheduling agent writes the durable tracking row on its first pass after startup.
        await WolverineStore.WaitUntilAsync(
            async () => (await schedules.QueryAsync(cancellationToken)).Any(s => s.Name == nameof(ReportOpenJobs)),
            "the ReportOpenJobs schedule to be tracked", cancellationToken);
        var schedule = (await schedules.QueryAsync(cancellationToken)).Should().ContainSingle(s => s.Name == nameof(ReportOpenJobs)).Subject;
        schedule.CronExpression.Should().Be("*/5 * * * *");
        schedule.Paused.Should().BeFalse();

        // Run it now instead of waiting for the next five-minute mark; the cron cadence is untouched.
        await schedules.TriggerAsync(nameof(ReportOpenJobs), cancellationToken);

        await WolverineStore.WaitUntilAsync(
            async () => await worker.Store.CountHandledOfTypeAsync(nameof(ReportOpenJobs), cancellationToken) >= 1,
            "the triggered ReportOpenJobs to be handled", cancellationToken);
        worker.Logs.Entries.Should().Contain(e => e.Message.Contains(" open jobs across ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Report_counts_each_organizations_open_jobs_as_the_application_role()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var two = await fixture.CreateOrganizationAsync(org =>
        {
            var first = TestData.NewJob(org, "first");
            first.Publish(now);
            var second = TestData.NewJob(org, "second");
            second.Publish(now);
            return [first, second, TestData.NewJob(org, "draft")];
        }, cancellationToken);
        var one = await fixture.CreateOrganizationAsync(org =>
        {
            var only = TestData.NewJob(org, "only");
            only.Publish(now);
            return [only];
        }, cancellationToken);

        // The worker's own shape: screenhire_app with no tenant, which row-level security shows no jobs at all.
        var tenant = new TenantContext();
        await using var db = await fixture.CreateDbContextAsync(tenant, cancellationToken);
        (await db.Jobs.IgnoreQueryFilters([ScreenHireDbContext.TenantFilter]).CountAsync(cancellationToken)).Should().Be(0);

        var counts = await ReportOpenJobsHandler.CountOpenJobsByOrganizationAsync(db, tenant, cancellationToken);

        counts[two.Id].Should().Be(2);
        counts[one.Id].Should().Be(1);
    }
}
