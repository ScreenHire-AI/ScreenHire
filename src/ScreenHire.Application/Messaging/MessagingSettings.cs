using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Messaging;

/// <summary>
/// Messaging names and retry timings. The API and worker use the defaults (the API with its own storage schema); tests
/// start their own Wolverine host with a private schema and short delays.
/// </summary>
public sealed class MessagingSettings
{
    /// <summary>The settings the API runs with: its own storage schema, and the queues it shares with the worker.</summary>
    public static MessagingSettings Api => new()
    {
        Schema = DatabaseRegistration.ApiMessagingSchema,
        TransportSchema = DatabaseRegistration.MessagingSchema,
    };

    /// <summary>Postgres schema for Wolverine's inbox, outbox, dead letters and node records.</summary>
    public string Schema { get; init; } = DatabaseRegistration.MessagingSchema;

    /// <summary>
    /// Postgres schema for the queue tables (<see cref="WorkerQueue"/>, <see cref="RealtimeQueue"/>). Defaults to
    /// <see cref="Schema"/>, so a test host with a private schema never touches the running stack's queues. The API sets
    /// it to the worker's schema: its storage is its own, its queues are shared.
    /// </summary>
    public string? TransportSchema { get; init; }

    /// <summary>The schema the queue tables live in.</summary>
    public string QueueSchema => TransportSchema ?? Schema;

    /// <summary>
    /// Whether this host creates and updates Wolverine's tables when it starts. Only for a connection that may create
    /// them: the API and worker connect as <c>screenhire_app</c>, which may not, so for them the tables come from
    /// <see cref="ScreenHireMessaging.ProvisionStorageAsync"/>, run as the owner. Tests' private-schema hosts set it.
    /// </summary>
    public bool AutoProvision { get; init; }

    /// <summary>The Postgres-backed queue the API publishes to and the worker listens on.</summary>
    public string WorkerQueue { get; init; } = "worker";

    /// <summary>
    /// The Postgres-backed queue every <see cref="ITenantEvent"/> goes to, from the API and the worker alike. The API
    /// listens on it and pushes each event to the event's organization over SignalR (docs/architecture/realtime.md).
    /// </summary>
    public string RealtimeQueue { get; init; } = "realtime";

    /// <summary>
    /// How long a tenant event may wait in <see cref="RealtimeQueue"/> before Wolverine discards it. A live update that
    /// arrives minutes late is noise: a client that was disconnected refetches when it reconnects.
    /// </summary>
    public TimeSpan RealtimeEventLifetime { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How often the API polls <see cref="RealtimeQueue"/> while it is idle. Wolverine's Postgres transport polls rather
    /// than listens, and its default (5 s) would let a live update sit in the queue for seconds.
    /// </summary>
    public TimeSpan RealtimePollingInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How often the worker polls <see cref="WorkerQueue"/> while it is idle. Every hop of a chain of events (an
    /// automation that moves an application raises the next <c>ApplicationMoved</c>) waits one poll, so the 5 s default
    /// would cost seconds per step.
    /// </summary>
    public TimeSpan WorkerPollingInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// In-process retries after a handler throws. The listener holds the message while it waits, so keep these short.
    /// </summary>
    public TimeSpan[] RetryDelays { get; init; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];

    /// <summary>
    /// Durable retries once <see cref="RetryDelays"/> are used up: the message goes back to the store and runs again
    /// after each delay. After the last one it moves to the dead-letter table (<c>wolverine_dead_letters</c>).
    /// </summary>
    public TimeSpan[] ScheduledRetryDelays { get; init; } = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)];
}
