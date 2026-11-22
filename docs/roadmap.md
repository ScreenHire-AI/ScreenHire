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
| The owner's second walk-through: a confirmation before a move into a stage that does something, shared views as an offer the team browses and adds, "Last application" and "Last call" lines on Reports and Usage, a toast for a new application, email-first sign-in, a checklist pointer and "1 of 10 on your plan" for a new team, a check before Publish, one day at a time on the phone's interview picker, countries as names, and a design review of all of it ([design system](design/design-system.md)) | "Owner's second walk-through: move confirmation, opt-in shared views, recency, and the first-hour fixes" | #158 to #166 |
| The consistency review of the dashboard: every page held to the List template and the checklist at 1440 and 390 px. A list's count lives on its tab, pill or panel and never beside the title (Team, Roles, API keys, Webhooks and Passkeys moved theirs into the panel header, hidden while the list is empty, and `HeaderBand` lost its `count`), and the settings lists search with the same "Search" placeholder as the main lists and show the menu button on a phone, not Back (API keys, Webhooks, MCP server and Slack are not records), Team remembers a sort by Last updated, the Saved tab keeps Add candidate in the header and drops its second count, a closed job says Closed once, the Activity log's grey outcomes are quiet, and the monthly budget panel has one name (Monthly limits) on the Overview, Usage and Notifications tabs. What was larger is ranked below | "Overnight polish: Verification wording, Reports and Jobs, meetings, the first hour, small-team templates, dates, and a consistency review" | #175 |

## Next

### The owner's review of the launch-readiness build

