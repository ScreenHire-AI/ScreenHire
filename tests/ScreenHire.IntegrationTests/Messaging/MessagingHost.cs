using System.Collections.Concurrent;
using JasperFx.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine;

namespace ScreenHire.IntegrationTests.Messaging;

/// <summary>
/// An in-process Wolverine host built with the same <see cref="ScreenHireMessaging"/> setup as the API or the worker,
/// against the AppHost's database but in a private schema. It never touches the real worker queue, so tests can make
/// handlers fail or transactions roll back without disturbing the running stack or each other.
/// </summary>
internal sealed class MessagingHost : IAsyncDisposable
{
    private readonly IHost host;

    private MessagingHost(IHost host, WolverineStore store, CapturedLogs logs)
    {
        this.host = host;
        Store = store;
        Logs = logs;
    }

    public WolverineStore Store { get; }

    public CapturedLogs Logs { get; }

    public IServiceProvider Services => host.Services;

    /// <summary>
    /// Starts a publisher configured like the API (<see cref="ScreenHireMessaging.ConfigurePublisher"/>), in solo mode
    /// so it is ready at once.
    /// </summary>
    public static Task<MessagingHost> StartPublisherAsync(AppHostFixture fixture, CancellationToken cancellationToken) =>
        StartAsync(fixture, new MessagingSettings { Schema = NewSchema(), AutoProvision = true }, ConfigureSoloPublisher, cancellationToken);

    /// <summary>
    /// Starts a publisher like <see cref="StartPublisherAsync"/> whose storage is private but whose queues are the running
    /// stack's: what it publishes reaches the real worker and API. For a message the test must put on the worker queue
    /// itself, such as <c>CheckLiveUpdates</c>.
    /// </summary>
    public static Task<MessagingHost> StartPublisherToAppHostQueuesAsync(AppHostFixture fixture, CancellationToken cancellationToken) =>
        StartAsync(
            fixture,
            new MessagingSettings { Schema = NewSchema(), TransportSchema = DatabaseRegistration.MessagingSchema, AutoProvision = true },
            ConfigureSoloPublisher,
            cancellationToken);

    private static void ConfigureSoloPublisher(WolverineOptions opts, string connectionString, MessagingSettings settings)
    {
        ScreenHireMessaging.ConfigurePublisher(opts, connectionString, settings);
        opts.Durability.Mode = DurabilityMode.Solo;
    }

    /// <summary>
    /// Starts a handler configured like the worker (<see cref="ScreenHireMessaging.ConfigureHandler"/>), in solo mode so
    /// its agents start at once, plus any extra handler types the test brings.
    /// </summary>
    public static Task<MessagingHost> StartHandlerAsync(
        AppHostFixture fixture, MessagingSettings settings, IEnumerable<Type> handlerTypes, CancellationToken cancellationToken,
        bool withRecurringSchedules = false) =>
        StartAsync(fixture, settings, (opts, connectionString, s) =>
        {
            ScreenHireMessaging.ConfigureHandler(opts, connectionString, s);
            if (withRecurringSchedules)
            {
                ScreenHireMessaging.ScheduleRecurringMessages(opts);
            }

            opts.Durability.Mode = DurabilityMode.Solo;
            foreach (var handlerType in handlerTypes)
            {
                opts.Discovery.IncludeType(handlerType);
            }
        }, cancellationToken);

    public static string NewSchema() => $"wolverine_test_{Guid.NewGuid():N}";

    private static async Task<MessagingHost> StartAsync(
        AppHostFixture fixture, MessagingSettings settings, Action<WolverineOptions, string, MessagingSettings> configure, CancellationToken cancellationToken)
    {
        var connectionString = await fixture.GetOwnerConnectionStringAsync(cancellationToken);
        var logs = new CapturedLogs();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders().AddProvider(logs);
        // As the owner, which creates the private schema. Row-level security is not what these hosts test;
        // WorkerMessagingTests covers the real API and worker, which connect as screenhire_app.
        builder.Services.AddScoped<TenantContext>();
        builder.Services.AddScoped<ITenantContext>(services => services.GetRequiredService<TenantContext>());
        builder.Services.AddDbContext<ScreenHireDbContext>(options => options.UseNpgsql(connectionString).UseScreenHireConventions());
        builder.UseWolverine(opts => configure(opts, connectionString, settings));
        builder.Services.AddResourceSetupOnStartup();

        var host = builder.Build();
        await host.StartAsync(cancellationToken);
        return new MessagingHost(host, new WolverineStore(connectionString, settings.Schema), logs);
    }

    public async ValueTask DisposeAsync()
    {
        await host.StopAsync();
        host.Dispose();
        await Store.DropSchemaAsync(CancellationToken.None);
    }
}

/// <summary>Every log entry the host writes, for asserting that failures are visible in logs.</summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception));
            }
        }
    }
}

internal sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);
