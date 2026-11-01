---
description: Run ScreenHire's local checks - by default the fast check (build with warnings as errors, API contract drift, type checks) plus the tests the branch added or changed, run by class or file; with "full", the whole suite (pnpm verify) for the owner and debugging. Use before every commit, before ending implementation, and inside /task-review, /pull-request and /merge-pull-request.
argument-hint: [full]
---

# Verify: local checks

The board's test gate is CI: `.github/workflows/ci.yml` runs every suite on GitHub's Linux runners for each pull
request, behind one check, `Gate` (ADR-0020, [`docs/process/testing.md`](../../../docs/process/testing.md#continuous-integration)).
Locally an agent proves only what it touched, cheaply, so a batch of tasks does not overload the machine.

**Usage:**
- `/verify` runs the fast check and the branch's added or changed tests. This is what every column runs.
- `/verify full` runs the whole suite locally (`pnpm verify`). It is for the owner and for debugging a failure CI
  cannot explain. No column runs it, and an agent runs it only when the owner asks.

## Prerequisites

- Dependencies match the lockfile: run `pnpm install --frozen-lockfile` if `package.json` or `pnpm-lock.yaml` changed
  since the worktree was created. If the frozen install fails because the branch changed dependencies, run
  `pnpm install` and commit the lockfile.
- Only when the test list below has an integration or end-to-end class: Docker is running (`docker info`). If it
  fails, run `pnpm docker:start`, which starts Docker Desktop outside this task and waits up to 3 minutes. Never start
  Docker Desktop yourself (the guard refuses it).
- Only for an end-to-end class: Playwright's Chromium (`pnpm e2e:install`, once per machine and after a
  `Microsoft.Playwright` bump). "Chromium is not installed" is setup, not a code failure.

## Default: the fast check and the branch's tests

1. Commit, or at least stage, your work. `verify:contract` compares the regenerated client with git, so correct but
   unstaged generated files read as drift.
2. `pnpm verify:fast`, in the foreground with the Bash timeout at 600000 ms. It runs, stopping at the first failure:
   1. `verify:dotnet`: `dotnet build ScreenHire.slnx -warnaserror` (compile, analyzers, NuGet audit). This also
      regenerates `packages/api-client/openapi.json`.
   2. `verify:contract`: regenerates the TypeScript client and fails if `packages/api-client` differs from what is
      committed.
   3. `pnpm check`: svelte-check (`--fail-on-warnings`), astro check, tsc.
3. List the branch's test files, committed and not:
   - `git diff --name-only --diff-filter=d origin/<base>...HEAD` (base: `git config kangentic.baseBranch`, else
     `main`; `--diff-filter=d` leaves out deleted files);
   - `git status --porcelain` for uncommitted and untracked files.
4. Run them, scoped, with one command per project or package:

   | Changed file | Command |
   |---|---|
   | `tests/ScreenHire.UnitTests/**/<Name>Tests.cs` | `dotnet test --project tests/ScreenHire.UnitTests --filter-class "*<Name>Tests"`, one `--filter-class` per class |
   | `tests/ScreenHire.IntegrationTests/**/<Name>Tests.cs` | the same with `--project tests/ScreenHire.IntegrationTests` (Docker) |
   | `tests/ScreenHire.E2E/**/<Name>Tests.cs` | `pnpm -r build` once, then the same with `--project tests/ScreenHire.E2E` (Docker, Chromium) |
   | `<package>/**/<name>.test.ts` | `pnpm --filter <package name> test <path inside the package>` (the package's `test` script keeps its `--fsModuleCachePath`) |
   | anything else under `tests/` or `test-support/` (fixtures, helpers) | nothing; CI runs the suites that use them |

   The `.cs` rows assume the class is named after its file. When a file holds other classes, or a differently named
   one, take the names from its `class ...Tests` lines. A `*.test.ts` in a package with no `test` script runs
   nowhere locally; CI covers it.
   You may also run one existing class or file that directly covers code you changed, in the same scoped form.
   Never run a whole test project, `pnpm -r test`, `verify:test` or `pnpm verify`: CI runs those.
5. A scoped Aspire run boots its own AppHost, about 40 s. Start it with `run_in_background` if it may pass the
   10-minute Bash limit, and wait for the notification.

## Full: `/verify full`

Only when the owner asks. `pnpm verify` runs `pnpm verify:fast`, then `pnpm -r build` (the marketing build also fails
on more than 15 KB of JS per page or a broken internal link), `pnpm -r --if-present test` (every Vitest suite) and
`verify:test` (`dotnet test --solution ScreenHire.slnx`: unit, Aspire integration and Playwright end-to-end in
parallel). It needs Docker and Chromium. With warm caches it took 3 min 26 s on the owner's machine on 2026-10-21;
with several worktrees running it takes much longer. Start it with `run_in_background` and wait for the notification.

## Marketing performance budgets

`pnpm verify:lighthouse` runs Lighthouse on every marketing page (mobile LCP under 1.8 s, CLS under 0.05) against the
marketing build. CI's `Lighthouse (report-only)` job runs it for a pull request that changes `apps/marketing`,
`packages/ui` or `pnpm-lock.yaml`, and `/pull-request` reports its result. It is report-only until it is calibrated on
the runners. Locally, run it with paths while you work on a page (`pnpm --filter @screenhire/marketing lighthouse
/pricing/`, with `MSYS_NO_PATHCONV=1` in Git Bash). Only one local run measures at a time; a second one waits.

## Never stop a running check

Never stop a running `pnpm verify:fast`, test run or `pnpm verify`, with TaskStop or any other way. On Windows TaskStop
ends the Bash tool's shell and leaves the command's whole tree running, out of reach of this session and holding the
worktree folder. Make further edits, let the run finish, then run it again. Each step has its own time limit.

## Reading failures

- **Build or analyzer error:** fix the code. Never add a `NoWarn` or `#pragma warning disable` to get through unless
  the warning is a known false positive, and then say why in a comment.
- **Contract drift:** an endpoint or DTO changed without `pnpm generate:api`. Commit the regenerated files.
- **Test failure:** decide whether the code or the test is wrong, from the brief and the docs, and fix that one.
  Never delete, skip or loosen a test only to pass. A failed end-to-end test leaves a screenshot in
  `tests/ScreenHire.E2E/bin/Debug/net10.0/TestResults/screenshots/`; look at it. An axe failure lists each violated
  rule with the element's selector: fix the markup or the token, not the test.
- **Flaky test** (passes on a rerun with no change): fix the cause (an unawaited task, a fixed delay, a shared port,
  ordering between tests). Never add retries or sleeps.
- **Infrastructure** (Docker still down after `pnpm docker:start`, a port in use, a container that will not start, a
  step that ended on its own time limit, the fixture's "was not healthy within 3 minutes"): report it plainly. These
  are not code failures and must not be "fixed" by stopping processes.

## Report

List each step with pass or fail, the test classes and files run with their counts, and for each failure the first
useful error lines. End with `Verify: passed` or `Verify: failed` as the last line.
