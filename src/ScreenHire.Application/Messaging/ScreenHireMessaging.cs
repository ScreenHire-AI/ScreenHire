using System.Reflection;
using JasperFx;
using JasperFx.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Jobs;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;

namespace ScreenHire.Application.Messaging;

/// <summary>
/// Wolverine setup shared by the API and the worker (ADR-0008, docs/architecture/messaging.md). Both hosts keep
/// Wolverine's envelope tables and the Postgres queue tables in one schema of the application database and connect to
/// Postgres directly: leader election and the inbox need a real session, so never route this through a
/// transaction-mode PgBouncer.
/// </summary>
/// <remarks>
/// Both hosts connect as <c>screenhire_app</c>, the role row-level security binds, and may read and write Wolverine's
/// tables but not create them. <see cref="ProvisionStorageAsync"/> creates them as the owner: the API calls it at
/// startup in Development, after the migrations; elsewhere the deploy step does.
/// </remarks>
public static class ScreenHireMessaging
{
    /// <summary>The connection the hosts use for messaging: the application role, the same one as their DbContext.</summary>
    public const string ConnectionName = DatabaseRegistration.AppConnectionName;

    /// <summary>
    /// The API's side: publishes through the EF Core transactional outbox (<c>IDbContextOutbox&lt;ScreenHireDbContext&gt;</c>)
    /// to the worker queue. It discovers no handlers and runs no background agents, so nothing is ever handled in the API.
    /// </summary>
    public static IHostApplicationBuilder AddScreenHireMessagePublishing(this IHostApplicationBuilder builder)
    {
        // The API has no handlers by design; Wolverine's startup warning about that is noise here.
        builder.Logging.AddFilter("Wolverine.Configuration.HandlerDiscovery", LogLevel.Error);

        if (IsBuildTimeDocumentGeneration)
        {
            // The API build writes openapi.json by running Program under the GetDocument.Insider tool, which starts the
            // host without the AppHost. Endpoints still need IDbContextOutbox registered, or minimal APIs would read it
            // as a request body, but nothing may connect to a database there.
            builder.UseWolverine(opts =>
            {
                opts.Durability.Mode = DurabilityMode.MediatorOnly;
                opts.Discovery.DisableConventionalDiscovery();
                opts.UseEntityFrameworkCoreTransactions();
            });
            return builder;
        }

        builder.UseWolverine(opts => ConfigurePublisher(opts, builder.GetMessagingConnectionString(), new MessagingSettings()));
        return builder;
    }

    /// <summary>
    /// The worker's side: listens on the worker queue with a durable inbox, runs every handler in this assembly inside
    /// an EF Core transaction, hosts the durability agent (outbox recovery, scheduled messages, dead-letter expiry) and
    /// the recurring schedules.
    /// </summary>
    public static IHostApplicationBuilder AddScreenHireMessageHandling(this IHostApplicationBuilder builder)
    {
        builder.UseWolverine(opts =>
        {
            ConfigureHandler(opts, builder.GetMessagingConnectionString(), new MessagingSettings());
            ScheduleRecurringMessages(opts);
        });
        return builder;
    }

    /// <summary>
    /// Creates or updates every Wolverine table both hosts use (envelopes, dead letters, nodes, deduplication, the worker
    /// queue), in <see cref="MessagingSettings.Schema"/>, as the role in <paramref name="ownerConnectionString"/>. Run it
    /// after the EF migrations, which create the schema and grant <c>screenhire_app</c> access to what this creates, and
    /// again after a Wolverine upgrade.
    /// </summary>
    public static async Task ProvisionStorageAsync(string ownerConnectionString, CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        // The worker's configuration is a superset of the API's: the same store, plus the queue it listens on and the
        // recurring schedules, whose tracking table only exists when a schedule is registered.
        builder.UseWolverine(opts =>
        {
            ConfigureHandler(opts, ownerConnectionString, new MessagingSettings { AutoProvision = true });
            ScheduleRecurringMessages(opts);
        });
        using var host = builder.Build();
        await host.SetupResources(cancellationToken);
    }

