---
type: Decision
title: "ADR-0021: Verification read, an API key scope only an Owner can give"
description: "A third API key scope, VerificationRead, that only an Owner can grant: it opens an application's verification and a list of applications by level to the key, over HTTP and the MCP server, records every read in the review log under the key, and changes nothing for a key without it; reports and saved candidates become MCP read tools; a level is still never a reason to reject."
tags: [adr, api-keys, verification, mcp]
---
# ADR-0021: Verification read, an API key scope only an Owner can give

- **Status:** Accepted (2026-11-02)
- **Deciders:** Owner
- **Amends:** ADR-0011 decisions 3 and 4 (verification stays out of keys; two scopes)
- **Builds on:** ADR-0004 (evidence, not verdicts), ADR-0014 (Owner-only permissions), ADR-0015 (keys see every job)
- **Detail:** `docs/architecture/sign-in-and-tenancy.md` "API keys", `docs/architecture/mcp-server.md`,
  `docs/architecture/verification-engine.md`

## Context

ADR-0011 kept verification away from API keys: a key gets no level, and the timeline it reads has no assessments.
The reason was sound. A level is evidence for a person to review, and an integrator's system could reject on it,
which ScreenHire's own automations may not (ADR-0004).

The owner now wants a power user to connect an assistant that can audit and act on nearly everything in ScreenHire,
verification included. An assistant that can read every application but not whether it looks automated cannot audit a
hiring process. So the question is how to open the evidence to a key without making the rejection risk invisible.

## Decision

1. **A third scope, `VerificationRead`.** It is separate from `Read` and `Write`. `Write` does not include it and it
   includes neither. Every key still has `Read` (the validator requires it), so a verification key connects like any
   other. A key without the scope behaves exactly as before: no new route answers it, no tool is listed to it, and the
   lists, the detail and the timeline it reads still carry no verification.
2. **Only an Owner can give it.** A new Owner-only permission, `GrantVerificationAccess`, sits beside `ManageOwners`
   (ADR-0014 decision 4). The Owner role holds it; Admin does not and a custom role never can. Creating a key with the
   scope checks it and answers 403 "Owners only" otherwise. A key's scopes are fixed when it is created, so nobody
   widens a key later. Revoking stays with Manage API keys, so an Admin can always switch a verification key off.
3. **What the scope opens.** `GET /v1/applications/{id}/verification` (levels, reason codes, summary, next step,
   signals with evidence and counter-evidence, the review log) and a new `GET /v1/verification/applications` that lists
   applications whose current level matches, newest first, never ranked by severity. The MCP server gets the same two
   as `get_verification` and `find_candidates_by_verification_level`, listed only to a key with the scope. The routes
   that act on a verification (dismiss, explain, note, request verification) stay the session's: a key reads and
   never decides.
4. **Every read through a key is in the review log.** One `ReadByKey` row per application read: the key, the time, and
   no acting user. A level search that returns ten applications writes ten rows, and one that returns none writes
   none, because the log belongs to an application. The row is saved before the answer
   goes out, so a read that cannot be recorded is not answered. The log stays append-only. The review log panel shows
   it as "Read through the API key X" with the person who made the key, and folds a run of reads by one key into one
   line with a count.
5. **Non-negotiable 2 is unchanged.** No verification tool rejects, moves, dismisses or notes. The server instructions
   and every verification tool description say a level is never a reason to reject on its own and that a candidate is
   not moved to a Rejected stage because of one, and every verification result repeats it in a `guidance` field. The
   workflow guardrail in `DefinitionValidator` is untouched, and the OpenAPI document still has no reject operation
   under verification.
6. **Reports and saved candidates are MCP read tools for any key.** `get_hiring_report` and `list_saved_candidates`
   need only `Read`, call the services their dashboard routes call, and leave those routes session-only. Reports hold
   counts and job titles; saved candidates are the talent pool (the team's "kept in mind" list) and carry the names and
   emails `find_candidates` already returns. Keys see every job (ADR-0015), so these tools are not narrowed by job
   access.
7. **What an applicant or their device supplied is marked as data.** Evidence values, quotes, compared texts and list
   items in a verification result are `candidateWritten`, as answers and file names already are.

## Consequences

- The risk ADR-0011 removed is back for a key that has the scope: the assistant, or a system behind it, can act on a
  level. ScreenHire cannot stop that. What it does is make the choice an Owner's, make the use auditable in the log,
  and say the rule in every place an assistant reads. A team that wants nothing of this never gives the scope.
- A key may hold `Write` and `VerificationRead` together, so one assistant can read a level and move a candidate. The
  owner asked for an assistant that audits and acts, so the combination is allowed. The guard is the rule in the tool
  descriptions, the guidance field and the instructions, plus the log of every read. A technical refusal of the pair
  would be a later decision.
- The review log has a new kind of entry and its actor column is nullable. A database check keeps a row to a person or
  a key, never both and never neither. The key's foreign key cascades like the table's others: the app role cannot
  delete a key, revoking keeps the row, and only an organization's own deletion removes one. `Down` deletes the key
  rows and takes the scope off every key, because the previous release cannot parse its name.
- The log grows by a row for every read and the verification route returns the whole log, so a key that polls one
  application makes each later read larger. The per-key rate limit bounds it and an application's review is short, so
  the rows stay; the panel folds them.
- The OpenAPI document gains two operations under the `apiKey` scheme and one nullable shape on `VerificationReviewEntry`.
- `docs/architecture/talent-pool.md` and `reports.md` said no MCP tool reads them. They are corrected.
- A removed Owner's keys keep working. Removing a member already offers to revoke the keys they made.
- Not decided here: webhooks for verification, a scope that lets a key act on a verification, and a per-key view of
  how much it has read. Each would be its own decision.