Four design reviews walked the build as a new team in its first hour, as a recruiter in a working day, as someone
setting ScreenHire up, and as a candidate on a phone. Each fixed what was contained (#146 to #149) and left the rest
for the owner. This is that rest, merged, with duplicates removed and ranked. An item marked Built was done by the
second walk-through's workflow (#158 to #166); the rest is not built. The owner
picks the items to build, and they become the next workflow. The rest stay here for a later pick, with "Later" and
"Before real customers" below.

How to read it:

- **Rank** is within a group, by how much the change helps a small team against its size. Number 1 is the best value.
- **Size**: S is a few files and a test. M is a client and API change, or a flow, with tests. L is a new flow across
  several screens. S to M is between the two.
- **Groups** are the five selling points and the candidate's side. "Built your way" also holds the first-hour items
  (onboarding, invitations, the trial, the first job), because they fit no other selling point.
- #147 closed with decisions and no proposals list, so its items here come from the alternatives it named for a
  choice it kept. Where #148 already built part of a #149 proposal, only the rest is listed.
- A few items need a legal call first. They say so.

**Best value first, across the groups:** the second walk-through built the first six of these (the setup checklist, the
check before publishing, the new-application toast, what "Open jobs" counts, the phone interview picker and country
names). What is left: a calmer empty state for a new team (S), tell a new Owner about the trial (S to M), orient someone
who accepts an invitation (S), and the small readability fixes in Reports and Jobs (S).

#### Real people

1. **Budgets in dollars by default.** Built: an organization with no limit of its own sees the deployment's default budget
   and cap as dollars, priced at the answering model and the usual input/output mix, and in tokens where there is no price
   ([AI usage](architecture/ai-usage.md#budget-alerts-and-the-cap)). Kept here so the numbers hold. (#147)
2. **Say "Information only" once on the Verification tab.** Built: the tile says "1 note" and the finding row carries its
   mark alone; the words are in the AI row's tooltip. Kept here so the numbers hold. (#148)
3. **Make the audit line the page's last line.** Built: the decision line and the audit line sit under every panel and in
   none of them ([design system](design/design-system.md), W10). Kept here so the numbers hold. (#148)

#### Never guessing

1. **Announce a new application.** Built: a new application shows the job's hiring team a toast, one count for a minute
   ([realtime](architecture/realtime.md#new-applications)). Kept here so the numbers below hold. (#160)
2. **Small readability fixes in Reports and Jobs.** The change pill reads "up 10" with no unit outside its tooltip, a
   zero in a bar row ("Hired 0 0%") sits on its own line away from the bars, and the Jobs home's Published and Last
   updated headers sit about 10 px off their values. Size S for all three. (#148)
3. **Say how recent Reports and Usage are.** Built: "Last application 3 hours ago" under Applications on Reports and "Last
   call 5 minutes ago" under Model calls on Usage (design system T7). Kept here so the numbers hold. (#158)

#### Automate anything

1. **Confirm a move, with what it will do.** Built: a dialog lists what entering the stage does, with Don't ask again kept
   per person ([workflow engine](architecture/workflow-engine.md)). Kept here so the numbers hold. (#166)
2. **Name a stage's meeting.** Built: a stage's `meetingName` ("trial shift") replaces the word "interview" in the
   interview emails, the booking page, the candidate's page, the scheduling sheet and the calendar event
   ([interview scheduling](architecture/interview-scheduling.md#the-stages-meeting-name)). The Trade role template's
   trial shift stage names it in Trade role version 3 and Hospitality version 2. (#147)

#### Integrations

Nothing was proposed. The reviews ran against the local stand-ins, so real Slack, calendars, SendGrid and a claude.ai
connector are still untested by hand ([MVP status](status.md#known-gaps)).

#### Built your way, and the first hour

1. **Share a view without putting it in everyone's sidebar.** Built: sharing is an offer the team browses and each person
   adds ([saved views](architecture/saved-views.md)). Kept here so the numbers hold. (#165)
2. **Make the setup checklist findable.** Built: the Jobs empty state points to it, and the bell opens once after
   onboarding ([notifications](architecture/notifications.md)). Kept here so the numbers hold. (#163)
3. **Check a job before it goes live.** Built: Publish warns, never blocks, about template fill-in text, a missing city or
   salary a chosen board needs, and a description a board would refuse, with Edit details beside each and Publish anyway
   ([job distribution](architecture/job-distribution.md#check-before-publishing)). Kept here so the numbers below hold.
4. **Say what "Open jobs 1 of 10" counts.** Built: the tile reads "1 of 10 on your plan". Kept here so the numbers hold. (#163)
5. **Tell a new Owner about the trial.** Only Billing, the bell near the end and a "0 of 10" tile say there is one, how
   long it is and that it needs no card. One line on onboarding and a quiet "Trial ends" line in the sidebar footer for
   Owners. Size S to M. (#149)
6. **Orient someone who accepts an invitation.** They land on Jobs with no word on their role or who invited them. A
   one-time line ("You joined Harbour Lane Studio as Recruiter. Jobs you can see are below."). Size S. (#149)
7. **Fix the careers page setup step.** It ticks only when someone embeds the page or changes questions per page, so
   a team that keeps the defaults can only dismiss the whole checklist. Drop the step or merge it into Branding; the
   count goes from five to four, and the tests and docs that say five change with it. Size S. (#149)
8. **Check the scorecard's 1 to 5 buttons on a phone.** Built: `ScorecardTests` measures each 1 to 5 button at 390 px
   under a coarse pointer (design system F4 wants 44 px). The 1 to 10 scale cannot be 44 px wide at that width; the test holds it
   to 24 px wide and 44 px high. Kept here so the numbers hold. (#171)
9. **Ask for a name at onboarding.** A member with no name shows as a truncated email address in Team, and the Email
   column repeats it. Ask on the onboarding form, prefilled from the identity provider when it has one. Size S to M. (#149)
10. **Quieter empty states.** Built: a list with no rows shows its title and empty state with no search, filters or Save
   view, and the Invite sheet has one line per role and a single "Only some jobs" switch ([design system](design/design-system.md)).
   Kept here so the numbers hold. (#149)
11. **Starting points for a cafe.** Built: a Hospitality starting point ("Barista, line cook, server") and a second version
    of Trade role with the Trial shift scorecard on its trial stage ([workflow engine](architecture/workflow-engine.md)).
    Kept here so the numbers hold. (#171)
12. **A guided first job.** The first job spans New job, Edit details and the job page, with a different hint at each.
    One flow from "pick a starting point" through the role's real details to Publish, on the first job only. It would
    absorb number 2. Size L. (#149)
13. **A calmer Jobs empty state for a new team.** It shows New job, "4 steps left to set up your organization" and Open
    the setup checklist, while the bell already carries the count 4 (W7). Keep the heading, one line and the one action
    (New job), and leave the checklist to the bell it opens from. The alternative is to keep the pointer and drop only the
    count sentence. The pointer was the point of the checklist item above, so this is the owner's call. It sits beside
    number 10, which quiets the other empty states. Size S. (second walk-through)
14. **Show Edit details once in the Publish check.** Built: with one problem the row's link is the only one, and the
    footer's Edit details appears for several. Kept here so the numbers hold. (second walk-through)
15. **Decide whether the bell opens itself.** After onboarding the bell opens once, takes focus and covers the top bar's
    Search and Jobs controls until closed. A dot on the bell with a tooltip is quieter and needs no dismissing, but loses
    the nudge. An owner's call, then Size S. (second walk-through)
16. **Reach Team views from the icon rail.** Built: in the rail, Browse team views is an icon button in the fixed group
    above Settings, outside the Views list that scrolls, so it stays in reach at 700 px tall. Kept here so the numbers
    hold. (second walk-through)
17. **A catalogue entry for the Team views sheet.** Built: section 11 has its row and the style guide its panel. Kept
    here so the numbers hold. (second walk-through)

#### The candidate's side

1. **The interview picker on a phone.** Built: one day open at a time, and a closed day says when a time is picked inside
   it ([interview scheduling](architecture/interview-scheduling.md)). Kept here so the numbers hold. (#162)
2. **Show a country as a name, not a code.** Built: "Leeds, United Kingdom" wherever a location is written
   ([job distribution](architecture/job-distribution.md)). Kept here so the numbers hold. (#159)
3. **Dates in the reader's language.** Pages show "November 9, 2026" beside en-GB prices, and emails write "9 December
   2026" (T5). Take the browser's language in one helper for pages and emails. Size M. (#146)
4. **Several reply files in the Drop area.** A reply's files are one native input with no name, size or Remove per file
   (A12). Let `DropArea` take several. Size S to M. (#146)
5. **Move the device-check consent to the review step.** Notice, assistive switch and consent put about 1000 px before
   the first field at 390 px. Moving the consent to where the check runs shortens the page but changes consent timing,
   so it needs a legal call first, and the EU and EEA matter most. The notice and the assistive switch stay
   before any collection either way. Size M. (#146)
6. **Promise a person only where it is true.** The footer no longer says a person reads every application, because an
   eligibility question can end one with no person involved. A scoped line ("A person reviews anything our checks
   flag") is true by construction. Needs a legal read of the wording. Size S. (#146)
7. **Suggest a retention period.** The default is "Keep them", which is a legal call. Suggesting a period to a new
   organization is the alternative #147 named. Size S, after the legal call. (#147)

### The consistency review of the dashboard

The overnight polish workflow ended with a walk of every dashboard page against the List template (design system
section 7) and the checklist, at 1440 and 390 px, with the design-review agent. It fixed what was contained (see the
Done table). This is the rest, ranked within each group by how much it helps a small team against its size, with the
size in the same terms as the section above. Nothing here is built. The owner picks.

#### Lists

1. **One rule for when a list has search.** The four main lists show search whenever they have a row. Team, API keys and
   Webhooks show it past 10 rows (S9) and Roles and Passkeys never do. A person who learned to look in the panel header
   on Jobs finds nothing on Team. Pick one: search always, or search past 10 everywhere, with Jobs, Templates and My tasks
   joining it. Size S.
2. **Team as two tabs.** Members and Invitations are two panels one under the other, the second with a count of its own.
   Templates and Candidates give each collection a tab with its count. Members and Invitations as `RecordTabs` would put
   Team on the template, and an invitation list that grows would stop pushing Members off the screen. Size S to M.
3. **State pills on Candidates, and Save view where there are filters.** Jobs, Templates and My tasks lead the row under
   the panel header with state pills. Candidates keeps its states in the Stage menu, and Save view exists only there.
   Adding pills to Current and All changes the page's first look, so it is the owner's call. Size M.
4. **One date range control on Usage and Activity log.** Reports has the Date range filter of section 11. Usage and the
   Activity log have a bare select each, labelled "Date range" on one and "Filter by date" on the other, offering This
   month, 30 and 90 days, and Usage's sits on the page background rather than in a panel (S3). The filter takes a custom
   range and the API already takes `from` and `to`. Size M.
5. **When a search or filter hides every row, say so one way.** Jobs and Saved candidates say it and offer Clear filters,
   Templates titles it "No templates match" with Show all, and My tasks, Team, API keys and Webhooks show a bare line with no
   way back (E1). Size S, one wording and one button.
6. **Decide once whether an empty list repeats the header's action.** Jobs, Templates, Team's Invitations, API keys and
   Webhooks put New job, New template, Invite, Create key and Add endpoint in the empty state and in the header, so a phone
   with the action bar shows two. Passkeys and Candidates do not repeat it. Write the rule in section 7, then apply it. Size S.
7. **Date columns line up one way.** Jobs and Templates right-align Last updated and Last applied, with the icon before the
   header's text (L2), and Tasks, Team, Roles, API keys and Webhooks left-align theirs, so the same column moves between
   lists. Left is the cheaper choice. Size S.
8. **Settings lists on a phone: the Sort chip and the list items.** Team, Roles, API keys, Passkeys and Saved candidates put
   a full-width row with only the Sort chip above the list, where Jobs, Templates and Tasks put it in the pill row (section 8),
   and Team, API keys and Passkeys draw their own list items instead of `ListItem`. Size S to M.
9. **One filter row.** The pill and filter row hides its scrollbar on Jobs and Candidates, shows a thin one on Templates and
   Tasks, and a full one on Saved candidates, whose filters are also named "Saved from" where Current says "Job" and whose
   selection is its own full-width row where Current shows "Selected N" in the panel header. A shared component would settle
   all three. Size M.
10. **A panel's title follows the pill.** Jobs and My tasks retitle the panel with the state (Open jobs, Draft jobs, Done
    tasks), and Templates keeps "Hiring templates" while Archived is pressed. Size S.
11. **One Add candidate for the Candidates tabs.** Current and Saved each wire the same button, sheet, phone action bar and
    "Added ... to ..." line, so a change to one can miss the other. One small component would hold all four. Size S.

#### Where the main action sits

1. **Which end of the action group the primary takes.** The job page puts Publish first in its group and the template
   builders put Publish version and Save last. Section 10 says one primary per page and nothing about its place. Pick the
   end, write it in section 10, then move the odd pages. Size S.
2. **One verb for creating.** The header's main action reads New job, New template and New role, but Add candidate, Add
   endpoint, Add a passkey, Create key and Invite. A6 asks for a verb, and "New" is the system's own on the main lists, so
   the choice is a naming one for the owner. Size S, and no logic changes.

#### Names and bands

1. **Edit pages with no Last updated or History.** A job's edit page and a webhook endpoint's have the form band and no
   facts, although both records have a history (T6). Size S each.
2. **Panels that repeat their band.** Roles under Roles, Domain under Email domain, Workspace under Slack, and the
   Checks, Policy and AI model forms under tabs of the same name (W7). The panel could take the name of what is in it
   (What each role can do, Sending domain). Size S for all of them.
3. **Copy against Duplicate.** The hiring template's primary reads Copy template, the scorecard's Copy scorecard, and the
   menu item on all three is Duplicate. One word for one action. Size S.
4. **Settings form pages and `form`.** The band's contents line up with the 688px column only when the band is passed
   `form`, and none of the settings pages passes it, so their titles sit at the left edge of the window above a centred
   column. `<main>` is also unstyled on Data requests, MCP server, Roles and Compare roles. Size S.
5. **Where a builder says what just happened.** The two job pages put "Saved." in the band's line under the title so it
   never wraps the actions, and the hiring template, scorecard and email builders put it beside the buttons, where a long
   message can push the actions onto a second row and change the band's height (S8). Size S.
6. **Edit pages: one heading, one button.** A job's edit page is headed with the job's title, its workflow page with
   "<title> workflow" and a webhook's edit page with its name. The job form's panel reads "Edit details" and its button "Save
   details", while New job says "Details" and every other form saves with "Save". Size S.
7. **One not-found state.** The job, template, scorecard, email, role, candidate, application and endpoint pages each say it
   a little differently: a hidden panel title, "Details", "X not found", a sentence as the title or as the description, and
   "does not exist" against "was deleted". One shared snippet. Size S to M.
8. **The Activity log's filters.** Its date, group, key and outcome filters are native selects, where Reports and the lists
   use the filter menus of section 11. The panel has no visible title and Export CSV in an otherwise empty header, where
   Reports puts it in the band. Folds in number 4 under Lists. Size M.
9. **Usage's tables and tiles.** "By model and key" does not sort (L2), and the Cost tile has no line under its value while
   its neighbours have one (T7). Size S.
10. **The hiring team drawn two ways.** A job has a Hiring team button with a count in the band, a hiring template has a
    Default hiring team fact with a small "0 people" button, and both open the same sheet. Size S.
11. **The contact line.** A candidate's email and phone are a `details` line of plain links, and an application's are the
    `meta` line with `MetaItem` and screen-reader labels (W9). Size S.
12. **Band button size and page rhythm.** Bands use the 36px button, but Reports and Slack's Reconnect use the 32px one a
    panel header takes, and Email domain flips with the device. Reports pads its content 20px and gaps it 16px where the
    Verification pages use 24 and 20. One content wrapper, one size. Size S.
13. **A role's page is a form, a webhook endpoint's is a detail.** Both are records in Settings. A role sits in the 688px
    column and an endpoint fills the window with a facts row. Say which in section 7, then match one to the other. The owner's
    call. Size S.
14. **The New hiring template page's name field.** It is a hand-written label around an `Input` instead of `Field`, as on
    every other form. Its read-only reason now sits in the band, as the New email template page's does. Size S.

The walk measured every dashboard route at 1440 px and all but two settings pages at 390 px, in the light theme, and the
seven list routes in the dark theme too. It found no sideways scroll, no text under 12 px and no nested panel anywhere, and
the one wrapped control was the scorecard builder's question rows, which wrap by design. The pages it did not photograph
were judged from the code, so touch-target sizes (F4) and the dark theme of the other pages stay unchecked by eye (the
dark walk in `DashboardThemeTests` covers contrast). At 800 px the tablet rail keeps Browse team views in its fixed group
above Settings.

### The owner's fourth walk-through (2026-11-21)

The owner annotated about 50 screens of the preview. The contained fixes are in the walk-through PR (branch
`fix/fourth-walk-through`); the rest is two overnight workflows, paused until the owner starts them, and each task waits
for that PR on `main`.

**Night 1:** one amount for AI use and a Billing page with the month's total, invoices and payment method; Verification
in four tabs (Overview, Activity log, Checks with each group's settings inside it, AI use), a reassuring clear result and
an activity log you can scan; Your account (profile, a notification grid by email and in the app, connected accounts,
sign-in); live activity, a workspace feed from any page with time ago and a link to each change, with toasts for
interviews and returned forms; the template editor (one Pipeline tab, short stage facts, closable questions, automations
under their stage, publishing that says what it does); named teams; shared tags with suggestions; a setup checklist you
can skip a step of for the whole organization; a Messages tab you can scan; short create forms in sheets; and a table row
that opens its record, everywhere.

**Night 2:** forms (several a hiring process, sent at the stage you choose, from a library); Try it, a click-through of
your own pipeline one stage at a time; scheduling three ways (pick from our times, tell us when you're free, we suggest a
time); post it yourself, with tracked links and manual postings in the postings list; an apply form that feels short;
the developer platform (full MCP tools with confirmation, more webhook events, a readable API reference, developer docs);
the whole Google Fonts catalogue, hosted by us; a distinct icon for every view in the rail; switching a job to another
template; and a marketing site refresh, last.

**Decided 2026-11-21:** Verification has four tabs and its budgets count billed dollars; named teams replace a template's
default hiring team; Clear, Review and Verify stay, and To review holds Review only; a table row click opens the record;
"Form use" becomes "Form activity".

**Still the owner's:**

1. **The pricing page's AI wording.** It publishes "the provider's price plus 20%" (`apps/marketing/src/lib/features.ts`);
   the app no longer shows the margin. Keep it public, or describe AI checks as billed by use.
2. **The built-in Software engineer template tags every Review or Verify candidate "verification-review"**, which repeats
   the level chip on every card. Proposal: a new version of the template without the tag step (a migration, like the
   rename that added it).
3. **The quiet Connected pill:** show nothing while connected and only "Reconnecting" when not.
4. **Legal reads:** equality-monitoring forms kept off the candidate page and reported in totals only; a shorter, layered
   notice on the apply form; and "A person looks at anything the checks flag", which is the same promise as the footer.

## Later

- **Job boards:** Indeed and ZipRecruiter (phase 2, with paying customers), LinkedIn, then European boards
  ([job boards](research/job-boards.md), [job distribution](architecture/job-distribution.md)).
- **Careers:** custom tenant domains (ADR-0003).
- **An organization's own font:** uploading a WOFF2 file of a typeface the company licenses, for its careers titles. The
  heading font is any Google Fonts family today, which ScreenHire downloads and hosts (ADR-0031,
  [branding](architecture/branding.md) section 3), and the body font stays ScreenHire's. An upload needs a licence the
  employer vouches for, a malware scan like a brand image's, and a check that the file is a font and only a font.
- **Sign-in through an organization's own identity provider:** one company's Okta or Entra ID, required for its members,
  set up by its Owner. Sign-in today offers Google, Microsoft, emailed links and passkeys, plus any OpenID Connect
  provider the installation configures for everyone ([sign-in and tenancy](architecture/sign-in-and-tenancy.md)). A
  per-organization provider is an enterprise feature, and no first customer needs it. The sign-in page asks for the email
  first, so this is where it will enter: Continue can send a company's email domain to its own identity provider. Nothing
  is built for it yet. GitHub sign-in stays out: few recruiters have an account, and it would be one more button for
  everyone else (owner's review of competitor sign-in pages, 2026-11-17).
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
- **Legal documents:** the Terms of Service, the Privacy Policy and a Data Processing Agreement with its sub-processor
  list, before the first paying customer. They are the owner's to write (a legal call). The sign-in page links each one
  from `ScreenHire:Legal:TermsUrl`, `PrivacyUrl` and `DpaUrl` and leaves a line out until its address is set.
- **Compliance** ([legal](research/legal.md)): bias audits,
  Colorado and California automated-decision rules (due 1 January 2027), EU AI Act provider duties (due 2 December
  2027), the customer documentation pack.
- **Accessibility:** an external WCAG 2.2 audit before general availability (ADR-0009).
- **Owner actions:** the accounts, keys and decisions in the [MVP status](status.md#owner-actions).

## Start now, outside the code

- **LinkedIn partner application:** approval takes 3 to 6 months ([job boards](research/job-boards.md)).
- **The Azure subscription and the domains:** both gate going live.
