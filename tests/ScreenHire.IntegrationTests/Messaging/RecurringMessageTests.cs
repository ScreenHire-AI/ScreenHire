using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Jobs;
using ScreenHire.Application.Messaging;
using Wolverine.Runtime.Recurring;

namespace ScreenHire.IntegrationTests.Messaging;

/// <summary>The worker's cron schedules (<see cref="ScreenHireMessaging.ScheduleRecurringMessages"/>), in a private schema.</summary>
public sealed class RecurringMessageTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Report_open_jobs_is_scheduled_every_five_minutes_and_its_handler_runs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = new MessagingSettings { Schema = MessagingHost.NewSchema() };
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
        worker.Logs.Entries.Should().Contain(e => e.Message.EndsWith("open jobs across all organizations", StringComparison.Ordinal));
    }
}
