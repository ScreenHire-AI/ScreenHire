using System.Reflection;
using JasperFx.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Jobs;
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
public static class ScreenHireMessaging
{
    /// <summary>Aspire's name for the application database; the connection string arrives as <c>ConnectionStrings:screenhire</c>.</summary>
    public const string ConnectionName = "screenhire";

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
        builder.Services.AddResourceSetupOnStartup();
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
        builder.Services.AddResourceSetupOnStartup();
        return builder;
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
        opts.UsePostgresqlPersistenceAndTransport(connectionString, settings.Schema, transportSchema: settings.Schema)
            .AutoProvision();
        opts.UseEntityFrameworkCoreTransactions();
        opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
    }

    private static bool IsBuildTimeDocumentGeneration => Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    private static string GetMessagingConnectionString(this IHostApplicationBuilder builder) =>
        builder.Configuration.GetConnectionString(ConnectionName)
        ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not set. Run through the AppHost (pnpm dev).");
}
