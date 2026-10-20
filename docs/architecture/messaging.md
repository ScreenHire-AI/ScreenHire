# Messaging and background work

Background work, domain events and scheduled jobs run on Wolverine 6.51 (MIT, ADR-0008) with Postgres as the only
infrastructure: no broker. A message published with a data change commits in the same transaction as that change,
and the worker handles it once. The setup lives in one place,
[`ScreenHireMessaging`](../../src/ScreenHire.Application/Messaging/ScreenHireMessaging.cs), and the `/add-message`
skill is the procedure for adding a message.

## Topology

```
API (publishes only)                         Postgres (screenhire database)            Worker (handles)
  endpoint: change entity                      schema "wolverine"
  + outbox.PublishAsync(event)       ───▶      wolverine_outgoing_envelopes  (outbox)
  + SaveChangesAndFlushMessagesAsync           wolverine_queue_worker        (queue)  ───▶  listener, durable inbox
      one transaction                          wolverine_incoming_envelopes  (inbox)        handler in an EF transaction
                                               wolverine_dead_letters                       retries, then dead letter
                                               wolverine_nodes, ...                         durability agent, cron
```

| | API | Worker |
|---|---|---|
| Call in `Program.cs` | `builder.AddScreenHireMessagePublishing()` | `builder.AddScreenHireMessageHandling()` |
| Handlers | none (conventional discovery off) | every handler in `ScreenHire.Application` |
| Routing | every message goes to the Postgres queue `worker` | listens on `worker`; messages between its own handlers use durable local queues |
| Durability mode | `ServerlessWithOutbox`: outbox only, no node record, no leader election, no agents | `Balanced`: leader election, durability agent, recurring schedules |

The API never handles a message. That is deliberate: a message published while the worker is down waits in the queue
table and runs when the worker comes back. If the API dies between committing and sending, the envelope stays in
`wolverine_outgoing_envelopes` owned by "any node" (owner 0). Wolverine documents that its durability agents recover
those rows in every mode except the serverless ones (`DurabilityMode.ServerlessWithOutbox` and
`RunDurabilityPassAsync` in the 6.51 API docs), so the worker sends it. No test covers that crash window: it needs a
process killed between two statements.

The API cannot receive messages either: `ServerlessWithOutbox` runs no listeners and no local queues. Anything that
must react inside the API process, such as pushing `ITenantEvent`s to connected clients, needs either a different
durability mode for the API (`Balanced`, which brings back leader election and agents there) or a hop through the
worker. Decide that in the task that needs it and update this page.

Both hosts keep every Wolverine table, including the queue tables, in the `wolverine` schema of the application
database. Wolverine creates and updates them at startup (`AutoProvision` plus JasperFx resource setup). They are not
part of the EF Core model, so EF migrations never touch them.

## Constraints

- **Direct connections.** Leader election uses Postgres advisory locks and the inbox needs a real session. Never put
  a transaction-mode PgBouncer between the hosts and Postgres.
- **No EF retrying execution strategy.** Aspire's `AddNpgsqlDbContext` enables one by default, and it refuses the
  explicit transaction the outbox and the handler middleware open. Both hosts register the context with
  `settings.DisableRetry = true`. Wolverine's error policy retries failed messages instead.
- **Runtime code generation.** Wolverine compiles handler adapters at startup (`TypeLoadMode.Dynamic`), which needs
  `WolverineFx.RuntimeCompilation` (Roslyn). Pre-generated code (`TypeLoadMode.Static`) would cut cold start for
  production; it is not set up yet.
- **Schema ownership.** Wolverine creates and migrates its own tables at startup, so the hosts' database role must be
  allowed to create the `wolverine` schema. ADR-0008's production app role neither owns tables nor bypasses RLS;
  when that role lands, the deploy step has to provision the `wolverine` schema (or grant it to a separate role) and
  the hosts stop creating it.
