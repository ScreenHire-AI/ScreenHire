---
type: Design
title: "Real-time updates"
description: "How an ITenantEvent reaches open dashboards: the API's own Wolverine cluster in wolverine_api, the SignalR hub at /v1/hubs/events and its tenant groups, the packages/realtime client and cache invalidation, the C# to Zod contract test, and scaling out."
tags: [realtime, signalr, events]
sources:
  - resource: apps/api/Realtime/EventsHub.cs
  - resource: apps/api/Realtime/RealtimeConnections.cs
  - resource: apps/api/Realtime/RealtimeGroups.cs
  - resource: apps/api/Realtime/RealtimeSetup.cs
  - resource: apps/api/Realtime/TenantEventPusher.cs
  - resource: packages/realtime/src/backoff.ts
  - resource: packages/realtime/src/client.ts
  - resource: packages/realtime/src/connection-state.svelte.ts
  - resource: packages/realtime/src/events.ts
  - resource: packages/realtime/src/index.ts
  - resource: packages/realtime/src/invalidation.ts
  - resource: packages/realtime/contract/events.json
  - resource: apps/dashboard/src/lib/realtime.ts
  - resource: tests/ScreenHire.UnitTests/Realtime/RealtimeContractTests.cs
---
# Real-time updates

The recruiter dashboard updates live. A job published or closed, and later a new application, a stage move or a
finished assessment, appears in every open tab of that organization without a reload, and nowhere else. Any message
that implements `ITenantEvent` is pushed. A feature task defines the event and its Zod schema, and never touches the
hub. The procedure is the [`/add-realtime-event`](../../.claude/skills/add-realtime-event/SKILL.md) skill.

## Path of an event

```
API endpoint or worker handler
  publishes JobPublished (ITenantEvent)            outbox, same transaction as the change
        │
        ├──▶ Postgres queue "worker"    ──▶ worker handlers (if any; otherwise marked handled)
        └──▶ Postgres queue "realtime"  ──▶ API: TenantEventPusher<JobPublished>
                                               └─▶ SignalR group org:{organizationId}  ──▶ browsers
                                                   (or org:{id}:{topic}:{recordId} for an ITopicEvent)
```

1. Both hosts route every `ITenantEvent` to the `realtime` queue and to the `worker` queue
   (`ScreenHireMessaging.RouteTenantEvents`). So an event reaches the browser whichever host raised it, and worker
   handlers still see it. An explicit Wolverine rule replaces local routing for the type, which is why a
   worker-raised event reaches the worker's own handlers through the `worker` queue as well.
2. The API listens on `realtime` and has one handler per concrete event type, `TenantEventPusher<T>`. Wolverine cannot
   generate a handler for an interface or a generic class. `ScreenHireMessaging.ConfigureApi` therefore registers a
   ready-made one per type in `ScreenHireMessaging.TenantEventTypes` with `WolverineOptions.AddMessageHandler`. A new
   event record needs no API code.
3. The pusher sends `{ type, data }` (`RealtimeEvent`) to the hub's client method `ReceiveEvent`. `type` is the record's
   name and `data` is the record, camelCased, with enums as strings and strict numbers (`RealtimeSetup.ConfigureJson`,
   the same rules as the REST API).

Wolverine's Postgres transport polls its queues instead of listening for notifications, and an idle listener waits
`ScheduledJobPollingTime` (5 s) between polls by default. The API's `realtime` listener polls every
`MessagingSettings.RealtimePollingInterval` (250 ms) instead, so an event reaches open tabs within a fraction of a
second. The 5 s default let live updates miss the end-to-end tests' 5 s budget once job distribution added traffic.

An event that waits in `realtime` longer than `MessagingSettings.RealtimeEventLifetime` (one minute) is discarded: a
live update that arrives late is noise. A failed push is retried twice, then dropped with the error logged. Delivery
is best-effort on purpose. A client that loses its connection refetches what it shows when it comes back, so a missed
event costs nothing.

## The API's own Wolverine cluster

The API used to publish only, in `DurabilityMode.ServerlessWithOutbox`. That mode leaves a Postgres queue unread, so
receiving needed a change. The API now runs `Balanced` in its own storage schema, `wolverine_api`
(`DatabaseRegistration.ApiMessagingSchema`). It shares only the queue tables in `wolverine` with the worker
(`MessagingSettings.TransportSchema`).

