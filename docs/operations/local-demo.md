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

```bash
pnpm install   # once, and again after pulling changes to any package.json
pnpm dev       # = dotnet run --project apps/apphost
```

The first start pulls the container images (Postgres with pgvector, Azurite, Mailpit, the Dev SSO server) and takes a
few minutes; later starts take about 30 seconds. The console prints the Aspire dashboard's link with a login token.
The API listens only once it has migrated and seeded the database, so when the dashboard shows every resource as
healthy, the demo data is in. The synthetic applicants' integrity levels follow a few seconds later, once the worker
has assessed them.

Stop with Ctrl+C. Aspire then removes the containers it started. Data stays in Docker volumes (one set per clone or
worktree), so the next start has the same candidates and anything you changed. To start from a clean demo, run once
with `ScreenHire__PersistData=false` (no volumes at all), or, with the stack stopped, delete the two data volumes
Aspire made for this clone in Docker Desktop (their names end in `postgres-data` and `storage-data`).

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

Another stack already on one of these ports? Pin others with `ScreenHire__Ports__Api`, `__Marketing`, `__Careers`,
`__Dashboard`, `__Mailpit` or `__DevSso`, or run with `ScreenHire__PinPorts=false` for random ports, which the Aspire
dashboard lists.

## Demo logins

Sign in at http://localhost:4323 with **Continue with Dev SSO**, then pick a user. No password anywhere.

| Dev SSO user | Signs in as |
|---|---|
| Avery Quinn (`avery@brightwater.example.com`) | Owner of Brightwater Studio, the seeded employer with all the demo data |
| Sam Rivera (`sam@example.org`) | A new user with no organization, who goes through onboarding. After the first time they keep the organization they made |
| Any email address you type | A new user each time you use a new address: the way to show onboarding again |
| Unverified address | Refused: the provider did not vouch for the address |

**Email me a sign-in link** works too: the link arrives in Mailpit.

## What the demo data is

Brightwater Studio, a fictional booking and payments software company. `DevelopmentDataSeeder` creates it on every
stack, tests included; `pnpm dev` adds the rest (`DevelopmentDemo` in `apps/api/Demo`). All names, addresses and
numbers are made up.

| Job | Status | What is in it |
|---|---|---|
| Senior .NET Engineer | Open, from the "Software engineer" template | The synthetic applicants: Robin Ashby (genuine), Sam Okafor (used AI and said so, through a VPN), Jordan Vale (a bot) and a three-person application farm. Emails go to candidates when they apply and when they reach Screening or Interview |
| Senior Product Designer | Open | Three candidates in Applied, Screening and Interview |
| Frontend Engineer (Svelte) | Open | Three candidates in Screening, Interview and Offer |
| Backend Engineer (Payments) | Draft, from Brightwater's own template | Ready to publish during the demo |
| Customer Success Lead | Draft | No template, an on-site job in Manchester |

Templates: the two system templates ("Software engineer", "Hourly role") and "Brightwater engineering", which is the
Software engineer pipeline plus the three candidate emails.

## A five-minute demo

1. **The jobs (30 s).** Sign in as Avery Quinn. The jobs list shows what is open and what is a draft. The sidebar's
   saved views count what needs attention: "Needs review" and "Verification recommended".
2. **Integrity, explained (90 s).** Open Senior .NET Engineer. The overview bar sorts the six applicants by
   authenticity level, and each row has its chip. Open Jordan Vale: the panel lists every reason (a hidden form field
   filled in, browser automation, a six-second form, a throwaway inbox, a phone number that is not valid, a hosting
   provider's network) with its evidence and a suggested next step. Point out what is not there: no score, no
   percentage, no reject button. Open a farm member (Alex Reed, Morgan Hale or Taylor Brook) for "Same device" and
   "Same resume file", then Sam Okafor, who used AI within the job's policy and raises no concern.
3. **Move a candidate, see the email (45 s).** Open Robin Ashby, choose **Move to stage**, then Screening. The timeline
   records the move. In Mailpit, Robin has "We received your application for Senior .NET Engineer" from when they
   applied and now "An update on your application for Senior .NET Engineer".
4. **Publish everywhere (60 s).** Open Backend Engineer (Payments) and press **Publish to 8 boards**. The careers page
   is live at once; the local sandbox board confirms in about 20 seconds; the feed boards show "Sent" with each one's
   usual delay. Open the careers page from the job's row on the board strip, or go to
   http://localhost:4322/brightwater.
5. **Apply as a candidate (60 s).** On the careers page, open the new job and apply: the notice comes first, then three
   short steps. The form works with JavaScript off too. Back in the dashboard, the application appears in the open job
   without a reload, and its chip follows once the checks finish.
6. **Take it down (15 s).** **Close & remove everywhere** on the job page. The careers page now answers "This job has
   closed" (HTTP 410) and the sandbox board removes the job about 30 seconds later.

Extras if there is time: the Templates page and its builder (questions, automations and the guardrail that refuses an
integrity-driven rejection), the workflow graph and "Simulate", Ctrl K for the command palette, the dark theme, and a
second browser window on the same job to show live updates.

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

The [MVP status](../README.md#mvp-status) lists what each of these needs before real use.

## When something looks wrong

- **A resource is unhealthy in the Aspire dashboard.** Open its logs there. Docker not running and a port already in
  use are the usual causes.
- **No integrity chips yet.** The worker assesses after the API has started; give it a few seconds. The first assessment
  after a cold start is the slowest.
- **The sandbox board still says "Submitted".** It waits 20 seconds on purpose, like a real board's review.
- **No email in Mailpit.** Automations send from the worker after the move commits; it takes a second or two. Only
  Senior .NET Engineer and jobs from "Brightwater engineering" send candidate emails.
