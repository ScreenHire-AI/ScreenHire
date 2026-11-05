---
type: Design
title: "Candidates across jobs"
description: "One person is one candidate in an organization however many jobs they apply to: matching by email, the profile page with every application and the history across them, the Also applied to link, keeping duplicate checks in step between applications, and erasing a candidate with a typed confirmation and a tombstone in the team log."
tags: [candidates, applications, erasure, privacy]
sources:
  - resource: src/ScreenHire.Domain/Candidates/Candidate.cs
  - resource: src/ScreenHire.Domain/Organizations/MembershipEvent.cs
  - resource: src/ScreenHire.Domain/Organizations/RolePermissions.cs
  - resource: src/ScreenHire.Application/Candidates/CandidateErased.cs
  - resource: src/ScreenHire.Application/Candidates/CandidateErasure.cs
  - resource: src/ScreenHire.Domain/Privacy/DataRequest.cs
  - resource: src/ScreenHire.Application/Applications/EraseApplicantFiles.cs
  - resource: src/ScreenHire.Application/Verification/RecheckApplicationDuplicates.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/CandidateConfiguration.cs
  - resource: apps/api/Applications/ApplicationSubmission.cs
  - resource: apps/api/Endpoints/CandidateEndpoints.cs
  - resource: apps/api/Contracts/CandidateContracts.cs
  - resource: apps/worker/BlobApplicantFileStore.cs
  - resource: apps/dashboard/src/routes/(app)/candidates/[candidateId]/+page.svelte
  - resource: apps/dashboard/src/lib/candidate-profile.ts
  - resource: apps/dashboard/src/lib/components/candidate/header-cards.svelte
  - resource: apps/dashboard/src/lib/components/erase-candidate-dialog.svelte
  - resource: apps/dashboard/src/lib/components/confirm-dialog.svelte
---
# Candidates across jobs

A person who applies to several jobs, perhaps a year apart, is one candidate.

## Matching

