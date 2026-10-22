# ScreenHire Documentation

ScreenHire is a hiring platform (ATS) for SMBs first — growing to mid-market, enterprise and agencies — in the US and
UK/EU. It sets itself apart by **detecting AI-generated, fraudulent and bot applicants with explainable, lawful
evidence**, by **per-job workflows built from user-defined templates**, by a **public API**, and by **in-house
one-click job posting and teardown**.

> Positioning (from research): *an integrity layer from first click to first day that recruiters and candidates can
> both understand, at GoHire prices.*

**Why it exists:** the owner's team hires on gohire.io (mainly .NET developer roles), and fake / AI-generated
candidates get through its screening. **Positioning (owner, 2026-09-23):** a simple, intuitive, affordable tracker for
small hiring teams in the style of GoHire or BambooHR — not an enterprise fraud platform like Sardine or Ashby.
Detection runs in the background (custom models possible later). A public job board alongside the tracker is a
possible later addition. **Domain:** `screenhire.io` planned (not yet bought; `.com` is taken) — the base domain is
configuration everywhere (ADR-0003).

## MVP status

Snapshot 2026-10-21, after the MVP workflow's tasks (PRs #1 to #18 and the acceptance task) and the "Design system
v0.2" workflow (PRs #22 to #33). The whole product runs on
one machine with `pnpm dev` ([local demo](operations/local-demo.md)), and one Playwright journey in `pnpm verify`
(`tests/ScreenHire.E2E/AcceptanceJourneyTests.cs`) walks it end to end: sign in, a job from a template, publish to the
careers page and the sandbox board, a genuine candidate and a bot apply, both arrive live with their levels and
evidence, a move sends the stage-change email, and closing takes the job down everywhere. Nothing is hosted yet.

What comes next, in order, is the [roadmap](roadmap.md).

### Built

| Area | What works | PRs |
|---|---|---|
| Sign-in and tenancy | Email links, Google, Microsoft and generic OIDC, onboarding, organizations, Postgres row-level security under an app role | #7 |
| Hiring | Jobs with versions, hiring templates (system and your own), candidates, applications, the pipeline | #9 |
| Workflows | Automations with guardrails (no integrity-driven rejection), simulate and validate, transactional email, signed webhooks | #10 |
| Webhook subscriptions | Organization endpoints for six events, a secret per endpoint (shown once, rotatable), signed deliveries with retries and backoff, a delivery log, replay, automatic disabling | the webhook subscriptions task |
| Careers site | Tenant boards and hosts, the apply flow with the notice first, three steps that work without JavaScript, direct-to-storage resume upload | #11 |
| Distribution | One-click publish and teardown, Google for Jobs JSON-LD and sitemaps, five XML feeds, a public job-board API, the Indexing API queue | #12 |
| Integrity | Authenticity and AI-assistance assessments from a versioned ruleset: network, identity, automation, duplicates, document and answer forensics, the LLM consistency check, aggregate form telemetry | #13, #15, #18 |
| Dashboard | Every page on [design system v0.2](design/design-system.md) (ADR-0010): the shell with a sidebar, an icon rail and a phone sheet; jobs and their forms; the candidates list and job pipeline; the candidate page with an inline resume preview; templates and the workflow builder; live updates; integrity badges and the evidence panel. A development-only style guide shows every component | #14, #16, #17, the acceptance task, #24 to #27, #29 to #33 |
| Demo data | The local demo fills every screen, with a questionnaire on every seeded job and answers for every candidate | #20, #23 |
| Platform | Wolverine messaging with the Postgres outbox, the SignalR hub, the test harness and gate, the marketing site with its SEO budgets, a visual design pass at three widths in `/task-review` | #4, #5, #6, #8, #22, #28 |

### Faked locally

Every external integration sits behind an interface with a local stand-in, and a provider with no configuration is
hidden rather than an error.

