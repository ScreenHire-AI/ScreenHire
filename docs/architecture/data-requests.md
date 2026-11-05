---
type: Design
title: "Data requests and record retention"
description: "Candidates' requests for a copy of their data or for its erasure, the rows and settings behind them, the shared erasure service, and the retention rule that deletes ended applications; the data model, erasure and the candidate's side of the API are built; the careers pages, the team page, the copy export and the schedule follow."
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

## Not built yet

The careers pages, the Data requests page and its admin routes, the copy export, and the retention schedule and its
settings routes.
