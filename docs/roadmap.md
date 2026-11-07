---
type: Reference
title: "Roadmap"
description: "The order of work after the MVP: what each finished workflow delivered and in which PRs, what comes next (page audits, editing a posting for one board, hosting readiness), later items, and what must exist before real customers."
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
| A remote MCP server on the API (`/v1/mcp`) for a team's AI assistant, signed in with an organization API key: find jobs and candidates, read an application, add notes and tags, create tasks, move a candidate, read reports and saved candidates, and with a scope an Owner gives, read verification results ([MCP server](architecture/mcp-server.md)) | "Design system v0.3, the review's features, and roles with job access" | #59 |
| Rich text for job descriptions and email templates: a small editor with a toolbar (bold, italic, headings, bulleted and numbered lists, links) in New job, Edit details, email templates and the composer, variables as chips, stored as sanitized HTML limited to the toolbar's elements and rendered on the careers pages, in Google for Jobs, in feeds and in email; existing plain text converted by migration ([rich text](architecture/rich-text.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | the rich text task |
| Branding (roadmap item 6): a brand colour and a second colour checked against WCAG 2.2 AA before they save, with the nearest passing shade, a heading font from six ScreenHire hosts, and a logo, favicon and sharing image uploaded through storage with the malware scan, on the careers pages, the apply flow and the top of every email to candidates ([branding](architecture/branding.md), ADR-0018) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | The branding task |
| Questionnaire sections with progress and save-and-resume: named sections in the form builder or an automatic split at each organization's questions per page, one page each on the apply form and follow-up questionnaires with "Step 2 of 4" and "9 of 24 answered", a draft under the candidate's email with a signed link to finish later, the submit-velocity check on active time per page, and no knockout before submission ([apply flow](architecture/apply-flow.md) §7, [verification engine](architecture/verification-engine.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | this workflow |
| Embeddable careers widget (roadmap item 3): one line of embed code from Settings, Organization puts the job list and the apply flow in a frame on a company's own site, from the careers origin, so the notice, telemetry and Verification checks stay intact; the https sites allowed to frame the careers pages set `frame-ancestors`, the frame sizes itself over postMessage from the careers origin only, the flow stays cookie-free, and the application records the site ([careers widget](architecture/careers-widget.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | The careers widget task |
| Teammates' changes as toasts (roadmap item 10): while the dashboard is open, a teammate's change to a candidate on a job the person is on, a task given to them or a hiring template they changed last shows as a toast saying who did what, with a link; events now carry who made the change; each person picks the kinds in a Notifications sheet from the account menu ([real-time updates](architecture/realtime.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | This task's PR |
| Candidate replies and sending through SendGrid, bought on the Azure Marketplace (roadmap item 1): every email to a candidate carries a signed reply address, so the answer lands in Messages and Activity with its attachments scanned and the person who sent the email told; bounces and spam complaints flag the address and stop further email; an Owner or Admin can add the company's own domain under Settings, Sending domain, and mail comes from it once SendGrid verifies its DNS records; a local sandbox needs no SendGrid ([email](architecture/email.md), ADR-0022) | "Owner review of the second build: saved candidates, one candidate across jobs, candidate header, reports, views, replies, scheduling and MCP" | the replies task |
| Interview scheduling (roadmap item 4): members connect their own Google or Outlook calendar from the account menu, a recruiter offers an interview from the candidate's page or a `ScheduleInterview` automation, and the candidate picks a time free for every interviewer on a no-account, no-script careers page, then moves or cancels it from the same link; one calendar event per interviewer with a Meet or Teams link, two interviewers never booked at once, a sandbox calendar for local use, and the `InterviewScheduled` trigger ([interview scheduling](architecture/interview-scheduling.md), ADR-0024) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | The interview scheduling task |
| Reports (roadmap item 5): headline numbers, a weekly trend, the funnel, days in each stage, sources and open jobs; adding a candidate by hand with a resume, server search behind Ctrl K and a Last activity column ([reports](architecture/reports.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items"; "Owner review of the second build" | #61 to #74, #81 |
| Saved candidates (roadmap item 7, first built as the talent pool): Save candidate beside Reject, kept with no end date until the team removes them or they stop it, with Rating, Last contacted and a note the team can edit ([saved candidates](architecture/talent-pool.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items"; "Owner review of the second build" | #61 to #74, #84 |
| Connect to Slack (roadmap item 9): an Admin installs the app from Settings, a channel gets the events the team picks, and each person gets their own mentions and tasks ([Slack](architecture/slack.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | #61 to #74 |
| Candidate data requests and record retention (roadmap item 13): a candidate asks for a copy of their data or to be erased from the careers privacy page through an emailed link, the team answers in Settings, Data requests (Send a copy, Erase, Decline) against a due date, and an off-by-default retention setting deletes ended applications daily after a dry-run count ([data requests](architecture/data-requests.md)) | "Production polish: page audits, per-board postings, data requests and hosting readiness" | the data requests task |
| Hosting readiness (roadmap item 14): the Azure definition, an AppHost publish model with the Bicep it generates under `infra/` and `azure.yaml`, a migrator that creates the schema as the database owner, production settings that stop a host naming what is missing, secrets and the Data Protection key in Key Vault, a CI check that the definition still generates, and the [deploy runbook](operations/deploy.md) (ADR-0007). Nothing is deployed | "Production polish: page audits, per-board postings, data requests and hosting readiness" | The hosting readiness task |
| Careers and marketing pages on the current design system (v0.3): the careers titles on one token and every section a panel with the soft shadow, buttons and inputs at the 8px radius and 44px height, margins and gaps on the 4px grid, marketing cards and buttons on the same tokens, and one status badge for the marketing pages, with `public-style.test.ts` failing on a regression; employer colours are still checked on the page and the panel, and the pages still ship no JavaScript beyond the apply island ([design system](design/design-system.md) sections 4.3 and 4.4) | "Owner review of the fourth build, and the roadmap's open items" | The careers and marketing pages task |
| Candidate portal: a candidate's own page on each careers site, reached by a signed emailed link with no account and the same answer for any address, with each application's job and date on four fixed steps (Applied, In review, Interview, Decision, then Hired, Not moving forward or Withdrawn) that never show the employer's stage names, a saved application to finish, a questionnaire or interview waiting, and the messages with a reply box that goes through the same ingest as an emailed reply; every email to a candidate now ends with the link ([candidate portal](architecture/candidate-portal.md), ADR-0027) | "Owner review of the fourth build, and the roadmap's open items" | The candidate portal task |

## Next

| # | Work | Workflow | Why now |
|---|---|---|---|
| 11 | Page audits: every dashboard, careers and marketing page checked against the [design system](design/design-system.md) and for usability, and fixed | "Production polish: page audits, per-board postings, data requests and hosting readiness" | The features work; UI gaps are what keep the app from being ready for customers |
| 12 | Edit a job posting for one board or for every board, so a board that refuses a job can be fixed without changing the others ([job distribution](architecture/job-distribution.md)) | "Production polish: page audits, per-board postings, data requests and hosting readiness" | Today the only fix for one board is editing the job for all of them |

## Later

- **Job boards:** Indeed and ZipRecruiter (phase 2, with paying customers), LinkedIn, then European boards
  ([job boards](research/job-boards.md), [job distribution](architecture/job-distribution.md)).
- **Careers:** custom tenant domains (ADR-0003).
- **The recruiter dashboard inside a customer's own site:** only if a customer asks. Sign-in cookies are SameSite Lax
  or Strict and do not reach a cross-site frame, Google's and Microsoft's sign-in pages refuse to load in a frame, and
  allowing framing invites clickjacking. The API and webhooks are the usual way to integrate.
- **Verification phase 2:** identity and liveness checks, with a "why we asked" page and an appeal flow
  ([verification engine](architecture/verification-engine.md) §9).
- **Verification phase 3:** interview verification.
- **Signal Network:** built and off until a legal review and DPIA (ADR-0005).

## Before real customers

- **Hosting:** defined and written down, not deployed. The [deploy runbook](operations/deploy.md) lists every owner step. CI runs on GitHub Actions (ADR-0020).
- **Billing:** none exists.
- **Compliance** ([legal](research/legal.md)): bias audits,
  Colorado and California automated-decision rules (due 1 January 2027), EU AI Act provider duties (due 2 December
  2027), the customer documentation pack.
- **Accessibility:** an external WCAG 2.2 audit before general availability (ADR-0009).
- **Owner actions:** the accounts, keys and decisions in the [MVP status](status.md#owner-actions).

## Start now, outside the code

- **LinkedIn partner application:** approval takes 3 to 6 months ([job boards](research/job-boards.md)).
- **The Azure subscription and the domains:** both gate going live.
