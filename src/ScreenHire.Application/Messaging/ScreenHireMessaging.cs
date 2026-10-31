using System.Collections.Frozen;
using System.Reflection;
using JasperFx;
using JasperFx.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Distribution;
using ScreenHire.Application.Distribution.GoogleIndexing;
using ScreenHire.Application.Verification;
using ScreenHire.Application.Verification.Content;
using ScreenHire.Application.Jobs;
using ScreenHire.Application.Slack;
using ScreenHire.Application.TalentPool;
using ScreenHire.Application.Webhooks;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.Runtime.Handlers;

namespace ScreenHire.Application.Messaging;

/// <summary>
/// Wolverine setup shared by the API and the worker (ADR-0008, docs/architecture/messaging.md). Both hosts keep their
/// Wolverine tables in the application database, share the Postgres queue tables in one schema, and connect to Postgres
/// directly: leader election and the inbox need a real session, so never route this through a transaction-mode
/// PgBouncer.
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
    /// Every concrete <see cref="ITenantEvent"/> in this assembly by its short name, which is the event's <c>type</c> on
    /// the wire to the dashboard (packages/realtime). Building the map fails if two events share a name.
    /// </summary>
    public static FrozenDictionary<string, Type> TenantEventTypes { get; } = typeof(ScreenHireMessaging).Assembly.GetExportedTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } && type.IsAssignableTo(typeof(ITenantEvent)))
        .ToFrozenDictionary(type => type.Name, StringComparer.Ordinal);

    /// <summary>
    /// The API's side. It publishes through the EF Core transactional outbox (<c>IDbContextOutbox&lt;ScreenHireDbContext&gt;</c>)
    /// to the worker queue, and every <see cref="ITenantEvent"/> to the real-time queue as well. It listens on the
    /// real-time queue only and handles each tenant event with the handler <paramref name="tenantEventHandler"/> makes for
    /// its type: the SignalR fan-out (docs/architecture/realtime.md). It discovers no handlers, so no background work
    /// ever runs in the API.
    /// </summary>
    public static IHostApplicationBuilder AddScreenHireApiMessaging(
        this IHostApplicationBuilder builder, Func<Type, IMessageHandler> tenantEventHandler)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The API has no discovered handlers by design; Wolverine's startup warning about that is noise here.
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

        builder.UseWolverine(opts => ConfigureApi(opts, builder.GetMessagingConnectionString(), MessagingSettings.Api, tenantEventHandler));
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
    /// Creates or updates every Wolverine table the hosts use, as the role in <paramref name="ownerConnectionString"/>:
    /// the worker's store and the queues in <see cref="DatabaseRegistration.MessagingSchema"/>, and the API's store in
    /// <see cref="DatabaseRegistration.ApiMessagingSchema"/>. Run it after the EF migrations, which create both schemas
    /// and grant <c>screenhire_app</c> access to what this creates, and again after a Wolverine upgrade.
    /// </summary>
    public static async Task ProvisionStorageAsync(string ownerConnectionString, CancellationToken cancellationToken = default)
    {
        // The worker's configuration covers its store, both queues (it listens on one and publishes to the other) and the
        // recurring schedules, whose tracking table only exists when a schedule is registered.
        await SetUpResourcesAsync(opts =>
        {
            ConfigureHandler(opts, ownerConnectionString, new MessagingSettings { AutoProvision = true });
            ScheduleRecurringMessages(opts);
        }, cancellationToken);

        // The API's own store: its outbox, the real-time queue's inbox, its node records and control queue.
        var api = MessagingSettings.Api;
        await SetUpResourcesAsync(
            opts => ConfigureApi(
                opts, ownerConnectionString,
                new MessagingSettings { Schema = api.Schema, TransportSchema = api.TransportSchema, AutoProvision = true },
                tenantEventHandler: null),
            cancellationToken);
    }

    /// <summary>
    /// The configuration behind <see cref="AddScreenHireApiMessaging"/>: <see cref="ConfigurePublisher"/>, plus the
    /// listener on the real-time queue and one handler per tenant event type from <paramref name="tenantEventHandler"/>
    /// (none when it is null, as when provisioning).
    /// </summary>
    /// <remarks>
    /// Receiving needs a durability mode that runs listeners. <c>ServerlessWithOutbox</c>, which the API used while it only
    /// published, leaves a Postgres queue unread. <c>Balanced</c> runs listeners, with leader election and a durability
    /// agent for the API's own outbox. The API keeps all of that in its own storage schema, so it is a separate Wolverine
    /// cluster from the worker: in one shared cluster the worker's leader assigned its recurring-schedule agent to API
    /// nodes, which cannot run it. <c>Solo</c> is no option either: it clears every node record in its store at startup.
    /// </remarks>
    public static void ConfigureApi(
        WolverineOptions opts, string connectionString, MessagingSettings settings, Func<Type, IMessageHandler>? tenantEventHandler)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(settings);

        ConfigurePublisher(opts, connectionString, settings);
        opts.Durability.Mode = DurabilityMode.Balanced;

        // Durable, like the publishing side of the same queue: Wolverine keeps one mode per endpoint for both directions.
        // The inbox is the API's own (settings.Schema), so the worker's durability agent never sees these envelopes.
        // Postgres queues are polled, and an idle listener otherwise waits ScheduledJobPollingTime (5 s) between polls:
        // too long for a live update, which is the one message whose latency people see.
        opts.ListenToPostgresqlQueue(settings.RealtimeQueue).UseDurableInbox().PollingInterval(settings.RealtimePollingInterval);

        if (tenantEventHandler is not null)
        {
            // Wolverine cannot generate a handler for an interface or a generic class, so the API registers a ready-made
            // handler per concrete event type (AddMessageHandler: no code generation).
            foreach (var eventType in TenantEventTypes.Values)
            {
                opts.AddMessageHandler(eventType, tenantEventHandler(eventType));
            }
        }

        // A live update that fails twice is dropped, with the error logged: by the time a later retry could succeed the
        // moment has passed, and a client that reconnects refetches anyway.
        opts.Policies.OnAnyException().RetryWithCooldown(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)).Then.Discard();
    }

    /// <summary>
    /// The publishing half of the API's configuration: the store, every message to the worker queue, and every
    /// <see cref="ITenantEvent"/> to the real-time queue as well. No listeners; <see cref="ConfigureApi"/> adds the API's.
    /// </summary>
    public static void ConfigurePublisher(WolverineOptions opts, string connectionString, MessagingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(settings);

        opts.Discovery.DisableConventionalDiscovery();

        UsePostgres(opts, connectionString, settings);
        opts.PublishAllMessages().ToPostgresqlQueue(settings.WorkerQueue);
        RouteTenantEvents(opts, settings);
    }

    /// <summary>The worker configuration behind <see cref="AddScreenHireMessageHandling"/>, without the recurring schedules.</summary>
    public static void ConfigureHandler(WolverineOptions opts, string connectionString, MessagingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(settings);

        opts.Discovery.IncludeAssembly(typeof(ScreenHireMessaging).Assembly);
        opts.Services.AddScreenHireAutomations();

        // Integrations the handlers use, each with a local adapter until a real one is registered first (CLAUDE.md).
        opts.Services.TryAddSingleton<IMalwareScanner, NoMalwareScanner>();
        opts.Services.AddScreenHireVerification();

        UsePostgres(opts, connectionString, settings);
        // Polled, like the real-time queue: a chain of events (an automation moving an application, which raises the next
        // ApplicationMoved) waits one idle poll per hop, so the default 5 s adds up.
        opts.ListenToPostgresqlQueue(settings.WorkerQueue).UseDurableInbox().PollingInterval(settings.WorkerPollingInterval);
        RouteTenantEvents(opts, settings);

        // Messages a handler publishes to another handler in the worker go through durable local queues, so they
        // survive a restart. Every handler that takes the DbContext runs in one EF Core transaction with its outbox.
        opts.Policies.UseDurableLocalQueues();
        opts.Policies.AutoApplyTransactions();

        // Analyses of one application run one at a time; different applications still run side by side, up to five at once
        // per message type (two that hash to one slot wait for each other). The submission
        // and each content stage as it lands all ask for one, often within milliseconds, and two at once both inserted
        // the same analyzer run: the second failed on the unique index and waited out a retry (1 s, then 5 s) before
        // finding the run there, which delayed the assessment people watch arrive live. Wolverine's partitioned
        // processing orders them by application instead. The consistency check races the same way on its own row.
        opts.MessagePartitioning
            .ByMessage<AnalyzeApplicationVerification>(message => message.ApplicationId.ToString())
            .ByMessage<CheckApplicationConsistency>(message => message.ApplicationId.ToString());
        opts.LocalQueueFor<AnalyzeApplicationVerification>().PartitionProcessingByGroupId(PartitionSlots.Five);
        opts.LocalQueueFor<CheckApplicationConsistency>().PartitionProcessingByGroupId(PartitionSlots.Five);

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

        // The last step of every branch also turns a dead-lettered automation action into an automation issue
        // (AutomationFailures); for any other message it does nothing.
        var policy = opts.Policies.OnAnyException();
        if (settings.RetryDelays.Length > 0 && settings.ScheduledRetryDelays.Length > 0)
        {
            policy.RetryWithCooldown(settings.RetryDelays).Then.ScheduleRetry(settings.ScheduledRetryDelays).Then.MoveToErrorQueue()
                .And(AutomationFailures.ReportAsync, AutomationFailures.Description);
        }
        else if (settings.RetryDelays.Length > 0)
        {
            policy.RetryWithCooldown(settings.RetryDelays).Then.MoveToErrorQueue().And(AutomationFailures.ReportAsync, AutomationFailures.Description);
        }
        else if (settings.ScheduledRetryDelays.Length > 0)
        {
            policy.ScheduleRetry(settings.ScheduledRetryDelays).Then.MoveToErrorQueue().And(AutomationFailures.ReportAsync, AutomationFailures.Description);
        }
        else
        {
            policy.MoveToErrorQueue().And(AutomationFailures.ReportAsync, AutomationFailures.Description);
        }
    }

    /// <summary>Cron schedules run in the worker only, owned by its durability agent. Times are UTC.</summary>
    public static void ScheduleRecurringMessages(WolverineOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        opts.Schedules.ScheduleRecurring<ReportOpenJobs>("*/5 * * * *");
        // Raw network and telemetry data past its retention, daily (docs/architecture/verification-engine.md, "Retention").
        opts.Schedules.ScheduleRecurring<PurgeExpiredVerificationData>("17 3 * * *");
        // Finished webhook deliveries past their retention, daily (docs/architecture/workflow-engine.md section 5).
        opts.Schedules.ScheduleRecurring<PurgeExpiredWebhookDeliveries>("37 3 * * *");
        // Answers saved to finish later, 30 days after their first save (docs/architecture/apply-flow.md section 7).
        opts.Schedules.ScheduleRecurring<PurgeExpiredApplicationDrafts>("7 4 * * *");
        // Records that Slack messages went, and the sandbox's messages, past their retention, daily (docs/architecture/slack.md).
        opts.Schedules.ScheduleRecurring<PurgeExpiredSlackMessages>("57 3 * * *");
        // People whose permission for future roles ran out leave the talent pool, daily (docs/architecture/talent-pool.md).
        opts.Schedules.ScheduleRecurring<ExpireTalentPool>("47 3 * * *");
        // Job distribution (docs/architecture/job-distribution.md): the reconciliation sweep, and the Indexing API queue.
        opts.Schedules.ScheduleRecurring<ReconcileJobPostings>("*/5 * * * *");
        opts.Schedules.ScheduleRecurring<DispatchIndexingNotifications>("* * * * *");
    }

    /// <summary>
    /// Every <see cref="ITenantEvent"/> goes to the real-time queue, for the API to push to the event's organization,
    /// and to the worker queue, for any worker handler. Both hosts apply this, so an event reaches the browser whichever
    /// host raised it. An explicit rule replaces Wolverine's local routing for the type, which is why an event the worker
    /// raises reaches the worker's own handler through the worker queue too; one that no worker handler wants is marked
    /// handled there and dropped.
    /// </summary>
    private static void RouteTenantEvents(WolverineOptions opts, MessagingSettings settings) =>
        opts.Publish(rule =>
        {
            rule.MessagesImplementing<ITenantEvent>();
            rule.ToPostgresqlQueue(settings.RealtimeQueue).DeliverWithin(settings.RealtimeEventLifetime);
            rule.ToPostgresqlQueue(settings.WorkerQueue);
        });

    private static void UsePostgres(WolverineOptions opts, string connectionString, MessagingSettings settings)
    {
        var postgres = opts.UsePostgresqlPersistenceAndTransport(connectionString, settings.Schema, transportSchema: settings.QueueSchema);
        if (settings.AutoProvision)
        {
            postgres.AutoProvision();
        }

        // Never patch the schema at startup unless asked: screenhire_app may not create tables (ProvisionStorageAsync).
        opts.AutoBuildMessageStorageOnStartup = settings.AutoProvision ? AutoCreate.CreateOrUpdate : AutoCreate.None;
        opts.UseEntityFrameworkCoreTransactions();
        opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
    }

    private static async Task SetUpResourcesAsync(Action<WolverineOptions> configure, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.UseWolverine(configure);
        using var host = builder.Build();
        await host.SetupResources(cancellationToken);
    }

    private static bool IsBuildTimeDocumentGeneration => Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    private static string GetMessagingConnectionString(this IHostApplicationBuilder builder) =>
        builder.Configuration.GetConnectionString(ConnectionName)
        ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not set. Run through the AppHost (pnpm dev).");
}
