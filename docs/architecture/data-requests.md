---
type: Design
title: "Data requests and record retention"
description: "Candidates' requests for a copy of their data or for its erasure, the rows and settings behind them, the shared erasure service, and the retention rule that deletes ended applications; the data model, erasure, the candidate's side of the API, the team's routes, the copy export and the retention schedule are built; the careers pages and the dashboard page follow."
tags: [privacy, erasure, retention, candidates]
sources:
  - resource: src/ScreenHire.Domain/Privacy/DataRequest.cs
  - resource: src/ScreenHire.Domain/Privacy/RetentionSettings.cs
  - resource: src/ScreenHire.Application/Privacy/PrivacyRegistration.cs
  - resource: src/ScreenHire.Application/Privacy/RetentionSelection.cs
  - resource: src/ScreenHire.Application/Candidates/CandidateErasure.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/PrivacyConfiguration.cs
  - resource: apps/worker/BlobDataExportStore.cs
  - resource: apps/api/Applications/DataExportStore.cs
  - resource: apps/api/Applications/DataRequestSetup.cs
  - resource: apps/api/Endpoints/PublicDataRequestEndpoints.cs
  - resource: apps/api/Contracts/PublicDataRequestContracts.cs
  - resource: src/ScreenHire.Application/Privacy/DataRequestLinks.cs
  - resource: src/ScreenHire.Application/Privacy/DataRequestEvents.cs
  - resource: src/ScreenHire.Application/Privacy/SendDataRequestLink.cs
  - resource: src/ScreenHire.Application/Privacy/BuildDataCopy.cs
  - resource: src/ScreenHire.Application/Privacy/CandidateDataExport.cs
  - resource: src/ScreenHire.Application/Privacy/PurgeEndedApplications.cs
  - resource: src/ScreenHire.Application/Privacy/NotifyDataRequestReceived.cs
  - resource: apps/api/Endpoints/DataRequestEndpoints.cs
  - resource: apps/api/Endpoints/RetentionEndpoints.cs
  - resource: apps/api/Contracts/DataRequestContracts.cs
  - resource: apps/careers/src/lib/data-request.ts
  - resource: apps/careers/src/actions/index.ts
  - resource: apps/careers/src/pages/[organization]/data-request/index.astro
  - resource: apps/careers/src/pages/[organization]/data-request/sent.astro
  - resource: apps/careers/src/pages/[organization]/data-request/[token].astro
  - resource: apps/careers/src/pages/[organization]/data-request/received.astro
  - resource: apps/careers/src/pages/[organization]/data-request/expired.astro
  - resource: apps/careers/src/pages/[organization]/data-request/download/[token].ts
  - resource: apps/careers/src/pages/[organization]/privacy.astro
---
# Data requests and record retention

## Built

- `data_requests`: one row per request, tenant-owned with forced row-level security. It holds no name and no address. Its
  `candidate_id` is a single-column foreign key with `ON DELETE SET NULL` (a composite tenant key would null
  `organization_id` too), so the row outlives the candidate. A partial unique index allows one open request of a kind per
  candidate. `DataRequest` carries the moves: `Decline`, `Complete`, `CopySent`, `ForgetCandidate`.
- `retention_settings`: one row per organization, `EndedApplicationsMonths` 1 to 120 or null for off (the default). Changes
  are in the settings history.
- `membership_events` gains `data_request_id` and `item_count` and the kinds `DataRequestReceived`, `DataRequestCopySent`,
  `DataRequestDeclined` and `RetentionApplied`. Ids and counts only.
