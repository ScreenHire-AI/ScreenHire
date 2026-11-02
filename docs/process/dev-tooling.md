---
type: Reference
title: "Claude Code tooling"
description: "Every skill, agent and rule in .claude/ and when to use each, commands that were never run, why pnpm's global virtual store was not adopted for worktree installs, where a new task's start-up time goes and what would cut it, what a preview stack commits in memory and why working set hides it, and how to add a new skill, agent or rule."
tags: [claude-code, skills, agents, rules]
sources:
  - resource: .claude/skills/add-careers-page/SKILL.md
  - resource: .claude/skills/add-dashboard-page/SKILL.md
  - resource: .claude/skills/add-endpoint/SKILL.md
  - resource: .claude/skills/add-marketing-page/SKILL.md
  - resource: .claude/skills/add-message/SKILL.md
  - resource: .claude/skills/add-migration/SKILL.md
  - resource: .claude/skills/add-realtime-event/SKILL.md
  - resource: .claude/skills/commit/SKILL.md
  - resource: .claude/skills/merge-back/SKILL.md
  - resource: .claude/skills/merge-pull-request/SKILL.md
  - resource: .claude/skills/preview/SKILL.md
  - resource: .claude/skills/pull-request/SKILL.md
  - resource: .claude/skills/sync-docs/SKILL.md
  - resource: .claude/skills/task-review/SKILL.md
  - resource: .claude/skills/task-review/visual-probe.js
  - resource: .claude/skills/verify/SKILL.md
  - resource: apps/dashboard/package.json
  - resource: apps/dashboard/scripts/prebundle.mjs
  - resource: apps/dashboard/vite.config.ts
  - resource: Directory.Build.props
  - resource: .claude/agents/design-review.md
  - resource: .claude/agents/doc-auditor.md
  - resource: .claude/agents/verification-guardrails.md
  - resource: .claude/agents/licence-check.md
  - resource: .claude/agents/migration-safety.md
  - resource: .claude/agents/review-finder.md
  - resource: .claude/agents/tenant-isolation-guard.md
  - resource: .claude/agents/test-builder.md
  - resource: .claude/rules/astro-public.md
  - resource: .claude/rules/cli-features-over-custom-layers.md
  - resource: .claude/rules/docs-stay-in-sync.md
  - resource: .claude/rules/dotnet-api.md
  - resource: .claude/rules/integration-conflicts.md
  - resource: .claude/rules/no-personal-info.md
  - resource: .claude/rules/skill-authoring.md
  - resource: .claude/rules/svelte-dashboard.md
  - resource: .claude/rules/unattended-runs.md
  - resource: .claude/rules/utc-timestamps.md
  - resource: .claude/rules/writing-style.md
---
# Claude Code tooling

What each skill, agent and rule in [`.claude/`](../../.claude/) is for, and when to reach for it. Agents working on
the board use these so every task follows the same paths; people can run any skill too (`/add-endpoint ...`). The
files are the source of truth. If this page and a file disagree, fix this page (`/sync-docs` checks it). The page
also records tooling experiments that did not ship, so nobody repeats them blind.

## Skills

A skill is a procedure, run with `/<name>` or loaded by Claude Code when the task matches its description.

