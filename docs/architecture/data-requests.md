---
type: Design
title: "Data requests and record retention"
description: "Candidates' requests for a copy of their data or for its erasure, the rows and settings behind them, the shared erasure service, and the retention rule that deletes ended applications; the data model and erasure are built, the public flow, the team page and the schedule follow."
tags: [privacy, erasure, retention, candidates]
sources:
  - resource: src/ScreenHire.Domain/Privacy/DataRequest.cs
  - resource: src/ScreenHire.Domain/Privacy/RetentionSettings.cs
  - resource: src/ScreenHire.Application/Privacy/PrivacyRegistration.cs
  - resource: src/ScreenHire.Application/Privacy/RetentionSelection.cs
  - resource: src/ScreenHire.Application/Candidates/CandidateErasure.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/PrivacyConfiguration.cs
  - resource: apps/worker/BlobDataExportStore.cs
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

## Not built yet

The public request flow on the careers sites, the Data requests page, the copy export, the retention schedule and its
settings routes.
