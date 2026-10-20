# Messaging and background work

Background work, domain events and scheduled jobs run on Wolverine 6.51 (MIT, ADR-0008) with Postgres as the only
infrastructure: no broker. A message published with a data change commits in the same transaction as that change,
and the worker handles it once. The setup lives in one place,
[`ScreenHireMessaging`](../../src/ScreenHire.Application/Messaging/ScreenHireMessaging.cs), and the `/add-message`
skill is the procedure for adding a message.

## Topology

```
API (publishes; handles tenant events only)  Postgres (screenhire database)            Worker (handles)
  endpoint: change entity                      schema "wolverine_api" (the API's store)
  + outbox.PublishAsync(event)       ───▶      wolverine_outgoing_envelopes  (API outbox)
  + SaveChangesAndFlushMessagesAsync           schema "wolverine" (the worker's store and the queues)
      one transaction                          wolverine_queue_worker        (queue)  ───▶  listener, durable inbox
                                               wolverine_queue_realtime      (queue)  ───▶  API: SignalR fan-out
                                               wolverine_incoming_envelopes  (inbox)        handler in an EF transaction
                                               wolverine_dead_letters                       retries, then dead letter
                                               wolverine_nodes, ...                         durability agent, cron
```

| | API | Worker |
|---|---|---|
| Call in `Program.cs` | `builder.AddScreenHireApiMessaging(TenantEventPusher.For)` | `builder.AddScreenHireMessageHandling()` |
| Handlers | one SignalR pusher per `ITenantEvent` type, nothing else (conventional discovery off) | every handler in `ScreenHire.Application` |
| Routing | every message to the Postgres queue `worker`; every `ITenantEvent` to `realtime` as well | listens on `worker`; `ITenantEvent`s go to `realtime` and `worker`; other messages between its own handlers use durable local queues |
| Storage schema | `wolverine_api`: its own outbox, the `realtime` inbox, its own node records | `wolverine` |
| Durability mode | `Balanced`, in its own cluster: leader election and a durability agent for its own outbox | `Balanced`: leader election, durability agent, recurring schedules |

Wolverine's Postgres transport polls queue tables; it does not use `LISTEN/NOTIFY`. An idle listener waits
`ScheduledJobPollingTime` (5 s) between polls by default, and every hop of a chain of events waits one poll, so both
listeners poll faster: the worker every `MessagingSettings.WorkerPollingInterval` and the API's `realtime` listener
every `RealtimePollingInterval` (250 ms each). That is four small queries a second per listener while idle.

The API never runs background work. That is deliberate: a message published while the worker is down waits in the
queue table and runs when the worker comes back. The one thing the API handles is a tenant event on the `realtime`
queue, which it pushes to connected dashboards ([real-time updates](realtime.md)). Receiving needs listeners, which
`ServerlessWithOutbox`, the API's mode before that, does not run for a Postgres queue. So the API runs `Balanced` in
its own storage schema, as a separate Wolverine cluster. realtime.md says why one shared cluster failed.

If the API dies between committing and sending, the envelope stays in the API's outbox
(`wolverine_api.wolverine_outgoing_envelopes`), and the API's own durability agent sends it once an API node is back.
No test covers that crash window: it needs a process killed between two statements.

The worker keeps its Wolverine tables and both queue tables in the `wolverine` schema of the application database
(`DatabaseRegistration.MessagingSchema`). The API keeps its own tables in `wolverine_api`
(`DatabaseRegistration.ApiMessagingSchema`) and shares the queue tables (`MessagingSettings.TransportSchema`). None of
them are part of the EF Core model.

## Roles and provisioning

The API and the worker connect as `screenhire_app` for EF and for Wolverine alike (`ScreenHireMessaging.ConnectionName`
is `DatabaseRegistration.AppConnectionName`). That role is bound by row-level security and may read and write
Wolverine's rows but not create tables, so the hosts never patch the schema at startup
(`AutoBuildMessageStorageOnStartup = None`, no `AutoProvision`, no resource setup).

The owner provisions instead, in two steps that always run in this order:

1. The EF migrations. `GrantAppRoleMessagingAccess` creates the `wolverine` schema and
   `GrantAppRoleApiMessagingAccess` the `wolverine_api` schema. Each grants `screenhire_app` `USAGE`, `SELECT`,
   `INSERT`, `UPDATE` and `DELETE`, with default privileges for tables the owner creates later.
2. `ScreenHireMessaging.ProvisionStorageAsync(ownerConnectionString)`, which builds two throwaway Wolverine hosts and
   runs JasperFx resource setup on each: one with the worker's configuration and schedules (the worker's store and
   both queues), one with the API's (the API's store). Run it again after every Wolverine upgrade.

