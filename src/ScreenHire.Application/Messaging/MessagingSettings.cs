using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Messaging;

/// <summary>
/// Messaging names and retry timings. The API and worker use the defaults; tests start their own Wolverine host with a
/// private schema and short delays.
/// </summary>
public sealed class MessagingSettings
{
    /// <summary>Postgres schema for Wolverine's inbox, outbox, dead letters, node records and queue tables.</summary>
    public string Schema { get; init; } = DatabaseRegistration.MessagingSchema;

    /// <summary>
    /// Whether this host creates and updates Wolverine's tables when it starts. Only for a connection that may create
    /// them: the API and worker connect as <c>screenhire_app</c>, which may not, so for them the tables come from
    /// <see cref="ScreenHireMessaging.ProvisionStorageAsync"/>, run as the owner. Tests' private-schema hosts set it.
    /// </summary>
    public bool AutoProvision { get; init; }

    /// <summary>The Postgres-backed queue the API publishes to and the worker listens on.</summary>
    public string WorkerQueue { get; init; } = "worker";

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