A candidate is one row per organization and email address: `NormalizedEmail` (the address trimmed and lower-cased) has a
unique index with the organization. Both ways in go through `ApplicationSubmission.SubmitAsync`: the careers site's
apply form and a recruiter adding someone by hand. It looks the candidate up by that key, ignoring job access (a member
on Only jobs they're on who adds someone who applied elsewhere attaches the new application to that candidate, ADR-0015)
and creates one only when there is none. A submission never changes an existing candidate's name or phone, because anyone
can type any address into a form. A candidate has at most one application per job.

No migration merges duplicates: the unique index has existed since the table did (`AddHiringModel` created
`normalized_email` as `NOT NULL` with the index), so none can exist. Two addresses that differ beyond case and
surrounding spaces, such as `a+tag@` and `a@`, stay two candidates, and the duplicate check flags them for a person to
look at.

## The profile

`/candidates/{id}` shows who they are (name, contact), every application as a sortable table that opens in the order they were made (the job, its stage or how it ended, when they
applied, how far it got, when it ended) and their history across all of them. "How far it got" is the stage an ended
application was in before its last move, shown once it has ended; while it is open its stage is the Stage column. "How it
ended" is Hired, Rejected, Withdrawn or Saved, with the date.
Both come from `GET /v1/candidates/{id}` and `GET /v1/candidates/{id}/history`. The history is every timeline entry of
every application the reader may see, newest first, each with its job. It has no verification output (ADR-0004) and no
scorecard scores or recommendations. Both routes are for people: the `/v1/candidates` group the hiring API shares with
organization API keys does not carry them, and no MCP tool reads the history.

Each application's page links to the profile as **Also applied to**, with the number of other jobs the reader sees.
It is the third mini card in the header (ADR-0023). Once someone scored the candidate that card is Rating, and its second line links to the profile in place of the count.

The history shows tags and notes as the team wrote them, the same as an application's own activity. A team that
tags from a verification condition in an automation therefore sees that tag in the history too. That is the team's
own action, not a ScreenHire verdict, so it is not filtered.

## Duplicate checks stay in step

The duplicate check of an earlier application re-runs when a later application shares a detail with it. The mechanism,
the review log entry and the limits are in [verification-engine.md](verification-engine.md) §10.2.

The review log entry names no other application, job or candidate. Its text can carry a count ("2 other applications"),
and that count is over the whole organization, so a member on Only jobs they're on may see a number that includes jobs
they cannot open. The duplicate signal on the application already shows the same count, so the entry adds nothing new.
Wording both without a number, or counting only the jobs the reader sees, is the alternative if that ever matters.

## Erasure

`POST /v1/candidates/{id}/erase` needs the **Erase candidates** permission (Owners and Admins, ADR-0014), a person who
sees every job (a member on Only jobs they're on gets a 404), the antiforgery token, and the candidate's full name as
`confirmation`. The dashboard asks the person to type the name; the server checks it again (a 400 otherwise).

The route opens the transaction, takes the candidate's row lock, checks the typed name and calls `CandidateErasure`
(`src/ScreenHire.Application/Candidates`), which opens no transaction and publishes nothing: it deletes through the
route's context and returns the messages, which the route sends through its outbox in the same save. The record
retention run calls the same service from the worker. In that one transaction, with the candidate's row locked so an
application arriving now waits and cannot go with the cascade while its files are missing from the list below:

- the candidate row is deleted. The foreign keys cascade to every application and everything under it: answers, file
  rows, notes, emails, replies and their attachment rows, tasks, scorecards, questionnaires, automation runs, the saved
  candidate entry, and all verification data. The application role cannot delete from the append-only tables (events, review log, signals) itself; the
  cascade does it, with the database's rights;
- drafts saved to finish an application, which are keyed by the address and have no foreign key to the candidate, are
  deleted;
- `erase_saved_note_history` deletes the history of their saved notes from `change_records`. That history holds the
  note's free text and is keyed by the entry alone, so an entry that left the list leaves no link to the candidate. The
  function keeps the records of every other candidate's entry and deletes the rest, which also takes the orphans of other
  people who left the list; nobody can read those. It runs with the owner's rights and only for the current tenant,
  because `screenhire_app` has no `DELETE` on the table ([change history](change-history.md#isolation));
- the tombstone is a `CandidateErased` entry in the team log (`membership_events`): who and when, nothing about the
  candidate, not even their id;
- `EraseApplicantFiles` carries the blob names to the worker, which deletes the uploads and the attachments of the
  candidate's replies, each from its own container. The names can hold a file name, so the message lives only until the
  worker has handled it;
- `RemoveInterviewEvents` carries the ids and times of the interview events on interviewers' calendars, because an event
  holds the candidate's name and address and the rows that say where it is go with the candidate. The route reads them
  under the interviewers' locks, and the worker deletes each event without notifying guests where the provider allows it
  ([interview scheduling](interview-scheduling.md#when-an-interview-is-cut-short));
- the candidate's data requests keep their rows with no candidate: the foreign key sets `candidate_id` to null, the copy's
  blob name and any decline reason are cleared, an open request is completed (erasing answers it), and `EraseDataExports`
  names the copies' blobs for the worker to delete. A request that caused the erasure puts its id on the tombstone;
- `RecheckApplicationDuplicates` is sent for every application that shared details with the erased ones, so no level
  keeps resting on someone who is gone; and `CandidateErased` tells open dashboards to drop them.

`EraseApplicationsAsync` deletes just the applications it is given and erases the candidate whole only when none is left. A
saved candidate is skipped. Its interview-events message carries the application's id as cause, so a later whole erasure
is not dropped as a repeat.

Messages already queued for an erased application (analysis, scans, automations, a webhook or Slack post) find nothing
and finish without error instead of retrying into the dead-letter table.

Two stops lead here. A candidate who uses the stop link in an invitation from Saved candidates comes off the list and
the profile then says so, with a pointer to **Erase candidate** (the link itself deletes nothing). A privacy request from a
candidate arrives by the email address the employer writes from, as the careers privacy page says; there is no intake form
for it yet, and the team erases from the profile.

Webhook delivery bodies carry ids and facts, never a name or an address, and Slack delivery records hold idempotency
keys only, so there is nothing of the candidate in them to sweep. What a message already posted to a company's real
Slack channel said is outside ScreenHire, and erasure cannot take it back.

## Not built

- Editing a candidate's name, email or phone.
- Merging two candidates the team knows are one person.
- A request form for candidates to ask for their data or its erasure.
