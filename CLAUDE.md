# ScreenHire — agent guide

ScreenHire is an affordable applicant tracking system (ATS) for small hiring teams that detects AI-generated, fraudulent
and bot applicants and explains every signal in plain language. Decisions, research and designs live in
[`docs/README.md`](docs/README.md) — read the relevant ADR / architecture doc before changing a subsystem.

## Repo layout

| Path | What |
|---|---|
| `apps/apphost` | .NET Aspire AppHost — runs the whole stack locally |
| `apps/api` | ASP.NET Core minimal API (`Endpoints/*`, `Contracts/*`); emits `packages/api-client/openapi.json` at build |
| `apps/worker` | .NET worker: hosts the Wolverine handlers, durability agent and cron schedules |
| `apps/careers` | Astro 7 SSR (Node) — tenant careers pages + apply flow |
| `apps/marketing` | Astro 7 static — SEO-first marketing site |
| `apps/dashboard` | SvelteKit 3 SPA — recruiter dashboard (shadcn-svelte on bits-ui) |
| `packages/ui` | Warm Grid design tokens, Tailwind 4 theme, self-hosted fonts |
| `packages/api-client` | Orval-generated TS client + Zod schemas (do not edit `src/generated`) |
| `src/ScreenHire.Application` | Message contracts (`ITenantEvent`), Wolverine handlers and the shared messaging setup; referenced by API and worker |
| `src/ScreenHire.Domain` | Domain model (no framework dependencies) |
| `src/ScreenHire.Infrastructure` | EF Core (Postgres, snake_case), migrations |
| `src/ScreenHire.ServiceDefaults` | Aspire service defaults: OpenTelemetry, health checks, service discovery, resilience |
| `tests/` | xUnit v3 + AwesomeAssertions: `ScreenHire.UnitTests`, `ScreenHire.IntegrationTests` (Aspire), `ScreenHire.E2E` (Playwright), `ScreenHire.Testing` (shared AppHost fixture). Vitest lives next to the frontend code. See [`docs/process/testing.md`](docs/process/testing.md) |

## Commands

- First run: Docker running, then `pnpm install` at the repo root (Aspire does **not** run installs), then
  `pnpm e2e:install` once per machine (Playwright's Chromium for the end-to-end tests; again after bumping
  `Microsoft.Playwright`).
- Run everything: `pnpm dev` (= `dotnet run --project apps/apphost`). API :5080, marketing :4321, careers :4322,
  dashboard :4323; the Aspire dashboard URL is printed on start. Sign in to the dashboard with **Dev SSO** (the
  local mock provider; "Avery Quinn" owns the seeded Brightwater) or an email link; every email lands in **Mailpit**.
  Both run on random ports shown in the Aspire dashboard. No real provider accounts or secrets are needed.
- **Parallel worktrees:** several Kangentic tasks may run at once. Before `pnpm dev`, check/reserve ports with the
  Kangentic dev-port tools (`kangentic_check_dev_ports` / `kangentic_reserve_dev_ports`) and pass them as
  `ScreenHire__Ports__Api|Marketing|Careers|Dashboard`, or run with `ScreenHire__PinPorts=false` for random ports
  (URLs are shown in the Aspire dashboard). Data volumes are already per-worktree; integration tests use no volumes and
  random ports.
- **Never stop processes by name, port or process tree.** This machine also runs Kangentic (the board driving these
  tasks; its dev server owns :5173 and Electron owns other local ports). Stop only what you started — via the tool that
  started it, or a PID whose full command line you have checked points at this repo — and remove containers by exact
  name after checking their Aspire labels.
- Build / check: `pnpm build` (= `dotnet build ScreenHire.slnx` + `pnpm -r build`), `pnpm check` (svelte-check
  `--fail-on-warnings`, astro check, tsc), `pnpm -r --if-present test` (Vitest), `dotnet test --solution
  ScreenHire.slnx` (Microsoft Testing Platform via `global.json`: unit, Aspire integration and Playwright
  end-to-end; the last two boot the AppHost, so Docker must be running, and the end-to-end tests serve the
  `pnpm -r build` output).
- **Tests:** which tier for what, running one test, the shared `AppHostFixture`, data isolation (each test creates its
  own organization; the seeded `brightwater` is read-only) and the flake policy are in
  [`docs/process/testing.md`](docs/process/testing.md). The AppHost's `ScreenHire:Frontends` is `Dev` (default),
  `Built` (production previews, used by the end-to-end tests) or `None` (used by the integration tests).
- **The gate:** `pnpm verify` runs all of the above with warnings as errors, plus an API-contract drift check, the
  marketing site's Lighthouse budgets and every test suite (unit, Vitest, Aspire integration, Playwright end-to-end;
  steps `verify:dotnet`, `verify:contract`, `verify:lighthouse` and `verify:test` run alone too). It is the only CI for
  now; see the `/verify` skill.
- Marketing budgets: `pnpm --filter @screenhire/marketing build` fails on more than 15 KB of JS per page or a broken
  internal link; `pnpm --filter @screenhire/marketing lighthouse [paths]` (= `verify:lighthouse`) checks mobile LCP
  under 1.8 s and CLS under 0.05 on the build, headless, using Playwright's Chromium.
- After changing an endpoint or contract: `pnpm generate:api` and commit `openapi.json` + `src/generated`.
- Migrations: `dotnet ef migrations add <Name> --project src/ScreenHire.Infrastructure --startup-project src/ScreenHire.Infrastructure --output-dir Persistence/Migrations`.

## Board pipeline (Kangentic)