| Skill | Use it when | What it does |
|---|---|---|
| [`add-endpoint`](../../.claude/skills/add-endpoint/SKILL.md) | adding or changing an API route or a request/response shape | DTO records in `Contracts/`, a `Map*Endpoints` area with `TypedResults`, authorization and validation, an Aspire integration test, `pnpm generate:api` |
| [`add-message`](../../.claude/skills/add-message/SKILL.md) | adding background work, a domain event or a scheduled job | contract in `ScreenHire.Application`, worker handler, publishing through the EF Core outbox, idempotency keys, `ITenantMessage` and `ITenantEvent`, scheduled or cron delivery, an Aspire integration test |
| [`add-realtime-event`](../../.claude/skills/add-realtime-event/SKILL.md) | a change should appear in open dashboards without a reload | an `ITenantEvent` record, its Zod schema and the C#/Zod contract file, the dashboard `load` dependency it invalidates, an optional record topic, integration tests with the SignalR client |
| [`add-migration`](../../.claude/skills/add-migration/SKILL.md) | an entity or EF configuration changes | naming, generating and reviewing the SQL, the tenant-table checklist (`OrganizationId`, query filter, RLS, grants), the never-edit-merged and merge-conflict rules |
| [`add-dashboard-page`](../../.claude/skills/add-dashboard-page/SKILL.md) | adding a recruiter-dashboard screen | design first from the design system (template, panels, sizes), SvelteKit 3 route with runes, `#lib` imports, `$app/state`, shadcn-svelte, the generated client, loading/empty/error states, Vitest and Playwright tests, `design-review` and a look at three widths |
| [`add-careers-page`](../../.claude/skills/add-careers-page/SKILL.md) | adding a candidate-facing page | Astro SSR route, `CareersLayout`, server-side API data, 404/410, tenant-host links, `JobPosting` JSON-LD, no client JS or inline styles, notice before collection |
| [`add-marketing-page`](../../.claude/skills/add-marketing-page/SKILL.md) | adding a marketing, pricing, legal or content page | static Astro page through `BaseLayout` and `Seo.astro`, JSON-LD, sitemap and `llms.txt`, no client JS |
| [`commit`](../../.claude/skills/commit/SKILL.md) | saving work mid-task | a local conventional commit; no push, no gate |
| [`merge-back`](../../.claude/skills/merge-back/SKILL.md) | the owner lands a small tooling, config or docs change from the root checkout | commit, rebase onto `origin/main`, path-scoped checks (or `full` for `pnpm verify`), push to `main` with no PR; only when the owner asks, and it refuses inside a task worktree |
| [`preview`](../../.claude/skills/preview/SKILL.md) | looking at the app, or checking a change in it (`/run` uses it) | starts the stack through the Aspire CLI (`dotnet aspire start --isolated` in a worktree), waits with `aspire wait` until the demo data is in, reports URLs and logins from `aspire describe`, signs in as Avery Quinn in the task's browser pane, rebuilds one service after a code change; `fresh` for a clean demo (the default in a worktree, so it leaves no volumes; `keep` persists them), `api` for the backend only, `stop` for `aspire stop`; Kangentic's Stop script ends a parked task's stack |
| [`sync-docs`](../../.claude/skills/sync-docs/SKILL.md) | a change touches commands, AppHost resources, projects, `.claude/` or `docs/` | owns the source-to-doc anchor table, maps a changed file to its docs with `node scripts/docs-bundle.mjs sources`, runs `doc-auditor`, fixes drifted docs and prose |
| [`verify`](../../.claude/skills/verify/SKILL.md) | before every commit, before calling implementation done, and inside Code Review and Testing | the local checks: `pnpm verify:fast` and the tests the branch added or changed, by class or file; `/verify full` runs `pnpm verify` for the owner |
| [`task-review`](../../.claude/skills/task-review/SKILL.md) | the Code Review column runs it | independent review with the agents below; for a dashboard change it starts `/preview`, screenshots `/style-guide` and the changed pages at 1440, 768 and 390px, measures them with [`visual-probe.js`](../../.claude/skills/task-review/visual-probe.js) and hands both to `design-review`, then stops the stack; fixes findings and proves the fixes with `/verify` and the covering test classes by name; `Verdict: Ready` or `Verdict: Blocked` |
| [`pull-request`](../../.claude/skills/pull-request/SKILL.md) | the Testing column runs it | commit, merge `main`, `/sync-docs`, `/verify`, one push, open and link the PR, watch CI's `Gate` with up to three fix rounds (one push each), report the Lighthouse result |
| [`merge-pull-request`](../../.claude/skills/merge-pull-request/SKILL.md) | the Merge column runs it | require `Gate` green on the PR's head and the PR mergeable, squash-merge with `--match-head-commit` (CI again only after a conflict), confirm, realign |

The last four are the board pipeline ([board-pipeline.md](board-pipeline.md)). Change them only in a task about the
pipeline.

## Agents

An agent is a subagent with its own instructions and tools, started with the Agent tool. Reviewers are read-only and
report findings as `file:line`, severity, evidence and fix; the caller verifies and applies them.

