---
type: Reference
title: "Roadmap"
description: "The order of work after the MVP: what each finished workflow delivered and in which PRs, what comes next (candidate replies by email with an optional company sending domain, the embeddable careers widget, interview scheduling, adding candidates by hand with server search and reports, organization branding, a talent pool, rich text for job descriptions and email templates, connecting to Slack, teammates' changes as toasts), later items, and what must exist before real customers."
tags: [roadmap, planning]
---
# Roadmap

What comes after the MVP, in order. What is already built is in the [MVP status](status.md). Snapshot 2026-10-25;
each item names the doc that designs it. Board workflows carry the detail.

## Done since the MVP

| Work | Workflow | PRs |
|---|---|---|
| Design system v0.2 in the dashboard: gate stability, demo questionnaires and answers, the foundation, components and a style guide, the app shell, an inline resume preview, every page, and a visual design pass in `/task-review` ([design system](design/design-system.md), ADR-0010) | "Design system v0.2" | #22 to #33 |
| Webhook subscriptions, organization API keys for the hiring API, team members with invitations and organization switching ([sign-in and tenancy](architecture/sign-in-and-tenancy.md), [workflow engine](architecture/workflow-engine.md) §5, ADR-0011) | "Platform: team, API access and webhooks" | #36 to #38 |
| Settings screens; notes, tags, tasks, a Run button for manual automations and a notification tray; scorecards that raise the "reviewer decision" trigger; editable email templates with manual and bulk email ([email](architecture/email.md), [workflow engine](architecture/workflow-engine.md) §8); the docs as an Open Knowledge Format bundle (ADR-0012) | "Team, collaboration and communication" | #39 to #42, and the docs restructure |
| Acting on stage SLAs (the "time limit passes" trigger and an Overdue status); follow-up questionnaires sent by an automation, with answers as automation facts; a workflow graph you build in, by keyboard as well as pointer ([workflow engine](architecture/workflow-engine.md) §4 and §8) | "Workflow builder" | #44, #45, and the editable graph |
| Design system v0.3 in the dashboard and what its review added: change history with Last updated and the History sheet, the foundation and its components in the style guide, one form builder, the Settings menu with Team and Roles, the job page and jobs list, the candidate page with fixed tabs, the details rail and notes with @mentions, the email composer and Messages, scorecard templates and the candidate rating, one Templates page, Tasks with Mine, For the team and Everyone's, and a design review of the whole dashboard ([design system](design/design-system.md), ADR-0013, ADR-0016); custom roles with permissions checked on the server and job access through each job's hiring team (ADR-0014, ADR-0015) | "Design system v0.3, the review's features, and roles with job access" | #47 to #58, and the remaining screens |
| A remote MCP server on the API (`/v1/mcp`) for a team's AI assistant, signed in with an organization API key: find jobs and candidates, read an application, add notes and tags, create tasks, move a candidate ([MCP server](architecture/mcp-server.md)) | "Design system v0.3, the review's features, and roles with job access" | #59 |
| Rich text for job descriptions and email templates: a small editor with a toolbar (bold, italic, headings, bulleted and numbered lists, links) in New job, Edit details, email templates and the composer, variables as chips, stored as sanitized HTML limited to the toolbar's elements and rendered on the careers pages, in Google for Jobs, in feeds and in email; existing plain text converted by migration ([rich text](architecture/rich-text.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | the rich text task |
| Branding (roadmap item 6): a brand colour and a second colour checked against WCAG 2.2 AA before they save, with the nearest passing shade, a heading font from six ScreenHire hosts, and a logo, favicon and sharing image uploaded through storage with the malware scan, on the careers pages, the apply flow and the top of every email to candidates ([branding](architecture/branding.md), ADR-0018) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | The branding task |
| Questionnaire sections with progress and save-and-resume: named sections in the form builder or an automatic split at each organization's questions per page, one page each on the apply form and follow-up questionnaires with "Step 2 of 4" and "9 of 24 answered", a draft under the candidate's email with a signed link to finish later, the submit-velocity check on active time per page, and no knockout before submission ([apply flow](architecture/apply-flow.md) §7, [integrity engine](architecture/integrity-engine.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | this workflow |

## Next

| # | Work | Workflow | Why now |
|---|---|---|---|
| 1 | Candidate replies, and an optional sending domain. Every email to a candidate goes out with a reply address for its application, so the candidate's answer lands in Messages and Activity, its attachments pass the malware scan, and whoever sent the email is told. This works out of the box, from ScreenHire's default sending address under the company's name. As an add-on, an Owner or Admin can add the company's own domain under Settings: ScreenHire lists the DNS records to add (ownership, DKIM and SPF, so the mail passes DMARC) and checks them, and email then comes from an address such as hiring@example.com | not yet planned | The Messages tab is one-sided until replies come back, and replies need no setup from the company. Mail from a company's own domain is trusted and answered more often, so the domain is offered, never required. Sending domains stay configuration (ADR-0003). It needs an email provider that receives mail and verifies custom domains, an owner decision while hosting is open (ADR-0007) |
| 3 | Embeddable careers widget: the job list and the apply flow in an iframe on a company's own site; a per-organization list of sites allowed to embed them (`frame-ancestors`, `'self'` today); a script that adds the frame and sizes it; the embedding site recorded as the source | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | Companies want candidates to apply without leaving their site. An iframe from ScreenHire's origin keeps the notice, telemetry and integrity checks intact, because the host page can neither read nor forge them (ADR-0003). The flow stays cookie-free inside the frame, since browsers block third-party cookies there |
| 4 | Interview scheduling | not yet planned | Refused at save today. Decide first: build it, or Cal.com, Nylas or Cronofy (workflow engine, open questions) |
| 5 | Basic reports (funnel, time to hire, source). Adding a candidate by hand (with a resume), server search behind Ctrl K and a Last activity column on every candidate table are built | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | Nothing shows how a team's hiring goes over time |
| 7 | Talent pool: "Keep in mind" sits beside Reject, for a strong candidate the team did not hire, from the candidate page, a selection or when a job closes. The candidate leaves the job without a rejection, and their answers, files, scorecards, notes, Verification results and Activity stay with them. The pool has search, tags and the note saying why; from it, the team invites someone to apply for a new job or adds them to one, and the new application links back to the old ones ([talent pool](architecture/talent-pool.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | A runner-up is often the next hire, and today the only ways out of a job are Rejected or leaving them in a closed one. Keeping someone's data for future roles needs their permission in the UK and EU, so the apply form asks ("Keep my details for future roles"), the pool shows when each person's permission runs out, and an erasure request removes them from it ([legal](research/legal.md)) |
| 9 | Connect to Slack: a Slack app an Admin installs for the organization from Settings, which posts the events a team picks (a new application, a candidate moved, an automation that failed, a job published) to a channel, and sends each person their own mentions and assigned tasks as direct messages, each linking to the record. Behind an interface with a local fake, like email, so no test needs Slack; integrity levels post only as a link to the candidate, never as a verdict in a channel | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | Teams live in Slack, and webhooks need someone to build the bridge themselves |
| 10 | Teammates' changes as toasts: while someone has the dashboard open, a change a teammate makes to something they work on (a candidate on a job they are on, a template, a task they were given) shows as a short toast naming who did what, with a link, from the live events the dashboard already receives; each person picks which kinds they want, and their own changes never notify them | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | Live updates already refresh the page you are on; changes elsewhere go unnoticed until someone looks |

## Later

- **Job boards:** Indeed and ZipRecruiter (phase 2, with paying customers), LinkedIn, then European boards
  ([job boards](research/job-boards.md), [job distribution](architecture/job-distribution.md)).
- **Careers:** custom tenant domains (ADR-0003).
- **Candidate portal:** a candidate's own page with each application's status, the messages and any questionnaire to
  answer, signed in with a link sent by email.
- **The recruiter dashboard inside a customer's own site:** only if a customer asks. Sign-in cookies are SameSite Lax
  or Strict and do not reach a cross-site frame, Google's and Microsoft's sign-in pages refuse to load in a frame, and
  allowing framing invites clickjacking. The API and webhooks are the usual way to integrate.
- **Integrity phase 2:** identity and liveness checks, with a "why we asked" page and an appeal flow
  ([integrity engine](architecture/integrity-engine.md) §9).
- **Integrity phase 3:** interview integrity.
- **Signal Network:** built and off until a legal review and DPIA (ADR-0005).
- **A webhook endpoint's change history:** its row holds the signing secret, which change history never reads, so
  the secret moves to a row of its own first (ADR-0016, [change history](architecture/change-history.md)).
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
