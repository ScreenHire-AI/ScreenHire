---
type: Design
title: "Candidate portal"
description: "A candidate's own page on each organization's careers site, reached by a signed emailed link with no account: each application's job, date and place on four fixed steps, a saved application to finish, a follow-up questionnaire or an interview to book, and the messages with a reply box that lands in the same Messages and Activity as an emailed reply; the link every email to a candidate ends with, and what the page never shows."
tags: [candidates, careers, email, privacy]
sources:
  - resource: src/ScreenHire.Application/Portal/CandidatePortalLinks.cs
  - resource: src/ScreenHire.Application/Portal/CandidateFunnel.cs
  - resource: src/ScreenHire.Application/Portal/SendPortalLink.cs
  - resource: src/ScreenHire.Application/Portal/PortalMessageText.cs
  - resource: src/ScreenHire.Application/Applications/CandidateMailRouting.cs
  - resource: src/ScreenHire.Infrastructure/Email/CandidatePortalFooter.cs
  - resource: src/ScreenHire.Infrastructure/Email/MailRoute.cs
  - resource: src/ScreenHire.Infrastructure/Email/TransactionalEmail.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewHandlers.cs
  - resource: src/ScreenHire.Application/Privacy/SendDataRequestLink.cs
  - resource: src/ScreenHire.Application/Privacy/BuildDataCopy.cs
  - resource: src/ScreenHire.Domain/Candidates/Candidate.cs
  - resource: apps/api/Endpoints/PublicPortalEndpoints.cs
  - resource: apps/api/Contracts/PublicPortalContracts.cs
  - resource: apps/api/Applications/PortalSetup.cs
  - resource: apps/api/Email/InboundEmailIngest.cs
  - resource: apps/api/Configuration/DomainOptions.cs
  - resource: apps/api/Endpoints/EmailEndpoints.cs
  - resource: apps/careers/src/lib/portal.ts
  - resource: apps/careers/src/actions/index.ts
  - resource: apps/careers/src/layouts/CareersLayout.astro
  - resource: apps/careers/src/pages/[organization]/my-applications/index.astro
  - resource: apps/careers/src/pages/[organization]/my-applications/sent.astro
  - resource: apps/careers/src/pages/[organization]/jobs/[job]/applied.astro
  - resource: apps/careers/src/components/PortalLinkProblem.astro
  - resource: apps/careers/src/pages/[organization]/my-applications/[token]/index.astro
  - resource: apps/careers/src/pages/[organization]/my-applications/[token]/[application].astro
  - resource: apps/careers/src/pages/[organization]/privacy.astro
---
# Candidate portal

A candidate has one place, on each organization's careers site, to pick up an application, see where it stands and talk to
the team. There is no account and no password. The decision is [ADR-0027](../adr/0027-candidate-portal-by-email-link.md).

## Built

- `candidates.portal_link_sent_at`: the cooldown for the emailed link, its own column so a data request link never holds a
  portal link back. The claim is an atomic `ExecuteUpdate`, as for data requests.
- Options `ScreenHire:Portal` (`LinkLifetime` 14 days, `LinkCooldown` 5 minutes, `PermitsPerMinute` 20, `ReplyBodyMax` 5,000
  characters, `RepliesPerDay` 10), forwarded by the AppHost to the API and the worker.
- `CandidatePortalLinks`: the signed link, `SendPortalLink` and its email, `CandidateFunnel`, `PortalMessageText`.
- `/v1/public/organizations/{slug}/portal`, five anonymous routes under the `portal` rate limit.
- Five careers pages under `my-applications`, a link in the footer of every other careers page and on the "Application sent" page.
- A footer on every email to a candidate, and a `MailRoute` for the interview emails, which had none.

## Getting in

1. **Ask for a link.** The careers page posts an address to `POST .../portal/links`. The route validates it, runs the bot
   check (Turnstile; only a failed check is a 403, and an unconfigured one goes on), and always answers 202 with no body.
   For a known address whose cooldown has passed it claims `portal_link_sent_at` with one atomic update and queues
   `SendPortalLink` in the same transaction. An unknown address, or one inside its cooldown, runs the same lookup and a
   no-op save. The answer is identical either way, so it tells nobody who has applied. The response time is not: the
   claim and the queued message cost a little, which is an accepted limit (as in [data requests](data-requests.md)).
2. **Open the link.** The worker issues the signed token itself, so it never sits in the outbox table, and mails it in
   ScreenHire's own words: what the link opens, how long it works, "Anyone who has it can open your page, so please do not
   forward this email", and "If you didn't ask for this you can ignore this email." It skips quietly when the candidate is
   gone, the address is flagged, or the host has no sender or careers address.