| Integration | Local stand-in |
|---|---|
| Identity providers | Dev SSO (navikt/mock-oauth2-server) with seeded users; Google, Microsoft and generic OIDC appear only with a client id and secret |
| Email | Mailpit, through the SMTP adapter behind `IEmailSender` |
| LLM | A deterministic fake model for the consistency check; with no provider the check records "unavailable"; off per organization by default |
| Job boards | A sandbox push board (Development only) and the feeds; Google Indexing has a local fake and is off unless set; Indeed is flags only |
| IP data | Configured ranges for the RFC 5737 documentation addresses; an ip2asn file and a Tor exit list are optional and absent |
| Storage, bot challenge, malware scan | Azurite; Turnstile unconfigured (recorded as "not checked", the honeypot still works); `NoMalwareScanner`, which records every file as "not scanned" |
| Not built | Identity verification and liveness (phase 2, the control is disabled), interview integrity (phase 3), the cross-employer Signal Network (built, flagged off until legal review) |

### Owner actions

Everything below needs a person: an account, a key, a decision or a legal review. The PR that raised each is in
brackets.

- **Sign-in.** Register the Google and Microsoft apps with redirect URIs `{AppOrigin}/v1/auth/signin-google` and
  `/signin-microsoft`, request Microsoft's `xms_edov` optional claim, and put the client ids and secrets in the secret
  store (#7). Persist and share the Data Protection key ring across instances, for sign-in cookies and telemetry tokens
  (#7, #18). List the reverse proxies and the careers servers in `ScreenHire:Auth:TrustedProxies` (#7, #11).
- **Email.** Choose the production provider behind `IEmailSender`, and set `ScreenHire:Email:From` and the worker's
  SMTP connection; without them automations skip email (#7, #10). Set a production `ScreenHire:Webhooks:SigningKey`
  for the API and the worker (#10, the webhook subscriptions task).
- **LLM.** Set `ScreenHire:Integrity:Llm:Provider=Anthropic` and the `anthropic-api-key` secret, then replace the
  hand-written recordings with live ones; add the provider to the sub-processor list and DPA before a real tenant turns
  the check on (#15).
- **Job boards.** Create a Google service account that owns the careers domain in Search Console, set
  `GoogleIndexing:Mode=Google` and `google-indexing-service-account-key`, and ask for more than 200 calls a day; the real
  client has never run (#12). Onboard each organization's feed with Careerjet, Talent.com, Adzuna and Jooble, and check
  Jooble's date format and Adzuna's schema with them (#12).
- **IP data and identity checks.** Supply an ip2asn file and a Tor exit list, or wire a commercial source behind
  `IIpIntelligence` (#13). Set `ScreenHire:Integrity:DeviceHashKey` (32+ characters) in production (#13). Refresh the
  embedded disposable-domain list from time to time (#13).
- **Storage and scanning.** Create the uploads container with a CORS rule for PUT from the careers hosts and GET from the
  dashboard host (the resume preview, #25), and a one-day lifecycle rule for unclaimed uploads (#11). Wire a real
  malware scanner behind `IMalwareScanner` (#11, #15). Optionally, Turnstile keys in `ScreenHire:Turnstile` (#11).
- **Deploy.** Run the EF migrations and `ScreenHireMessaging.ProvisionStorageAsync` as the owner, give `screenhire_app`
  its login, and give the API and worker only that connection (#7). Wolverine's static code generation is deferred
  until hosting (#5). Set `SITE_URL` for marketing builds and redirect URLs without a trailing slash (#6); set Astro's
  `security.allowedDomains` for careers behind a CDN (#11); keep access logs for 30 days or less (#11). Set
  `PUBLIC_APP_URL` so the marketing site's "Start free" reaches sign-in (#6).
- **Legal and policy.** A legal review of the law tracker before publishing (#6); the analytics decision, with a privacy
  page and the ePrivacy position per market (#6); a legal review before any typing-rhythm signal (#18); a check of the
  device-check defaults per jurisdiction (notice outside the EU and EEA, consent inside) (#18).
- **Product.** Set prices and job limits in `apps/marketing/src/lib/pricing.ts` (#6). Move jobs still on "Software
  engineer" version 1 to version 2 (#13).

### Known gaps

- `answers.*` and `application.source` facts are not wired into automation conditions yet (#11).
- With the demo seeding on, a live update can arrive late while the worker works through the demo's backlog just after
  startup (integrity-engine.md §10.9). In the browser pane, `/preview` waits for an integrity chip before it checks anything live.

## Decision log

| ADR | Decision | Status |
|---|---|---|
| [0001](adr/0001-api-first-monorepo.md) | API-first, single monorepo | Accepted |
| [0002](adr/0002-dotnet-backend.md) | ASP.NET Core on .NET 10 LTS, PostgreSQL, object storage behind an abstraction | Accepted |
| [0003](adr/0003-astro-public-surfaces.md) | Astro for marketing site (SSG, SEO-first) and careers pages (SSR) | Accepted |
| [0004](adr/0004-evidence-not-verdicts.md) | Integrity output is evidence, not verdicts; never auto-reject on integrity | Accepted |
| [0005](adr/0005-cross-tenant-signal-network.md) | Cross-employer Signal Network: pseudonymised, opt-in, legal-gated | Accepted with gate |
| [0006](adr/0006-dashboard-svelte.md) | Recruiter dashboard: Svelte 5 + SvelteKit 3 SPA | Accepted |
| [0007](adr/0007-hosting-azure.md) | Azure bootstrap (~$45–65/mo pre-credits), Cloudflare in front | Accepted |
| [0008](adr/0008-dotnet-building-blocks.md) | .NET building blocks (Wolverine, Identity, Aspire, Orval, RLS) under licence policy | Accepted |
| [0009](adr/0009-design-system-warm-grid.md) | "Warm Grid" design direction; shadcn-svelte (dashboard) + daisyUI CSS-only (public pages); one token source | Accepted, amended by 0010 |
| [0010](adr/0010-design-system-v0-2.md) | Design system v0.2: simple and approachable; layers, page templates, fluid sizes, words | Accepted |

## Architecture

- [Integrity Engine](architecture/integrity-engine.md) — signals, two assessments, collection timeline, Signal Network
- [Workflow Engine](architecture/workflow-engine.md) — templates, stages, automations, guardrails, execution model
- [Job Distribution Engine](architecture/job-distribution.md) — adapters, posting state machine, honest teardown
- [Real-time updates](architecture/realtime.md) - SignalR hub, tenant-scoped fan-out of `ITenantEvent` through a
  Wolverine queue, the `packages/realtime` client, the C#/Zod contract test and the scale-out path
- [Messaging](architecture/messaging.md) - Wolverine on Postgres: outbox, worker queue, idempotency, retries and
  dead letters, cron, telemetry
- [Sign-in and tenant isolation](architecture/sign-in-and-tenancy.md) - providers, email links, domains,
  organizations, the query filter and row-level security, Dev SSO and Mailpit
- [Careers apply flow](architecture/apply-flow.md) - notice before collection, the three steps without JavaScript,
  direct-to-storage uploads, public submission, employer branding and the content security policy, tenant hosts

## Design

- [Design system](design/design-system.md) - the one source of truth for the UI: principles, the review checklist,
  colour, type, layers, page templates, screen sizes, flow, components, words, dates, icons, integrity UI

## Operations

- [Run ScreenHire locally and demo it](operations/local-demo.md) - prerequisites, `pnpm dev`, the URLs, the demo
  logins and data, a five-minute demo script, what is faked locally

## Process

- [Board pipeline](process/board-pipeline.md) — Kangentic columns, outcome lines, notifications and how to unblock a
  task; where the gate, conflict rules and process guard live; processes on the dev machine (Aspire teardown, Docker,
  stopped gates)
- [Testing](process/testing.md) — which tier for what, running one test, the shared Aspire fixture, data isolation,
  end-to-end setup and the flake policy
- [Claude Code tooling](process/dev-tooling.md) - the skills, agents and rules in `.claude/`, and when to use each

## Research (snapshot 2026-10-18)

- [Frontend benchmarks](research/frontend.md)
- [Dashboard: Svelte 5 vs Angular 22](research/dashboard-framework.md)
- [Svelte 5 UI libraries & ecosystem map](research/component-libraries.md)
- [Hosting comparison](research/hosting.md) · [Bootstrap hosting < $250/mo](research/hosting-bootstrap.md)
- [.NET building blocks & licence audit](research/dotnet-building-blocks.md)
- [Legal & regulatory landscape](research/legal.md) — *not legal advice*
- [Job-board integration landscape](research/job-boards.md)
- [Competitive teardown](research/competitors.md)
