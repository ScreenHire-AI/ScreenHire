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
  - resource: src/ScreenHire.Infrastructure/Persistence/DevelopmentDataSeeder.cs
  - resource: .claude/skills/preview/SKILL.md
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
`/preview fresh` starts a clean demo and `/preview stop` stops it. The Aspire CLI is a repo-local tool: run
`dotnet tool restore` once. Without Claude Code:

```bash
pnpm install   # once, and again after pulling changes to any package.json
pnpm dev       # = dotnet run --project apps/apphost
```

The first start pulls the container images (Postgres with pgvector, Azurite, Mailpit, the Dev SSO server) and takes a
few minutes; later starts are much quicker. The console prints the Aspire dashboard's link with a login token.
The API listens only once it has migrated and seeded the database, and reports healthy only once the demo's data is
in, so when the dashboard shows every resource as healthy, the demo data is there. The synthetic applicants' integrity
levels follow within a minute, once the worker has assessed them.

Frontend changes reload on their own while it runs. After a change to the API or the worker, rebuild just that service
while the rest keeps running: `dotnet aspire resource api rebuild` (or `worker`), or **Rebuild** on the resource in the
Aspire dashboard. Attach a debugger to the `ScreenHire.Api` or `ScreenHire.Worker` process, or start the AppHost from
your IDE. After a change to the AppHost itself, start it again; this clone's running stack is replaced.

Stop with Ctrl+C (or `/preview stop`, which is `dotnet aspire stop`). Aspire then removes the containers it started. The database, uploaded files and
Mailpit's inbox stay in Docker volumes (one set per clone or worktree), so the next start has the same candidates, the
same emails and anything you changed.

### Start over

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

Sign in at http://localhost:4323 with **Continue with Dev SSO** in the sign-in page's Single sign-on panel, then pick
a user. No password anywhere.

| Dev SSO user | Signs in as |
|---|---|
| Avery Quinn (`avery@brightwater.example.com`) | Owner of Brightwater Studio, the seeded employer with all the demo data |
| Jordan Ellis (`jordan@brightwater.example.com`) | Recruiter at Brightwater Studio: works on jobs and candidates, but cannot change the team or the organization's settings |
| Sam Rivera (`sam@example.org`) | A new user with no organization, who goes through onboarding. After the first time they keep the organization they made |
| Any email address you type | A new user each time you use a new address: the way to show onboarding again |
| Unverified address | Refused: the provider did not vouch for the address |

The Email link panel's **Email me a sign-in link** works too: the link arrives in Mailpit, and opening it asks for
one more click (**Sign in**) so a mail scanner cannot use it up.

Avery manages the team in **Settings > Team**: **Invite** sends the invitation to Mailpit. Its link opens the
dashboard's **Join your team** page, and **Accept invitation** signs the invitee in to Brightwater with the invited
role. Open the link in a private window so Avery stays signed in. Jordan, a Recruiter, has no Settings in the sidebar.

## What the demo data is

Brightwater Studio, a fictional booking and payments software company. `DevelopmentDataSeeder` creates it on every
stack, tests included; `pnpm dev` adds the rest (`DevelopmentDemo` in `apps/api/Demo`). All names, addresses and
numbers are made up.

| Job | Status | Boards | What is in it |
|---|---|---|---|
| Senior .NET Engineer | Open, from the "Software engineer" template | Every board | The synthetic applicants: Robin Ashby (genuine), Sam Okafor (used AI and said so, through a VPN), Jordan Vale (a bot) and a three-person application farm, all in Applied. Joel Mensah, a genuine candidate, is in Technical exercise with a note |
| Senior Product Designer | Open, with the demo's designer questionnaire | Every board | Three candidates in Applied, Screening and Interview |
| Frontend Engineer (Svelte) | Open, with the demo's frontend questionnaire | Careers page and sandbox board only | Three candidates in Screening, Interview and Offer (with a note) |
| Support Engineer | Closed, from Brightwater's own template | Removed everywhere | Three candidates who went through the stages: one Hired, one Rejected and one Withdrawn, each with the recruiter's notes. Its careers page answers "This job has closed" |
| Backend Engineer (Payments) | Draft, from Brightwater's own template | None | Ready to publish during the demo |
| Customer Success Lead | Draft, with the demo's customer success questionnaire | None | No template, an on-site job in Manchester |

Every job asks questions, and every demo candidate answers each question of their job, so their pages show answers.
The synthetic applicants keep the answers their integrity scenarios need.
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
same people the integrity golden tests use ([integrity-engine.md](../architecture/integrity-engine.md) §10.8).

Templates: the two system templates ("Software engineer", "Hourly role") and "Brightwater engineering", which is the
Software engineer pipeline and questionnaire plus the three candidate emails.

Change history ([change-history.md](../architecture/change-history.md)): the demo makes its template, emails and jobs
as Avery Quinn, so their History sheets name her. On top, Jordan Ellis starts version 2 of "Brightwater engineering"
(a notice period question, then made required, and two more days for Interview) and changes the salary of "Backend
Engineer (Payments)", and Avery adds an "Interview invitation" email template and edits its subject. The jobs `DevelopmentDataSeeder`
makes have no history until someone edits them.

### What is seeded where

