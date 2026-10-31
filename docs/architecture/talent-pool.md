---
type: Design
title: "Talent pool"
description: "Keeping a strong candidate in mind: the application ends without a rejection and keeps everything, only with the candidate's own permission for future roles from the apply form, which runs out after the organization's period (12 months by default, 6 to 24); the Talent pool page, inviting someone to apply for another job or adding them to one, the signed link to stop being kept, the daily expiry sweep, job access, and what is not built."
tags: [talent-pool, consent, applications]
sources:
  - resource: src/ScreenHire.Domain/Applications/JobApplication.cs
  - resource: src/ScreenHire.Domain/TalentPool/TalentPoolEntry.cs
  - resource: src/ScreenHire.Domain/TalentPool/TalentPoolSettings.cs
  - resource: src/ScreenHire.Application/TalentPool/TalentPoolMembership.cs
  - resource: src/ScreenHire.Application/TalentPool/TalentPoolLinks.cs
  - resource: src/ScreenHire.Application/TalentPool/SendTalentPoolInvitation.cs
  - resource: src/ScreenHire.Application/TalentPool/TalentPoolExpiry.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/TalentPoolConfiguration.cs
  - resource: src/ScreenHire.Infrastructure/Email/Templates/talent-pool-invitation.body.liquid
  - resource: apps/api/Endpoints/TalentPoolEndpoints.cs
  - resource: apps/api/Contracts/TalentPoolContracts.cs
  - resource: apps/api/Applications/ApplicationSubmission.cs
  - resource: apps/careers/src/pages/[organization]/talent-pool/[token].astro
  - resource: apps/careers/src/lib/talent-pool.ts
  - resource: apps/dashboard/src/routes/(app)/talent-pool/+page.svelte
  - resource: apps/dashboard/src/routes/(app)/settings/talent-pool/+page.svelte
  - resource: apps/dashboard/src/lib/talent-pool.ts
  - resource: apps/dashboard/src/lib/components/keep-in-mind-sheet.svelte
  - resource: apps/dashboard/src/lib/components/close-job-dialog.svelte
  - resource: tests/ScreenHire.UnitTests/Applications/TalentPoolTests.cs
---
# Talent pool

A runner-up is often the next hire (roadmap item 7). Before this, the only ways out of a job were Hired, Rejected,
Withdrawn, or sitting in a closed job. **Keep in mind** is a fourth: the candidate leaves the job without a rejection,
and everything on the application stays with it (answers, files, scorecards, notes, Verification results and Activity).
They join the organization's **talent pool**, where the team can invite them to apply for another job or add them to
one.

Keeping someone's data for future roles needs their permission in the UK and EU. So only a candidate who said so on the
apply form can be kept, and only until that permission runs out.

## Permission for future roles

The apply form's review step has an optional, unticked box, "Keep my details for future roles", with one line saying
how long and that every invitation has a link to stop ([apply flow](apply-flow.md) §2). The careers server sends
`keepForFutureRoles`, and the API stores the permission **on that application**, never on the candidate:

