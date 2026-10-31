# ScreenHire — agent guide

ScreenHire is an affordable applicant tracking system (ATS) for small hiring teams that detects AI-generated, fraudulent
and bot applicants and explains every signal in plain language. Decisions, research and designs live in `docs/`.
Start at [`docs/index.md`](docs/index.md): it and each folder's `index.md` list every doc with one line on what only
that doc holds, so open the one that answers the question. Read the relevant ADR / architecture doc before changing a
subsystem.

## Repo layout

| Path | What |
|---|---|
| `apps/apphost` | .NET Aspire AppHost — runs the whole stack locally |
| `apps/api` | ASP.NET Core minimal API (`Endpoints/*`, `Contracts/*`), the SignalR hub (`Realtime/*`, `/v1/hubs/events`) and the MCP server for AI assistants (`Mcp/*`, `/v1/mcp`, [`docs/architecture/mcp-server.md`](docs/architecture/mcp-server.md)); emits `packages/api-client/openapi.json` at build |
| `apps/worker` | .NET worker: hosts the Wolverine handlers, durability agent and cron schedules |
| `apps/careers` | Astro 7 SSR (Node) — tenant careers pages + apply flow ([`docs/architecture/apply-flow.md`](docs/architecture/apply-flow.md)) |
| `apps/marketing` | Astro 7 static — SEO-first marketing site |
| `apps/dashboard` | SvelteKit 3 SPA — recruiter dashboard (shadcn-svelte on bits-ui) |
| `packages/ui` | Warm Grid design tokens, Tailwind 4 theme, self-hosted fonts, and the candidate question components (`./questions`, `./questions.css`) shared by the careers apply page and the dashboard's questionnaire preview |
| `packages/api-client` | Orval-generated TS fetch client, TanStack Query hooks (`./svelte-query`) and Zod schemas (do not edit `src/generated`) |
| `packages/telemetry` | Framework-free form telemetry for the apply flow: per-field counts only (never key values, timings per key or content), automation markers, the device fingerprint, `sendBeacon` snapshots; under 5 KB brotli, checked by its `build` |
| `packages/realtime` | SignalR client for live updates: reconnect and re-subscribe, a runes connection-state store, Zod schemas per tenant event (checked against `contract/events.json`), cache invalidation by event type |
| `src/ScreenHire.Application` | Message contracts (`ITenantMessage`, `ITenantEvent`), Wolverine handlers and the shared messaging setup; referenced by API and worker |
| `src/ScreenHire.Domain` | Domain model (no framework dependencies) |
| `src/ScreenHire.Infrastructure` | EF Core (Postgres, snake_case), migrations |
| `src/ScreenHire.ServiceDefaults` | Aspire service defaults: OpenTelemetry, health checks, service discovery, resilience |
| `tests/` | xUnit v3 + AwesomeAssertions: `ScreenHire.UnitTests`, `ScreenHire.IntegrationTests` (Aspire), `ScreenHire.E2E` (Playwright), `ScreenHire.Testing` (shared AppHost fixture). Vitest lives next to the frontend code. See [`docs/process/testing.md`](docs/process/testing.md) |

## Commands