| Layer | Runs | Adds |
|---|---|---|
| `DevelopmentDataSeeder` (`src/ScreenHire.Infrastructure/Persistence`) | Every stack in Development, tests included, before the API listens | Brightwater Studio, its brand colour and integrity settings, Avery Quinn as Owner, three open jobs and the Customer Success Lead draft. Tests treat it as read-only ([testing.md](../process/testing.md)) |
| `DevelopmentDemo` (`apps/api/Demo`) | `pnpm dev` and `/preview` only (`ScreenHire:Demo`), after the API starts; `/health` waits for it | Everything else on this page. Each step looks for what it adds first, so a restart with kept data adds nothing |
| Dev SSO users (`apps/apphost/DevSso`) | Every Development stack | The sign-in choices under "Demo logins" |


## A five-minute demo

1. **The jobs (30 s).** Sign in as Avery Quinn. The jobs list shows each job's status first, opens on the last
   updated, sorts by any column header, and the Status filter counts what is open and what is a draft. The sidebar's saved views count what needs attention: "Needs review" and "Verification suggested".
2. **Integrity, explained (90 s).** Open Senior .NET Engineer. The authenticity chips over the table count the seven
   applicants by level, each chip filters the table to it, and each row has its level. Open Jordan Vale: the panel lists every reason (a hidden form field
   filled in, browser automation, a six-second form, a throwaway inbox, a phone number that is not valid, a hosting
   provider's network) with its evidence and a suggested next step. Point out what is not there: no score, no
   percentage, no reject button. Open Taylor Brook, the last of the three-person farm to apply, for "Same device" and
   "Same resume file" shared with Alex Reed and Morgan Hale, then Sam Okafor, who used AI within the job's policy and
   raises no concern.
3. **Move a candidate, see the email (45 s).** Open Robin Ashby, choose **Move to stage**, then Screening. The
   Activity panel records the move, and Mailpit (http://localhost:8025) shows "An update on your application for Senior .NET Engineer"
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

Extras if there is time: Support Engineer for a closed job and its candidates' activity with the recruiter's notes,
the Templates page and its builder (questions, automations and the guardrail that refuses an
integrity-driven rejection), the workflow graph and "Simulate", Ctrl K for the command palette, the dark theme, and a
second browser window on the same job to show live updates.

Settings, as Avery (owners and admins only):

Settings is one item at the bottom of the sidebar; its pages share the Settings menu.

- **Team and Roles.** **Invite** opens a sheet that starts at Viewer and says what each role is for; **Compare roles**
  opens Roles, the table of what each role can do.
- **Branding.** Brightwater's teal is saved. Change it, press **Save colour**, and reload
  http://localhost:4322/brightwater: the Apply buttons and the header mark take the new colour. A pale colour comes
  back darkened, with "Darkened so text on it stays readable", and the colours the careers pages will use.
- **API keys.** **Create key** shows the key once, with Copy; afterwards the list shows only its first characters.
  Under the keys, "Use the ScreenHire API" gives the base URL and links the API reference.
  `curl -H "Authorization: Bearer <key>" http://localhost:5080/v1/jobs` lists Brightwater's jobs until you revoke it
  from the row's menu.
- **Email templates.** Open "Application received", change the subject, and the preview with the made-up Jordan Lee
  follows as you type; **Send test** lands in Mailpit with `[Test]` in front. Then, on a job's pipeline, tick a few
  candidates and press **Email**: each gets the email in Mailpit, and it shows in their Activity. Back on the template,
  Last updated says "by you", and **History** shows "You changed the subject" with the old and new subject.
- **Team**, **Organization** (rename; the careers address stays), **Integrity** (the consistency check, off until
  turned on; the Fake model answers locally) and **Webhooks** (add an endpoint, see its signing secret once, then its
  delivery log with replay).

## What is faked locally

| Real thing | Locally | Configured by |
|---|---|---|
| Google, Microsoft and other sign-in providers | Dev SSO (navikt/mock-oauth2-server) with the users above; the real providers are hidden until their client ids are in user secrets | `apps/apphost/DevSso`, `ScreenHire:Auth` |
| Email provider | Mailpit catches every email | `ScreenHire:Email` |
| File storage | Azurite (Azure Storage emulator) | Aspire |
| LLM consistency check | A deterministic fake model; on for Brightwater only | `ScreenHire:Integrity:Llm` (worker) |
| Job boards | The sandbox board (accepts after 20 s, removes after 30 s) and the feeds; nothing leaves the machine. Google Indexing is off | `ScreenHire:Distribution` |
| IP intelligence | Development's fixed ranges for the RFC 5737 documentation addresses the synthetic applicants use | `ScreenHire:Integrity:Network` (worker) |
| Bot challenge (Turnstile) | Not configured, so recorded as "not checked"; the honeypot still works | `ScreenHire:Turnstile` |
| Malware scanning | `NoMalwareScanner`, which records every file as "not scanned" | `IMalwareScanner` |

The [MVP status](../status.md#owner-actions) lists what each of these needs before real use.

## When something looks wrong

- **A resource is unhealthy in the Aspire dashboard.** Open its logs there. Docker not running and a port already in
  use are the usual causes. `pnpm docker:start` starts Docker Desktop and waits for the engine.
- **No integrity chips yet.** The worker assesses after the API has started; give it a few seconds. The first assessment
  after a cold start is the slowest.
- **The sandbox board still says "Submitted".** It waits 20 seconds on purpose, like a real board's review.
- **No email in Mailpit.** Automations send from the worker after the move commits; it takes a second or two. Only
  the three seeded open jobs and jobs from "Brightwater engineering" send candidate emails. With `fresh` Mailpit
  starts empty, and the demo's own emails are sent once, on the start that adds their candidates.
