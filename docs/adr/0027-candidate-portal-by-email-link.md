---
type: Decision
title: "ADR-0027: The candidate portal is a page reached by an emailed link, with no account"
description: "A candidate sees their applications, a generic four-step status, anything waiting and their messages on a careers page opened by a signed, time-limited emailed link, with no account or password; a reply from the page and a reply by email share one ingest and one thread, and every email to a candidate ends with the link."
tags: [adr, candidates, careers, email, privacy]
---
# ADR-0027: The candidate portal is a page reached by an emailed link, with no account

- **Status:** Accepted (2026-11-07), amended (2026-11-07)
- **Deciders:** Owner
- **Detail:** [candidate portal](../architecture/candidate-portal.md)

## Context

A candidate could do three things after applying: wait, reply to an email, and open a link the team sent for one purpose
(a questionnaire, an interview time, finishing a saved draft). They had no place to see where an application stood, no way
to find a saved draft or a questionnaire again without the email that held it, and a reply went to whichever email they
answered. The roadmap listed a candidate portal under Later. The owner wanted it simple: no account, no password.

The constraints are the product's own. Verification output is never shown or explained to a candidate (CLAUDE.md
non-negotiables 1 to 4), candidate text never explains how decisions are made, careers pages ship about 0 KB of JavaScript
and collect nothing about the browser, and every tenant row sits behind row-level security.

## Decision

1. **A page reached by an emailed link, with no account.** A candidate types an address on a careers page. The page always
   gives the same answer, so it tells nobody who has applied. If the address has an application, a signed, time-limited link is
   mailed to it. The link names one candidate of one organization and nothing else; a person's access is to one organization.
   The route copies the data request flow: the apply flow's bot check, a rate limit per address, an atomic cooldown claim, and a
   Data Protection token under its own purpose.
2. **Access is by the link alone.** It does not use the device check, the apply flow's telemetry or any cookie. A forged,
   expired, wrong-organization or erased-candidate link is one 404.
3. **Every email to a candidate ends with a link to their page.** The line is ScreenHire's fixed wording, added at send time and
   never to the copy the team keeps, because the link opens the page and so goes to the candidate alone. The link lives 14 days;
   every new email carries a fresh one.
4. **Four fixed steps for every job.** Applied, In review, Interview, Offer, mapped from the stage's kind, and an ended
   state of Hired, Not moving forward or Withdrawn. Amended 2026-11-07: the fourth step was Decision, shown only at an Offer
   stage. A decision is the team's move from the last interview to an offer or to the end, not a stage to wait in, so the
   step is named for where that move leads. A stage of another kind takes the step of the last Interview or Offer stage
   before it, so the steps never go back when an assessment or a holding stage follows an interview. Never the employer's stage names, how many stages there are, a rating, a
   scorecard, a note or anything about verification. A saved candidate reads as Not moving forward, with no mention of a list.
5. **A reply from the page is an emailed reply.** It goes through the same ingest (`InboundEmailIngest`): one reply row, one
   entry in Activity, attachments kept and scanned, the sender told. Both channels answer the same sent email, so they are one
   thread. A reply from the page never clears a bounce or spam flag, because typing on a page says nothing about the mailbox.
6. **Saved drafts are found by the address of the candidate the link names.** This amends the line in the apply flow that a
   draft is found by its token and never by its email: the portal token proves the holder received mail at that address, which is
   what a draft's own token proves.
7. **The notice names the page.** The privacy page has a section for it, and its version moves, because a reply written on a
   page is a new way for text to be kept.

## Consequences

- Anyone who holds a link can read the candidate's messages with the organization until it expires, and can write as them. The
  lifetime is short, the email says not to forward it, a reply is capped per application per day, and the page shows only what
  the candidate already received. A mailbox that is compromised was already enough to take over the candidate's emails.
- A candidate who only saved a draft, with no application sent, cannot sign in. Their draft email carries their link.
- A page reply needs a sent email to answer, so the reply box shows only after the team has written.
- Legal review of the page, the footer on every email and the notice change is an owner action (docs/status.md).
- Mailing the footer puts the link in the interview emails too, which now carry a Reply-To like every other email about an
  application.