| Agent | Access | Started by | What it checks or does |
|---|---|---|---|
| [`review-finder`](../../.claude/agents/review-finder.md) | read-only | `/task-review` (one per dimension) | one review dimension: correctness, non-negotiables and security, or conventions |
| [`tenant-isolation-guard`](../../.claude/agents/tenant-isolation-guard.md) | read-only | `/task-review`, `/add-migration` | `OrganizationId` on tenant rows, query scope, named query filters, RLS, cross-tenant paths |
| [`verification-guardrails`](../../.claude/agents/verification-guardrails.md) | read-only | `/task-review` | no scores (the candidate rating only under ADR-0013), no rejection from verification, no emotion or biometric inference, notice before collection, telemetry aggregates only, no scraping |
| [`licence-check`](../../.claude/agents/licence-check.md) | read-only | `/task-review`, any skill that adds a package | every new NuGet or npm package against ADR-0008 |
| [`migration-safety`](../../.claude/agents/migration-safety.md) | read-only | `/add-migration`, `/task-review` | generated SQL, types, existing data, destructive operations, snapshot drift, RLS for tenant tables |
| [`doc-auditor`](../../.claude/agents/doc-auditor.md) | read-only | `/sync-docs` | lists in docs against the sources that define them |
| [`design-review`](../../.claude/agents/design-review.md) | read-only | `/task-review` (UI changes), `/add-dashboard-page` | screens against the review checklist in `docs/design/design-system.md`, the one source of truth for UI; reads the screenshots and probe measurements `/task-review` passes it for the checks code cannot show |
| [`test-builder`](../../.claude/agents/test-builder.md) | writes tests | the three page skills, `/task-review` | picks the tier, writes and red-greens tests; audit-only mode returns a gap table |

## Rules

A rule is standing guidance in [`.claude/rules/`](../../.claude/rules/). A rule with a `paths:` list loads only
when Claude Code reads a matching file; the others always load. CLAUDE.md keeps the one-line version of each
convention; the rule holds the detail.

| Rule | Loads for | What it says |
|---|---|---|
| [`unattended-runs`](../../.claude/rules/unattended-runs.md) | always | never wait for a person, stay local, start from what is merged, finish in a state the gate accepts |
| [`integration-conflicts`](../../.claude/rules/integration-conflicts.md) | always | merge `main` (not rebase); take `main` and regenerate the lockfile, the API client and migrations |
| [`writing-style`](../../.claude/rules/writing-style.md) | always | prose without model tells: no em-dashes or curly quotes, plain words, short sentences |
| [`no-personal-info`](../../.claude/rules/no-personal-info.md) | always | no personal paths, emails, secrets or real applicant data in committed files |
| [`cli-features-over-custom-layers`](../../.claude/rules/cli-features-over-custom-layers.md) | always | use Aspire, ASP.NET Core, EF Core, Orval and Claude Code features before writing a layer; no model-name lists |
| [`dotnet-api`](../../.claude/rules/dotnet-api.md) | `apps/api`, `apps/worker`, `apps/apphost`, `src`, `tests`, `Directory.Packages.props` | backend conventions: endpoints, contracts, errors, data, tenancy, background work, integrations |
| [`utc-timestamps`](../../.claude/rules/utc-timestamps.md) | `src`, `apps/api`, `apps/worker` | `DateTimeOffset` in UTC from `TimeProvider`, `timestamp with time zone` |
| [`svelte-dashboard`](../../.claude/rules/svelte-dashboard.md) | `apps/dashboard`, `packages/ui` | Svelte 5 runes, SvelteKit 3 imports, generated client, shadcn-svelte, tokens, status marks, accessibility; points to the design system for how screens look |
| [`astro-public`](../../.claude/rules/astro-public.md) | `apps/careers`, `apps/marketing` | about 0 KB JS (one upload island), forms that work without JS, strict CSP, SEO and JSON-LD, domains as configuration, notice before collection |
| [`docs-stay-in-sync`](../../.claude/rules/docs-stay-in-sync.md) | sources that docs enumerate | update the doc that lists an item when the item changes |
| [`skill-authoring`](../../.claude/rules/skill-authoring.md) | `.claude/skills`, `.claude/agents`, `.claude/rules` | grounded commands, no questions, read-only reviewers, no forked mutating skills |

## Commands that were not run

Every command in these files was run once when they were written (October 2026), except these:

| Command | Named in | Why not |
|---|---|---|
| `dotnet ef migrations remove`, `dotnet ef database update` | `add-migration`, `migration-safety` (as forbidden) | both connect to whatever Postgres answers on `localhost:5432`, which on a shared machine is not this worktree's |
| `dotnet aspire start` without `--isolated`, in the root checkout | `preview` | agents run it from a worktree, with `--isolated`; the root checkout belongs to the owner, who runs it there and whose own stack may be running |
| `gh run rerun <run-id> --failed` | `pull-request` | it starts CI jobs again; it runs the first time a pull request's CI fails in code its branch did not touch |
| `merge-pull-request` Step 2 (merge `origin/main` into a conflicting PR, `pnpm verify:fast`, push, one CI run) | `merge-pull-request` | it needs a PR that conflicts with `main`; the steps are `/pull-request`'s Steps 2 and 7, which were run |
| `git rebase origin/main` and its conflict steps (`git diff --name-only --diff-filter=U`, `git checkout --ours`, `git rebase --continue`), `git push origin HEAD:main` | `merge-back` | they move `main`, so they run only when the owner asks for a merge back |