In Development the API runs both at startup, before it listens; the worker waits for a healthy API. In production the
deploy step runs both (`docs/architecture/sign-in-and-tenancy.md`, "Deploy prerequisites"), and neither host holds the
owner's credentials. Tests' private-schema hosts (`MessagingHost`) connect as the owner with
`MessagingSettings.AutoProvision = true`; `WorkerMessagingTests` exercises the real hosts as `screenhire_app`.

## Constraints

- **Direct connections.** Leader election uses Postgres advisory locks and the inbox needs a real session. Never put
  a transaction-mode PgBouncer between the hosts and Postgres.
- **No EF retrying execution strategy.** Aspire enables one by default, and it refuses the explicit transaction the
  outbox and the handler middleware open. Both hosts register the context through `AddScreenHireDatabase`, which
  turns it off (`DisableRetry`). Wolverine's error policy retries failed messages instead.
- **Runtime code generation.** Wolverine compiles handler adapters at startup (`TypeLoadMode.Dynamic`), which needs
  `WolverineFx.RuntimeCompilation` (Roslyn). Pre-generated code (`TypeLoadMode.Static`) would cut cold start for
  production; it is not set up yet.
- **Schema ownership.** The owner creates and migrates Wolverine's tables (see Roles and provisioning). A host that
  fails at startup with `relation "wolverine.…" does not exist` after a Wolverine upgrade is missing step 2.
- **OpenAPI at build time.** The API build runs `Program` under `GetDocument.Insider`, which starts the host without
  the AppHost. The API's messaging setup detects it and registers Wolverine in `MediatorOnly` mode with no database, so the
  build never connects anywhere.

## Publishing from an endpoint

Take `IDbContextOutbox<ScreenHireDbContext>`, change entities through `outbox.DbContext`, publish, then save once:

```csharp
if (job.Publish(time.GetUtcNow(), latest) is { } version)
{
    db.JobVersions.Add(version);
    await outbox.PublishAsync(new JobPublished(job.OrganizationId, job.Id, version.PublishedAt, version.Version, version.Id));
    await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
}
```

Publish only on a real state change. `POST /v1/jobs/{jobId}/publish` raises `JobPublished` when a draft goes live or
when changed content is republished as a new job version, and publishing unchanged content commits nothing and raises
nothing. The same rule holds for `JobClosed`, `ApplicationSubmitted`, `ApplicationMoved` and
`HiringTemplateVersionPublished`: closing a closed job, moving an application to its current stage or publishing a
published template version raises nothing. That state check is not race-proof: `Job` has
no concurrency token, so two simultaneous requests for one draft both commit and raise two events. Today's handler
only logs. The board-posting handler from job-distribution.md must hold on its own idempotency key and effect check
(see Idempotency).

Inside a worker handler, return the next message (a cascading message) or take `IMessageContext`. Handlers that take
the `DbContext` already run in one transaction with their outbox (`AutoApplyTransactions`).

## Tenant events

`ITenantEvent` marks an integration event that belongs to one organization (`OrganizationId`) and that its signed-in
users may see. The API pushes every `ITenantEvent` to that organization's connected clients
([real-time updates](realtime.md)), so a feature task only defines the event and its Zod schema
(`/add-realtime-event`). Events carry ids and the facts a client needs to refresh, never personal data.

A worker handler connects as `screenhire_app` too, so it sees no tenant rows until it has a tenant. Every message that
touches one organization's data implements `ITenantMessage` (`OrganizationId`); `ITenantEvent` extends it.
`TenantMessageMiddleware` runs before each such handler and makes `OrganizationId` the tenant of the handler's scope,
and `TenantSessionInterceptor` then sets `app.tenant_id` inside the handler's transaction (`AutoApplyTransactions`
gives each handler one). The tenant filter and row-level security scope the handler exactly as they scope a request.
`JobPublishedHandler` reads its job back, so a missing tenant fails loudly and goes through the retry policy.

Wolverine's code generation would construct `TenantContext` itself; `ConfigureHandler` makes it come from the
handler's scope (`AlwaysUseServiceLocationFor`), so the middleware and the DbContext share one instance.

A handler for work that is not about one organization, such as `ReportOpenJobs`, has no tenant and sees no tenant
rows. It never goes around the policies: it lists organizations (readable by anyone) and sets each one as the tenant
in turn.

### How tenant data stays apart in Wolverine's tables

Wolverine's tables (outbox, queue, inbox, dead letters) have no row-level security: they hold envelopes, not tenant
rows, and only the hosts read them. Message bodies stay safe by what they carry. A message holds the organization id
and other ids, plus the facts its handler needs, never names, emails, answers or other personal data, and never
another organization's data. The handler reads anything else it needs from the database, as the message's tenant, so
row-level security decides what it can see. The dead-letter table keeps a failed message's full body, which is one more
reason the body has nothing worth leaking.

## Idempotency

Wolverine delivers at least once. Three layers keep a handler's effect exactly once:

