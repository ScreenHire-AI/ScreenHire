---
type: Decision
title: "ADR-0023: A candidate's header is the name, a pipeline tracker and three mini cards"
description: "A candidate's header drops the meta line: the first line is the name and one status badge, a pipeline tracker of dots replaces the stage pill, and Contact, Application and Also applied to (or Rating) become mini cards; Rejected, Withdrawn and Hired get a status of their own, and the tracker's dots are the one exception to square steps."
tags: [adr, design, candidate, dashboard]
---
# ADR-0023: A candidate's header is the name, a pipeline tracker and three mini cards

- **Status:** Accepted (2026-11-02)
- **Deciders:** Owner
- **Amends:** ADR-0017 decision 13 (a record's header is a meta line of icons and values) for a candidate; the stage pill
- **Builds on:** ADR-0004 (evidence, not verdicts), ADR-0010 (steps are square markers), ADR-0013 (the rating)
- **Detail:** `docs/design/design-system.md` sections 7, 11 and 21

## Context

ADR-0017 put a candidate's facts in a meta line under the name: the stage as a muted pill, the job, when and where they
applied, the profile link and the rating. With saved candidates (a candidate who left the job without a rejection) and
one profile across jobs, that line carried six facts and could not show where a person stands in the process, only a
stage name.

## Decision

1. **The first line is the name and one status badge.** Verification suggested (or another level that needs a look)
   while the candidate is in the pipeline. Once they left, Saved, Rejected, Withdrawn or Hired replaces it. Rejected and
   Withdrawn are the grey Not in effect status and Hired is Done, so no status was added to section 3.2.
2. **The actions sit on the right.** Email and Move to stage. A saved candidate gets Message and Invite to another job
   instead, because moving a saved candidate back into a stage is not what a recruiter reaches for first.
3. **A pipeline tracker replaces the stage pill.** Every stage of the job's workflow runs left to right as a dot with
   its name. Passed stages are filled with the date reached, the current stage is ringed with "Here 3 days", later
   stages are empty, and the stage a candidate left from is dark with how and when ("Saved Oct 31"). Rejected and
   Withdrawn are ways out, so they are not drawn as steps. It reads stages, dates and the stage kind only (ADR-0004).
4. **Dots are the one exception to square steps.** ADR-0010 and T4 make steps square markers so they never read as
   counts. The tracker says where a person is, not how far a form has got, and the brief asked for dots. T4 now names
   the exception.
5. **Three mini cards under the tracker.** Contact (email and phone, each a link with a Copy button; "Last contacted"
   for a saved candidate), Application (the job, and when and where from, which is where the source went), and Also
   applied to, or Rating once someone scored the candidate. The card never says "Not rated yet".
6. **Where the dropped items went.** The Overdue badge sits beside "Here 3 days" in the tracker. The email-issue badge
   sits on the Contact card's email. The link to Saved candidates stays as the one meta item, because a badge is never
   a link (A5).
7. **On a phone** the cards stack and the tracker shows the current stage with "2 of 6".

## Consequences

- The header is taller than the meta line was. It is the same on every tab, so the header still does not move when the
  tab changes (S8).
- "Last contacted" is the newest email to or from the candidate, read from the history the page already loads. It uses
  the time-ago wording of section 13, so after a week it is a date.
- A candidate whose stage is no longer in the job's workflow gets a tracker with no ringed dot, rather than a guess.