The unit, Vitest and end-to-end commands (`dotnet test --project tests/ScreenHire.UnitTests --filter-class ...`,
`pnpm --filter @screenhire/careers test`, `dotnet test --project tests/ScreenHire.E2E --filter-class ...`) were run
once on 2026-10-20, after the test harness landed.

`/task-review`'s visual pass was run once on 2026-10-21 against the style guide change (`e1c3177`): `/preview`
and `/preview stop`, and `kangentic_browser_set_viewport`, `_screenshot`, `_eval` with `visual-probe.js`, `_scroll`
and `_close_pane` on `/style-guide` and a job page at the three widths, then `design-review` on the result. The
probe needs Kangentic's Agent Browser > Allow eval setting, which was on.

## pnpm's global virtual store was tried for worktree installs and not adopted

Every worktree runs `pnpm install --frozen-lockfile` as Kangentic's post-worktree script. On 2026-10-21 we measured
pnpm's [global virtual store](https://pnpm.io/global-virtual-store) (`enableGlobalVirtualStore: true` in
`pnpm-workspace.yaml`) and a bump to pnpm 10.34.7, the latest 10.x, as ways to make that faster. Neither shipped.
The setting needs pnpm 10.12.1 or later, and pnpm 11.23.0 renamed it to `virtualStoreType: global`.

Method: one worktree on Windows 11 with a warm store, 667 packages, every `node_modules` deleted with Node's
`fs.promises.rm` before each run, one uncounted warm-up per configuration. The install column is pnpm's "Done in".

| Configuration | Runs | Install | Delete `node_modules` |
|---|---|---|---|
| pnpm 10.5.0 (what `main` pins) | 6 | 22.0 to 26.4 s, median 23.9 s | 21 to 25 s |
| pnpm 10.34.7 | 6 | 26.0 to 34.8 s, median 27.7 s | 19 to 29 s |
| pnpm 10.34.7 with the global store, first install (fills `<store>\links`) | 1 | 24.0 s | 21 s |
| pnpm 10.34.7 with the global store, `links` already filled | 3 | 33.7 to 35.1 s, median 34.0 s | 0.3 s |

The first-install row is a single run, so it does not show that filling `links` beats reusing it. The filled row is
what every worktree after the first would see.

The board's own run of the same install for this worktree took 25.6 s on 10.5.0. Run back to back, 10.5.0 took 22.0
to 24.0 s and 10.34.7 took 26.0 to 26.7 s.

Why neither shipped:

- The store made installs slower, not faster. pnpm still spent about 32 s in its linking step when all 667 entries
  in `links` already existed, with or without `verify-store-integrity`. Only the delete got faster.
- It is unsafe with Kangentic's cleanup. Git for Windows 2.51 (2.51.0.windows.2) `git worktree remove --force`
  follows a junction inside an untracked folder and deletes every file in the junction's target. To reproduce it, put
  a junction to a folder holding one file inside an untracked folder of a throwaway worktree, remove the worktree
  with `--force`, and the file is gone. Kangentic's Done cleanup removes the root
  `node_modules`, then runs that command on a worktree that still has `apps/*/node_modules` and
  `packages/*/node_modules`. With the store on, those hold junctions into the shared `<pnpm store path>\links`, so
  finishing one task would empty packages that every other worktree uses. Today the same junctions point inside the
  worktree being deleted, so the behaviour does no harm. Node's `fs.rm` and `fs.rmSync` (Node 24, and Kangentic's
  Electron 44), Git Bash `rm -rf`, and `Remove-Item -Recurse -Force` in PowerShell 5.1 and 7 all removed the
  junctions without following them.
- Turning the store on over an existing `node_modules` makes pnpm purge it, and without a TTY that aborts with
  `ERR_PNPM_ABORTED_REMOVE_MODULES_DIR_NO_TTY` unless `confirmModulesPurge: false` is set.
