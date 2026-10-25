---
type: Reference
title: "Roadmap"
description: "The order of work after the MVP: what each finished workflow delivered and in which PRs, what comes next (design system v0.3, questionnaire sections with progress and save-and-resume, the embeddable careers widget, interview scheduling, adding candidates by hand with server search and reports), later items, and what must exist before real customers."
tags: [roadmap, planning]
---
# Roadmap

What comes after the MVP, in order. What is already built is in the [MVP status](status.md). Snapshot 2026-10-24;
each item names the doc that designs it. Board workflows carry the detail.

## Done since the MVP

| Work | Workflow | PRs |
|---|---|---|
| Design system v0.2 in the dashboard: gate stability, demo questionnaires and answers, the foundation, components and a style guide, the app shell, an inline resume preview, every page, and a visual design pass in `/task-review` ([design system](design/design-system.md), ADR-0010) | "Design system v0.2" | #22 to #33 |
| Webhook subscriptions, organization API keys for the hiring API, team members with invitations and organization switching ([sign-in and tenancy](architecture/sign-in-and-tenancy.md), [workflow engine](architecture/workflow-engine.md) §5, ADR-0011) | "Platform: team, API access and webhooks" | #36 to #38 |
| Settings screens; notes, tags, tasks, a Run button for manual automations and a notification tray; scorecards that raise the "reviewer decision" trigger; editable email templates with manual and bulk email ([email](architecture/email.md), [workflow engine](architecture/workflow-engine.md) §8); the docs as an Open Knowledge Format bundle (ADR-0012) | "Team, collaboration and communication" | #39 to #42, and the docs restructure |
| Acting on stage SLAs (the "time limit passes" trigger and an Overdue status); follow-up questionnaires sent by an automation, with answers as automation facts; a workflow graph you build in, by keyboard as well as pointer ([workflow engine](architecture/workflow-engine.md) §4 and §8) | "Workflow builder" | #44, #45, and the editable graph |

## Next

| # | Work | Workflow | Why now |
|---|---|---|---|
| 1 | Design system v0.3: pages that fill wide screens, status icons instead of shapes, cards with weight, fixed tabs and a details rail on detail pages, full-window file and form previews | in review on the design canvas | Wide screens leave half the window empty, the job page is crowded, and the candidate page shows everything at once. Every item below builds on it |
| 2 | Questionnaire sections with progress and save-and-resume: named sections, one per page, or an automatic split at a page size each company sets; "Step 2 of 4" with "9 of 24 answered"; a draft kept under the candidate's email, with a signed link to finish later; the same for follow-up questionnaires | not yet planned | One long scrolling page of questions loses candidates. Two integrity rules come with it: the "faster than a person could" check measures active time per page, not time on the clock, and a knockout answer on an early page never ends the application ([apply flow](architecture/apply-flow.md), [integrity engine](architecture/integrity-engine.md)) |
| 3 | Embeddable careers widget: the job list and the apply flow in an iframe on a company's own site; a per-organization list of sites allowed to embed them (`frame-ancestors`, `'self'` today); a script that adds the frame and sizes it; the embedding site recorded as the source | not yet planned | Companies want candidates to apply without leaving their site. An iframe from ScreenHire's origin keeps the notice, telemetry and integrity checks intact, because the host page can neither read nor forge them (ADR-0003). The flow stays cookie-free inside the frame, since browsers block third-party cookies there |
| 4 | Interview scheduling | not yet planned | Refused at save today. Decide first: build it, or Cal.com, Nylas or Cronofy (workflow engine, open questions) |
| 5 | Add a candidate by hand, search on the server, basic reports (funnel, time to hire, source) | not yet planned | The API for adding a candidate exists with no screen; Ctrl K only filters what is loaded |

## Later

- **Job boards:** Indeed and ZipRecruiter (phase 2, with paying customers), LinkedIn, then European boards
  ([job boards](research/job-boards.md), [job distribution](architecture/job-distribution.md)).
- **Careers:** custom tenant domains (ADR-0003).
- **The recruiter dashboard inside a customer's own site:** only if a customer asks. Sign-in cookies are SameSite Lax
  or Strict and do not reach a cross-site frame, Google's and Microsoft's sign-in pages refuse to load in a frame, and
  allowing framing invites clickjacking. The API and webhooks are the usual way to integrate.
- **Integrity phase 2:** identity and liveness checks, with a "why we asked" page and an appeal flow
  ([integrity engine](architecture/integrity-engine.md) §9).
- **Integrity phase 3:** interview integrity.
- **Signal Network:** built and off until a legal review and DPIA (ADR-0005).
- **Careers and marketing pages on design system v0.2.**

## Before real customers

- **Hosting and CI:** nothing is hosted; ADR-0007 sets the Azure plan, and the repo has no infrastructure or CI yet.
- **Billing:** none exists.
- **Compliance** ([legal](research/legal.md)): erasure and data-access requests, record retention, bias audits,
  Colorado and California automated-decision rules (due 1 January 2027), EU AI Act provider duties (due 2 December
  2027), the customer documentation pack.
- **Accessibility:** an external WCAG 2.2 audit before general availability (ADR-0009).
- **Owner actions:** the accounts, keys and decisions in the [MVP status](status.md#owner-actions).

## Start now, outside the code

- **LinkedIn partner application:** approval takes 3 to 6 months ([job boards](research/job-boards.md)).
- **Hosting decision and the domain:** both gate going live.