| | API | Worker |
|---|---|---|
| Storage schema (outbox, inbox, nodes, dead letters) | `wolverine_api` | `wolverine` |
| Queue tables | `wolverine` | `wolverine` |
| Listens on | `realtime` (durable inbox in `wolverine_api`) | `worker` |
| Mode | `Balanced`: its own leader and durability agent, which recovers the API's outbox | `Balanced`: leader, durability agent, cron |

Two rejected options:

- **One shared cluster.** The worker's leader assigned its recurring-schedule agent (`wolverine-recurring://`) to the
  API node. The API cannot run that agent, and the command went to the dead-letter table. Turning off the API's
  durability agent and leader eligibility did not stop it.
- **`Solo` for the API.** At startup it clears every node record and releases all envelope ownership in its store,
  which in a shared store would take the worker's work.

The migration `GrantAppRoleApiMessagingAccess` creates `wolverine_api` and grants `screenhire_app` the same rights as on
`wolverine`. `ScreenHireMessaging.ProvisionStorageAsync` creates both stores' tables and both queues as the owner
(docs/architecture/messaging.md, "Roles and provisioning").

## Hub

`EventsHub` at `/v1/hubs/events`, mapped in `RealtimeSetup.MapRealtimeEndpoints`:

- **Authentication** is the dashboard's session cookie (`sh.session`), like every API route. The fallback policy
  answers an anonymous negotiate or WebSocket request with 401. `CloseOnAuthenticationExpiration` closes a connection
  when its cookie expires.
- **Origin.** CORS does not cover a WebSocket upgrade, and the `Lax` session cookie goes with a request from any
  same-site page, such as a careers page under the dashboard's apex. `WebSocketOptions.AllowedOrigins` (set in
  `AddScreenHireRealtime`) therefore admits only the dashboard's origin (`ScreenHire:Domains:AppHost`), and the
  `UseWebSockets` that `MapHub` runs answers any other `Origin` with 403. A request with no `Origin` is not a
  browser's and gets through to the cookie check.
- **Tenant.** Each hub call runs in its own DI scope, which `TenantResolutionMiddleware` never sees. Each call
  therefore resolves the user's organization itself (`Memberships.ResolveTenantAsync`). On connect the connection
  joins `org:{organizationId}`. A signed-in user without an organization is disconnected.
- **Subscriptions.** `Subscribe(topic, id)` and `Unsubscribe(topic, id)` add a connection to one record's group,
  `org:{organizationId}:{topic}:{id}`. `RealtimeTopics.Exists` checks the record as the caller's organization, so
  another organization's record fails exactly like a missing one. Today the only topic is `job`. `application` arrives
  with the applications entity, which the hub cannot validate yet. An event that implements `ITopicEvent` goes only to
  its record's group, for chatty per-record updates. Every other event goes to the whole organization.
- **One direction.** The hub has no methods that change data. Changes stay REST, with antiforgery.

Group names always start with the organization, so no group a client can join spans two tenants. Removing someone
from an organization closes their connections into it at once: `RealtimeConnections` lists this process's
connections by user and organization, and the remove route aborts theirs. A connection keeps the groups it joined at
connect, so switching organizations (`POST /v1/auth/switch-organization`) aborts the user's connections into the one
they left, on every device, and each client reconnects into the new one.
Accepting an invitation also makes the new organization the active one but closes nothing: a dashboard already
connected as that user follows it on its next reconnect.

## Client: `packages/realtime`

- `RealtimeClient` wraps `@microsoft/signalr`. It retries with backoff (1 s doubling to 30 s, 20% jitter, never giving
  up) through SignalR's automatic reconnect, plus its own retry when the first start or a reconnect fails. A
  connection the server closes within 10 s of opening keeps the backoff growing. It stops on a 401: the session is
  gone, and the next navigation sends the person to sign in. SignalR's negotiate error drops the status code, so the
  connection uses a `DefaultHttpClient` subclass that reports every 401 to the client. It re-sends every `subscribe`
  after each reconnect and calls `onResumed` handlers so the app refetches.
- `createConnectionState(client)` is a runes store (`$state`) with the connection status, for the shell's indicator.
- `invalidateOnEvents(client, map, invalidate)` invalidates cache keys by event type, and every mapped key after a
  reconnect. The dashboard maps each event to the API paths it changes (`JobPublished` to `/v1/jobs`,
  `ApplicationMoved` to `/v1/jobs`, `/v1/applications` and `/v1/candidates`), and invalidates every TanStack Query
  query under each path. `apps/dashboard/src/lib/realtime.ts` holds the map. An event that needs more than a refetch
  goes through `LiveUpdates.onEvent`: the shell turns `TeamNotified` into a notification in its tray, after reading the
  application and the run through the API, because the event carries ids only. `TeammateMentioned` reaches the whole
  organization like every event, and only the dashboard of `mentionedUserId` shows its card; the worker's handler of the
  same event sends the mention email.
