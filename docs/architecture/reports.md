---
type: Design
title: "Reports: hiring funnel, time to hire and sources"
description: "The Reports page and GET /v1/reports/hiring: what the funnel, time to hire and sources count, the raw SQL that computes them under row-level security and job access, the date range in the person's own days, why no verification data appears, and the known limits."
tags: [reports, sql, tenancy, dashboard]
sources:
  - resource: apps/api/Reports/HiringReportReads.cs
  - resource: apps/api/Endpoints/ReportEndpoints.cs
  - resource: apps/api/Contracts/ReportContracts.cs
  - resource: apps/dashboard/src/routes/(app)/reports/+page.svelte
  - resource: apps/dashboard/src/lib/report-range.ts
  - resource: apps/dashboard/src/lib/reports.ts
---
# Reports: hiring funnel, time to hire and sources

Roadmap item 5's reports. One page in the dashboard (design system section 21, Reports) and one endpoint that feeds it.

## 1. The endpoint

`GET /v1/reports/hiring?from=...&to=...&jobId=...` (`apps/api/Endpoints/ReportEndpoints.cs`), named `GetHiringReport`.

- **Session only.** `.RequireAuthorization()`: API keys reach only the hiring API groups (ADR-0011), so a key gets 401.
  A report tool for the MCP server, or key access, is a later change.
- **Every member.** No `.RequirePermission`: reads stay open to every member (ADR-0014), and See jobs and candidates is
  in every role. Job access (ADR-0015) narrows the numbers, never the page.
- **The range is two instants**, `from` inclusive and `to` exclusive, both required. The API never computes "the last
  30 days": the browser turns the person's days into their local midnights (`#lib/report-range.ts`, design system W8),
  so the numbers follow the person's own calendar and tests can name an exact window. `from` must be before `to`, and
  the range covers 5 years at most; otherwise 400.
- **`jobId`**, repeated, narrows every panel to those jobs (200 at most). An id the person cannot see, or that does not
  exist, adds nothing: the answer is the same as for a job with no applications, so it says nothing about hidden jobs.
- One response, `HiringReport`, carries the three panels. Its three queries run in one `REPEATABLE READ` transaction, so
  they read one snapshot and their numbers agree: the funnel's Hired row equals the sources' hires.

## 2. What the numbers count

| Panel | Counts | Basis |
|---|---|---|
| Hiring funnel | Applications that reached each stage kind or a later one, and how many of those moved on to a later row | Applications submitted in the range |
| Time to hire | Median days from submitting to entering a Hired stage, per job and over every hire, with the hire counts | Hires made in the range, whenever they applied |
| Sources | Applications per `ApplicationSource`, and how many of them are hired now | Applications submitted in the range |

- **Rows by stage kind.** Each job names its own stages; the funnel groups them by kind, in `StageKind`'s order:
  Applied, Screening, Assessment, Interview, Offer, Hired (`HiringReportReads.FunnelKinds`). A row shows when a job with
  an application in the range has that kind; Applied and Hired always do.
- **Furthest reached.** An application counts at every row up to the furthest kind it entered, read from its
  `Submitted` and `StageChanged` timeline entries. A skipped stage counts as passed (Applied straight to Interview
  counts at Screening), a rejection counts where it happened (rejected after Interview counts at Interview), and
  "moved on" is the share of a row whose furthest kind is after it, so it never passes 100%.
- **Hired means hired now.** An application counts as hired while its current stage is a Hired one. One moved back out
  of Hired counts at the furthest other kind it reached. Its hire time is `stage_entered_at`, exact while it sits in
  that stage.
- **Time to hire is by hire date.** Counting by applied date would favour short times: in a 30-day window only quick
  hires would show. The overall figure is the median of every hire, not a median of the jobs' medians
  (`percentile_cont(0.5)`, with an empty grouping set for the overall row).

## 3. Scope: raw SQL under row-level security

`HiringReportReads` runs raw SQL (`Database.SqlQuery`), because the stage kinds live in each job's `workflow` JSON,
which LINQ cannot reach through the column's converter, and because it takes a median. EF's named query filters do not
apply to raw SQL, so row-level security is the only scope, and it is enough: `TenantSessionInterceptor` sets
`app.tenant_id`, `app.job_scope` and `app.member_id` for every command, and `jobs`, `applications` and
`application_events` carry the tenant policy and the restrictive `job_scope` policy (`sign-in-and-tenancy.md`, Job
access). The queries read those three tables only, never `candidates`, whose policy is tenant-only. They add no
`organization_id` filter of their own (CLAUDE.md). `ReportsTests` checks another organization's history, a member on
Only jobs they're on, and a filter naming a hidden job.

## 4. No verification data

Reports show no levels, no signals and no counts by level (ADR-0004). They read no verification table. A
**Verification-kind stage has no row** either: verification conditions may route candidates into it
(`DefinitionValidator.SignalSafeStages`), so counting it would count what the checks found. An application that passed
through one counts at the rows it reached around it. Custom stages have no row because they have no fixed place in a
pipeline, and Rejected and Withdrawn end an application without moving it on.

## 5. The page

`apps/dashboard/src/routes/(app)/reports/+page.svelte`, on the Report template (design system section 7). Dates and Job
sit in the header band and live in the address (`?range=90`, `?from=2026-01-01&to=2026-03-31`, `?job=...` repeated), so
a link or the trip back keeps them. Each panel is a `ChartPanel` with its table view. While new numbers load the panels
keep the old ones, dimmed. `ApplicationSubmitted` and `ApplicationMoved` refresh `/v1/reports` (`#lib/realtime.ts`).
Chart colours are the two chart tokens (design system section 3.5).

## 6. Known limits

- **Kinds come from each job's current workflow.** Only the current workflow is kept, so a timeline entry pointing at
  a stage removed since adds nothing, and a stage whose kind changed is read as it is now.
- **Two sources.** `ApplicationSource` is Careers page or Added by a recruiter. Nothing yet records which job board sent
  a careers applicant; that needs a source on the apply link, and the sources panel will show it once it is stored.
- **No MCP tool or key access** (section 1).
