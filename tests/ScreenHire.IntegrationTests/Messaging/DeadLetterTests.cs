using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests.Messaging;

/// <summary>
/// The worker's error policy (<see cref="ScreenHireMessaging.ApplyErrorPolicy"/>): a handler that keeps failing is
/// retried with backoff, then moved to the dead-letter table, and each step shows up in logs, traces and metrics.
/// </summary>
public sealed class DeadLetterTests(AppHostFixture fixture)
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400)];

    [Fact]
    public async Task A_failing_handler_is_retried_with_backoff_then_dead_lettered_with_logs_and_telemetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var marker = Guid.NewGuid();
        var markerText = marker.ToString();
        var settings = new MessagingSettings { Schema = MessagingHost.NewSchema(), RetryDelays = RetryDelays, ScheduledRetryDelays = [] };

        var spanEvents = new ConcurrentQueue<string>();
        using var activities = ListenToWolverineSpans(spanEvents);
        var deadLettered = new ConcurrentQueue<long>();
        using var meters = ListenToDeadLetterCounter(deadLettered);

        await using var worker = await MessagingHost.StartHandlerAsync(fixture, settings, [typeof(AlwaysFailsHandler)], cancellationToken);
        await worker.Services.GetRequiredService<IMessageBus>().SendAsync(new AlwaysFails(marker));

        await WolverineStore.WaitUntilAsync(
            async () => (await worker.Store.GetDeadLettersAsync(markerText, cancellationToken)).Count == 1,
            "the failing message to reach the dead-letter table", cancellationToken);

        var deadLetter = (await worker.Store.GetDeadLettersAsync(markerText, cancellationToken)).Single();
        deadLetter.ExceptionType.Should().Contain(nameof(InvalidOperationException));
        deadLetter.ExceptionMessage.Should().Contain(markerText);

        // One attempt plus one per retry delay, each at least that delay after the previous one.
        var attempts = AlwaysFailsHandler.Attempts[marker].ToArray();
        attempts.Should().HaveCount(1 + RetryDelays.Length);
        for (var i = 0; i < RetryDelays.Length; i++)
        {
            (attempts[i + 1] - attempts[i]).Should().BeGreaterThanOrEqualTo(RetryDelays[i] - TimeSpan.FromMilliseconds(20));
        }

        worker.Logs.Entries.Where(e => e.Exception?.Message.Contains(markerText, StringComparison.Ordinal) == true)
            .Should().HaveCountGreaterThanOrEqualTo(attempts.Length, "every failed attempt is logged with its exception");
        worker.Logs.Entries.Should().Contain(
            e => e.Level == LogLevel.Error && e.Message.Contains("moved to the error queue", StringComparison.Ordinal)
                && e.Exception != null && e.Exception.Message.Contains(markerText, StringComparison.Ordinal),
            "moving a message to the dead-letter table is logged as an error with its exception");

        spanEvents.Should().Contain("wolverine.error.queued", "the message's trace records the move to the dead-letter table");
        deadLettered.Sum().Should().BeGreaterThanOrEqualTo(1, "the wolverine-dead-letter-queue counter counts it");
    }

    [Fact]
    public async Task Scheduled_retries_run_after_the_in_process_retries_before_the_message_is_dead_lettered()
    {
        // The worker's shape: in-process retries, then durable scheduled retries, then the dead-letter table.
        var cancellationToken = TestContext.Current.CancellationToken;
        var marker = Guid.NewGuid();
        var retryDelay = TimeSpan.FromMilliseconds(100);
        var scheduledDelay = TimeSpan.FromSeconds(1);
        var settings = new MessagingSettings { Schema = MessagingHost.NewSchema(), RetryDelays = [retryDelay], ScheduledRetryDelays = [scheduledDelay] };

        await using var worker = await MessagingHost.StartHandlerAsync(fixture, settings, [typeof(AlwaysFailsHandler)], cancellationToken);
        await worker.Services.GetRequiredService<IMessageBus>().SendAsync(new AlwaysFails(marker));

        await WolverineStore.WaitUntilAsync(
            async () => (await worker.Store.GetDeadLettersAsync(marker.ToString(), cancellationToken)).Count == 1,
            "the failing message to reach the dead-letter table after its scheduled retry", cancellationToken);

        var attempts = AlwaysFailsHandler.Attempts[marker].ToArray();
        attempts.Should().HaveCount(3, "one attempt, one in-process retry and one scheduled retry");
        (attempts[1] - attempts[0]).Should().BeGreaterThanOrEqualTo(retryDelay - TimeSpan.FromMilliseconds(20));
        (attempts[2] - attempts[1]).Should().BeGreaterThanOrEqualTo(scheduledDelay - TimeSpan.FromMilliseconds(20));
    }

    private static ActivityListener ListenToWolverineSpans(ConcurrentQueue<string> events)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Wolverine",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.DisplayName.Contains(nameof(AlwaysFails), StringComparison.Ordinal)
                    || activity.Tags.Any(tag => tag.Value?.Contains(nameof(AlwaysFails), StringComparison.Ordinal) == true))
                {
                    foreach (var activityEvent in activity.Events)
                    {
                        events.Enqueue(activityEvent.Name);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static MeterListener ListenToDeadLetterCounter(ConcurrentQueue<long> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Name == "wolverine-dead-letter-queue")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => Record(value, tags));
        listener.SetMeasurementEventCallback<int>((_, value, tags, _) => Record(value, tags));
        listener.Start();
        return listener;

        void Record(long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            foreach (var tag in tags)
            {
                if (tag.Value is string text && text.Contains(nameof(AlwaysFails), StringComparison.Ordinal))
                {
                    measurements.Enqueue(value);
                    return;
                }
            }
        }
    }
}

/// <summary>A message whose handler always throws, so the error policy runs to the end.</summary>
public sealed record AlwaysFails(Guid Marker);

public static class AlwaysFailsHandler
{
    public static ConcurrentDictionary<Guid, ConcurrentQueue<DateTimeOffset>> Attempts { get; } = new();

    public static void Handle(AlwaysFails message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Attempts.GetOrAdd(message.Marker, _ => new ConcurrentQueue<DateTimeOffset>()).Enqueue(DateTimeOffset.UtcNow);
        throw new InvalidOperationException($"AlwaysFails {message.Marker} failed on purpose.");
    }
}
