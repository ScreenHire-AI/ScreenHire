---
type: Reference
title: "Roadmap"
description: "The order of work after the MVP: what each finished workflow delivered and in which PRs, what comes next, later items, and what must exist before real customers."
tags: [roadmap, planning]
---
# Roadmap

What comes after the MVP, in order. What is already built is in the [MVP status](status.md). Snapshot 2026-11-09;
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
| Embeddable careers widget (roadmap item 3): one line of embed code from Settings, Careers site, Careers page puts the job list and the apply flow in a frame on a company's own site, from the careers origin, so the notice, telemetry and Verification checks stay intact; the https sites allowed to frame the careers pages set `frame-ancestors`, the frame sizes itself over postMessage from the careers origin only, the flow stays cookie-free, and the application records the site ([careers widget](architecture/careers-widget.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | The careers widget task |
| Teammates' changes as toasts (roadmap item 10): while the dashboard is open, a teammate's change to a candidate on a job the person is on, a task given to them or a hiring template they changed last shows as a toast saying who did what, with a link; events now carry who made the change; each person picks the kinds in a Notifications sheet from the account menu ([real-time updates](architecture/realtime.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | This task's PR |
| Candidate replies and sending through SendGrid, bought on the Azure Marketplace (roadmap item 1): every email to a candidate carries a signed reply address, so the answer lands in Messages and Activity with its attachments scanned and the person who sent the email told; bounces and spam complaints flag the address and stop further email; an Owner or Admin can add the company's own domain under Settings, Organization, Email domain, and mail comes from it once SendGrid verifies its DNS records; a local sandbox needs no SendGrid ([email](architecture/email.md), ADR-0022) | "Owner review of the second build: saved candidates, one candidate across jobs, candidate header, reports, views, replies, scheduling and MCP" | the replies task |
| Interview scheduling (roadmap item 4): members connect their own Google or Outlook calendar from the account menu, a recruiter offers an interview from the candidate's page or a `ScheduleInterview` automation, and the candidate picks a time free for every interviewer on a no-account, no-script careers page, then moves or cancels it from the same link; one calendar event per interviewer with a Meet or Teams link, two interviewers never booked at once, a sandbox calendar for local use, and the `InterviewScheduled` trigger ([interview scheduling](architecture/interview-scheduling.md), ADR-0024) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | The interview scheduling task |
| Reports (roadmap item 5): headline numbers, a weekly trend, the funnel, days in each stage, sources and open jobs; adding a candidate by hand with a resume, server search behind Ctrl K and a Last activity column ([reports](architecture/reports.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items"; "Owner review of the second build" | #61 to #74, #81 |
| Saved candidates (roadmap item 7, first built as the talent pool): Save candidate beside Reject, kept with no end date until the team removes them or they stop it, with Rating, Last contacted and a note the team can edit ([saved candidates](architecture/talent-pool.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items"; "Owner review of the second build" | #61 to #74, #84 |
| Connect to Slack (roadmap item 9): an Admin installs the app from Settings, a channel gets the events the team picks, and each person gets their own mentions and tasks ([Slack](architecture/slack.md)) | "After the owner's review: scorecards, verification review, candidates, reports, branding and the roadmap's next items" | #61 to #74 |
| Candidate data requests and record retention (roadmap item 13): a candidate asks for a copy of their data or to be erased from the careers privacy page through an emailed link, the team answers in Organization, Data requests (Send a copy, Erase, Decline) against a due date, and an off-by-default retention setting deletes ended applications daily after a dry-run count ([data requests](architecture/data-requests.md)) | "Production polish: page audits, per-board postings, data requests and hosting readiness" | the data requests task |
| Hosting readiness (roadmap item 14): the Azure definition, an AppHost publish model with the Bicep it generates under `infra/` and `azure.yaml`, a migrator that creates the schema as the database owner, production settings that stop a host naming what is missing, secrets and the Data Protection key in Key Vault, a CI check that the definition still generates, and the [deploy runbook](operations/deploy.md) (ADR-0007). Nothing is deployed | "Production polish: page audits, per-board postings, data requests and hosting readiness" | The hosting readiness task |
| Careers and marketing pages on the current design system (v0.3): the careers titles on one token and every section a panel with the soft shadow, buttons and inputs at the 8px radius and 44px height, margins and gaps on the 4px grid, marketing cards and buttons on the same tokens, and one status badge for the marketing pages, with `public-style.test.ts` failing on a regression; employer colours are still checked on the page and the panel, and the pages still ship no JavaScript beyond the apply island ([design system](design/design-system.md) sections 4.3 and 4.4) | "Owner review of the fourth build, and the roadmap's open items" | The careers and marketing pages task |
| Candidate portal: a candidate's own page on each careers site, reached by a signed emailed link with no account and the same answer for any address, with each application's job and date on four fixed steps (Applied, In review, Interview, Decision, then Hired, Not moving forward or Withdrawn) that never show the employer's stage names, a saved application to finish, a questionnaire or interview waiting, and the messages with a reply box that goes through the same ingest as an emailed reply; every email to a candidate now ends with the link ([candidate portal](architecture/candidate-portal.md), ADR-0027) | "Owner review of the fourth build, and the roadmap's open items" | The candidate portal task |
| Page audits (roadmap item 11): every dashboard, careers and marketing page checked against the [design system](design/design-system.md) and for usability, and fixed, then a second pass for the states and widths the first one skipped | "Production polish: page audits, per-board postings, data requests and hosting readiness" | #91 to #93, #95, #97, #105, #110 |
| Edit a job posting for one board (roadmap item 12), so a board that refuses a job can be fixed without changing the others ([job distribution](architecture/job-distribution.md)) | "Production polish: page audits, per-board postings, data requests and hosting readiness" | #94 |
| The owner's review of the fourth build: a stage flow for a workflow's stages, one file upload with drag and drop, interview reminders and new times for a booked interview, webhook endpoint history, OAuth sign-in for the MCP server (ADR-0028), a Candidates switch and settings as three areas, the workflow graph rebuilt, Branding on one screen, Simulate as a guided test, an organization's own AI key and model, and billing (ADR-0029) | "Owner review of the fourth build, and the roadmap's open items" | #99, #100, #102 to #104, #107 to #109, #111 to #114 |
| Marketing site rebuilt in direction B + C (roadmap item 15): design rules and a shared shell ([design system](design/design-system.md) section 22, ADR-0030), the [messaging](design/marketing-site.md) as five selling points told as pain, promise and proof with sourced numbers, a home page that tells them in order, pricing read from the plan catalogue with every feature on every plan, a product overview and one page per selling point with screenshots captured from the demo data, and compare, hiring-AI laws, blog and 404 on the same shell; then an audit of every page at 390 to 1920 px, a design review, Impeccable's detector and Lighthouse, and fixes | "Marketing site: direction B + C" | #118 to #123, and the audit task |
| Launch readiness: the November redesign (one Settings home with Verification in the sidebar, a template builder with starting points and a live Test tab, the candidate page with one-word verification states, a notifications bell and a Jobs overview, a scorecards roster), built-in AI (provider presets, a usage ledger, budgets, alerts, a cap and the Verification area), billing (trial reminders, plan-change cost, Stripe Tax, AI use charged at the provider's price plus a margin), proof of what had never been walked (dark mode and phone widths on every dashboard route, large lists, the Slack and webhook states), and four design reviews of the first hour, a recruiter's day, setup and the candidate's phone ([design](design/redesign-2026-11.md)) | "Launch readiness: the November redesign, built-in AI, billing and proving the untested states" | #128 to #149 |

## Next

### The owner's review of the launch-readiness build

Four design reviews walked the build as a new team in its first hour, as a recruiter in a working day, as someone
setting ScreenHire up, and as a candidate on a phone. Each fixed what was contained (#146 to #149) and left the rest
for the owner. This is that rest, merged, with duplicates removed and ranked. Nothing in it is built. Pick the items
you want and they become the next workflow, then the owner picks from "Later" and "Before real customers" below.

How to read it:

- **Rank** is within a group, by how much the change helps a small team against its size. Number 1 is the best value.
- **Size**: S is a few files and a test. M is a client and API change, or a flow, with tests. L is a new flow across
  several screens.
- #147 closed with decisions and no proposals list, so its items here come from the alternatives it named for a
  choice it kept. Where #148 already built part of a #149 proposal, only the rest is listed.
- A few items need a legal call first. They say so.

**Best value first, across the groups:** make the setup checklist findable (S), check a job before it goes live (M),
announce a new application (S to M), say what "Open jobs 1 of 10" counts (S), the interview picker on a phone (M) and
a country shown as a name (S to M).

#### Real people

1. **Budgets in dollars by default.** The AI budget shows tokens until someone opens the Usage tab, and a token count
   means little to a small team. Default the budget and cap to a dollar figure priced from the deployment's
   `DefaultMonthlyTokenBudget`. Size S. (#147)
2. **Say "Information only" once on the Verification tab.** It appears in the map tile and again in the finding row;
   the redesign says once, in the tooltip. Size S. (#148)
3. **Make the audit line the page's last line.** "No automated decision has been made..." sits inside the Form use
   panel and reads as its footer; design system W10 wants it last on the page. Size S. (#148)

#### Never guessing

1. **Announce a new application.** After the first application the only signal is two Jobs tiles ticking up. #148
   added a Candidates column to the Jobs table; what is left is a toast or a bell line. A candidate has no actor, so
   teammate-change toasts skip an application today. Size S to M. (#149)
2. **Small readability fixes in Reports and Jobs.** The change pill reads "up 10" with no unit outside its tooltip, a
   zero in a bar row ("Hired 0 0%") sits on its own line away from the bars, and the Jobs home's Published and Last
   updated headers sit about 10 px off their values. Size S for all three. (#148)

#### Automate anything

1. **Name a stage's meeting.** Interview reminders and the booking page say "interview" for a trial shift. A
   per-stage meeting name fixes the word. It is a backend change. Size S to M. (#147)

#### Integrations

Nothing was proposed. The reviews ran against the local stand-ins, so real Slack, calendars, SendGrid and a claude.ai
connector are still untested by hand ([MVP status](status.md#known-gaps)).

#### Built your way, and the first hour

1. **Make the setup checklist findable.** It lives only in the bell, which shows an unlabelled count, and the Jobs
   empty state never mentions it. Label the badge on a new organization ("Setup 5"), point to it from the empty state,
   or open the bell once after onboarding. Size S. (#149)
2. **Check a job before it goes live.** Three of the five built-in templates ship fill-in prompts as the description,
   so a new Owner can publish "Describe the work, the office and the team." to the public careers page, and some boards
   refuse an on-site job without a city only after publishing. Warn, not block, when Publish is pressed, with a link
   to Edit details. Size M. (#149)
3. **Say what "Open jobs 1 of 10" counts.** The tile does not say 10 is the plan's limit. "1 of 10 on your plan",
   linking to Billing, if it fits a phone tile. Size S. (#149)
4. **Tell a new Owner about the trial.** Only Billing, the bell near the end and a "0 of 10" tile say there is one, how
   long it is and that it needs no card. One line on onboarding and a quiet "Trial ends" line in the sidebar footer for
   Owners. Size S to M. (#149)
5. **Orient someone who accepts an invitation.** They land on Jobs with no word on their role or who invited them. A
   one-time line ("You joined Harbour Lane Studio as Recruiter. Jobs you can see are below."). Size S. (#149)
6. **Fix the careers page setup step.** It ticks only when someone embeds the page or changes questions per page, so
   a team that keeps the defaults can only dismiss the whole checklist. Drop the step or merge it into Branding; the
   count goes from five to four, and the tests and docs that say five change with it. Size S. (#149)
7. **Check the scorecard's 1 to 5 buttons on a phone.** They looked about 36 px, and design system F4 wants 44 px under
   `pointer: coarse`. A check first, then a fix if it fails. Size S. (#148)
8. **Ask for a name at onboarding.** A member with no name shows as a truncated email address in Team, and the Email
   column repeats it. Ask on the onboarding form, prefilled from the identity provider when it has one. Size S to M. (#149)
9. **Quieter empty states.** An empty Candidates panel shows the whole toolbar (Stage filter, three zero chips, a Save
   view that looks disabled), and the Invite sheet stacks six two-line cards. Hide the toolbar until there are rows; one
   line per role and a single "Only some jobs" switch. Size S to M. (#149)
10. **Starting points for a cafe.** The Trial shift scorecard is not on the Trade role template's stage, and the
    template's example line is "Plumber, electrician, driver". Wiring the scorecard in and adding a Hospitality
    starting point each need a data migration. Size S to M. (#147)
11. **A guided first job.** The first job spans New job, Edit details and the job page, with a different hint at each.
    One flow from "pick a starting point" through the role's real details to Publish, on the first job only. It would
    absorb number 2. Size L. (#149)

#### The candidate's side

1. **The interview picker on a phone.** Five days of about 20 times each make the page about 3000 px tall with Book at
   the bottom. Open one day at a time, as a list of days that open in place or a day select then its times. Size M. (#146)
2. **Show a country as a name, not a code.** The careers cards and job page, and the dashboard's Location column, show
   "US" for a job with no city, against design system W8. Format "Leeds, United Kingdom" where the API builds the
   location text. Size S to M. (#149)
3. **Dates in the reader's language.** Pages show "November 9, 2026" beside en-GB prices, and emails write "9 December
   2026" (T5). Take the browser's language in one helper for pages and emails. Size M. (#146)
4. **Several reply files in the Drop area.** A reply's files are one native input with no name, size or Remove per file
   (A12). Let `DropArea` take several. Size S to M. (#146)
5. **Move the device-check consent to the review step.** Notice, assistive switch and consent put about 1000 px before
   the first field at 390 px. Moving the consent to where the check runs shortens the page but changes consent timing,
   so it needs a legal call first, and the EU and EEA matter most. Size M. (#146)
6. **Promise a person only where it is true.** The footer no longer says a person reads every application, because an
   eligibility question can end one with no person involved. A scoped line ("A person reviews anything our checks
   flag") is true by construction. Needs a legal read of the wording. Size S. (#146)
7. **Suggest a retention period.** The default is "Keep them", which is a legal call. Suggesting a period to a new
   organization is the alternative #147 named. Size S, after the legal call. (#147)

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
- **Marketing site:** a character pass after the app is complete. The owner found the rebuilt site a good base without soul (2026-11-09); the second-pass mockup and `docs/design/marketing-site.md` are its starting point.

## Before real customers

- **Hosting:** defined and written down, not deployed. The [deploy runbook](operations/deploy.md) lists every owner step. CI runs on GitHub Actions (ADR-0020).
- **Billing:** built behind a local fake: plans, a free trial, Stripe Checkout and the portal, and a 402 when a plan has no room for another open job ([billing](architecture/billing.md), ADR-0029). What is left is the owner's: a Stripe account, products and prices, the prices and limits themselves, tax and the terms of service ([MVP status](status.md#owner-actions)).
- **Compliance** ([legal](research/legal.md)): bias audits,
  Colorado and California automated-decision rules (due 1 January 2027), EU AI Act provider duties (due 2 December
  2027), the customer documentation pack.
- **Accessibility:** an external WCAG 2.2 audit before general availability (ADR-0009).
- **Owner actions:** the accounts, keys and decisions in the [MVP status](status.md#owner-actions).

## Start now, outside the code

- **LinkedIn partner application:** approval takes 3 to 6 months ([job boards](research/job-boards.md)).
- **The Azure subscription and the domains:** both gate going live.
