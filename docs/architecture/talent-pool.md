---
type: Design
title: "Saved candidates"
description: "Saving a strong candidate: the application ends without a rejection and keeps everything, only with the candidate's own permission for future roles from the apply form, with no end date; the Saved candidates page with Rating, Last contacted, Message and a note the team can edit, inviting someone to apply for another job or adding them to one, the signed link to stop being kept, job access, and what is not built."
tags: [talent-pool, consent, applications]
sources:
  - resource: src/ScreenHire.Domain/Applications/JobApplication.cs
  - resource: src/ScreenHire.Domain/TalentPool/TalentPoolEntry.cs
  - resource: src/ScreenHire.Application/TalentPool/TalentPoolMembership.cs
  - resource: src/ScreenHire.Application/TalentPool/TalentPoolLinks.cs
  - resource: src/ScreenHire.Application/TalentPool/SendTalentPoolInvitation.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/TalentPoolConfiguration.cs
  - resource: src/ScreenHire.Infrastructure/Email/Templates/talent-pool-invitation.body.liquid
  - resource: apps/api/Endpoints/TalentPoolEndpoints.cs
  - resource: apps/api/Contracts/TalentPoolContracts.cs
  - resource: apps/api/Applications/ApplicationSubmission.cs
  - resource: apps/careers/src/pages/[organization]/talent-pool/[token].astro
  - resource: apps/careers/src/lib/talent-pool.ts
  - resource: apps/dashboard/src/lib/components/saved-candidates.svelte
  - resource: apps/dashboard/src/lib/talent-pool.ts
  - resource: apps/dashboard/src/lib/components/save-candidate-sheet.svelte
  - resource: apps/dashboard/src/lib/components/edit-saved-note-sheet.svelte
  - resource: apps/dashboard/src/lib/components/close-job-dialog.svelte
  - resource: tests/ScreenHire.UnitTests/Applications/TalentPoolTests.cs
---
# Saved candidates