- pnpm 10.34.7 alone was about 4 s slower per install, so the bump buys nothing here.
- Builds, tests and the Vite dev servers were not checked with the store on, because it had already failed on speed
  and safety.

Worth another try once Kangentic deletes every `node_modules` in a worktree before it calls `git worktree remove`,
and a pnpm release reuses filled `links` entries without linking every package again.

## Adding to this

Follow [`skill-authoring`](../../.claude/rules/skill-authoring.md): run every command once, word not-yet-merged
dependencies by what exists, and add a row here in the same change.

## Where a new task's start-up time goes

Measured on 2026-10-31 from Kangentic's log for task #80, which started when its dependency reached Done. It took
43 s from that Done to Claude's `SessionStart`, and every step waited for the one before it:

| Step | Time |
|---|---|
| Done until Kangentic's git queue starts (cause not found) | 4.2 s |
| Resolve the base branch, `git fetch` | 2.2 s |
| `git worktree add`, long paths and sparse-checkout settings | 1.4 s |
| `pnpm install --frozen-lockfile` (the post-worktree script) | 27.0 s |
| Kangentic's spawn until "Trust ensured", then Claude's start until `SessionStart` | 7.8 s |

The install is the cost. It links about 55,000 files into 712 packages. A cold install in a throwaway worktree took
19.9 and 25.4 s, and a no-op install 2.6 s. The board's 27 s ran beside Kangentic deleting the finished task's
`node_modules`, which took 32 s. None of pnpm's flags helped: `--prefer-offline` took 20.3 to 20.7 s, hardlinks
(`package-import-method`) 19.3 to 19.8 s, `--ignore-scripts` 19.7 s and higher network concurrency 20.6 s. Resolution
and the network are not the cost.