- First run: Docker running (`pnpm docker:start` starts Docker Desktop when the engine is down and waits for it;
  never launch Docker Desktop from a task's shell, the guard refuses it), then `pnpm install` at the repo root
  (Aspire does **not** run installs) and `dotnet tool restore` (the repo-local tools: `dotnet-ef` and the Aspire CLI, run as `dotnet aspire ...`), then
  `pnpm e2e:install` once per machine (Playwright's Chromium for the end-to-end tests; again after bumping
  `Microsoft.Playwright`).
- Run everything: `pnpm dev` (= `dotnet run --project apps/apphost`). API :5080, marketing :4321, careers :4322,
  dashboard :4323, Mailpit :8025, Dev SSO :5090; the Aspire dashboard URL is printed on start. Sign in to the dashboard
  with **Dev SSO** (the local mock provider; "Avery Quinn" owns the seeded Brightwater) or an email link; every email
  lands in **Mailpit**. No real provider accounts or secrets are needed. `pnpm dev` also seeds the demo data
  (`DevelopmentDemo`); the five-minute demo is [`docs/operations/local-demo.md`](docs/operations/local-demo.md).
- **To look at the app, use `/preview`** (`/preview fresh` for a clean demo, `/preview stop`). It runs the stack
  through the Aspire CLI (`dotnet aspire start`, `wait`, `describe`, `stop`), reports the URLs and logins and opens the
  dashboard in the task's browser pane; `/run` uses it too. Agents never `dotnet run` the AppHost. After a change to the
  API or worker, `dotnet aspire resource api rebuild` (or `worker`) rebuilds and restarts just that service.
- **Parallel worktrees:** several Kangentic tasks may run at once. In a worktree `/preview` starts with
  `dotnet aspire start --isolated`, which gives every port, the Aspire dashboard's included, a random free number, so
  stacks never collide and nothing needs reserving. Data volumes are already per-worktree; integration tests use no
  volumes and random ports.
- **Aspire MCP server:** `.mcp.json` runs `dotnet tool run aspire agent mcp`, which gives agents the running stack's
  resources, console and structured logs, traces and resource commands. Never run `aspire agent init`: it installs
  hooks and MCP settings into the machine-wide Claude Code and Copilot configuration. `.claude/settings.json` approves
  every `.mcp.json` server (`enabledMcpjsonServers`) and its tools (`mcp__<name>` in `permissions.allow`), so a new
  worktree starts without a prompt; add both when you add a server, or `ProjectMcpServersTests` fails.
- **Never stop processes by name, port or process tree.** This machine also runs Kangentic (the board driving these
  tasks; its dev server owns :5173 and Electron owns other local ports). Stop only what you started — via the tool that
  started it, or a PID whose full command line you have checked points at this repo — and remove containers by exact
  name after checking their Aspire labels. Never TaskStop a running `pnpm verify`: on Windows its tree keeps running.
  Aspire's teardown on Windows can also kill an unrelated process whose launcher has exited; see "Processes on the
  dev machine" in [`docs/process/board-pipeline.md`](docs/process/board-pipeline.md).
- Build / check: `pnpm build` (= `dotnet build ScreenHire.slnx` + `pnpm -r build`), `pnpm check` (svelte-check
  `--fail-on-warnings`, astro check, tsc), `pnpm -r --if-present test` (Vitest), `dotnet test --solution
  ScreenHire.slnx` (Microsoft Testing Platform via `global.json`: unit, Aspire integration and Playwright
  end-to-end; the last two boot the AppHost, so Docker must be running (`pnpm docker:start`), and the end-to-end
  tests serve the `pnpm -r build` output).
- **Tests:** which tier for what, running one test, the shared `AppHostFixture`, data isolation (each test creates its
  own organization; the seeded `brightwater` is read-only) and the flake policy are in
  [`docs/process/testing.md`](docs/process/testing.md). The AppHost's `ScreenHire:Frontends` is `Dev` (default),
  `Built` (the production builds served as they ship, used by the end-to-end tests) or `None` (used by the integration tests).
  `ScreenHire:Distribution:*` (the local sandbox board, `GoogleIndexing:Mode` `Off`/`Local`/`Google`) is forwarded to
  the API and worker alike; see [`docs/architecture/job-distribution.md`](docs/architecture/job-distribution.md) §5.
  `ScreenHire:Verification:Llm:*` (the consistency check's model: `Fake` in Development, `None`, or `Anthropic` with the
  `anthropic-api-key` secret) goes to the worker. `ScreenHire:Slack:*` (real Slack with `ClientId` and the
  `slack-client-secret` parameter, or `Sandbox:Enabled` for the local sandbox workspace; off in `pnpm dev`, on in the
  test stacks) goes to the API and worker alike; see [`docs/architecture/slack.md`](docs/architecture/slack.md). `ScreenHire:PersistData` (default `true`) keeps Postgres, Azurite and
  Mailpit data in per-worktree volumes; `false` starts with none, as the tests do. `ScreenHire:Demo` (default: on with `Dev`
  frontends only) seeds the local demo's data on Brightwater, and the API is healthy only once it is in. The header of `apps/apphost/AppHost.cs`
  describes every setting.
- **The gate:** `pnpm verify` runs all of the above with warnings as errors, plus an API-contract drift check and
  every test suite (unit, Vitest, Aspire integration, Playwright end-to-end; steps `verify:dotnet`, `verify:contract`
  and `verify:test` run alone too). It is the only CI for now; see the `/verify` skill.
- Marketing budgets: `pnpm --filter @screenhire/marketing build` fails on more than 15 KB of JS per page or a broken
  internal link; `pnpm --filter @screenhire/marketing lighthouse [paths]` (= `verify:lighthouse`) checks mobile LCP
  under 1.8 s and CLS under 0.05 on the build, headless, using Playwright's Chromium. It is not in `pnpm verify`: the
  Testing column runs it once for a branch that changes the marketing build, and only one run measures at a time on
  the machine (the next one waits).
- After changing an endpoint or contract: `pnpm generate:api` and commit `openapi.json` + `src/generated`.
- After changing code a doc describes: `node scripts/docs-bundle.mjs sources <file>` names the docs whose `sources:`
  list it (`/sync-docs`). A new or changed doc keeps its frontmatter and its line in the folder's `index.md` in step;
  `DocsBundleTests` checks both (ADR-0012).
- After changing an `ITenantEvent` record: `UPDATE_REALTIME_CONTRACT=1 dotnet test --project tests/ScreenHire.UnitTests --filter-class "*RealtimeContractTests"`
  rewrites `packages/realtime/contract/events.json`; then update `packages/realtime/src/events.ts` until
  `pnpm --filter @screenhire/realtime test` passes, and commit both (`/add-realtime-event`).
- After changing a verification analyzer, the signal catalogue or the ruleset: `UPDATE_VERIFICATION_GOLDEN=1 dotnet test --project tests/ScreenHire.UnitTests --filter-class "*VerificationGoldenTests"`
  rewrites the synthetic applicants' golden files; review the diff, and bump `VerificationRuleset.Version` for a rule change
  ([`docs/architecture/verification-engine.md`](docs/architecture/verification-engine.md) §10).
- Migrations: `dotnet ef migrations add <Name> --project src/ScreenHire.Infrastructure --startup-project src/ScreenHire.Infrastructure --output-dir Persistence/Migrations`.

## Board pipeline (Kangentic)

Tasks move (Planning →) Executing → Code Review → Testing → Merge → Done on their own. Long, complex tasks go through
Planning, where Kangentic approves the plan automatically; simple ones start at Executing, and their briefs are the
plan. Each column's automation reads the **last line** of the
agent's final message, so these lines are contracts: print one only when its condition holds, alone, with nothing
after it.

| Column | Skill | Outcome lines |
|---|---|---|
| Executing | (the brief) | `Implementation: complete` when the brief is done and `pnpm verify` passes. The column then asks for a findings pass, which ends `Findings: applied` and moves the task to Code Review. |
| Code Review | `/task-review` (isolated session) | `Verdict: Ready` → Testing; numbered steps + `Verdict: Blocked` → back to Executing (at most 3 times) |
| Testing | `/pull-request` (plus Lighthouse for a marketing change) | `PR: ready to merge` → Merge; `PR: blocked` stays and notifies |
| Merge | `/merge-pull-request` | `PR: merged` → Done (worktree removed, dependents start); `PR: blocked` stays and notifies |

Unattended-run rules are in `.claude/rules/unattended-runs.md`, and the conflict playbook for integrating `main` is
in `.claude/rules/integration-conflicts.md`. `scripts/guard-processes.mjs` is a PreToolUse hook that refuses
process-killing commands.

## Product & legal non-negotiables (ADR-0004, ADR-0005, docs/research/legal.md)

1. **Evidence, not verdicts.** Verification output is categorical levels with reason codes, evidence, counter-evidence,
   a plain-language summary and a suggested next step — never a numeric fraud score or "% AI-written".
2. **Verification signals never drive rejection.** No auto-reject from `verification.*` conditions; the workflow engine must
   refuse such definitions at validation time. No "reject" control inside verification UI.
3. **No emotion / intent / honesty / personality inference.** No biometric templates. **v1 has no biometrics at all**
   (ID/liveness is phase 2, interview verification phase 3).
4. **Notice before collection.** Browsing collects only server/security logs. Fingerprinting and form telemetry start at
   application start, after the notice, and the assistive-tech toggle disables telemetry with no negative inference.
   Telemetry is aggregates only — never raw keystrokes. Telemetry alone never raises a level.
5. **No scraping or automated fetching of LinkedIn** (or any third-party profile). Use candidate-supplied data only.
6. **Tenant isolation.** Every tenant-owned row has `OrganizationId`; EF named query filters + Postgres RLS with
   transaction-local `set_config('app.tenant_id', …, true)`. Cross-tenant access only via the audited Signal Network
   path, which stays feature-flagged off until legal review.
7. **Domains are configuration.** Never hard-code `screenhire.*` hosts (`screenhire.com` is not ours); use
   `ScreenHire:Domains:*` / env vars (ADR-0003).

## Frontend rules

Detail and the add-component command: `.claude/rules/svelte-dashboard.md` and `.claude/rules/astro-public.md`.
Procedures: `/add-dashboard-page`, `/add-careers-page`, `/add-marketing-page`.

- **Design system:** [`docs/design/design-system.md`](docs/design/design-system.md) is the one source of truth for
  every screen (ADR-0010). Read its principles and its review checklist (section 2) before building UI; the
  `design-review` agent checks every UI change against that checklist. Change the design there, never only in a rule,
  a skill or a component.

- **Svelte 5 runes only** (`compilerOptions.runes: true`); no `export let`, `$:`, `on:`, `<slot>`.
- **SvelteKit 3:** config in `vite.config.ts` (no `svelte.config.js`); import via `#lib/...` with `.js` extensions
  (not `$lib`); `$app/state` (not `$app/stores`); `$app/env` (not `$app/environment`); tsconfig extends `$app/tsconfig`.
- **Dashboard data:** TanStack Query through the generated hooks in `@screenhire/api-client/svelte-query`; live events
  invalidate queries by API path (`#lib/realtime.ts`). Single-key shortcuts can be turned off (WCAG 2.1.4).
- **shadcn-svelte:** components in `apps/dashboard/src/lib/components/ui` are our code; edit freely. Use `field` +
  superforms 3, not the `form` component; Sheet, not Drawer.
- **Tokens only:** colours, fonts, type sizes and radii come from `@screenhire/ui` (Tailwind classes like `bg-card`,
  `text-status-review`); never hard-code hex values or pixel font sizes. Status = its icon + colour + text label,
  always.
- **Public pages ship ~0 KB JS:** Astro components and native HTML controls; Svelte islands only where behaviour is
  needed. Embeddable apply widget = iframe (never Shadow DOM).
- Accessibility: WCAG 2.2 AA, visible focus ring, non-drag alternatives for drag-and-drop.

## Backend rules

Detail: `.claude/rules/dotnet-api.md` and `.claude/rules/utc-timestamps.md`. Procedures: `/add-endpoint`,
`/add-migration`, `/add-message`, `/add-realtime-event`.

- .NET 10, central package management (`Directory.Packages.props`). **Licence policy (ADR-0008):** MIT/Apache/BSD
  preferred; do not add MediatR, AutoMapper, MassTransit 9, FluentAssertions 8, Duende, ImageSharp, Finbuckle, iText,
  or Polly/Verify directly. Check the licence of every new package.
- Minimal APIs with `TypedResults`, one `Map*Endpoints` extension per area under `apps/api/Endpoints`, DTO records in
  `apps/api/Contracts`. Enums serialize as strings; numbers are strict (clean OpenAPI → clean TS).
- Messaging/background work: Wolverine with the Postgres outbox (not hosted-service loops); see
  [`docs/architecture/messaging.md`](docs/architecture/messaging.md) and `/add-message`. The API publishes, through
  `IDbContextOutbox<ScreenHireDbContext>` and one `SaveChangesAndFlushMessagesAsync`; the worker runs every handler.
  The API's one exception is pushing tenant events to dashboards over SignalR, in its own Wolverine schema
  (`wolverine_api`, [`docs/architecture/realtime.md`](docs/architecture/realtime.md)).
  A message about one organization's data implements `ITenantMessage`, or its handler sees no tenant rows under RLS;
  client-visible events implement `ITenantEvent`, which extends it. Every `ITenantEvent` reaches its organization's open
  dashboards with no hub code; add its Zod schema and invalidation with `/add-realtime-event`.
- Change history: an edit of a hiring template, an email template or a job is recorded by `ChangeHistoryInterceptor`
  in the same save; `TrackedKinds` is the allow-list, and tracking another kind is one line there plus a history route
  ([`docs/architecture/change-history.md`](docs/architecture/change-history.md)). Change tracked objects through the
  change tracker, never `ExecuteUpdate`/`ExecuteDelete`, or the change goes unrecorded.
- Idempotency: delivery is at least once. The durable inbox drops redelivered envelopes; a message that can be sent
  twice for one intent carries an idempotency key from stable ids (never the clock or a new `Guid`) and its handler is
  `[Deduplicated(ValueSource.InputMember, ...)]`; and every handler effect is idempotent on its own (state check,
  unique index or upsert).
- Sign-in and tenancy (`docs/architecture/sign-in-and-tenancy.md`): every route needs a signed-in user unless it
  calls `.AllowAnonymous()`; cookie-authenticated POSTs need `.RequireAntiforgeryToken()`. Hiring API groups (jobs,
  applications, candidates, templates) use `.AllowApiKeys()`, which also accepts an organization API key (ADR-0011);
  nothing else does. Tenant-owned entities implement `ITenantOwned` and get a row-level security policy in their
  migration; a row under one job implements `IJobScoped` or `IApplicationScoped` and gets a restrictive `job_scope`
  policy too, and an event that carries a `JobId` implements `IJobEvent` (ADR-0015). The API and worker connect as
  `screenhire_app` (bound by RLS); handlers never filter by organization themselves. Google, Microsoft and generic OIDC providers appear only when their client id and secret are in user
  secrets.
- Postgres: connect directly (not transaction-mode PgBouncer) — Wolverine leader election and transaction-local tenant
  settings need it. The DbContext is registered without EF's retrying execution strategy (`DisableRetry`), which
  refuses the outbox's transaction.

## Claude Code tooling

Skills (`/add-endpoint`, `/add-migration`, `/add-message`, `/add-realtime-event`, `/add-dashboard-page`, `/add-careers-page`,
`/add-marketing-page`, `/commit`, `/merge-back`, `/preview`, `/sync-docs`), review and test agents, and rules live in `.claude/`. What each is for and when to use it:
[`docs/process/dev-tooling.md`](docs/process/dev-tooling.md).