    /// <summary>The publisher configuration behind <see cref="AddScreenHireMessagePublishing"/>.</summary>
    public static void ConfigurePublisher(WolverineOptions opts, string connectionString, MessagingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(settings);

        // The outbox stays transactional, but the API registers no node, elects no leader and runs no agents: the
        // worker's durability agent recovers anything the API committed and did not get to send.
        opts.Durability.Mode = DurabilityMode.ServerlessWithOutbox;
        opts.Discovery.DisableConventionalDiscovery();

        UsePostgres(opts, connectionString, settings);
        opts.PublishAllMessages().ToPostgresqlQueue(settings.WorkerQueue);
    }

    /// <summary>The worker configuration behind <see cref="AddScreenHireMessageHandling"/>, without the recurring schedules.</summary>
    public static void ConfigureHandler(WolverineOptions opts, string connectionString, MessagingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(settings);

        opts.Discovery.IncludeAssembly(typeof(ScreenHireMessaging).Assembly);

        UsePostgres(opts, connectionString, settings);
        opts.ListenToPostgresqlQueue(settings.WorkerQueue).UseDurableInbox();

        // Messages a handler publishes to another handler in the worker go through durable local queues, so they
        // survive a restart. Every handler that takes the DbContext runs in one EF Core transaction with its outbox.
        opts.Policies.UseDurableLocalQueues();
        opts.Policies.AutoApplyTransactions();

        // A message about one organization's data runs as that organization: the tenant filter and row-level security
        // then scope the handler's queries, exactly as they scope a signed-in request.
        opts.Policies.AddMiddleware(typeof(TenantMessageMiddleware), chain => chain.MessageType.IsAssignableTo(typeof(ITenantMessage)));
        // Wolverine's generated handler code would otherwise construct TenantContext itself with `new`, while the DbContext
        // gets the scope's own instance from the container, and the middleware would set a tenant nobody reads.
        opts.CodeGeneration.AlwaysUseServiceLocationFor<TenantContext>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<ITenantContext>();

        // Storage for [Deduplicated] handlers (logical idempotency keys, see CLAUDE.md "Messaging").
        opts.Durability.MessageDeduplicationMode = MessageDeduplicationMode.CompareByHash;

        ApplyErrorPolicy(opts, settings);
    }

    /// <summary>
    /// Retry with backoff, then durable scheduled retries, then the dead-letter table. Wolverine logs each step and
    /// records it on the message's trace (<c>wolverine.envelope.retried</c>, <c>wolverine.error.queued</c>) and in the
    /// <c>wolverine-dead-letter-queue</c> counter.
    /// </summary>
    public static void ApplyErrorPolicy(WolverineOptions opts, MessagingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(settings);

        var policy = opts.Policies.OnAnyException();
        if (settings.RetryDelays.Length > 0 && settings.ScheduledRetryDelays.Length > 0)
        {
            policy.RetryWithCooldown(settings.RetryDelays).Then.ScheduleRetry(settings.ScheduledRetryDelays).Then.MoveToErrorQueue();
        }
        else if (settings.RetryDelays.Length > 0)
        {
            policy.RetryWithCooldown(settings.RetryDelays).Then.MoveToErrorQueue();
        }
        else if (settings.ScheduledRetryDelays.Length > 0)
        {
            policy.ScheduleRetry(settings.ScheduledRetryDelays).Then.MoveToErrorQueue();
        }
        else
        {
            policy.MoveToErrorQueue();
        }
    }

    /// <summary>Cron schedules run in the worker only, owned by its durability agent. Times are UTC.</summary>
    public static void ScheduleRecurringMessages(WolverineOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        opts.Schedules.ScheduleRecurring<ReportOpenJobs>("*/5 * * * *");
    }

    private static void UsePostgres(WolverineOptions opts, string connectionString, MessagingSettings settings)
    {
        var postgres = opts.UsePostgresqlPersistenceAndTransport(connectionString, settings.Schema, transportSchema: settings.Schema);
        if (settings.AutoProvision)
        {
            postgres.AutoProvision();
        }

        // Never patch the schema at startup unless asked: screenhire_app may not create tables (ProvisionStorageAsync).
        opts.AutoBuildMessageStorageOnStartup = settings.AutoProvision ? AutoCreate.CreateOrUpdate : AutoCreate.None;
        opts.UseEntityFrameworkCoreTransactions();
        opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
    }

    private static bool IsBuildTimeDocumentGeneration => Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    private static string GetMessagingConnectionString(this IHostApplicationBuilder builder) =>
        builder.Configuration.GetConnectionString(ConnectionName)
        ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not set. Run through the AppHost (pnpm dev).");
}
