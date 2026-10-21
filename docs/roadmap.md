# Roadmap

What comes after the MVP, in order. What is already built is in the [MVP status](README.md#mvp-status). Snapshot
2026-10-21; each item names the doc that designs it. Board workflows carry the detail.

## In flight

| Work | Workflow | Notes |
|---|---|---|
| Design system v0.2 in the dashboard | "Design system v0.2" | Gate stability first, then the foundation, components and every page ([design system](design/design-system.md), ADR-0010) |

## Next

| # | Work | Workflow | Why now |
|---|---|---|---|
| 1 | Team members and invites, API keys for integrators, webhook subscriptions (backend only) | "Platform: team, API access and webhooks" | Every member is an Owner today and nobody can invite a colleague; the public API is a headline feature but every route needs a browser session ([sign-in and tenancy](architecture/sign-in-and-tenancy.md), [workflow engine](architecture/workflow-engine.md) §5) |
| 2 | Settings and team screens, notes, tags, tasks, scorecards, editable email templates, manual and bulk email | "Team, collaboration and communication" | The basics a hiring team uses daily. Starts after the design system and platform workflows, on the new components |
| 3 | Workflow builder: an editable xyflow graph, firing the "questionnaire completed" and "reviewer decision" triggers, the send-questionnaire action, a Run button for manual automations, acting on stage SLAs | not yet planned | The graph is read-only today and two triggers can be chosen but never fire ([workflow engine](architecture/workflow-engine.md) §8) |
| 4 | Interview scheduling | not yet planned | Refused at save today. Decide first: build it, or Cal.com, Nylas or Cronofy (workflow engine, open questions) |
| 5 | Add a candidate by hand, search on the server, basic reports (funnel, time to hire, source) | not yet planned | The API for adding a candidate exists with no screen; Ctrl K only filters what is loaded |

## Later

- **Job boards:** Indeed and ZipRecruiter (phase 2, with paying customers), LinkedIn, then European boards
  ([job boards](research/job-boards.md), [job distribution](architecture/job-distribution.md)).
- **Careers:** custom tenant domains and the embeddable apply widget (ADR-0003).
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
- **Owner actions:** the accounts, keys and decisions in the [MVP status](README.md#owner-actions).

## Start now, outside the code

- **LinkedIn partner application:** approval takes 3 to 6 months ([job boards](research/job-boards.md)).
- **Hosting decision and the domain:** both gate going live.
