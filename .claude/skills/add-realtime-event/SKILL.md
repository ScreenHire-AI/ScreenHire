---
description: Add a live update for the recruiter dashboard - an ITenantEvent record in src/ScreenHire.Application raised through the outbox, its Zod schema in packages/realtime, the C#/Zod contract file, the dashboard load dependency it invalidates, an optional record topic (ITopicEvent), and tests. Use whenever a change should appear in open dashboards without a reload.
argument-hint: [EventName - what happened, and which screen should refresh]
---

# Add a real-time event

Any message that implements `ITenantEvent` reaches its organization's open dashboards: the API pushes it from the
`realtime` queue to the SignalR group `org:{organizationId}`. Nothing in the hub changes. Read
[`docs/architecture/realtime.md`](../../../docs/architecture/realtime.md) first. `JobClosed` is the worked example:
`src/ScreenHire.Application/Jobs/JobClosed.cs`, `CloseJob` in `apps/api/Endpoints/JobEndpoints.cs`, its schema in
`packages/realtime/src/events.ts`, and `apps/dashboard/src/lib/realtime.ts`.

**Request:** $ARGUMENTS

## 1. The event

Follow `/add-message` steps 1 and 3 for the record and how it is raised: a `public sealed record <EventName>` in
`src/ScreenHire.Application/<Area>/`, past tense, implementing `ITenantEvent`, published through the outbox with the
change or returned from a worker handler. A worker handler is optional; without one the worker marks the event
handled and drops it.

- Every signed-in member of the organization receives the payload in their browser. Carry ids, statuses and times,
  never names, emails, answers or integrity evidence. The client refetches anything else through the REST API, which
  applies its own authorization.
- The record's name is its `type` on the wire. It must be unique among events; `ScreenHireMessaging.TenantEventTypes`
  fails at startup otherwise.
- Property types the contract test can sample: `Guid`, `DateTimeOffset`, `string`, `int`, `bool`, enums and their
  nullables. Teach `RealtimeContractTests.SampleValue` anything else.
- **Only one record's screen needs it** (progress on one application, say)? Implement `ITopicEvent` instead, with
  `Topic` (a name from `RealtimeTopics` in `apps/api/Realtime/RealtimeGroups.cs`) and `TopicId`. It then goes only to
  clients that called `subscribe(topic, id)`. A new topic needs an entry in `RealtimeTopics.Exists` that checks the
  record as the caller's organization: an `AnyAsync` on the new entity's `DbSet`, like the `job` entry.

## 2. The contract

```
UPDATE_REALTIME_CONTRACT=1 dotnet test --project tests/ScreenHire.UnitTests --filter-class "*RealtimeContractTests"
```

That rewrites `packages/realtime/contract/events.json` with a sample of every event. Add a schema to
`realtimeEventSchemas` in `packages/realtime/src/events.ts` with the same camelCase properties: `id` for a `Guid`,
`instant` for a `DateTimeOffset`, `z.enum([...])` with the C# names for an enum. Then:

```
pnpm --filter @screenhire/realtime test
```

passes only when every event in the file parses with its schema and every property is known.

## 3. The dashboard

- The page whose data the event changes declares a dependency in its `load`: `depends('api:<name>')`, as the jobs
  list does with `jobsDependency`.
- Map the event to that key in `invalidations` in `apps/dashboard/src/lib/realtime.ts`. The shell connects once, and
  re-runs every mapped `load` after a reconnect, so a page never needs its own connection.
- For an `ITopicEvent`, the page subscribes while it is open. The shell's `startLiveUpdates()` returns `subscribe`;
  pass it down or move the instance into a context when the first page needs it, and call the returned function when
  the page closes.

## 4. Tests

- Integration, in `tests/ScreenHire.IntegrationTests/Realtime`: connect with `EventsConnection.ConnectAsync(session)`,
  trigger the change through the API (or, for a worker-raised event, put the message on the worker queue with
  `MessagingHost.StartPublisherToAppHostQueuesAsync`), then `WaitForAsync("<EventName>", "<idProperty>", id)`. Assert a
  second organization's connection received nothing about that id (`ReceivedAnythingAbout`).
- For a topic event: a client that did not subscribe receives nothing.
- Vitest for the dashboard mapping when it is more than one line; Playwright only when the event changes a user
  journey (see `tests/ScreenHire.E2E/LiveUpdatesTests.cs`).

```
dotnet test --project tests/ScreenHire.IntegrationTests --filter-class "*RealtimeHubTests"
```

## 5. Done when

- The contract test, the realtime Vitest suite and the new integration test pass; `pnpm verify` passes.
- `docs/architecture/realtime.md` still holds. Update its tests table if you added a test class.