- `candidates.data_request_link_sent_at`, the cooldown for the emailed link. The claim is an atomic `ExecuteUpdate`.
- `CandidateErasure` (see [candidates](candidates.md#erasure)): `EraseCandidateAsync` and `EraseApplicationsAsync`.
- `RetentionSelection.FindAsync`: the applications retention would delete.
- Options `ScreenHire:DataRequests` (`DueDays`, `LinkLifetime`, `LinkCooldown`, `ExportLifetime`, `PermitsPerMinute`,
  `ExportContainer`) and `ScreenHire:Retention` (`BatchSize`), forwarded by the AppHost to the API and the worker.

## Retention rule

A saved candidate (any saved candidates entry) is skipped entirely. Of the rest, an application is deleted when it ended
before the cutoff: its stage is Hired, Rejected or Withdrawn and it entered that stage before the cutoff; or the team kept the
candidate in mind from it before the cutoff and they have since left the saved list; or its job closed before the cutoff. A
job closed with no recorded time is left alone. A candidate is erased whole only when no application is left.

## The candidate's side

A candidate asks for a copy or for erasure in three steps, none of which needs an account.

1. **Ask for a link.** The careers page posts an address to `POST /v1/public/organizations/{slug}/data-requests/links`. The
   route validates it, runs the bot check (Turnstile; only a failed check is a 403, and an unconfigured one goes on), and
   always answers 202 with no body. It looks the address up in the organization's candidates. For a known address whose
   cooldown has passed it claims the cooldown with one `UPDATE candidates SET data_request_link_sent_at = @now WHERE id = @id AND
   (data_request_link_sent_at IS NULL OR data_request_link_sent_at < @now - LinkCooldown)`, and when that changed a row it
   queues `SendDataRequestLink` in the same transaction. An unknown address, or one inside its cooldown, runs the same
   lookup and a no-op save and queues nothing. The answer is identical either way, so it tells nobody who has applied.
   The response time is not: the claim and the queued message cost a little, which is an accepted limit.
2. **Open the link.** The worker handler issues the signed token itself (so it never sits in the outbox table) and mails the
   address through `IEmailSender` in ScreenHire's own words: what the link does, how long it works (`LinkLifetime`), and
   "If you didn't ask for this you can ignore this email." It skips quietly when the candidate is gone, the address is
   flagged as undeliverable, or the host has no sender or no careers address. `[Deduplicated]` on the key (candidate id and
   the claim's instant) stops a redelivered message sending twice.
3. **Choose.** `GET {token}` shows the organization's name; `POST {token}` with `{ "kind": "Copy" | "Erasure" }` opens a
   `DataRequest`, or returns the one already open for that kind. The candidate id comes from the token and is checked
   through the tenant-filtered candidate set, so the single-column foreign key can never point at another organization's
   candidate. A repeat click, a second device and simultaneous clicks all end in one request: the loser of the race hits the
   partial unique index, reloads the winner's row and returns it. The save also adds a `DataRequestReceived` row to the
   team log (no actor, the request id only) and publishes the `DataRequestReceived` realtime event, all in one
   transaction. The response is the request id, kind and receipt time. It never carries the due date, which is the team's
   target.

Every route that takes a token answers 404 for a token that is forged, damaged, expired, made for another organization, made
for another purpose, or names a candidate who has since been erased, so a page cannot tell them apart. All five routes are 404
where the deployment cannot send the email (`enabled` false in `GET ""`, which the careers page uses to keep its "write to
the employer" text).

`DataRequestLinks` holds the two token purposes on a time-limited protector over the shared key ring:
`ScreenHire.DataRequest.Link.v1` (organization and candidate, `LinkLifetime`) and `ScreenHire.DataRequest.Download.v1`
(organization and request, `ExportLifetime`). The expiry is inside the token. Addresses are `{careers board}/data-request/{token}`
and `{careers board}/data-request/download/{token}`.

### Download

`GET downloads/{token}` reads the download token, checks it names the slug's organization, loads the request and requires
it to be Completed with an export name and an `ExportExpiresAt` in the future, then returns `{ url }`, a read-only SAS for
the blob that lives five minutes and downloads as the attachment `your-data.json`. Anything else is a 404. The worker builds
the copy and writes the blob; the API only issues the link.

### Copy store

`IDataExportStore` has three members: `DeleteAsync`, `SaveAsync(blobName, content, contentType)` and
`IssueReadLinkAsync(blobName, fileName)`. The worker's `BlobDataExportStore` writes and deletes and refuses read links
(`NotSupportedException`). The API's `DataExportStore` issues read links and refuses the other two. `NoDataExportStore`
(a host with no storage) deletes nothing, returns no link, and throws from `SaveAsync` so a copy is never marked sent without a blob.

### Rate limit and events

The routes share the `data-requests` rate limit policy: a fixed window per visitor address, `PermitsPerMinute` a minute
(default 10), partitioned on the address the careers server forwards. `DataRequestReceived` (request id, kind, time) and
`DataRequestChanged` (request id) are `ITenantEvent`s with Zod schemas in `packages/realtime`; the dashboard does not map
either yet.

## The team's side

`/v1/data-requests` (people only, no API keys, not in the MCP server). Every route needs `Permission.EraseCandidates` and a
person who sees every job; a member on Only jobs they're on gets a 404, as on the erase route. Another organization's
request, or an unknown id, is a 404.

| Route | What it does |
|---|---|
| `GET ""` (`ListDataRequests`) | Every open request and those answered in the last 90 days, earliest due date first, with `openCount`. Each item has the kind, status, `receivedAt`, `dueAt`, `overdue`, `resolvedAt`, `copySentAt`, `declineReason` and the candidate (`id`, `name`, `email`) while they exist; `candidate` is null after an erasure. |
| `POST {id}/send-copy` (`SendDataRequestCopy`) | An open Copy request only (else 409). Publishes `BuildDataCopy` and answers 202. The request stays open until the worker has built the file. |
| `POST {id}/erase` (`EraseDataRequest`) | An open Erasure request only (else 409). Body `{ confirmation }`, the candidate's full name, ignoring case and spaces (else a validation problem). Same explicit transaction, row lock and `CandidateErasure.EraseCandidateAsync` as the erase route, with the request's id on the tombstone. The service completes this request and the candidate's other open ones. |
| `POST {id}/decline` (`DeclineDataRequest`) | An open request only (else 409). Body `{ reason }`, 1 to 500 characters once trimmed. Sets Declined, adds a `DataRequestDeclined` entry to the team log (the actor and the request id, never the reason), publishes `SendDataRequestDeclined` and `DataRequestChanged`. |

Mutations require the antiforgery token. The routes are in the `RolePermissionsTests` table. The team log's entries carry
`dataRequestId` and, for `RetentionApplied`, `itemCount`.

### The copy

`BuildDataCopy(OrganizationId, RequestId)` is an `ITenantMessage` with an idempotency key (`copy:{requestId}`) and a
`[Deduplicated]` handler. The handler returns quietly when the request is gone, not open, not a Copy, or has no candidate.
Otherwise it loads the candidate's data in a handful of queries (`DataExportLoader`), builds one JSON document with
`CandidateDataExport` (camel case, indented), writes it to the blob `{organizationId:N}/{requestId:N}.json` in the
`data-exports` container (the same name every time, so a redelivery replaces the file), calls `DataRequest.CopySent` with an
expiry of `ExportLifetime`, adds a `DataRequestCopySent` entry to the team log (no actor) and emails the candidate the
download link. The email is skipped, with a log line, when the address is flagged or the host has no email sender or careers
address; the file is kept. Only a first `CopySent` sends the email.

The file holds:

- the candidate's name, address, phone and the date they first applied;
- for each application: the job title, when it was submitted, its current stage, the privacy notice version, whether the
  candidate asked to be kept for future roles, their answers (each with its question's label from the job's form, a choice
  with its option's label, or the question's id when the form no longer has it), the names of the files they uploaded, the
  emails the organization sent, the replies received with their attachments' names, and the team's notes;
- the note on the candidate's saved entry, if there is one.

It leaves out verification output and scorecards (ADR-0004; a legal review item), tasks, the timeline and every internal id.
A note's author is shown as "Team member" and an `@` mention of a colleague as "@Team member". Files are listed by name, never
included.

`PurgeExpiredDataExports` runs daily at 03:47 UTC and fans out `PurgeOrganizationDataExports(OrganizationId, Now)`, which
deletes each expired copy's blob through `IDataExportStore.DeleteAsync`, clears the request's export columns and raises
`DataRequestChanged`. The candidate's download route already refuses a copy past `ExportExpiresAt`, so the sweep is cleanup.

### Messages to the candidate and the team

`SendDataRequestDeclined(OrganizationId, RequestId)` carries ids only. Its handler reads the reason from the request and mails it
in ScreenHire's words around the team's, with no statement about the law. It skips quietly when the candidate is gone, the
request is not Declined, the address is flagged or there is no email sender. A stable key (`declined:{requestId}`) keeps it to
one email.

`DataRequestReceived` also has a worker handler. It emails each member who holds Erase candidates and sees every job: "A candidate
asked {organization} for a copy of their data" or "to erase their data", with a link to Settings > Data requests built from the
dashboard host (`ScreenHire:Domains:AppHost`). It carries no name or address. Delivery is at least once, so a crash between a
send and the commit can repeat a notification.

## Retention

`GET /v1/organization/data-retention` (`GetOrganizationRetention`) returns `months` (null is off), `lastUpdated` (the usual
shape, "by ScreenHire" while it is the default) and `lastRun` (`at`, `applicationsRemoved`), read from the newest
`RetentionApplied` entry of the team log. `PUT` (`UpdateOrganizationRetention`, antiforgery) takes `{ months }` from 1 to 120 or
null, upserts the row and records the change in the settings history; two first saves at once end with the last one applied.
`GET .../preview?months=N` (`PreviewOrganizationRetention`) returns `{ applications, candidates }` from `RetentionSelection`
with the same cutoff the run would use. `GET .../history` (`GetOrganizationRetentionHistory`) pages the settings history. All
four need Erase candidates and a person who sees every job.

`PurgeEndedApplications` runs daily at 04:12 UTC and fans out `PurgeOrganizationEndedApplications(OrganizationId, Now)`. Each
handler reads its own setting and does nothing when it is off. Otherwise it runs `RetentionSelection.FindAsync` (the worker's
tenant has no user, so it sees every job), takes up to `BatchSize` application ids, and calls
`CandidateErasure.EraseApplicationsAsync` inside the handler's transaction. It returns the erasure's messages (files, calendar
events, duplicate checks and one `CandidateErased` event per candidate left with nothing) and, while more are waiting, itself
again. It adds a `RetentionApplied` entry with the count when the count is above zero, and nothing about the people. A rerun finds
nothing. When a run takes several batches, "Last run" shows the latest batch's count. Applications removed from a candidate who
keeps another application raise no event of their own; the list refetches when the dashboard next focuses.

## Careers pages

The pages are under `apps/careers/src/pages/[organization]/data-request/`. Each is server rendered with no client JavaScript;
the only script is Cloudflare's bot check, and only where the deployment has one. The forms are native and post to the
`requestDataLink` and `makeDataRequest` Astro Actions (`apps/careers/src/actions/index.ts`), whose logic and outcome mapping are in
`apps/careers/src/lib/data-request.ts`. Every call forwards the visitor's address, so the API's rate limit counts the visitor and
not the careers server. Links are built from the tenant base, so they are right on a tenant host.

| Page | What it does |
|---|---|
| `index.astro` | Explains in plain words what happens (a link by email if there is an application from the address, valid for about an hour, then a copy or erasure, decided by the team), then one labelled email field and "Email me a link". With `enabled` false, or any answer but 200 from `GET ""`, it shows only the old text: write to the employer through the email address they use to write to you. With a Turnstile site key it adds the widget (the script and frame hosts are added to that page's content security policy, as on the apply page) and says the check needs JavaScript. A problem shows in place and keeps the typed address; nothing personal is ever in the page's address. |
| `sent.astro` | The one answer after asking for a link: "If we have an application from that address, we have emailed a link". It is the same for an address that applied, one that did not and one in its cooldown, and the address in the URL carries nothing. |
| `[token].astro` | The emailed link. The token's shape is checked before it goes into an API path. A good link shows two radio choices (a copy; erasure, which says it cannot be undone) and a hidden token field. A forged, expired or erased-candidate link, or a malformed one, shows "This link has expired or is not valid" with a link back to the form (status 404); a busy network shows a short message (429). Never cached (`Cache-Control: no-store`), `noindex`, no referrer. |
| `received.astro` | The confirmation after choosing, reached by redirect so a refresh never posts twice. It carries no token and does not repeat the choice. It says the team will see the request; that for a copy we email a link when the file is ready; and that for erasure we cannot email afterwards because the address is erased. It promises no date. |
| `download/[token].ts` | The address in the copy email. It reads the organization from the host or path, checks the token's shape, asks `GET downloads/{token}` for the read link and answers 302 to it (only a web address is followed), with `no-store` and no referrer. Anything else is a 302 to `expired.astro`. |
| `expired.astro` | A token-free page for a link that no longer works, with a link to ask for a new one. |

The privacy page's "Your choices" section links to the form and says the team decides what to do with each request. A visitor who
cannot pass the bot check (JavaScript off, a strict privacy tool) is told to write to the employer; the form itself stays
usable and is refused only when the check is configured and fails. The notice version moved to `2026-11-05.1` with that change.

## Not built yet

The dashboard's Data requests page.