3. **Every email carries one.** The same link ends every email a candidate gets about an application (see
   [the footer](#the-link-every-email-carries)), so a candidate who is still in touch always has a fresh one.

`CandidatePortalLinks` is a time-limited Data Protection token, purpose `ScreenHire.CandidatePortal.Link.v1`, holding the
organization and the candidate. The expiry is inside the token, so an expired link is refused like a forged one. Every
route that takes a token answers 404 for a token that is forged, damaged, expired, made for another organization, made for
another purpose or names a candidate who has since been erased, and for an application that is not the token's
candidate's, so a page cannot tell them apart. All routes are 404 where the deployment cannot send the email.

The page needs no cookie and no session: the token is in the address of every portal page, which sends `Cache-Control:
no-store`, `noindex` and `no-referrer`. Access is by email link alone. It does not depend on the device check, reads
nothing from verification, and collects nothing about the browser: no telemetry, no device code (CLAUDE.md
non-negotiable 4).

## The page

`GET .../portal/{token}` returns the organization's name, the candidate's first name, and for each of the candidate's
applications to this organization (newest first):

- the job's title and when they applied;
- the funnel (below);
- what is waiting: an open follow-up questionnaire or an interview to book, each as the token of its existing careers page
  (`/questionnaires/{token}`, `/interviews/{token}`). The rules are the pages' own: a questionnaire is listed when it is
  unanswered, unexpired, still in the job's workflow, the job is not closed and the application has not ended; an interview
  when it is open and the application has not ended;
- whether there are messages and whether the candidate can reply.

It also lists saved applications: the drafts under the candidate's address for jobs that are still open, each with the token
of the apply page that finishes it (`/jobs/{slug}/apply?draft={token}`). A draft is found by the address of the candidate
the portal token names, which is safe because that token proves they received mail there; the draft's own link remains the
only way to open one without a portal ([apply flow](apply-flow.md#7-pages-progress-and-saved-drafts)). A person who has only
a draft and no application cannot sign in to the portal: their draft email already has their link.

### The funnel

Four fixed steps for every job: **Applied, In review, Interview, Offer**, mapped from the stage's kind and its place in
the pipeline by `CandidateFunnel.For`:

| Stage | Shows |
|---|---|
| Applied | Applied |
| Interview | Interview |
| Offer | Offer |
| Screening, Assessment, Verification or Custom | The step of the last Interview or Offer stage before it in the pipeline, or In review when there is none |
| A stage the employer has since removed | In review |
| Hired | Hired (ended) |
| Rejected | Not moving forward (ended) |
| Withdrawn | Withdrawn (ended) |

A decision is a move, not a stage: the team moves an application from its last interview to an offer, or ends it. So a
take-home between two interviews shows Interview, a holding stage after the last interview (a "Final review") shows
Interview until the team decides, and a check after the offer shows Offer. The step never goes back unless the team moves
the application back. Offer shows as soon as the team moves an application there, so a team moves it when it extends the
offer, as a rejection shows the moment it is made.

An application the team kept in mind for future roles reads as Not moving forward, with no word about a list or a reason. An
ending stage decides first, so a withdrawn application that was once saved is still Withdrawn. Once an application has ended
no step is drawn, only the status.

The response never carries a stage's id or name, how many stages the job has, a rating, a scorecard, a note, a tag,
anything about verification, or the workflow. This is the owner's rule that candidate text never explains how decisions are
made (non-negotiables 1 to 4), and an integration test scans the page's payload for it.

## Messages and replies

`GET .../portal/{token}/applications/{id}/messages` returns one application's thread, oldest first, as plain text: the
emails sent to the candidate (the kept copy of each email that has an `EmailSent` entry) and their replies, except automatic
ones. A kept copy says "[the candidate's link, not kept in this copy]" where the candidate's own link was; on the
candidate's own page `PortalMessageText` shows "(your link is in the email we sent you)" instead. Messages show the
organization's name and "You", never a team member's name. A mail to the signed reply address from another address reads as
"You" too: only the candidate was sent that address. The thread is every email the candidate got, so a saved candidate still sees
the invitation they were sent; only the status stays silent about it. Dates are shown without a time, because the server does
not know the candidate's time zone and a label never names one (design system section 2).

`POST` on the same path is a reply. It goes through the same ingest as an emailed reply (`InboundEmailIngest.IngestForAsync`),
so a reply from the page and a reply by email are one thing: an `inbound_emails` row, an `EmailReceived` entry in Activity, up
to ten attachments kept in the `email-attachments` container and scanned, and the `CandidateReplied` event that tells the person
who sent the original (a card, and one fixed email that never quotes the reply). The two stay in one thread because both answer
the same sent email.

- It answers the latest email that went for the application, which is the one the person is told about. With no email sent yet
  the route answers 409 and the page says the team has not written to the candidate: ingest needs a send to answer.
- **Files.** A native `multipart/form-data` form with `<input type="file" multiple>`: up to 3 files, 10 MB in all (the careers
  server takes 12 MB from a form), PDF, Word, PNG or JPEG. The careers server posts them to the API as base64 in JSON, and the
  existing `AttachmentFiles` checks (extension and first bytes) and malware scan apply.
- **Idempotency.** The form carries its own random id (32 hex digits, made when the page renders). The reply is stored under
  the `Message-ID` `<portal-{application}-{id}@portal.invalid>`, so sending the same form twice, or a retry after a lost answer, stores one
  reply. A problem re-renders the page with the same id.
- **A reply from the page never clears a bounce or spam flag.** The ingest's step that turns email back on for an address that
  writes from it (`clearsEmailIssue`) is on for an email and off for the page: a footer link sent before a bounce still opens
  the page, and typing there says nothing about the mailbox.
- **Limits.** `RepliesPerDay` per application (the day's `portal-` replies are counted; the next is a 429) and the per-address
  `portal` rate limit bound what a leaked link can do to the people who are told about each reply. The count is read and then
  written, so posts at the same instant can pass it by a few: a soft cap, with the rate limit as the hard one. A reply is at most
  `ReplyBodyMax` characters.

## The link every email carries

`CandidatePortalFooter` ends an email's text and HTML with "See where your application stands and read your messages:" and the
link. ScreenHire's fixed wording, added after an organization's template is rendered and sanitized (as the header is), so a
template cannot change or remove it. `MailRoute` carries it (`PortalLink`), and `MailRoute.Apply` adds it to the email that
goes out. `TransactionalEmail` returns the email as rendered, so the copy the team keeps never holds the link: it opens the
candidate's page, so it goes to the candidate alone, as the follow-up questionnaire link does. A reply by email usually quotes
that footer, so ingest cuts the link back to `/my-applications` before it stores a reply (`PortalMessageText.WithoutPortalToken`).
The HTML line goes inside the document's `<body>`, because a rendered template is a whole document.

Where it is added: every application email (a person's send, an automation's `SendEmail`, `Reject` and `SendQuestionnaire`, the
stage-change and application-received templates), the three interview emails, the invitation to apply, and the three data
request emails (the link, the copy and the decline). The saved-application email goes to someone who may not be a candidate yet,
and mail to the team is not correspondence with a candidate, so neither carries one. The portal's own sign-in email is the link and
carries no footer. The email composer's preview and a template's test send show the line with the page where anyone asks for
a link (`DomainOptions.PortalEntryUrl`), since only the worker can issue the candidate's own.

The interview emails used to go with no Reply-To, so an emailed reply to one could not land. `CandidateMailRouting.ForAsync`
now routes them like every other email about an application.

## Careers pages

Under `apps/careers/src/pages/[organization]/my-applications/`. Each is server rendered with no client JavaScript; the only
script is Cloudflare's bot check on the first page, and only where the deployment has one. The forms are native and post to the
`requestPortalLink` and `sendPortalReply` Astro Actions, whose logic is in `apps/careers/src/lib/portal.ts`. Every call forwards
the visitor's address, so the API's rate limit counts the visitor and not the careers server.

| Page | What it does |
|---|---|
| `index.astro` | What happens, then one email field and "Email me a link". With the portal disabled, or any answer but 200 from `GET ""`, it says the page is not available and to reply to the last email. |
| `sent.astro` | The one answer after asking: "If we have an application from that address, we have emailed a link". The same for an address that applied, one that did not and one in its cooldown. |
| `[token]/index.astro` | The overview: saved applications to finish, then each application with its job, date and four steps (icon, text and `aria-current`, never colour alone) or its ended status, anything waiting as a button, and a link to its messages. |
| `[token]/[application].astro` | The thread and a reply form. A redirect after a send (`?sent=1`) so a refresh never posts twice. |

A forged, expired, wrong-organization or erased-candidate link, or a malformed one, shows "This link has expired or is not valid"
with a link back to the form and the line to reply to the last email (status 404); a busy network says the same in a `Notice`
(429). Both come from `PortalLinkProblem`. The footer of every careers page but these links to `/my-applications`.

## Notice

The privacy page has a "Your page" section: the emailed link and how it works, what the page shows, that a reply typed there is
kept with the application like an emailed one, and that it collects what is typed and nothing about the browser. The notice
version moved to `2026-11-07.1` with it, because a reply written on a page is a new way for text to be kept.

## Not built

- A candidate withdrawing an application from the page.
- Booked interviews on the page: only an interview to book is listed (a booked one has its own link in its emails).
- A reply before the team has written: ingest answers a send, so there must be one.
- Showing a candidate who has only a saved application, with no application sent, their page.