- **OpenAPI at build time.** The API build runs `Program` under `GetDocument.Insider`, which starts the host without
  the AppHost. Publishing setup detects it and registers Wolverine in `MediatorOnly` mode with no database, so the
  build never connects anywhere.

## Publishing from an endpoint

Take `IDbContextOutbox<ScreenHireDbContext>`, change entities through `outbox.DbContext`, publish, then save once:

```csharp
job.Publish(time.GetUtcNow());
await outbox.PublishAsync(new JobPublished(job.OrganizationId, job.Id, job.PublishedAt!.Value));
await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
```

Publish only on a real state change. `POST /v1/jobs/{jobId}/publish` raises `JobPublished` when a draft goes live,
and publishing an open job again commits nothing and raises nothing.

Inside a worker handler, return the next message (a cascading message) or take `IMessageContext`. Handlers that take
the `DbContext` already run in one transaction with their outbox (`AutoApplyTransactions`).

## Tenant events

`ITenantEvent` marks an integration event that belongs to one organization (`OrganizationId`) and that its signed-in
users may see. The real-time work fans every `ITenantEvent` out to that organization's clients, so a feature task
only defines the event. Events carry ids and the facts a client needs to refresh, never personal data.

## Idempotency

Wolverine delivers at least once. Three layers keep a handler's effect exactly once:

1. **Delivery.** The worker's durable inbox records each envelope id, so a redelivered envelope is not handled again.
2. **Logical duplicates.** Two envelopes can carry the same intent: a client retries a request, or a schedule fires
   twice. Give the message an idempotency key (for example `(applicationId, automationId, eventId)` from
   docs/architecture/workflow-engine.md, or `(jobId, boardId, jobVersion)` from job-distribution.md) and mark the
   handler `[Deduplicated(ValueSource.InputMember, nameof(Message.IdempotencyKey))]`. The worker turns on
   `MessageDeduplicationMode.CompareByHash`, which stores claims for 24 hours by default.
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
table stays until someone replays or deletes it; the workflow engine will surface its own dead letters as an
"automation issues" inbox (workflow-engine.md section 3).

## Scheduled and recurring messages

- **One-off delay:** `await bus.ScheduleAsync(message, TimeSpan.FromHours(1))` or `DeliveryOptions.ScheduleDelay`.
  The message is stored durably and released by the durability agent.
- **Recurring:** `opts.Schedules.ScheduleRecurring<T>("cron")` in `ScheduleRecurringMessages`, worker only. Times are
  UTC. The example is `ReportOpenJobs` every five minutes, which replaced the scaffold's heartbeat loop. A schedule
  faster than every five seconds is refused.

## Telemetry

`AddServiceDefaults()` subscribes OpenTelemetry to the `Wolverine` activity source and the `Wolverine:*` meters, so
message spans, span events and counters reach the Aspire dashboard. `DeadLetterTests` checks the span event and the
counter in process; no test reads them back from the dashboard.

## Tests

`tests/ScreenHire.IntegrationTests/Messaging`:

| Test | Proves |
|---|---|
| `WorkerMessagingTests` | through the running API and worker: an event commits with its job and is handled once; no second event for a no-op; a message published while the worker is stopped is handled after it restarts. Runs in the non-parallel `WorkerLifecycle` collection because it stops the worker. |
| `OutboxTests` | with the API's setup in a private schema: a change and its message commit together, and a failed `SaveChanges` sends nothing |
| `DeadLetterTests` | with the worker's setup in a private schema and short delays: retries with backoff, then the dead-letter table, the error log, the trace event and the counter; and the production shape, where a scheduled retry follows the in-process retries |
| `RecurringMessageTests` | `ReportOpenJobs` is registered every five minutes, tracked durably, and its handler runs when triggered |

`MessagingHost` starts those private-schema hosts. `WolverineStore` reads Wolverine's tables and finds a test's own
messages by a value in their body.
