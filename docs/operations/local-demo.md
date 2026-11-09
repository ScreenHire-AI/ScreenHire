---
type: Guide
title: "Run ScreenHire locally and demo it"
description: "Running the whole stack locally with pnpm dev or /preview: prerequisites, the URLs, the Dev SSO demo logins, what DevelopmentDemo seeds on Brightwater, a five-minute demo script, what is faked locally, and what to do when something looks wrong."
tags: [demo, local, aspire]
sources:
  - resource: apps/api/Demo/DemoCandidates.cs
  - resource: apps/api/Demo/DemoQuestionnaires.cs
  - resource: apps/api/Demo/DevelopmentDemo.cs
  - resource: apps/apphost/DevSso/config.json
  - resource: apps/apphost/DevSso/login.html
  - resource: apps/dashboard/src/lib/setup-checklist.ts
  - resource: src/ScreenHire.Infrastructure/Persistence/DevelopmentDataSeeder.cs
  - resource: .claude/skills/preview/SKILL.md
  - resource: kangentic.json
---
# Run ScreenHire locally and demo it

Everything runs on one machine with no accounts, keys or internet services: `pnpm dev` starts the whole stack in
Docker and .NET Aspire, seeds a demo employer, and catches every email in Mailpit. This page is the morning checklist
and a five-minute demo script.

## Prerequisites

- .NET SDK 10.0.400 or later, Node 22.22 or later, pnpm 10 (`corepack enable` gives the pinned 10.5.0), and Docker
  Desktop, running.
- A trusted .NET development certificate, because the Aspire dashboard is served over HTTPS:
  `dotnet dev-certs https --trust` once per machine. `dotnet dev-certs https --check --trust` says whether you have one.
- On Windows, clone to a short path such as `C:\src\ScreenHire`. pnpm's store nests package folders deep enough that
  a clone about 120 characters down passes Windows' 260-character path limit, and `pnpm install` then fails in
  `svelte-kit sync` with `ERR_PACKAGE_IMPORT_NOT_DEFINED`. Turning on long paths in Windows also fixes it.

## Start it

In Claude Code, run **`/preview`** ([the skill](../../.claude/skills/preview/SKILL.md)). It checks Docker and the
packages, starts the stack in the background with the Aspire CLI (`dotnet aspire start`; in a Kangentic worktree with
`--isolated`, which gives it random free ports), waits until the demo data is in, and reports the URLs, the logins and
the Aspire dashboard's link. In a Kangentic task it also signs in as Avery Quinn in the task's browser pane.
`/preview fresh` starts a clean demo, `/preview api` starts the backend only and `/preview stop` stops it. The Aspire
CLI is a repo-local tool: run `dotnet tool restore` once. Without Claude Code:

```bash
pnpm install   # once, and again after pulling changes to any package.json
pnpm dev       # = dotnet run --project apps/apphost
```

The first start pulls the container images (Postgres with pgvector, Azurite, Mailpit, the Dev SSO server) and takes a
few minutes; later starts are much quicker. The console prints the Aspire dashboard's link with a login token.
The API listens only once it has migrated and seeded the database, and reports healthy only once the demo's data is
in, so when the dashboard shows every resource as healthy, the demo data is there. The synthetic applicants' verification
levels follow within a minute, once the worker has assessed them.

Frontend changes reload on their own while it runs. After a change to the API or the worker, rebuild just that service
while the rest keeps running: `dotnet aspire resource api rebuild` (or `worker`), or **Rebuild** on the resource in the
Aspire dashboard. Attach a debugger to the `ScreenHire.Api` or `ScreenHire.Worker` process, or start the AppHost from
your IDE. After a change to the AppHost itself, start it again; this clone's running stack is replaced.

Stop with Ctrl+C (or `/preview stop`, which is `dotnet aspire stop`). Aspire then removes the containers it started. The database, uploaded files and
Mailpit's inbox stay in Docker volumes (one set per clone, or per worktree started with `/preview keep`), so the next
start has the same candidates, the same emails and anything you changed.