What would help, filed as tasks:
- In Kangentic (#955 to #957 on its board): start the agent while the install runs and hold its first prompt until
  the install ends; keep a finished task's cleanup out of its dependents' installs; investigate a spare worktree that
  is already installed.
- Here (#81, done): `lighthouse` alone reached 73 packages, about 8,300 files, that nothing else needs. It now lives in
  `tools/lighthouse`, outside the workspace, and `pnpm verify:lighthouse` installs it on first use. A cold install in a
  throwaway worktree went from 23.0 s, 55,089 files and 714 packages to 18.0 s, 44,092 files and 640 packages (one
  run each, 2026-11-01). The dashboard's icons then moved from `@lucide/svelte` (three files per icon, 8,164 files) to
  `unplugin-icons` with `@iconify-json/lucide` (one JSON file): a clean install with `--ignore-scripts` took 16.5 s and
  44,093 files before, 12.3 s and 35,929 files after (same day, one run each).
- The same task set `SatelliteResourceLanguages` to `en` and hardlinks for copy-local files in `Directory.Build.props`. Seven
  bin folders held the same package DLLs (Playwright's `node.exe` alone is 94 MB) and about 1,100 localized resource
  files, 77 MB, that nothing reads; a worktree's bin was about 895 MB. The saving is a finder's estimate, not a timed
  build.

Things that look like levers but are not (all measured on 2026-11-01, one run each, so a gain under a second is inside the noise):
- Superforms installs every validator library it has an adapter for (`typebox`, `effect`, `class-validator` and
  more), although the dashboard uses only `zod4`. `sveltekit-superforms/adapters` imports them all statically, so
  leaving them out with pnpm's `ignoredOptionalDependencies` would break the dashboard build.
- Kangentic's `linkNodeModules` links only the root `node_modules`, from the root checkout's lockfile, so the
  `apps/*` and `packages/*` folders would still install. Keep it off.
- pnpm's global virtual store (`enableGlobalVirtualStore`, pnpm 10.34) on Windows: the first install took 28 s, and a
  second fresh worktree 28 s again with the shared links already there, against 18 s for the normal install.
- Skipping `verify:dotnet` and `verify:contract` in `/verify` when no .NET input changed: 19 of the last 20 merged
  pull requests changed one, so the rule would have skipped the build once.
- CI's analyzer and OpenAPI flags on a local test build (`-p:RunAnalyzers=false -p:EnforceCodeStyleInBuild=false
  -p:OpenApiGenerateDocuments=false`): a warm build of `tests/ScreenHire.UnitTests` took 7.1 to 7.7 s without them and
  4.6 to 5.1 s with them. The saving is 2.5 s, so `/verify` does not carry the flags.
- Root `pnpm.overrides` for `aria-query` (5.3.2), `yaml` (2.9.1) and `@opentelemetry/api` (`-`, an optional peer nothing
  imports): the checks, the builds and 199 dashboard tests passed, and it removed 1,138 of 38,220 files, about 3%, about
  0.5 s. Three permanent pins for that were reverted.
- Moving `orval` to `tools/orval` like Lighthouse: 2,320 files, about 1 s, and an install step inside `verify:contract`,
  which every column runs.

`.kangentic/config.json` also lists `.claude/settings.local.json` in `copyFiles`, but Kangentic skips `.claude/` entries,
so a new worktree does not get that file.

## What a preview stack commits

Kangentic's low-memory pause reads commit (private bytes), not working set, and working set hides most of it. Measured
on 2026-11-01 in a worktree with no data volumes, one run each, as the private bytes (`PrivateMemorySize64`) of every
process whose command line names the worktree and of their descendants. That is a wider count than the local demo
guide's 5 and 8 processes, so its process counts and working set do not match this table's. The containers run in
Docker's VM and are not counted. The worker's commit swings between samples (251 to 897 MB across them), so read the
totals to within a few hundred MB.

A real session is the number to read. "Browsed" means signed in as Avery Quinn with `/`, the Senior .NET Engineer job
and `/style-guide` loaded in a browser, sampled 20 s later. "Curl" means four requests to `/` and `/sign-in`, sampled 60 s
later, which never load the app's modules.

| Run | Load | Dashboard `vite dev` commit | Resident | Whole stack commit |
|---|---|---|---|---|
| Cold cache, `vite dev` as it was | Browsed | 3,185 MB | 1,306 MB | 6,456 MB (13 processes) |
| Cold cache, `prebundle.mjs` without `optimizeDeps.include` | Browsed | 3,308 MB | 1,390 MB | 6,271 MB |
| Cold cache, `prebundle.mjs` with `optimizeDeps.include` (now) | Browsed | 576 MB | 313 MB | 3,845 MB |
| Warm cache, dashboard restarted | Browsed | 573 MB | 308 MB | 3,805 MB |
| Cold cache, `vite dev` as it was | Curl | 1,201 MB | 352 MB | 4,035 MB |
| Warm cache, dashboard restarted | Curl | 349 MB | 182 MB | 3,198 MB |
| Cold cache, `vite optimize && vite dev` | Curl | 1,162 MB | 354 MB | 4,028 MB |
| Cold cache, `prebundle.mjs` without `optimizeDeps.include` | Curl | 344 MB | 184 MB | 2,956 MB |
| `/preview api`, backend only | None | not started | not started | 1,353 MB (10 processes; working set 2,005 MB) |

A cold cache costs the dashboard about 2.6 GB of commit that the process keeps for its whole life, the shape Kangentic's
#873 found in its own dev server. A worktree always starts cold. The cause is the in-process dependency pre-bundle, and
it happens twice. The log of a cold start says "Forced re-optimization of dependencies". Then Vite's dependency scan
reports "no dependencies found" in these routes, so the first page load discovers 15 packages and bundles them in the
dev server ("optimized dependencies changed. reloading"). Careers (1,064 MB) and marketing (788 MB) hold more commit
than their working set (346 and 286 MB) in the same way, and were not investigated.

`vite optimize` is the native way to pre-bundle in a process that exits, and it does not work here. It calls
`optimizeDeps` alone, but vite-plugin-svelte checks in its `buildStart` hook, which only a dev server runs, whether
Svelte's settings changed since the cache was written. With no `_svelte_metadata.json` it forces a second, in-process
optimization when the real server starts.

So `apps/dashboard/scripts/prebundle.mjs` starts a Vite server in middleware mode, which runs those hooks and opens no
port, waits until each dependency is written, and closes. The wait is on each dependency's `processing` promise, because
`createServer` returns once the SSR run is done and closing then discards a client run that is still going. The 15
packages are in `optimizeDeps.include` in `apps/dashboard/vite.config.ts`, so that run covers them. A package a page
starts importing joins that list: without it the page still works, the first load bundles it in-process, and the log
says "dependencies optimized". The dashboard's `dev` script runs the pre-bundle first. It takes about 5 s on a cold cache
and 2 s on a warm one. It always exits 0: if it fails, it warns and `vite dev` bundles in-process as before. The
`/preview` figures in [the local demo guide](../operations/local-demo.md#start-over) are working set; the commit above is
beside them.
