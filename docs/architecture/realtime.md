---
type: Design
title: "Real-time updates"
description: "How an ITenantEvent reaches open dashboards: the API's own Wolverine cluster in wolverine_api, the SignalR hub at /v1/hubs/events and its tenant and job groups, the packages/realtime client and cache invalidation, teammates' changes as toasts with each member's notification settings, the C# to Zod contract test, and scaling out."
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
  - resource: apps/dashboard/src/lib/teammate-changes.ts
  - resource: apps/dashboard/src/lib/teammate-toasts.ts
  - resource: apps/dashboard/src/lib/notifications.svelte.ts
  - resource: apps/dashboard/src/lib/components/notification-tray.svelte
  - resource: apps/dashboard/src/lib/components/notification-settings-sheet.svelte
  - resource: apps/api/Endpoints/NotificationSettingsEndpoints.cs
  - resource: src/ScreenHire.Domain/Organizations/NotificationSettings.cs
  - resource: tests/ScreenHire.UnitTests/Realtime/RealtimeContractTests.cs
---
# Real-time updates

The recruiter dashboard updates live. A job published or closed, and later a new application, a stage move or a
finished assessment, appears in every open tab of that organization that sees the job without a reload, and nowhere
else. Any message
that implements `ITenantEvent` is pushed. A feature task defines the event and its Zod schema, and never touches the
hub. The procedure is the [`/add-realtime-event`](../../.claude/skills/add-realtime-event/SKILL.md) skill.

## Path of an event