Tasks move Executing → Code Review → Testing → Merge → Done on their own. Workflow tasks skip Planning, which waits
for a person to approve the plan; their briefs are the plan. Each column's automation reads the **last line** of the
agent's final message, so these lines are contracts: print one only when its condition holds, alone, with nothing
after it.

| Column | Skill | Outcome lines |
|---|---|---|
| Executing | (the brief) | `Implementation: complete` when the brief is done and `pnpm verify` passes. The column then asks for a findings pass, which ends `Findings: applied` and moves the task to Code Review. |
| Code Review | `/task-review` (isolated session) | `Verdict: Ready` → Testing; numbered steps + `Verdict: Blocked` → back to Executing (at most 3 times) |
| Testing | `/pull-request` | `PR: ready to merge` → Merge; `PR: blocked` stays and notifies |
| Merge | `/merge-pull-request` | `PR: merged` → Done (worktree removed, dependents start); `PR: blocked` stays and notifies |

Unattended-run rules are in `.claude/rules/unattended-runs.md`, and the conflict playbook for integrating `main` is
in `.claude/rules/integration-conflicts.md`. `scripts/guard-processes.mjs` is a PreToolUse hook that refuses
process-killing commands.

## Product & legal non-negotiables (ADR-0004, ADR-0005, docs/research/legal.md)

1. **Evidence, not verdicts.** Integrity output is categorical levels with reason codes, evidence, counter-evidence,
   a plain-language summary and a suggested next step — never a numeric fraud score or "% AI-written".
2. **Integrity signals never drive rejection.** No auto-reject from `integrity.*` conditions; the workflow engine must
   refuse such definitions at validation time. No "reject" control inside integrity UI.
3. **No emotion / intent / honesty / personality inference.** No biometric templates. **v1 has no biometrics at all**
   (ID/liveness is phase 2, interview integrity phase 3).
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

- **Svelte 5 runes only** (`compilerOptions.runes: true`); no `export let`, `$:`, `on:`, `<slot>`.
- **SvelteKit 3:** config in `vite.config.ts` (no `svelte.config.js`); import via `#lib/...` with `.js` extensions
  (not `$lib`); `$app/state` (not `$app/stores`); `$app/env` (not `$app/environment`); tsconfig extends `$app/tsconfig`.
- **shadcn-svelte:** components in `apps/dashboard/src/lib/components/ui` are our code; edit freely. Use `field` +
  superforms 3, not the `form` component; Sheet, not Drawer.
- **Tokens only:** colours, fonts and radii come from `@screenhire/ui` (Tailwind classes like `bg-card`,
  `text-status-review`); never hard-code hex values. Status = shape mark + colour + text label, always.
- **Public pages ship ~0 KB JS:** Astro components and native HTML controls; Svelte islands only where behaviour is
  needed. Embeddable apply widget = iframe (never Shadow DOM).
- Accessibility: WCAG 2.2 AA, visible focus ring, non-drag alternatives for drag-and-drop.

## Backend rules

Detail: `.claude/rules/dotnet-api.md` and `.claude/rules/utc-timestamps.md`. Procedures: `/add-endpoint`,
`/add-migration`, `/add-message`.

- .NET 10, central package management (`Directory.Packages.props`). **Licence policy (ADR-0008):** MIT/Apache/BSD
  preferred; do not add MediatR, AutoMapper, MassTransit 9, FluentAssertions 8, Duende, ImageSharp, Finbuckle, iText,
  or Polly/Verify directly. Check the licence of every new package.
- Minimal APIs with `TypedResults`, one `Map*Endpoints` extension per area under `apps/api/Endpoints`, DTO records in
  `apps/api/Contracts`. Enums serialize as strings; numbers are strict (clean OpenAPI → clean TS).
- Messaging/background work: Wolverine with the Postgres outbox (not hosted-service loops); see
  [`docs/architecture/messaging.md`](docs/architecture/messaging.md) and `/add-message`. The API only publishes, through
  `IDbContextOutbox<ScreenHireDbContext>` and one `SaveChangesAndFlushMessagesAsync`; the worker runs every handler.
  Client-visible events implement `ITenantEvent`.
- Idempotency: delivery is at least once. The durable inbox drops redelivered envelopes; a message that can be sent
  twice for one intent carries an idempotency key from stable ids (never the clock or a new `Guid`) and its handler is
  `[Deduplicated(ValueSource.InputMember, ...)]`; and every handler effect is idempotent on its own (state check,
  unique index or upsert).
- Sign-in and tenancy (`docs/architecture/sign-in-and-tenancy.md`): every route needs a signed-in user unless it
  calls `.AllowAnonymous()`; cookie-authenticated POSTs need `.RequireAntiforgeryToken()`. Tenant-owned entities
  implement `ITenantOwned` and get a row-level security policy in their migration. The API and worker connect as
  `screenhire_app` (bound by RLS); handlers never filter by organization themselves. Google, Microsoft and generic
  OIDC providers appear only when their client id and secret are in user secrets.
- Postgres: connect directly (not transaction-mode PgBouncer) — Wolverine leader election and transaction-local tenant
  settings need it. The DbContext is registered without EF's retrying execution strategy (`DisableRetry`), which
  refuses the outbox's transaction.

## Claude Code tooling

Skills (`/add-endpoint`, `/add-migration`, `/add-message`, `/add-dashboard-page`, `/add-careers-page`,
`/add-marketing-page`, `/commit`, `/sync-docs`), review and test agents, and rules live in `.claude/`. What each is for and when to use it:
[`docs/process/dev-tooling.md`](docs/process/dev-tooling.md).