1. **Delivery.** The worker's durable inbox records each envelope id, so a redelivered envelope is not handled again.
2. **Logical duplicates.** Two envelopes can carry the same intent: a client retries a request, or a schedule fires
   twice. Give the message an idempotency key (for example `(applicationId, automationId, eventId)` from
   docs/architecture/workflow-engine.md, or `(jobId, boardId, jobVersion)` from job-distribution.md) and mark the
   handler `[Deduplicated(ValueSource.InputMember, nameof(Message.IdempotencyKey))]`. The worker turns on
   `MessageDeduplicationMode.CompareByHash`, which stores claims for 24 hours by default. `SyncJobPosting` is the first
   handler that uses it; its key adds the action and the posting's request sequence, so a deliberate second request
   for the same version is not dropped as a duplicate.
3. **Effects.** Make the side effect itself idempotent: a unique index on the natural key, a state check
   ("already open, nothing to do"), or an upsert. This is the only layer that holds after the deduplication window.

Never generate the key from the clock or a fresh `Guid` at publish time; it must be the same on every retry.

## Failures

Every handler in the worker runs under one policy (`ApplyErrorPolicy`):

1. Retry in process after 1 s, 5 s and 30 s.
2. Then schedule durable retries after 5 and 30 minutes.
3. Then move the message to `wolverine.wolverine_dead_letters` with the exception type and message.

Each failure is logged at Error with the exception, the move to the dead-letter table is logged as "moved to the error
queue", the message's trace records `wolverine.envelope.retried` and `wolverine.error.queued`, and the
`wolverine-dead-letter-queue` counter goes up. The Aspire dashboard shows all three. A message in the dead-letter
table stays until someone replays or deletes it, with its full body. That is one more reason no message, tenant
event or not, carries personal data.

The last step of every branch also calls `AutomationFailures.ReportAsync`. For a dead-lettered
`ExecuteAutomationAction` it publishes `AutomationActionFailed`, whose handler marks the action and its run failed, so
the workflow engine's "automation issues" list shows it (workflow-engine.md section 8). For any other message it does
nothing.

## Scheduled and recurring messages

- **One-off delay:** `await bus.ScheduleAsync(message, TimeSpan.FromHours(1))` or `DeliveryOptions.ScheduleDelay`.
  The message is stored durably and released by the durability agent.
- **Recurring:** `opts.Schedules.ScheduleRecurring<T>("cron")` in `ScheduleRecurringMessages`, worker only. Times are
  UTC. The example is `ReportOpenJobs` every five minutes, which replaced the scaffold's heartbeat loop. A schedule
  faster than every five seconds is refused.
- **Job distribution** ([job-distribution.md](job-distribution.md) §5) uses both. A push board's answer schedules
  `ReconcileJobPosting` for the time the board gave, as a cascading message (`OutgoingMessages.Schedule`). Two
  recurring messages back it up: `ReconcileJobPostings` every five minutes and `DispatchIndexingNotifications` every
  minute. Both walk the organizations and hand each organization's work to an `ITenantMessage`, which runs as that
  organization.

## Telemetry

`AddServiceDefaults()` subscribes OpenTelemetry to the `Wolverine` activity source and the `Wolverine:*` meters, so
message spans, span events and counters reach the Aspire dashboard. `DeadLetterTests` checks the span event and the
counter in process; no test reads them back from the dashboard.

## Tests

`tests/ScreenHire.IntegrationTests/Messaging`:

| Test | Proves |
|---|---|
| `WorkerMessagingTests` | through the running API and worker: an event commits with its job and is handled once; no second event for a no-op; a message published while the worker is stopped is handled after it restarts. Runs in the non-parallel `WorkerLifecycle` collection because it stops the worker. |
| `OutboxTests` | with the API's publishing setup in a private schema: a change and its message commit together, and a failed `SaveChanges` sends nothing |
| `DeadLetterTests` | with the worker's setup in a private schema and short delays: retries with backoff, then the dead-letter table, the error log, the trace event and the counter; and the production shape, where a scheduled retry follows the in-process retries |
| `RecurringMessageTests` | `ReportOpenJobs` is registered every five minutes, tracked durably, and its handler runs when triggered |
| `Automations/AutomationEmailTests` | the workflow engine through the running stack: entering a stage sends the configured email (read back from Mailpit), and replaying the same `ApplicationMoved` sends nothing twice |
| `Automations/TimeElapsedTests` | entering a stage schedules a durable timer; the timer runs its automation when it fires, and a timer from a stage entry the application has left does nothing |
| `Automations/WorkflowEngineTests` | validate and simulate; a manual run's tag and rejection-review task; two automations moving an application back and forth stopped as a loop; and, in a private schema with no retries, a dead-lettered action reaching the automation issues list |

Real-time delivery has its own tests (realtime.md). `MessagingHost` starts those private-schema hosts, and a publisher
whose storage is private but whose queues are the running stack's, to put a message on the real worker queue. `WolverineStore` reads Wolverine's tables and finds a test's own
messages by a value in their body.
