---
type: Decision
title: "ADR-0020: The test gate runs on GitHub Actions, and agents run only fast checks and their own tests locally"
description: "The board's test gate moves from the local pnpm verify to CI's Gate check on a public AGPL-3.0 repository closed to contributions: verify:fast plus a branch's own tests by class or file locally, one push per Testing run, Merge on a green Gate without a re-run when main moved, a red main fixed by the next task, Lighthouse report-only, and why private hosted runners, a rented self-hosted runner and a machine-wide stack limit were rejected."
tags: [adr, ci, testing, process, kangentic]
---
# ADR-0020: The test gate runs on GitHub Actions, and agents run only fast checks and their own tests locally

- **Status:** Accepted (2026-10-31)
- **Deciders:** Owner, through task #80 on the board
- **Amends:** ADR-0003's Lighthouse enforcement (report-only in CI until it is calibrated on the runners)
- **Detail:** [`docs/process/testing.md`](../process/testing.md#continuous-integration),
  [`docs/process/board-pipeline.md`](../process/board-pipeline.md)

## Context

Every board task ran the full local gate, `pnpm verify`, in several columns: Executing before it finished, Testing
before it pushed, Merge again whenever `main` had moved. That came to about 185 runs a week on the one Windows machine
that also runs Kangentic and every task's agent. Each run boots two Aspire AppHosts with their containers. When a batch
ran, several gates overlapped, and most of the flake history in testing.md from 2026-10-30 and 2026-10-31 names load
from sibling worktrees as the suspect: stalled workers, memory stops, timeouts on first loads.

Task #79 added `.github/workflows/ci.yml`. It runs the same suites on GitHub's hosted Linux runners, sharded, behind
one check, `Gate`. It made CI a second opinion. This decision makes it the gate.

## Decision

1. **The repository is public under AGPL-3.0-only and closed to contributions** (`LICENSE`, `CONTRIBUTING.md`). A
   public repository gets hosted runner minutes for free, and its logs and artifacts are public, so CI never reads a
   secret and never runs on a self-hosted runner.
2. **The test gate is CI's `Gate` check.** `Gate` passes only when the `checks` job and every shard of `vitest`,
   `integration` and `e2e` succeeded.
3. **Local runs are small.** An agent runs `pnpm verify:fast` (`verify:dotnet`, `verify:contract`, `pnpm check`) and
   the tests its branch added or changed, scoped to those test classes and files. It never runs a whole test project
   or `pnpm verify` unless the owner asks.
4. **A branch's tests come from git.** `git diff --name-only origin/main...HEAD` lists them. .NET test classes run
   with `--filter-class`, Vitest files by path.
5. **`/verify` defaults to the fast check.** Claude Code's Bash tool tells agents to run `/verify` right before every
   `git commit`, so `/verify` has to be cheap. It runs `pnpm verify:fast` and the branch's added or changed tests.
   `/verify full` keeps the full local gate for the owner and for debugging. No column runs it.
6. **One push.** In Testing, every step that makes a commit happens before the first push: integrating `main`, the
   docs sync, and fixes from the local checks. All the fixes from one failed CI run go out in a single push, for at
   most 3 rounds. The fix waits for the whole run to finish, because a push cancels the run in flight.
7. **Merge does not re-run CI when `main` has moved.** A pull request run tests `refs/pull/<n>/merge`, the head merged
   with `main` as it was when the run started.
   - Merge requires `Gate` to be SUCCESS on the current head SHA, and the PR to be mergeable. Then it squash-merges
     with `--match-head-commit`.
   - Only a conflict sends it back: merge `origin/main`, resolve as `integration-conflicts.md` says, run
     `pnpm verify:fast`, push, wait for one CI run, then merge.
   - CI on every push to `main` is the backstop for anything two green branches break together.
8. **A red `main` is fixed by the next task whose CI fails on it.** If the failure is in code the branch did not
   touch and the latest `main` run fails the same test, the branch fixes it and records it under Decisions in its PR
   body. Merge never blocks on a red `main`. A failure that `main` does not share is rerun once with
   `gh run rerun --failed`, without a push, to tell a flake from a defect.
9. **Never `gh pr merge --admin`, and no Bash timeout above 600000 ms.** Kangentic's merge skill, the model for these
   skills, uses 2400000 ms; that is over the Bash tool's ceiling.
10. **Lighthouse is report-only in CI for now.** `/pull-request` reports its result in the PR body and does not block
    on a budget miss. A failed `lighthouse` job on its own is not a CI failure for either skill. Until a later task
    calibrates the budgets on runners, an LCP or CLS regression on the marketing site is reported and not enforced.
    This amends ADR-0003, which enforced the budgets once per marketing change in the Testing column.
11. **This task could edit the process files** (`kangentic.json`, `.claude/settings.json` and the `verify`,
    `task-review`, `pull-request` and `merge-pull-request` skills), which `unattended-runs.md` normally forbids,
    because the change is about them.

## Rejected options

- **Hosted runners on a private repository.** At the current run rate they would cost roughly $85 to $180 a month,
  for no gain over a public repository: nothing in the repository is secret.
- **A self-hosted runner on a rented server.** It costs money and upkeep, and on a public repository it would run
  pull request code on a machine we keep. GitHub advises against that, and `CiWorkflowTests` refuses it.
- **A machine-wide limit on test stacks.** It queues the load and does not remove it. A batch of tasks would wait in
  line for the one machine, and the machine would still run every suite of every task.

## Consequences

- The dev machine runs about 50 s of fast checks and a few test classes per task step, instead of the whole suite
  several times per task.
- CI runs on Linux only. A defect that shows only on Windows can reach `main`; the owner's `/verify full` or
  `/merge-back` is where it shows.
- Two branches that are green on their own can break `main` together. The run on every push to `main` shows it, and
  the next task whose CI fails on it fixes it.
- A pull request waits about 5 to 9 minutes for CI, most of it queueing for runners.
- As of 2026-10-31 branch protection on `main` does not require `Gate`, so GitHub would merge a red PR. The skills
  check `Gate` themselves. Making it a required check, without "Require branches to be up to date", is an owner
  action. With the up-to-date rule on, every merge after `main` moved would need another CI run, which decision 7
  avoids.
