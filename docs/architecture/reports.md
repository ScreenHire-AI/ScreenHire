---
type: Design
title: "Reports: headline numbers, trend, funnel, time to hire, sources and open jobs"
description: "The Reports page and GET /v1/reports/hiring: what each panel counts, the raw SQL that computes it under row-level security and job access, the date range and time zone in the person's own days, the previous period, the CSV export, why no verification data appears, and the known limits."
tags: [reports, sql, tenancy, dashboard]
sources:
  - resource: apps/api/Reports/HiringReportReads.cs
  - resource: apps/api/Endpoints/ReportEndpoints.cs
  - resource: apps/api/Contracts/ReportContracts.cs
  - resource: apps/dashboard/src/routes/(app)/reports/+page.svelte
  - resource: apps/dashboard/src/lib/report-range.ts
  - resource: apps/dashboard/src/lib/reports.ts
  - resource: apps/dashboard/src/lib/csv.ts
  - resource: apps/dashboard/src/lib/candidate-filters.ts
  - resource: apps/dashboard/src/lib/components/week-chart.svelte
  - resource: apps/dashboard/src/lib/components/report-stat.svelte
---
# Reports: headline numbers, trend, funnel, time to hire, sources and open jobs

Roadmap item 5's reports. One page in the dashboard (design system section 21, Reports), one endpoint that feeds it and
one that lists the applications behind a funnel row.

## 1. The endpoints

`GET /v1/reports/hiring` (`apps/api/Endpoints/ReportEndpoints.cs`), named `GetHiringReport`.

- **Session only over HTTP.** `.RequireAuthorization()`: API keys reach only the hiring API groups (ADR-0011), so a key
  gets 401. The MCP server's `get_hiring_report` tool reads the same numbers for any key with the Read scope, by calling
  `ReportEndpoints.Problems` and `HiringReportReads.ReadAsync` directly (ADR-0021 decision 6). A key sees every job
  (ADR-0015), so the tool's numbers are not narrowed by job access.
- **Every member.** No `.RequirePermission`: reads stay open to every member (ADR-0014), and See jobs and candidates is
  in every role. Job access (ADR-0015) narrows the numbers, never the page.
- **The range is two instants**, `from` inclusive and `to` exclusive, both required. The API never computes "the last
  30 days": the browser turns the person's days into their local midnights (`#lib/report-range.ts`, design system W8),
  so the numbers follow the person's own calendar and tests can name an exact window. `from` must be before `to`, and
  the range covers 5 years at most; otherwise 400.
- **The previous range** is `previousFrom` and `previousTo`, both or neither, with the same limits. The browser builds
  it as the same number of local days ending where the range starts (`previousInstants`), so a clock change keeps whole
  days. With it, the response carries `previous`, the headline of that range; without it, `previous` is null.
- **`timeZone`** is the person's IANA zone (`Europe/London`), used only to cut weeks at their own midnights. It is checked
  against `pg_timezone_names` (400 otherwise) and defaults to UTC.
- **`jobId`**, repeated, narrows every panel to those jobs (200 at most). An id the person cannot see, or that does not
  exist, adds nothing: the answer is the same as for a job with no applications, so it says nothing about hidden jobs.
- One response, `HiringReport`, carries every panel. Its queries run in one `REPEATABLE READ` transaction, so they read one
  snapshot and their numbers agree.

`GET /v1/reports/hiring/funnel/{kind}/applications` (`GetFunnelApplications`) takes the same range and `jobId` and returns
the ids of the applications submitted in the range that reached that stage kind or a later one: exactly the number the
funnel row shows. The candidates page uses it for a funnel link (section 5). A kind with no funnel row is a 404.

## 2. What the numbers count

| Panel | Counts | Basis |
|---|---|---|
| Headline: Applications, Reached interview, Offers | Applications that reached the Applied, Interview and Offer kinds or a later one | Applications submitted in the range |
| Headline: Hires, Median time to hire | Hires made, and the median days from submitting to being hired | Hires made in the range, whenever they applied |
| Applications and hires by week | Applications submitted per week, and hires made per week | Each by its own date, inside the range |
| Hiring funnel | Applications that reached each stage kind or a later one, and how many of those moved on to a later row | Applications submitted in the range |
| Time to hire | Median days each hire spent in each stage kind, and the median days to hire by job | Hires made in the range |
| Sources | Applications per source, and how many of them are hired now, with the hire rate | Applications submitted in the range |
| Open jobs | Per open job: applications in the range, applications in an Interview stage now, days open, hires in the range | See below |

- **Two bases, said once.** Applications, Reached interview, Offers, the funnel and Sources count the applications that
  arrived in the range, so the Reached interview and Offers cards equal the funnel's Interview and Offer rows. Hires,
  the median and the weekly hires count the hires made in the range, so the card, the sum of the weekly hires and the
  hires behind the median agree (`ReportsTests` asserts it). The funnel's Hired row and the Sources hires are the other
  basis: applied in the range and hired now. Time to hire is by hire date because counting by applied date would favour
  short times: in a 30-day window only quick hires would show.
- **Rows by stage kind.** Each job names its own stages; the funnel groups them by kind, in `StageKind`'s order:
  Applied, Screening, Assessment, Interview, Offer, Hired (`HiringReportReads.FunnelKinds`). A row shows when a job with
  an application in the range has that kind; Applied and Hired always do. The headline's Reached interview and Offers use
  the same rule but count even when no job has that row.
- **Furthest reached.** An application counts at every row up to the furthest kind it entered, read from its
  `Submitted` and `StageChanged` timeline entries. A skipped stage counts as passed (Applied straight to Interview
  counts at Screening), a rejection counts where it happened (rejected after Interview counts at Interview), and
  "moved on" is the share of a row whose furthest kind is after it, so it never passes 100%.
