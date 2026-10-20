---
description: Add a Wolverine message or domain event with its worker handler - contract in src/ScreenHire.Application, publishing through the EF Core outbox in the same transaction as the change, idempotency (inbox, [Deduplicated] keys, idempotent effects), ITenantMessage for messages about one organization's data and ITenantEvent for client-visible events, scheduled or recurring delivery, and an Aspire integration test. Use for any background work, domain event or job.
argument-hint: [MessageName - what triggers it and what the handler does]
---

# Add a message

Background work runs on Wolverine with a Postgres outbox (ADR-0008). The API publishes, the worker handles, and a
message commits in the same transaction as the data it describes. Read
[`docs/architecture/messaging.md`](../../../docs/architecture/messaging.md) first; it explains the topology, the
idempotency layers and the failure policy that this procedure relies on. `JobPublished` is the worked example:
`src/ScreenHire.Application/Jobs/JobPublished.cs`, its handler, and `PublishJob` in
`apps/api/Endpoints/JobEndpoints.cs`.

**Request:** $ARGUMENTS

## 1. Contract

Add a `public sealed record` in `src/ScreenHire.Application/<Area>/<MessageName>.cs`, with a `/// <summary>` that
says what happened (event, past tense: `JobPublished`) or what to do (command, imperative: `SyncJobPosting`).

- Carry ids and the facts the handler needs, never personal data (names, emails, resume text, answers).
- If it is about one organization's data, implement `ITenantMessage` (`OrganizationId`): the worker makes that
  organization the tenant before the handler runs, and without it the handler sees no tenant rows
  (docs/architecture/messaging.md, Tenant events). If that organization's clients may see it, implement
  `ITenantEvent` instead, which extends it: the API pushes it to the organization's open dashboards, and
  `/add-realtime-event` adds its Zod schema and what it refreshes.
- If two envelopes could carry the same intent (a client retry, a schedule that fires twice, a fan-out per board),
  add an idempotency key built from stable ids, such as `$"{applicationId}:{automationId}:{eventId}"`. Never build it
  from the clock or a new `Guid`.
- Messages are serialized as JSON. Renaming or removing a property breaks messages already in the queue; add new
  properties as optional instead.

## 2. Handler

Add `public static class <MessageName>Handler` next to the contract with a `Handle` (or `HandleAsync`) method. The
worker discovers it by name; the API never runs it.

- Take what you need as parameters: the message, `ScreenHireDbContext`, `ILogger<T>`, `TimeProvider`,
  `CancellationToken`. A handler that takes the `DbContext` runs in one EF transaction with its outbox, so do not
  call `SaveChangesAsync` yourself.
- To trigger more work, return the next message (or a tuple of messages) instead of publishing by hand. It commits
  with the handler's transaction.
- Make the effect idempotent: check state first ("already open, nothing to do"), or rely on a unique index or an
  upsert. With an idempotency key from step 1, also mark the handler
  `[Deduplicated(ValueSource.InputMember, nameof(<MessageName>.IdempotencyKey))]`.
- Do not catch exceptions to hide them. A throw triggers the retry policy and, at the end, the dead-letter table.
  Throw early for a message that can never succeed; there is nothing to gain from retrying it.
- Log with `[LoggerMessage]` and ids only.

## 3. Publish it

- **From an endpoint** (the change and the message must commit together): take
  `IDbContextOutbox<ScreenHireDbContext> outbox`, load and change entities through `outbox.DbContext`, call
  `await outbox.PublishAsync(message)`, then `await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken)` once.
  Publish only when the state really changed. Follow `/add-endpoint` for the route itself.
- **From a handler:** return the message (step 2).
- **Later:** `await bus.ScheduleAsync(message, delay)` with an injected `IMessageBus`, or `outbox.ScheduleAsync`
  inside an outbox transaction.
- **On a schedule:** add `opts.Schedules.ScheduleRecurring<<MessageName>>("<cron>")` to `ScheduleRecurringMessages`
  in `src/ScreenHire.Application/Messaging/ScreenHireMessaging.cs`. Cron times are UTC; nothing faster than every five
  seconds. The message needs a public parameterless constructor.

## 4. Test

Add tests to `tests/ScreenHire.IntegrationTests/Messaging` (or an area folder) that use the shared `AppHostFixture`:

- Through the API: call the endpoint, assert the entity changed, then wait for the handler with
  `WolverineStore.WaitUntilAsync(... CountHandledAsync(<an id from the message>) == 1 ...)`. Find messages by an id in
  their body so the test only sees its own. Cover the no-op path (no second message).
- The handler's effect: assert on the rows it wrote, once.
- Pure logic in the handler goes in a unit test in `tests/ScreenHire.UnitTests` instead.
- Tests that stop or restart the worker belong in the `WorkerLifecycle` collection. Everything else runs in
  parallel.

Run them while you work:

```
dotnet test --project tests/ScreenHire.IntegrationTests --filter-class "*WorkerMessagingTests"
```

## 5. Done when

- `pnpm verify:dotnet` passes and the new tests pass.
- If you added or changed an endpoint, `pnpm generate:api` has run and its output is staged.
- `docs/architecture/messaging.md` still describes the setup. Update it if you changed `ScreenHireMessaging`.