```
API endpoint or worker handler
  publishes JobPublished (ITenantEvent)            outbox, same transaction as the change
        │
        ├──▶ Postgres queue "worker"    ──▶ worker handlers (if any; otherwise marked handled)
        └──▶ Postgres queue "realtime"  ──▶ API: TenantEventPusher<JobPublished>
                                               └─▶ SignalR groups org:{id}:all-jobs and org:{id}:team:{jobId}
                                                   for an IJobEvent, org:{id}:{topic}:{recordId} for an
                                                   ITopicEvent, org:{id} for anything else  ──▶ browsers
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
  therefore resolves the user's organization and job access itself (`Memberships.ResolveTenantAsync`). On connect the
  connection joins `org:{organizationId}`. A signed-in user without an organization is disconnected.
- **Job groups** (ADR-0015). A connection of a member who sees all jobs also joins `org:{organizationId}:all-jobs`; one
  of a member on Only jobs they're on joins `org:{organizationId}:team:{jobId}` for each job whose hiring team they are
  on. A connection is in one of the two kinds, never both, so an event sent to both arrives once, and neither name is a
  record topic. Every tenant event that carries a job id implements `IJobEvent` and goes to the all-jobs group and its
  job's team group (`RealtimeGroups.For`, held by `RealtimeGroupsTests`); one with no job, such as
  `HiringTemplateVersionPublished`, goes to `org:{organizationId}`. `TeamNotified` and `TeammateMentioned` are job
  events too, so a notification card never tells anyone about a job they cannot see.
- **Subscriptions.** `Subscribe(topic, id)` and `Unsubscribe(topic, id)` add a connection to one record's group,
  `org:{organizationId}:{topic}:{id}`. `RealtimeTopics.Exists` checks the record as the caller, so another
  organization's record, and a job outside a member's hiring teams, fails exactly like a missing one. Today the only
  topic is `job`. `application` arrives with the applications entity, which the hub cannot validate yet. An event that
  implements `ITopicEvent` goes only to its record's group, for chatty per-record updates.
- **One direction.** The hub has no methods that change data. Changes stay REST, with antiforgery.

Group names always start with the organization, so no group a client can join spans two tenants. Removing someone
from an organization closes their connections into it at once: `RealtimeConnections` lists this process's
connections by user and organization, and the remove route aborts theirs. A connection keeps the groups it joined at
connect, so switching organizations (`POST /v1/auth/switch-organization`) aborts the user's connections into the one
they left, on every device, and each client reconnects into the new one.
Accepting an invitation also makes the new organization the active one but closes nothing: a dashboard already
connected as that user follows it on its next reconnect.

A change to the jobs a member sees moves their connections too (`RealtimeConnections`). Joining a hiring team, or
creating a job, adds the job's team group to the open connections of a member on Only jobs they're on, before the save,
because the save's own `HiringTeamChanged` can reach the hub before the request runs on; if the save fails, their
connections close. Leaving a team, a change of job access, or a role change across Owner or Admin closes their
connections, and each client reconnects into the right groups and refetches. `HiringTeamChanged` refetches the jobs
and the Team page's counts, so a person just added sees the job appear.

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
  `ApplicationMoved` to `/v1/jobs`, `/v1/applications`, `/v1/candidates` and `/v1/reports`), and invalidates every TanStack Query
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

## Who made a change

An event a person causes carries `ActorUserId`: the member who made the change, or null when no member did (an API
key, an automation, the candidate, the demo's setup). `ApplicationMoved`, `ApplicationSubmitted` (a candidate added by
hand), `ApplicationNoteAdded`, `ApplicationTagsChanged`, `ScorecardSubmitted`, `ApplicationEmailed`, the three task
events and `HiringTemplateVersionPublished` have it, and `TeammateMentioned` has its `AuthorUserId`. The task events
also carry `AssigneeUserId` (null for anyone on the hiring team), and `HiringTemplateVersionPublished` carries
`PreviousEditorUserId`: the member who changed the template last, read from its change history before the publish
(`ChangeHistoryQueries.LastAsync`), and null when that was the publisher, an API key or ScreenHire. They are ids, like everything else in an
event. Each was added last with a null default, so an envelope queued before it still deserializes, and the Zod side
reads a missing one as no member.

## Teammates' changes as toasts

While someone has the dashboard open, a teammate's change to something they work on shows as a toast in the shell's
notification tray (design system section 11, Notification tray): who did what, with the record as a link. The shell
feeds every event to `TeammateToasts` (`apps/dashboard/src/lib/teammate-toasts.ts`) through `LiveUpdates.onEvent`;
`changeFor` (`teammate-changes.ts`) decides, from ids alone:

| Kind | Event | For the person when |
|---|---|---|
| Moves, New candidates, Notes, Tags, Emails, Scorecards | `ApplicationMoved`, `ApplicationSubmitted`, `ApplicationNoteAdded`, `ApplicationTagsChanged`, `ApplicationEmailed`, `ScorecardSubmitted` | the job is one whose hiring team they are on (`GET /v1/organization/members/{memberId}/jobs`); seeing the job is not enough (ADR-0015) |
| Tasks | `ApplicationTaskCreated`, `ApplicationTaskCompleted`, `ApplicationTaskReopened` | the task is assigned to them |
| Templates | `HiringTemplateVersionPublished` | they are its `PreviousEditorUserId` |

Every toast needs an `ActorUserId` that is not the person's own. A change by an API key, an automation or the candidate
never toasts: it is not a teammate's, and a key may be the person's own AI assistant. Any event the table does not name
never toasts, so a verification result (`IntegrityAssessed`) cannot. Edits to a hiring template's draft raise no event,
so only a publish toasts. Email and scorecard template edits raise no event, so they do not toast yet.

Each change waits one second under its kind, its actor and its job before anything is read: one becomes a toast about
the candidate, read with `GET /v1/applications/{id}`; several become one toast about the job, so a bulk move or a bulk
email makes one toast and no request per candidate. A `TeammateMentioned` for the person drops a waiting note toast
from the same author on the same application: the mention card already says it. Reads go through the shell's query
cache, so a burst of events reads the team once, and a read that fails drops the toast.

Each member picks what they get in the Notifications sheet, from the account menu: toasts on or off, each kind, and
whether toasts close on their own. The settings are on their membership (`organization_members.notifications`, JSONB,
`NotificationSettings`), so each organization keeps its own and they follow the person to another browser. They hold
the kinds turned off, so a kind added later starts on.

| Route | Who | What it does |
|---|---|---|
| `GET /v1/notification-settings` | The signed-in member | Their settings in the current organization; everything on until they change it. |
| `PUT /v1/notification-settings` | The signed-in member | Replaces them. A kind named twice in `off` is a 400. Session only, with antiforgery; no permission, because it changes only the caller's own membership. |

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
| `HiringRealtimeTests` (integration) | publishing a job (with its version), adding an application, moving it and publishing a template version each reach their own organization with their facts and never another's; republishing changed content pushes a second `JobPublished`, an unchanged publish none; editing a job's workflow raises `JobWorkflowChanged` with the new version to its own organization only; the worker's `IntegrityAssessed` reaches its own organization with ids and levels only; a note, a tag, a task made by hand, a completed and a reopened task and a submitted scorecard raise `ApplicationNoteAdded`, `ApplicationTagsChanged`, `ApplicationTaskCreated`, `ApplicationTaskCompleted`, `ApplicationTaskReopened` and `ScorecardSubmitted` with ids only, to their own organization only. `TalentPoolChanged` (keeping someone in mind, leaving the pool, an invitation) carries ids only and refreshes the pool and the lists ([talent pool](talent-pool.md)) |
| `ApplicationNotesTests` (integration) | editing and deleting a note raise `ApplicationNoteEdited` and `ApplicationNoteDeleted`, and a mention raises `TeammateMentioned` with the mentioned person's id, ids only and to their own organization only |
| `EmailTests` (integration) | a send to a selection raises `ApplicationEmailed` to its own organization once each email is on the timeline, and never to another |
| `FollowUpQuestionnaireTests` (integration) | answering a follow-up questionnaire raises `QuestionnaireAnswered` with ids only, to its own organization only |
| `AutomationRealtimeTests` (integration) | a NotifyTeam automation raises `TeamNotified` and `AutomationRunFinished` to its own organization, with ids and the status only, and never to another |
| `JobCloseTests` (integration) | `POST /v1/jobs/{id}/close` closes once and raises `JobClosed` once; 404 for another organization; antiforgery and sign-in |
| `DistributionTests` (integration) | publish and close raise one `JobPostingsChanged` per change, not one per board (job-distribution.md §5) |
| `RealtimeContractTests`, `RealtimeGroupsTests` (unit) | the contract file; group names, topic and job routing, and every event with a job id being an `IJobEvent` |
| `packages/realtime` (Vitest) | backoff, reconnect and re-subscribe, 401 handling (against the real SignalR stack too), schema parsing, invalidation, the runes store |
| `JobScopeRealtimeTests` (integration) | a member on Only jobs they're on receives events about their team's job and none about another, `TeamNotified` and `TeammateMentioned` included; subscribing to a hidden job fails; joining a team starts its events without a reconnect; leaving it, or a change of job access, closes the connection |
| `TeammateChangesRealtimeTests` (integration) | a person's move, note, tags, scorecard, task create, complete and reopen, and email carry their `actorUserId`, and the task events `assigneeUserId`; the same move by an API key carries none; publishing a template another member last changed carries both ids; each reaches its own organization only |
| `NotificationSettingsTests` (integration) | the defaults, saving and reading back, each member and each organization keeping their own, a kind named twice, antiforgery, and an API key refused |
| `teammate-changes.test.ts`, `teammate-toasts.test.ts`, `notifications.test.ts` (Vitest) | which changes toast and their words, grouping, a mention's note left to its card, the shared limit of three, and the countdown and what holds it |
| `TeammateToastsTests` (Playwright) | a recruiter's move shows as a toast in the owner's dashboard with its link, the owner's own move does not, turning Moves off in the sheet stops it, and on a phone the toast ends above the jobs list's action bar |
| `LiveUpdatesTests` (Playwright) | two browser contexts in one organization: publishing and closing a job in one updates the other without a reload, over the hub's WebSocket (no fallback transport) |

`CheckLiveUpdates` exists so that a test, or an operator, can prove the worker-to-browser path without a feature that
happens to raise an event from the worker.