- **Hired means hired now.** An application counts as hired while its current stage is a Hired one. One moved back out
  of Hired counts at the furthest other kind it reached. Its hire time is `stage_entered_at`, exact while it sits in
  that stage.
- **Median over every hire**, not a median of the jobs' medians (`percentile_cont(0.5)`).
- **Weeks** start on Monday in the person's zone. Weeks with nothing in them are still listed, and the first and last
  are partial: only what falls inside the range counts. The table and the readout name a week with its year, because a
  range can span years; only the chart's axis drops it.
- **Days in each stage.** For each hire made in the range, the time between entering a stage and entering the next (the
  last stay ends when the hire entered its Hired stage), summed per kind when the hire passed through it twice, then
  the median over the hires that were in that kind. A stay in a Verification or Custom stage separates the stays around
  it but is not counted (section 4).
- **Source** is one key per application: `recruiter`, else `website` when the careers page was embedded in the
  organization's own site (`SourceSite`, careers-widget.md), else `board:{id}` when the candidate followed a job
  board's apply link (`SourceBoard`, the `src` tag in apply-flow.md and job-distribution.md), else `careers`. The board
  tag is self-reported: anyone can open a link with `src=talent`, so it can move an organization's own Sources numbers,
  never another's.
- **Open jobs** are the jobs with status Open that the person sees, narrowed by the job filter. Days open is whole days
  since the job was first published, by the API's clock. In interview is now, not in the range.
- **The biggest drop** and the share of applicants are computed in the browser from the funnel rows
  (`#lib/reports.ts`, `biggestDrop`): the row below the last with the lowest moved-on share, among rows with at least 3
  applications, the earlier row on a tie.

## 3. Scope: raw SQL under row-level security

`HiringReportReads` runs raw SQL (`Database.SqlQueryRaw` with named `NpgsqlParameter`s), because the stage kinds live in
each job's `workflow` JSON, which LINQ cannot reach through the column's converter, and because it takes medians. The
queries share their CTE text (the jobs in scope, their stage kinds, the cohort, the furthest row, the hires) and carry
every value as a parameter, the time zone included; nothing is concatenated into SQL. EF's named query filters do not
apply to raw SQL, so row-level security is the only scope, and it is enough: `TenantSessionInterceptor` sets
`app.tenant_id`, `app.job_scope` and `app.member_id` for every command, and `jobs`, `applications` and
`application_events` carry the tenant policy and the restrictive `job_scope` policy (`sign-in-and-tenancy.md`, Job
access). The queries read those three tables only, never `candidates`, whose policy is tenant-only. They add no
`organization_id` filter of their own (CLAUDE.md). `ReportsTests` checks another organization's history, a member on
Only jobs they're on, a filter naming a hidden job, and the funnel link's ids for the same member.

## 4. No verification data

Reports show no levels, no signals and no counts by level (ADR-0004). They read no verification table. A
**Verification-kind stage has no row** either: verification conditions may route candidates into it
(`DefinitionValidator.SignalSafeStages`), so counting it would count what the checks found. An application that passed
through one counts at the rows it reached around it, and its time in that stage is not shown. Custom stages have no row
because they have no fixed place in a pipeline, and Rejected and Withdrawn end an application without moving it on.

## 5. The page

`apps/dashboard/src/routes/(app)/reports/+page.svelte`, on the Report template (design system section 7). Dates, Job and
the Compare switch sit in the header band and live in the address (`?range=90`, `?from=2026-01-01&to=2026-03-31`, `?job=...`
repeated, `?compare=off`), so a link or the trip back keeps them. Export CSV is the band's one action.

- **Headline cards** (`ReportStat`): five numbers, each with its change from the previous period in words and an arrow
  ("18 more than before", "3 days faster"), always muted text, never a status colour. Compare off, or a previous period
  with nothing to compare, shows no change line.
- **Panels** are `ChartPanel`s with a table view; the weekly chart is `WeekChart`. No sentence sits under a panel's title
  (design system W9); what a panel counts is read out to screen readers only. While new numbers load the panels keep the
  old ones, dimmed. Chart colours are the two chart tokens (design system section 3.5).
- **Funnel links.** Each stage name links to `/candidates?reached={kind}&from=...&to=...&job=...`. The candidates page
  asks `GetFunnelApplications` for the ids and shows only those applications, with a chip that clears the filter, so the
  list matches the number on the funnel.
- **Export CSV** is built in the browser from the loaded report: one UTF-8 file with a section per panel (title row,
  header row, rows, blank line), numbers as numbers, and recruiter-typed text such as job titles quoted so a spreadsheet
  never runs them as formulas (`#lib/csv.ts`).
- Live updates: `ApplicationSubmitted`, `ApplicationMoved`, `JobPublished`, `JobClosed`, `JobWorkflowChanged` and
  `HiringTeamChanged` refresh `/v1/reports` (`#lib/realtime.ts`).

## 6. Known limits

- **Kinds come from each job's current workflow.** Only the current workflow is kept, so a timeline entry pointing at
  a stage removed since adds nothing (the time in that stage counts for no row), and a stage whose kind changed is read
  as it is now.
- **Job board sources cover feed boards.** The `src` tag goes on the apply link of the XML feed, Careerjet, Talent.com,
  Adzuna, Jooble and the sandbox board. Google for Jobs and the public job-board API must keep their canonical URLs, so
  their applicants count as Careers page, as do applications finished from a saved draft link, which drops the tag.
- **Weeks follow Monday.** The first day of the week is not taken from the person's locale.
- **No key access over HTTP** (section 1). Only the MCP tool reads reports for a key.