A runner-up is often the next hire (roadmap item 7). Before this, the only ways out of a job were Hired, Rejected,
Withdrawn, or sitting in a closed job. **Save candidate** is a fourth: the candidate leaves the job without a rejection,
and everything on the application stays with it (answers, files, scorecards, notes, Verification results and Activity).
An interview offered or booked on it is cancelled ([interview scheduling](interview-scheduling.md#when-an-interview-is-cut-short)).
They join the organization's **saved candidates**, where the team can write to them, invite them to apply for another
job or add them to one.

A saved candidate has no end date. They stay until the team removes them or the candidate uses the link in an
invitation to stop it. The product used to end each permission after a period (12 months by default); that period, its
setting and the daily sweep that enforced it are gone.

The names in code and on the wire are the older ones: `KeptInMind` is the event type, `KeptInMindAt` the column,
`/v1/talent-pool` the route family, `TalentPoolChanged` the realtime event and `/{organization}/talent-pool/{token}` the
stop link (it is already in sent emails). Only labels changed. `TalentPoolExit.Expired` stays in the enum so old
Activity rows still read, but nothing writes it any more.

Keeping someone's data for future roles needs their permission in the UK and EU. So only a candidate who said so on the
apply form can be saved. Whether keeping their details with no end date is acceptable in each market is an owner
action: it needs a legal review ([status](../status.md#owner-actions)).

## Permission for future roles

The apply form's review step has an optional, unticked box, "Keep my details for future roles", with one line saying
that the employer keeps the details until the candidate asks, or the employer removes them, and that every invitation
has a link to stop ([apply flow](apply-flow.md) §2). The careers server sends `keepForFutureRoles`, and the API stores the
permission **on that application**, never on the candidate:

| `applications` column | What |
|---|---|
| `future_roles_consent_given_at` | When they ticked it (the application's submission time) |
| `future_roles_withdrawn_at` | When they took it back, a person removed them at their request, or (for a permission given before the period went) it ran out |

A careers form never changes an existing candidate, because anyone can type any email address
(`ApplicationSubmission`), so a stranger cannot extend someone's permission. A tick on one application covers that
application only. An unticked box on a later application withdraws nothing.

The permission has no period and no end date. `JobApplication.AllowsFutureRoles` is true once it was given and until it
is withdrawn, and application reads carry it as `allowsFutureRoles`. (It replaced `futureRolesUntil` and the
`future_roles_consent_months` column.) `futureRolesGivenAt` and `futureRolesWithdrawnAt` stay.

Removing the period does not bring back a permission that had already run out. The migration
`SavedCandidatesWithoutExpiry` applies the old rule one last time: a permission past the shorter of its own period and the
organization's gets `future_roles_withdrawn_at` set to the day it ended, and anyone still saved on one leaves the list
with `Expired` on their Activity. A permission still running when the migration ran now has no end date, although the form
that collected it named a period. That is part of the legal review.

## Saving

`JobApplication.KeepInMind` sets `KeptInMindAt` and returns a `KeptInMind` timeline entry whose note says why. It refuses
an application that has ended or has no permission that holds. The application **keeps its stage**: a stage kind would
have meant rewriting every job's frozen workflow, the validator and the templates. Instead one predicate,
`JobApplication.HasEnded(stage)`, counts a Hired, Rejected or Withdrawn stage or `KeptInMindAt`, and everything that
skips an ended application uses it:

- automations do not move, reject or send a questionnaire to it (`ExecuteAutomationActionHandler`), and its stage's
  timers do nothing (`RunAutomationsHandler`);
- the stage-change email skips it (`ApplicationEmails.Skips`);
- a follow-up questionnaire link says the questions are no longer open;
- `slaDueAt` is null, so it is never overdue.

The dashboard reads `keptInMindAt` through `#lib/talent-pool.ts`. A saved application shows the **Saved** badge (a
bookmark icon, the word and a colour) where its stage was, counts in a job board's Closed column as "Saved", in a row of
its own that opens in place to list those candidates, and drops out of the stage views and filters.

**Moving it back is the undo.** `MoveTo` on a saved application clears `KeptInMindAt` and records a `StageChanged`, even
to the stage it was in, and the API takes the candidate out of the saved candidates. That is how a rejection is undone too.

Where people save someone (`POST /v1/talent-pool`, Move candidates, up to 200 applications, one note):

- **The candidate page:** the last item of Move to stage, beside Rejected, opening the Save candidate sheet with the
  note. Move to stage stays the page's one primary. It shows only for an open application whose permission holds (A3).
  The Application tab shows the candidate's answer ("Allowed", or "Allowed, then stopped").
- **A job's selection bar:** Save candidate beside Move. Hidden while a Verification filter or status view picked the
  rows, as bulk Rejected and Email are, because it ends a candidacy on the job (ADR-0004).
- **Closing a job:** the close dialog offers "Also save the N candidates who allowed it", which saves them before the
  job closes. Saving only some of them is the selection bar's job, before closing.

The response counts who was saved, who did not allow it and who had already left. Nothing here reads Verification
output, and no automation action saves anyone (ADR-0004). Saving a candidate who is already saved from another
application replaces the entry, and the note is the new request's note.

## The list

`talent_pool_entries` holds one row per saved candidate: the application they were saved from, the `KeptInMind` entry,
who saved them, their last invitation and the **note**. The entry holds no other candidate data: the tags are the
application's, and whether they stay comes from the application's permission.

The entry belongs to its application (`IApplicationScoped`), so it has the restrictive `job_scope` policy: a member on
Only jobs they're on sees the people saved from their own jobs (ADR-0015). It cascades with the application and the
candidate.

`GET /v1/talent-pool` lists everyone saved whose permission still holds. **Saved candidates** is the Saved choice on the
Candidates page, beside its title (G then S goes to `/candidates?list=saved`; `/saved` and `/talent-pool` redirect there).
It is the list template: search over name, email, job
and note; Saved from and Tag filters; columns Candidate, Rating, Note, Tags, Saved from, Saved and Last contacted; and a
row menu with Invite to apply, Add to a job, Message, Edit note and Remove from saved. There is no Verification
column or filter, so the list never becomes one picked by level for bulk invitations (ADR-0004).

**Rating** is the saved-from application's rating, read by the same code as the candidate page (`RatingReads`). A
reviewer who has a scorecard to give on it sees "After yours" instead of the number, as on the candidate page, so
the list leaks nothing the gate hides.

**Last contacted** is the newest `EmailSent` (an email to them) or `EmailReceived` (their reply) event over every
application of the candidate that the reader can see, whichever job it was for (the same source as the candidate's
Messages). The event types counted are one list in `TalentPoolReads` (`ContactTypes`). Its limits:

- A member on Only jobs they're on sees only email through jobs they are on, so their date can be older than
  another member's.

**Message** in the row menu opens the email composer for the saved-from application. It shows only for a reader who
may email candidates, with a sending-enabled sender.

**The note** starts as the one written when they were saved and the team can edit it: Edit note in the row menu, or
press the note. `PUT /v1/talent-pool/{candidateId}/note` (Move candidates, 404 without an entry the reader can see)
replaces it, and a blank note clears it. The edit goes through the change tracker, so it is recorded as a
`TalentPoolEntry` change with before and after, shown as History on the sheet and as "last updated" under the note
(`GET /v1/talent-pool/{candidateId}/history`, [change history](change-history.md)). The History is keyed by the entry's
own id, not the candidate's. Saving the candidate again from another job replaces the entry, which starts a fresh note
and a fresh History, so a member on Only jobs they're on never reads edits made under a job they cannot see. The
timeline stays append-only: the Activity entry for saving keeps the note as first written, and later thoughts show in the
note's History.

**Leaving the list** deletes the entry and records why on the saved application's Activity (`LeftTalentPool`, with
`poolExit` and, where it applies, the other job and application):

| `poolExit` | When |
|---|---|
| `Expired` | Legacy. Written by the retired daily sweep and, once, by the migration that removed it; old rows still read |
| `Removed` | A person took them out |
| `CandidateAsked` | The candidate stopped it from their link, or a person removed them at their request, which also withdraws the permission |
| `AddedToJob` | A person added them to a job (from the list or by hand) |
| `AppliedAgain` | They applied for any job on the careers site |
| `BackInJob` | A person moved the saved application back to a stage |
| `KeptFromAnotherJob` | A person saved them from another application, which now stands for them |

A member on Only jobs they're on who adds a candidate by hand cannot see an entry saved from another job, so that entry
stays until it ends another way.

## Invitations

Invite to apply (`POST /v1/talent-pool/invitations`, Email candidates, open jobs only, hidden without an email sender)
queues one `SendTalentPoolInvitation` per person through the outbox, carrying ids only. The worker checks again that
they are still saved with a permission that holds and that the job is open. It renders the organization's wording
of the built-in `talent-pool-invitation` template ([email](email.md)), with:

- `{{ jobTitle }}`, the job they are invited to;
- `{{ jobLink }}`, that job's public careers page, with no token or personal data;
- `{{ removeLink }}`, the signed link to stop being kept.

It sends nothing to an address the email provider flagged as bounced or marked as spam. It sends the email, then keeps the copy for Messages with "[the candidate's link, not kept in this copy]" in place of
the remove link, because that link acts for the candidate. It records `InvitedToApply` and `EmailSent` on the saved
application, so an invitation also moves Last contacted. Only Invite to apply sends this template: the email composer
never offers it, and no automation names it. Its wording is edited on Templates, not per send. The sheet previews it
(`POST /v1/talent-pool/invitations/preview`).

The candidate applies through the ordinary apply form. Their email finds the same candidate, so the new application
takes them out of the saved candidates (`AppliedAgain`). The candidate page's **Other applications** panel lists every
application of the candidate the reader can see, so each one links to the others.

**Add to a job** (`POST /v1/talent-pool/applications`, Add candidates, any job that is not closed) gives each person an
application in the job's first stage, as a recruiter adding them by hand does. It carries the saved application's
permission.

## Stopping

`{{ removeLink }}` opens `/{organization}/talent-pool/{token}` on the careers site. The page is one native form with no
JavaScript, `no-store`, `noindex` and `no-referrer`. The token is the organization and the candidate, protected with
Data Protection under its own purpose (`TalentPoolLinks`), like a questionnaire link. It never expires: stopping should
always work, and stopping is all it can do. `POST /v1/public/organizations/{slug}/talent-pool/{token}` withdraws every
permission the candidate gave the organization and takes them out of the saved candidates, in one transaction. It lands
on `/talent-pool/stopped`, with no token in the address. A forged token, or one for another organization, is a 404.

A candidate can also answer the invitation: it carries the same reply address as other email about an application, and the
reply lands in Messages ([email](email.md), "Replies"). The link stays, because a link works without writing anyone
(GDPR Art. 7(3)).

## Not built

- **Erasure.** An Owner or Admin can erase a candidate from their profile ([candidates.md](candidates.md)). The entry
  cascades from the candidate, so erasing removes them from the list, and the erasure deletes the note's History from
  `change_records`, including that of entries that had already left the list ([candidates.md](candidates.md)). A candidate's
  own request to stop being kept deletes nothing; the profile then offers Erase candidate. A candidate can also ask for
  a copy or for erasure themselves ([data requests](data-requests.md)), and the team answers it. A person removes them from
  the list with "They asked to be removed", which also withdraws the permission. When someone
  leaves the list and is not erased, the note's History stays: nobody can read it, and nothing links it to the candidate.
- **Record retention.** An organization can delete ended applications after a number of months ([data
  requests](data-requests.md#retention-rule)), off by default. The run skips a saved candidate entirely, so nothing expires
  a saved candidate: their details stay until a person removes them or the candidate stops it. Once they leave the list,
  their ended applications count again.
- **A legal review** of keeping details with no expiry, and of the apply form and privacy page wording per market, is an
  owner action ([status](../status.md#owner-actions)).
- **API keys, MCP and webhooks.** The routes take no API key and there is no webhook event for it. The MCP
  server's `list_saved_candidates` tool reads the list, for any key with the Read scope, through `TalentPoolReads` (the
  same read the list route uses, ADR-0021 decision 6), including each person's rating and last contacted. It saves,
  removes and invites no one, edits no note, and nothing else about the list reaches a key.