| `applications` column | What |
|---|---|
| `future_roles_consent_given_at` | When they ticked it (the application's submission time) |
| `future_roles_consent_months` | The organization's period then, which the form showed them |
| `future_roles_withdrawn_at` | When they took it back, or a person removed them at their request |

A careers form never changes an existing candidate, because anyone can type any email address
(`ApplicationSubmission`), so a stranger cannot extend someone's permission. A tick on one application covers that
application only. An unticked box on a later application withdraws nothing.

`JobApplication.FutureRolesUntil(months)` is when it runs out: the moment given plus the **shorter** of the period then
and the organization's period now. Lengthening the setting never extends a permission already given; shortening it ends
every permission sooner. Null without a permission, or once withdrawn.

The period is `TalentPoolSettings.PermissionMonths`, 12 by default, 6 to 24, set on Settings, Talent pool by a role with
Edit settings (`GET`/`PUT /v1/organization/talent-pool-settings`, with History, [change history](change-history.md)).
The default and the range are a product choice awaiting a legal review ([status](../status.md#owner-actions)).

## Keeping in mind

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

The dashboard reads `keptInMindAt` through `#lib/talent-pool.ts`. A kept application shows "Kept in mind" where its stage
was, counts in a job board's Closed column, and drops out of the stage views and filters.

**Moving it back is the undo.** `MoveTo` on a kept application clears `KeptInMindAt` and records a `StageChanged`, even
to the stage it was in, and the API takes the candidate out of the pool. That is how a rejection is undone too.

Where people keep someone in mind (`POST /v1/talent-pool`, Move candidates, up to 200 applications, one note):

- **The candidate page:** the last item of Move to stage, beside Rejected, opening a sheet with the note. Move to stage
  stays the page's one primary. It shows only for an open application whose permission holds (A3). The Application tab
  shows the candidate's answer.
- **A job's selection bar:** Keep in mind beside Move. Hidden while a Verification filter or status view picked the
  rows, as bulk Rejected and Email are, because it ends a candidacy on the job (ADR-0004).
- **Closing a job:** the close dialog offers "Also keep in mind the N candidates who allowed it", which keeps them
  before the job closes. Keeping only some of them is the selection bar's job, before closing.

The response counts who was kept, who did not allow it and who had already left. Nothing here reads Verification
output, and no automation action keeps anyone in mind (ADR-0004).

## The pool

`talent_pool_entries` holds one row per candidate in the pool: the application they were kept from, the `KeptInMind`
entry (whose note is the pool's only copy of why), who kept them and their last invitation. The entry holds no
candidate data of its own: the note lives on the timeline, the tags are the application's, and when they leave comes
from the application's permission. Because the timeline is append-only, the note cannot be edited; later thoughts go in
ordinary notes.

The entry belongs to its application (`IApplicationScoped`), so it has the restrictive `job_scope` policy: a member on
Only jobs they're on sees the people kept from their own jobs (ADR-0015). It cascades with the application and the
candidate.

**Leaving the pool** deletes the entry and records why on the kept application's Activity (`LeftTalentPool`, with
`poolExit` and, where it applies, the other job and application):

| `poolExit` | When |
|---|---|
| `Expired` | The permission ran out (the daily sweep) |
| `Removed` | A person took them out |
| `CandidateAsked` | The candidate stopped it from their link, or a person removed them at their request, which also withdraws the permission |
| `AddedToJob` | A person added them to a job (from the pool or by hand) |
| `AppliedAgain` | They applied for any job on the careers site |
| `BackInJob` | A person moved the kept application back to a stage |
| `KeptFromAnotherJob` | A person kept them in mind from another application, which now stands for them |

A member on Only jobs they're on who adds a candidate by hand cannot see an entry kept from another job, so that entry
stays until it ends another way.

`GET /v1/talent-pool` lists everyone whose permission still holds, so someone past their date is off the list even
before the sweep runs. The **Talent pool** page (a sidebar item after Candidates, G then P) is the list template: search
over name, email, job and note; Kept from and Tag filters; columns Candidate, Why, Tags, Kept from, Kept and Permission
ends; and a row menu with Invite to apply, Add to a job and Remove from talent pool. There is no Verification column or
filter, so the pool never becomes a list picked by level for bulk invitations (ADR-0004).

## Invitations

Invite to apply (`POST /v1/talent-pool/invitations`, Email candidates, open jobs only, hidden without an email sender)
queues one `SendTalentPoolInvitation` per person through the outbox, carrying ids only. The worker checks again that
they are still in the pool with a permission that holds and that the job is open. It renders the organization's wording
of the built-in `talent-pool-invitation` template ([email](email.md)), with:

- `{{ jobTitle }}`, the job they are invited to;
- `{{ jobLink }}`, that job's public careers page, with no token or personal data;
- `{{ removeLink }}`, the signed link to stop being kept.

It sends the email, then keeps the copy for Messages with "[the candidate's link, not kept in this copy]" in place of
the remove link, because that link acts for the candidate. It records `InvitedToApply` and `EmailSent` on the kept
application. Only Invite to apply sends this template: the email composer never offers it, and no automation names it.
Its wording is edited on Templates, not per send. The sheet previews it (`POST /v1/talent-pool/invitations/preview`).

The candidate applies through the ordinary apply form. Their email finds the same candidate, so the new application
takes them out of the pool (`AppliedAgain`). The candidate page's **Other applications** panel lists every application
of the candidate the reader can see, so each one links to the others.

**Add to a job** (`POST /v1/talent-pool/applications`, Add candidates, any job that is not closed) gives each person an
application in the job's first stage, as a recruiter adding them by hand does. It carries the kept application's
permission, so its clock does not restart.

## Stopping

`{{ removeLink }}` opens `/{organization}/talent-pool/{token}` on the careers site. The page is one native form with no
JavaScript, `no-store`, `noindex` and `no-referrer`. The token is the organization and the candidate, protected with
Data Protection under its own purpose (`TalentPoolLinks`), like a questionnaire link. It never expires: stopping should
always work, and stopping is all it can do. `POST /v1/public/organizations/{slug}/talent-pool/{token}` withdraws every
permission the candidate gave the organization and takes them out of the pool, in one transaction. It lands on
`/talent-pool/stopped`, with no token in the address. A forged token, or one for another organization, is a 404.

Replies to candidate email are not read yet (roadmap item 1), so without this link an invitation would offer no way
out (GDPR Art. 7(3)).

## Expiry

`ExpireTalentPool` runs daily at 03:47 UTC in the worker and fans out one `ExpireOrganizationTalentPool` per
organization, like the integrity data purge ([messaging](messaging.md)). It removes each entry whose permission has run
out, with `LeftTalentPool(Expired)`. The kept application stays as it is: it has ended, and its data follows the
organization's normal retention.

## Not built

- **Erasure.** No erasure or data-access request feature exists yet ("Before real customers" in the
  [roadmap](../roadmap.md)). The entry cascades from the candidate, so a future erasure removes them from the pool.
  Until then, a person removes them with "They asked to be removed", which also withdraws the permission.
- **Record retention.** Nothing deletes an ended application after a period; that is the same roadmap item.
- **API keys, MCP and webhooks.** The pool's routes take no API key, the MCP server has no pool tool, and there is no
  webhook event for it.
