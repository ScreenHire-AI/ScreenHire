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
| [`preview`](../../.claude/skills/preview/SKILL.md) | looking at the app, or checking a change in it (`/run` uses it) | starts the stack through the Aspire CLI (`dotnet aspire start --isolated` in a worktree), waits with `aspire wait` until the demo data is in, reports URLs and logins from `aspire describe`, signs in as Avery Quinn in the task's browser pane, rebuilds one service after a code change; `fresh` for a clean demo, `stop` for `aspire stop` |
| [`sync-docs`](../../.claude/skills/sync-docs/SKILL.md) | a change touches commands, AppHost resources, projects, `.claude/` or `docs/` | owns the source-to-doc anchor table, runs `doc-auditor`, fixes drifted docs and prose |
| [`verify`](../../.claude/skills/verify/SKILL.md) | before calling implementation done, and inside the Testing and Merge columns | the local gate, `pnpm verify` |
| [`task-review`](../../.claude/skills/task-review/SKILL.md) | the Code Review column runs it | independent review with the agents below; for a dashboard change it starts `/preview`, screenshots `/style-guide` and the changed pages at 1440, 768 and 390px, measures them with [`visual-probe.js`](../../.claude/skills/task-review/visual-probe.js) and hands both to `design-review`, then stops the stack; fixes findings; `Verdict: Ready` or `Verdict: Blocked` |
| [`pull-request`](../../.claude/skills/pull-request/SKILL.md) | the Testing column runs it | commit, integrate `main`, gate (plus Lighthouse for a marketing change), push, open and link the PR |
| [`merge-pull-request`](../../.claude/skills/merge-pull-request/SKILL.md) | the Merge column runs it | re-gate if `main` moved, squash-merge, confirm, realign |

The last four are the board pipeline ([board-pipeline.md](board-pipeline.md)). Change them only in a task about the
pipeline.

## Agents

An agent is a subagent with its own instructions and tools, started with the Agent tool. Reviewers are read-only and
report findings as `file:line`, severity, evidence and fix; the caller verifies and applies them.

| Agent | Access | Started by | What it checks or does |
|---|---|---|---|
| [`review-finder`](../../.claude/agents/review-finder.md) | read-only | `/task-review` (one per dimension) | one review dimension: correctness, non-negotiables and security, or conventions |
| [`tenant-isolation-guard`](../../.claude/agents/tenant-isolation-guard.md) | read-only | `/task-review`, `/add-migration` | `OrganizationId` on tenant rows, query scope, named query filters, RLS, cross-tenant paths |
| [`integrity-guardrails`](../../.claude/agents/integrity-guardrails.md) | read-only | `/task-review` | no scores, no rejection from integrity, no emotion or biometric inference, notice before collection, telemetry aggregates only, no scraping |
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
| [`dotnet-api`](../../.claude/rules/dotnet-api.md) | `apps/api`, `apps/worker`, `apps/apphost`, `src`, `tests` | backend conventions: endpoints, contracts, errors, data, tenancy, background work, integrations |
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