In a Kangentic task a stack also ends by itself. The project's Stop script (`stopScript` in `kangentic.json`) runs
`dotnet aspire stop` for that clone when the task pauses after the idle timeout or for low memory, unless a browser tab
or the task's Browser pane is connected to the stack. A Pause or Stop pressed by a person leaves the stack up. A move to
Done or To Do, or a delete, stops it whether or not a browser is connected. After a pause, the next agent is told what
stopped and starts it again with `/preview` when it needs the app; a worktree without `keep` reseeds the demo. The
details are in [Processes on the dev machine](../process/board-pipeline.md#processes-on-the-dev-machine).

### Start over

- **A worktree starts clean:** in a Kangentic worktree `/preview` starts with no volumes, so the demo reseeds on each
  start (the API was healthy 25 s after the start command on 2026-11-01) and the worktree leaves nothing behind. Kept
  data had left 123 volumes, about 3 GB, by that date. `/preview keep` keeps this worktree's data instead.
- **Backend only:** `/preview api` starts no frontends. Measured on 2026-11-01, one run each with no data volumes: 5
  processes and 1,219 MB, healthy after 31 s, against 8 processes and 2,272 MB, healthy after 25 s, for the full stack.
  Those are working set. Commit, which Kangentic's low-memory pause reads, is a different number: the dashboard's Vite
  alone held 3.2 GB of it after a page load on a cold cache, before the pre-bundle moved that to a process that exits; see [what a preview stack commits](../process/dev-tooling.md#what-a-preview-stack-commits).
- **A clean demo, keeping your data:** `/preview fresh`, or
  `dotnet aspire start --apphost apps/apphost/ScreenHire.AppHost.csproj -- --ScreenHire:PersistData=false` (add
  `--isolated` before `--` in a worktree). It runs with no volumes at all and deletes nothing; the next normal start has
  your data back.
- **Wipe this clone's data for good:** stop the stack, then remove its three data volumes one at a time by exact name,
  as `/preview` reports them: `screenhire.apphost-<hash>-postgres-data`, `-storage-data` and `-mailpit-data`, each with
  its own `docker volume rm`. The hash comes from this clone's path, so other worktrees' volumes have other names; never
  prune. The next start seeds a new demo.

## The URLs

| What | URL |
|---|---|
| Recruiter dashboard | http://localhost:4323 |
| Careers pages (Brightwater) | http://localhost:4322/brightwater, or on its own host: http://brightwater.localhost:4322/ |
| Marketing site | http://localhost:4321 |
| API reference (Scalar) | http://localhost:5080/scalar |
| Mailpit (every email the stack sends) | http://localhost:8025 |
| Dev SSO (the local sign-in provider) | http://localhost:5090, reached through "Continue with Dev SSO" on the sign-in page |
| Aspire dashboard (resources, logs, traces) | https://localhost:17113, with the token from the console |

These are the main checkout's ports. `/preview` in a worktree starts with `--isolated`, so every port, the Aspire
dashboard's included, is random; it prints the real ones (`dotnet aspire describe` lists them too). Another stack already
on one of these ports? Start this one with `dotnet aspire start --isolated` (what `/preview` does in a worktree), or move
single ports with `ScreenHire__Ports__Api`, `__Marketing`, `__Careers`, `__Dashboard`, `__Mailpit` or `__DevSso`.

## Demo logins

Sign in at http://localhost:4323 with **Continue with Dev SSO** on the sign-in card, then pick a person. The Dev SSO
page says under each one what their role can and cannot do. No password anywhere.

| Dev SSO user | Signs in as |
|---|---|
| Avery Quinn (`avery@brightwater.example.com`) | Owner of Brightwater Studio, the seeded employer with all the demo data: everything, including billing, settings, the team and making other Owners |
| Jordan Ellis (`jordan@brightwater.example.com`) | Recruiter at Brightwater Studio, on every job: jobs, candidates and hiring templates, but no settings, integrations, team, email templates or billing |
| Robin Hale (`robin@brightwater.example.com`) | Interviewer at Brightwater Studio, a custom role Avery made from Viewer: sees candidates, adds notes and tags and fills in scorecards, but is offered no Move and no Settings. On Only jobs they're on, and on the Senior .NET Engineer job's hiring team, so that is the one job he sees |
| Sam Rivera (`sam@example.org`) | A new user with no organization, who goes through onboarding. After the first time they keep the organization they made |
| Unverified address | Avery's address from a provider that does not vouch for it: ScreenHire refuses to link it, and the sign-in page says why |
| Any email address you type | A new user each time you use a new address, the way to show onboarding again. An address someone already uses signs in as them |

**Email me a sign-in link**, under the card's "or", works too: the link arrives in Mailpit, and opening it asks for
one more click (**Sign in**) so a mail scanner cannot use it up.

Avery manages the team in **Organization > Team**: **Invite** sends the invitation to Mailpit. Its link opens the
dashboard's **Join your team** page, and **Accept invitation** signs the invitee in to Brightwater with the invited
role. Open the link in a private window so Avery stays signed in. Jordan, a Recruiter, and Robin, an Interviewer, have
no Settings in the sidebar.

## What the demo data is

Brightwater Studio, a fictional booking and payments software company. `DevelopmentDataSeeder` creates it on every
stack, tests included; `pnpm dev` adds the rest (`DevelopmentDemo` in `apps/api/Demo`). All names, addresses and
numbers are made up.

| Job | Status | Boards | What is in it |
|---|---|---|---|
| Senior .NET Engineer | Open, from the "Software engineer" template | Every board | The synthetic applicants: Robin Ashby (genuine), Sam Okafor (used AI and said so, through a VPN), Jordan Vale (a bot) and a three-person application farm (run by people, so its browsers pass the bot challenge and its speed reason shows that as counter-evidence), all in Applied. Joel Mensah, a genuine candidate who applied nine days ago, is in Technical exercise (moved there three days ago) with a note, and his Activity has two notes from the team: Jordan Ellis mentions Avery Quinn, and Avery mentions Jordan in a note she then edited. His Messages tab has the "Interview invitation" email Avery changed for him alone, with a sentence about his billing ledger answer that the template does not have. Its Interview stage uses Brightwater's own "Technical interview" scorecard, which asks every answer kind |
| Senior Product Designer | Open, with the demo's designer questionnaire | Every board | Three candidates in Applied, Screening and Interview. Jordan Ellis scored Priya Natarajan's interview, and Avery Quinn has a review to give on her, so signed in as Avery her rating reads "Rating after your scorecard" (Jordan sees it) |
| Frontend Engineer (Svelte) | Open, with the demo's frontend questionnaire | Careers page and sandbox board only | Three candidates in Screening, Interview and Offer (with a note). Hannah Okoro is rated 4.2 from Avery's and Jordan's interview scorecards and Eli Thompson 3.5 from Jordan's screening one; the Rating column sorts them |
| Support Engineer | Closed, from Brightwater's own template | Removed everywhere | Six candidates who went through the stages over the weeks before the demo: three Hired (Ruth Mwangi added by a recruiter, Sam Brennan through an Adzuna link), one Rejected (through a Talent.com link), one Withdrawn (through the careers page embedded in the company's own site), so Reports' Sources panel has more than one source, the recruiter's notes on most, and Grace Adeyemi, the runner-up, saved for future roles, so Saved candidates has her with the note saying why (the note can be edited there, and shows its History). Priya Natarajan and Hannah Okoro also allowed it, so Save candidate shows in their Move to stage. Its careers page answers "This job has closed" |
| Backend Engineer (Payments) | Draft, from Brightwater's own template | None | Ready to publish during the demo |
| Customer Success Lead | Draft, with the demo's customer success questionnaire | None | No template, an on-site job in Manchester |

Every job asks questions, and every demo candidate answers each question of their job, so their pages show answers.
The synthetic applicants keep the answers their verification scenarios need.
The .NET, Support Engineer and Backend Engineer (Payments) jobs ask the "Software engineer" questions through their
template. The designer, frontend and Customer Success Lead jobs are seeded without one, so the demo adds a questionnaire
to each, as a recruiter's workflow edit (`DemoQuestionnaires` in `apps/api/Demo`). All three ask about right to work
and the notice period. The designer job adds years of experience, a portfolio link and a research story; the frontend
job adds years of experience, Svelte use, two office days in London and a work sample link; Customer Success Lead adds
office days in Manchester, accounts looked after and a customer kept. The answers fit each candidate's CV, and every
link is an `example.org` address. A data volume from before the questionnaires gets them on the next start, but the
designer and frontend candidates in it applied without answers and stay that way. Use `/preview fresh` to see every answer.

Between them every stage of both pipelines holds someone. The jobs' Distribution tabs show the states a local stack can
reach: Live (careers page, sandbox board), Sent (feeds and the job-board API), Needs fixes (a feed that wants a longer
description), Not posted, Removal requested and Removed. Rejected and Error need a real board's answer and do not occur
locally.

The three open jobs send the candidate emails: one when a candidate applies, and one when they reach Screening or
Interview. Mailpit keeps them with the rest of the data. Every candidate except the synthetic applicants is in
`apps/api/Demo/DemoCandidates.cs`; the synthetic applicants are `SyntheticApplicants` in `ScreenHire.Application`, the
same people the verification golden tests use ([verification-engine.md](../architecture/verification-engine.md) §10.8).

Templates: the Templates page has a tab each for hiring templates, scorecards and emails. Hiring: the two system
templates ("Software engineer", "Hourly role") and "Brightwater engineering", which is the Software engineer pipeline and
questionnaire plus the three candidate emails, with Jordan's version 2 as a draft. Scorecards: the built-ins and
Brightwater's own "Technical interview". Emails: the built-ins, "Interview invitation", and Brightwater's wording of the
built-in rejection email, "Not progressing", which offers Reset to original in its menu.

Change history ([change-history.md](../architecture/change-history.md)): the demo makes its template, emails and jobs
as Avery Quinn, so their History sheets name her. On top, Jordan Ellis starts version 2 of "Brightwater engineering"
(a notice period question, then made required, and two more days for Interview) and changes the salary of "Backend
Engineer (Payments)", and Avery adds an "Interview invitation" email template, edits its subject and rewords the
built-in rejection email. The jobs `DevelopmentDataSeeder` makes have no history until someone edits them.

### What is seeded where

| Layer | Runs | Adds |
|---|---|---|
| `DevelopmentDataSeeder` (`src/ScreenHire.Infrastructure/Persistence`) | Every stack in Development, tests included, before the API listens | Brightwater Studio, its brand colour and verification settings, Avery Quinn as Owner, three open jobs and the Customer Success Lead draft. Tests treat it as read-only ([testing.md](../process/testing.md)) |
| `DevelopmentDemo` (`apps/api/Demo`) | `pnpm dev` and `/preview` only (`ScreenHire:Demo`), after the API starts; `/health` waits for it | Everything else on this page. Each step looks for what it adds first, so a restart with kept data adds nothing |
| Dev SSO users (`apps/apphost/DevSso`) | Every Development stack | The sign-in choices under "Demo logins" |


## A five-minute demo

1. **The jobs (30 s).** Sign in as Avery Quinn. The jobs list shows each job's status first, opens on the last
   updated, sorts by any column header, and the Status filter counts what is open and what is a draft. The sidebar's views pick out what needs attention: "To review" and "To verify".
2. **Verification, explained (90 s).** Open Senior .NET Engineer. The authenticity chips over the table count the seven
   applicants by level, each chip filters the table to it, and each row has its level. Open Jordan Vale and its Verification tab, marked with a dot: the level sits on its three-step scale, a card per kind of check says what it found, and the tab lists every reason (a hidden form field
   filled in, browser automation, a six-second form, a throwaway inbox, a phone number that is not valid, a hosting
   provider's network) with its evidence and a suggested next step. Point out what is not there: no score, no
   percentage, no reject button. Open Taylor Brook, the last of the three-person farm to apply, for "Same device" and
   "Same resume file" shared with Alex Reed and Morgan Hale, then Sam Okafor, who used AI within the job's policy and
   raises no concern.
3. **Move a candidate, see the email (45 s).** Open Robin Ashby, choose **Move to stage**, then Screening. The
   rail's Stage changes and Activity records the move, and Mailpit (http://localhost:8025) shows "An update on your application for Senior .NET Engineer"
   to Robin within a second or two. Every candidate's "We received your application" email is there too, and the
   Screening and Interview emails for the designer and frontend candidates.
4. **Publish everywhere (60 s).** Open Backend Engineer (Payments). **Edit details** opens the form with everything the
   posting carries (salary, location, closing date, description). Back on the job, press **Publish to 8 boards**, the
   page's one primary action. The Distribution tab counts the boards the job is on. The careers page
   is live at once; the local sandbox board confirms in about 20 seconds; the feed boards show "Sent" with each one's
   usual delay. The job is now on http://localhost:4322/brightwater.
5. **Apply as a candidate (60 s).** On the careers page, open the new job and apply: the notice comes first, then three
   short steps. The form works with JavaScript off too. Back in the dashboard, the application appears in the open job
   without a reload, and its chip follows once the checks finish.
6. **Take it down (15 s).** On the job page, open **More actions**, choose **Close & remove everywhere** and confirm. The careers page now answers "This job has
   closed" (HTTP 410) and the sandbox board removes the job about 30 seconds later.

Extras if there is time: **Reports** in the sidebar, where Support Engineer's dated history gives the funnel, time to
hire and both sources numbers to show (Last 90 days covers all of it; each panel's Table shows the same numbers); the
Rating column on Frontend Engineer and a candidate's stars, which open to each scorecard; Joel Mensah's Messages (open the changed interview email as it went, or email him from the box at the top: start from a template, change it, and Preview shows it with his first name), his notes (type @ in the note box to mention a teammate; the mention emails are in
Mailpit and never quote the note; Avery's own note offers Edit and Delete in its menu), a resume opened full window
from the details rail's View, Support Engineer for a closed job and its candidates' activity with the recruiter's notes,
Saved candidates (the **Saved** choice beside the Candidates title; Grace Adeyemi, saved from Support Engineer, with no end date: the page shows Rating and Last contacted (Never until an email goes, so the invitation sets it), Message opens the email composer, Edit note changes the note and shows its History, and Invite to apply sends the invitation to Mailpit with the
job's link and her link to stop being kept, which opens on the careers site), the Templates page (below) and its builder (questions, automations and the guardrail that refuses a
verification-driven rejection), the workflow graph and "Simulate", Ctrl K for the command palette, the dark theme, and a
second browser window on the same job to show live updates.

Replies, as Avery: send an email to a candidate from the box on their Messages tab, then open it in Mailpit and copy its
Reply-To address (`reply+...@reply.localhost`). There is no SendGrid here, so deliver the reply with the sandbox route, on the API's
address from the Aspire dashboard (http://localhost:5080 when the ports are pinned):

```
curl -X POST http://localhost:5080/v1/dev/inbound-email -H "Content-Type: application/json" \
  -d '{"to":"<the Reply-To address>","from":"<the candidate's address>","subject":"Re: your email","text":"Thursday works for me."}'
```

The reply shows on the candidate's Messages tab and in Activity, and a card appears in Avery's tray. Reporting a bounce needs
SendGrid's signature, so it is covered by the integration tests, not this walk-through. Settings > Organization > **Email domain** takes any
domain you own (example.org will do): the page lists made-up records, Check records says they are missing until
`POST /v1/dev/sending-domains/publish` with `{"domain":"example.org"}`, and the next check verifies the domain.

Templates, as Avery: **Templates** in the sidebar, one tab per kind, each filtered by All, In use, Not used in 6 months
and Archived.

- **Emails.** Open "Application received", change the subject, then switch to **Preview**: the email with the made-up
  Jordan Lee follows your words, and the window icon opens it in a second window that follows too. **Send me a test**
  lands in Mailpit with `[Test]` in front. On a job's pipeline, tick a few candidates and press **Email**: each gets the
  email in Mailpit, and it shows in their Activity. Back on the template, Last updated says "by you", and **History**
  shows "You changed the subject". "Not progressing" has Brightwater's wording: its menu's **Reset to original**
  confirms, then puts ScreenHire's wording back, and History says so. The old Settings address redirects here.
- **Scorecards.** Open "Technical interview": Used by lists the hiring templates that name it, Edit is the form builder
  with the rating kinds, and Preview is the scorecard as a reviewer fills it in. A built-in such as "Screening" reads
  only and offers **Copy scorecard**.
- **Hiring.** "Brightwater engineering" shows version 1 and the Draft 2 badge. **Duplicate** in a row's menu makes
  "Brightwater engineering (copy)" and opens it; **Archive** moves your own template under Archived, out of the new job
  page's list, until **Restore**.

Settings, as Avery (any role with a settings permission; Owners and Admins have them all):

Settings is one item at the bottom of the sidebar. It opens the Settings home, which lists every setting in three groups,
**Organization**, **Careers site** and **Integrations** (Slack where it is set up, MCP server, Webhooks, API keys), with a search box
("logo" finds Branding), and a press on a name opens its page, which has its group's menu. Email domain says "Not set up" or
"Records to add" there until it is verified. **Verification** is its own item above the views. A new
organization's Owner or Admin also sees **Set up your organization** on Jobs, with a tick for each step the data shows done.

- **Team and Roles.** **Invite** opens a sheet that starts at Viewer and says what each role is for, Interviewer
  included; **Compare roles** opens the table of what each role can do. **Roles** lists the four built-in roles and
  Interviewer, which Avery made from Viewer and gave to Robin: open it to see its permissions, its History ("created the
  role from Viewer") and Robin under People, with "1 job". **New role** copies a role and opens the copy to change.
  Deleting Interviewer is refused while Robin has it, saying so.
- **Job access.** Team's Jobs column says "1 job" for Robin and "All jobs" for everyone else. **Edit access** in Robin's
  menu opens his role and job access, with the Senior .NET Engineer job listed. On a job, **Hiring team** lists Jordan
  (on every job) and, on the .NET job, Robin; **Add a person** and **Copy from another job** change it. Sign in as Robin
  through Dev SSO: Jobs lists only Senior .NET Engineer, another job's address says it is not found, and the
  candidates, saved views and My tasks hold only that job's.
- **Branding.** Brightwater's teal is saved. Type a pale brand colour such as ffd400: the field says links in it are
  hard to read and offers the nearest shade that passes; **Use** fills it in. Pick a second colour and a heading font,
  upload a PNG logo, watch it say Scanning for a moment, then press **Save**. Reload
  http://localhost:4322/brightwater: the header band, the logo, the titles' font and the Apply buttons follow, and an
  email from the Templates page's test send starts with the logo. History lists each change.
- **API keys.** **Create key** shows the key once, with Copy; afterwards the list shows only its first characters.
  Under the keys, "Use the ScreenHire API" gives the base URL and links the API reference.
  `curl -H "Authorization: Bearer <key>" http://localhost:5080/v1/jobs` lists Brightwater's jobs until you revoke it
  from the row's menu.
- **Team**, **Organization** (rename; the careers address stays), **Verification** (the consistency check, off until
  turned on; the Fake model answers locally) and **Webhooks** (add an endpoint, see its signing secret once, then its
  delivery log with replay).
- **Slack.** With no Slack app Settings has no Slack page, as `pnpm dev` starts. To try it, turn on the local
  sandbox workspace before starting: `dotnet user-secrets set ScreenHire:Slack:Sandbox:Enabled true --project apps/apphost`
  (or `ScreenHire__Slack__Sandbox__Enabled=true` in the environment of `dotnet aspire start`; `dotnet user-secrets
  remove ScreenHire:Slack:Sandbox:Enabled --project apps/apphost` turns it off again). **Add to Slack** comes straight
  back connected to "ScreenHire sandbox"; pick `#hiring` and the events, and **Save**. Avery's menu, at the foot of the
  sidebar, then has **Slack account**, which links Avery's own account. Submit an application on the careers page, or
  mention Avery in a note, and open http://localhost:5080/v1/slack/sandbox/messages in the signed-in browser to read
  what the sandbox received ([Slack](../architecture/slack.md)).
- **The candidate's page.** Open http://localhost:4322/brightwater and use **Your applications** in the footer, or go to
  `/brightwater/my-applications`. Type the address of a seeded candidate, such as one you found in Mailpit; the page answers the
  same for any address and Mailpit holds "Your link to your applications at Brightwater Studio" for a real one only. The link
  opens the page with no script: the candidate's applications with four steps, and **Read and answer your messages** once the team
  has written. Send an email from a candidate's page in the dashboard, then answer it on the candidate's page: the reply
  appears in that candidate's Messages and Activity, and an email reply posted to the sandbox route `/v1/dev/inbound-email` (below) joins the same thread.
  Every email in Mailpit now ends with a link to the candidate's page ([candidate portal](../architecture/candidate-portal.md)).
- **Interview scheduling.** The sandbox calendar is on in `pnpm dev`, so Avery's account menu has **Calendar**. Open it,
  press **Connect Sandbox calendar** and the browser comes straight back connected. Open a candidate who is still in the
  hiring process, such as Robin Ashby, and press **Schedule interview** beside Email: Avery is ticked as the interviewer
  (Jordan and Robin show "No calendar connected" until they connect theirs, which the same menu does when you sign in as
  them), choose a length and the days, and **Send invitation**. Mailpit and the candidate's Messages tab hold "Pick a time to talk with Brightwater Studio
  about ..." with a link to http://localhost:4322/brightwater/interviews/... Open it in a private window: the page needs no script,
  lists the free times by day in a time zone you can change, and books one with a radio and a button. Back in the
  dashboard the header has an Interview card, the Overview's Interview panel lists the interviewer and the meeting link on
  `meet.example.org`, and Mailpit holds "Your interview with Brightwater Studio is set", whose link moves or cancels the time. To have a workflow offer the interview, add a **Schedule an interview** action on a stage
  ([interview scheduling](../architecture/interview-scheduling.md)).

## What is faked locally

| Real thing | Locally | Configured by |
|---|---|---|
| Google, Microsoft and other sign-in providers | Dev SSO (navikt/mock-oauth2-server) with the users above; the real providers are hidden until their client ids are in user secrets | `apps/apphost/DevSso`, `ScreenHire:Auth` |
| Email provider | Mailpit catches every email | `ScreenHire:Email` |
| File storage | Azurite (Azure Storage emulator) | Aspire |
| LLM consistency check | A deterministic fake model; on for Brightwater only | `ScreenHire:Verification:Llm` (worker) |
| Job boards | The sandbox board (accepts after 20 s, removes after 30 s) and the feeds; nothing leaves the machine. Google Indexing is off | `ScreenHire:Distribution` |
| IP intelligence | Development's fixed ranges for the RFC 5737 documentation addresses the synthetic applicants use | `ScreenHire:Verification:Network` (worker) |
| Bot challenge (Turnstile) | Not configured, so recorded as "not checked"; the honeypot still works | `ScreenHire:Turnstile` |
| Malware scanning | `NoMalwareScanner`, which records every file as "not scanned" | `IMalwareScanner` |
| Email provider | Mailpit sends; no SendGrid. Replies come through the sandbox route `/v1/dev/inbound-email`, and a sending domain verifies once `/v1/dev/sending-domains/publish` publishes it | `ScreenHire:Email` |
| Google and Outlook calendars | The sandbox calendar, on in `pnpm dev`, whose provider buttons are hidden unless the real client ids and secrets are set; busy time lives in Postgres and the meeting link is on `meet.example.org` | `ScreenHire:Calendar` |
| Stripe | The sandbox payment provider, on in `pnpm dev`: choosing a plan "pays" at once and the portal cancels at once, and nobody is charged. Brightwater is seeded on the Unlimited plan, so the demo is never held to a limit; a new organization starts a 14-day trial | `ScreenHire:Billing` |
| Slack | Off, so Integrations > Slack says it is not set up; the sandbox workspace when turned on, whose messages `/v1/slack/sandbox/messages` lists | `ScreenHire:Slack` |

The [MVP status](../status.md#owner-actions) lists what each of these needs before real use.

## When something looks wrong

- **A resource is unhealthy in the Aspire dashboard.** Open its logs there. Docker not running and a port already in
  use are the usual causes. `pnpm docker:start` starts Docker Desktop and waits for the engine.
- **No verification chips yet.** The worker assesses after the API has started; give it a few seconds. The first assessment
  after a cold start is the slowest.
- **The sandbox board still says "Submitted".** It waits 20 seconds on purpose, like a real board's review.
- **No email in Mailpit.** Automations send from the worker after the move commits; it takes a second or two. Only
  the three seeded open jobs and jobs from "Brightwater engineering" send candidate emails. With `fresh` Mailpit
  starts empty, and the demo's own emails are sent once, on the start that adds their candidates.
