---
description: Run ScreenHire's local quality gate (build with warnings as errors, API contract drift, type checks, frontend builds, unit/Vitest/Aspire integration/Playwright tests) and report the result. Use before ending implementation, and inside /pull-request and /merge-pull-request.
argument-hint: [quick]
---

# Verify: the local gate

ScreenHire has no hosted CI yet. This gate is the only thing standing between a branch and `main`, so run all of it
and read the results rather than skimming the exit code.

**Usage:** `/verify` runs the full gate. `/verify quick` skips `pnpm -r build` and the .NET test run, for a check
mid-task (never as the final gate).

## Prerequisites

- Docker is running (`docker info`). The integration and end-to-end tests boot the Aspire AppHost with fresh
  containers and random ports, so they do not collide with a running dev stack or a sibling task.
- Playwright's Chromium is installed for this machine: `pnpm e2e:install` (once per machine, and again after a
  `Microsoft.Playwright` bump). If the end-to-end tests fail with "Chromium is not installed", run it; that is setup,
  not a code failure.
- Dependencies match the lockfile: run `pnpm install --frozen-lockfile` if `package.json` or `pnpm-lock.yaml` changed
  since the worktree was created. If the frozen install fails because the branch changed dependencies, run
  `pnpm install` and commit the lockfile.

## Run

Commit (or at least stage) your work first. `verify:contract` compares the regenerated client with git, so correct
but unstaged generated files read as drift.

Full gate: `pnpm verify`. It runs, in order and stopping at the first failure:

1. `dotnet build ScreenHire.slnx -warnaserror`: compile, analyzers and NuGet audit. This also regenerates
   `packages/api-client/openapi.json`.
2. `verify:contract`: regenerates the TypeScript client and fails if `packages/api-client` differs from what is
   committed. Fix by committing the regenerated files, after checking the API change was intended.
3. `pnpm check`: svelte-check (`--fail-on-warnings`), astro check, tsc.
4. `pnpm -r build`: every frontend production build. The marketing build also fails on more than 15 KB of JS per
   page or a broken internal link (`apps/marketing/scripts/check-dist.mjs`).
5. `pnpm -r --if-present test`: Vitest (`apps/dashboard`, `apps/careers`, `apps/marketing`, `packages/api-client`).
6. `verify:lighthouse`: headless Lighthouse on every marketing page in the sitemap (mobile LCP under 1.8 s, CLS
   under 0.05), using Playwright's Chromium from `pnpm e2e:install`. About 90 s.
7. `dotnet test --solution ScreenHire.slnx`: `ScreenHire.UnitTests`, `ScreenHire.IntegrationTests` (Aspire, frontends
   off) and `ScreenHire.E2E` (Playwright and axe against the step 4 builds), run in parallel.

With warm caches the full gate takes about 3 minutes on the owner's machine (2 min 54 s to 2 min 58 s in October
2026), about 90 s of it Lighthouse. Most of the rest is booting the two AppHosts. The first run after a dependency or
container-image change is slower (restores and image pulls). A warm run that takes several minutes means something is waiting on a timeout: read the output.

Quick: `pnpm verify:dotnet`, then `pnpm verify:contract`, then `pnpm check`, then `pnpm -r --if-present test`.

The full gate can run past the Bash tool's 10-minute ceiling. Start it with `run_in_background` and wait for the
completion notification; do not poll, and do not stop it by name or port if it hangs (use TaskStop).

## Reading failures

- **Build or analyzer error:** fix the code. Never add a `NoWarn` or `#pragma warning disable` to get through the gate
  unless the warning is a known false positive, and then say why in a comment.
- **Contract drift:** an endpoint or DTO changed without `pnpm generate:api`. Commit the regenerated files.
- **Test failure:** decide whether the code or the test is wrong, from the brief and the docs, and fix that one.
  Never delete, skip or loosen a test only to pass the gate. A failed end-to-end test leaves a screenshot in
  `tests/ScreenHire.E2E/bin/Debug/net10.0/TestResults/screenshots/` (the path is in the test output); look at it.
  An axe failure lists each violated rule with the element's selector: fix the markup or the token, not the test.
- **Flaky test** (passes on a rerun with no change): fix the cause (an unawaited task, a fixed delay, a shared port,
  ordering between tests). Never add retries or sleeps.
- **Infrastructure** (Docker not running, a port in use, a container that will not start): report it plainly. These
  are not code failures and must not be "fixed" by stopping processes.

## Report

List each step with pass or fail, test counts per suite, and for each failure the first useful error lines. End with
`Gate: passed` or `Gate: failed` as the last line.