- `realtimeEventSchemas` has a Zod schema per event. `parseRealtimeEvent` ignores an unknown type, which a newer API may
  send, and throws on a known type that does not match its schema.

The dashboard's Vite proxy forwards `/v1` with `ws: true` in `dev`, and the AppHost's Caddyfile does the same for the
built dashboard (`ScreenHire:Frontends=Built`, `apps/apphost/Caddy/dashboard/Caddyfile`), so the hub stays same-origin
and the cookie applies.

## Contract test

`tests/ScreenHire.UnitTests/Realtime/RealtimeContractTests.cs` serializes a sample of every `ITenantEvent` exactly as
the hub does and compares it with `packages/realtime/contract/events.json`. `packages/realtime/src/events.test.ts`
parses each sample with its Zod schema, checks that no schema is missing or extra, and checks that the schema knows
every property. A changed event therefore fails one side or the other. To accept a change, rewrite the file:

```
UPDATE_REALTIME_CONTRACT=1 dotnet test --project tests/ScreenHire.UnitTests --filter-class "*RealtimeContractTests"
```

Then update `events.ts` until `pnpm --filter @screenhire/realtime test` passes.

## Scaling out

v1 runs one API instance, with no Redis. Two limits apply to more than one instance:

1. Self-hosted SignalR keeps connections in the process that accepted them.
2. The `realtime` queue has competing consumers: each event reaches one API instance, and so only that instance's
   clients.

The path is **Azure SignalR Service** (ADR-0007 hosts on Azure). With `AddAzureSignalR()`, connections live in the
service, and a group send from any API instance reaches every client. The second limit then stops mattering. The hub,
the groups and the client stay as they are. A Redis backplane would solve the same problem with a component we do not
otherwise run.

## Tests

| Test | Proves |
|---|---|
| `RealtimeHubTests` (integration) | with the .NET SignalR client and a signed-in cookie: an anonymous connection gets 401; a WebSocket from another origin gets 403; a user without an organization is disconnected; each organization receives its own job events and never another's; an event the worker raises (`CheckLiveUpdates` in, `LiveUpdatesChecked` out) reaches its organization only; subscribing checks the job's organization |
| `HiringRealtimeTests` (integration) | publishing a job (with its version), adding an application, moving it and publishing a template version each reach their own organization with their facts and never another's; republishing changed content pushes a second `JobPublished`, an unchanged publish none; editing a job's workflow raises `JobWorkflowChanged` with the new version to its own organization only; the worker's `IntegrityAssessed` reaches its own organization with ids and levels only; a note, a tag, a completed task and a submitted scorecard raise `ApplicationNoteAdded`, `ApplicationTagsChanged`, `ApplicationTaskCompleted` and `ScorecardSubmitted` with ids only, to their own organization only |
| `ApplicationNotesTests` (integration) | editing and deleting a note raise `ApplicationNoteEdited` and `ApplicationNoteDeleted`, and a mention raises `TeammateMentioned` with the mentioned person's id, ids only and to their own organization only |
| `EmailTests` (integration) | a send to a selection raises `ApplicationEmailed` to its own organization once each email is on the timeline, and never to another |
| `FollowUpQuestionnaireTests` (integration) | answering a follow-up questionnaire raises `QuestionnaireAnswered` with ids only, to its own organization only |
| `AutomationRealtimeTests` (integration) | a NotifyTeam automation raises `TeamNotified` and `AutomationRunFinished` to its own organization, with ids and the status only, and never to another |
| `JobCloseTests` (integration) | `POST /v1/jobs/{id}/close` closes once and raises `JobClosed` once; 404 for another organization; antiforgery and sign-in |
| `DistributionTests` (integration) | publish and close raise one `JobPostingsChanged` per change, not one per board (job-distribution.md §5) |
| `RealtimeContractTests`, `RealtimeGroupsTests` (unit) | the contract file; group names and topic routing |
| `packages/realtime` (Vitest) | backoff, reconnect and re-subscribe, 401 handling (against the real SignalR stack too), schema parsing, invalidation, the runes store |
| `LiveUpdatesTests` (Playwright) | two browser contexts in one organization: publishing and closing a job in one updates the other without a reload, over the hub's WebSocket (no fallback transport) |

`CheckLiveUpdates` exists so that a test, or an operator, can prove the worker-to-browser path without a feature that
happens to raise an event from the worker.
