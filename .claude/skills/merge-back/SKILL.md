---
description: Owner's quick-push escape hatch. From the root checkout on main, commit the working tree, rebase onto origin/main, run the checks the changed paths need, and push straight to origin/main with no PR. Use only when the person asks to merge back, land or push changes ("merge back these changes", /merge-back). Never from a board task; task worktrees use /pull-request and /merge-pull-request. Not for a plain local commit (/commit).
allowed-tools: Read, Glob, Grep, Edit, Write, Agent, Bash(git:*), Bash(pnpm:*), Bash(dotnet:*)
argument-hint: [full] [commit message]
---

# Merge back

Lands a small change on `origin/main` without a PR. It skips the board's review and the full gate, so it is for
changes like a `kangentic.json` tweak, a rule or skill edit, or a docs fix, made in the root checkout. Product code
goes through a task and the board.

Run it only when the person asks, by `/merge-back` or in words ("merge back these changes"). Board agents never
push to `main` (`.claude/rules/unattended-runs.md`), and step 1 refuses inside a task worktree, where every board
task runs.

**Usage:**

- `/merge-back` writes the commit message from the diff.
- `/merge-back <message>` uses the message, adding a conventional prefix if it has none.
- `/merge-back full <message>` runs the full gate (`pnpm verify`) instead of the path-scoped checks.

**Arguments:** $ARGUMENTS

Every Bash call is one command: no `&&`, `||`, `|`, `;` or `$(...)`. Run git from the current directory.

## 1. Pre-flight

1. If the current directory is inside `.kangentic/worktrees/`, stop: a task branch reaches `main` through the board
   (`/pull-request`, then `/merge-pull-request`), which squashes it and realigns the worktree.
2. `git rev-parse --abbrev-ref HEAD` must print `main`. Otherwise report the branch and stop.
3. `git status --porcelain` and `git diff --stat`. Leave out secrets, `.env` files, real applicant data and machine
   paths (`.claude/rules/no-personal-info.md`), and say so.
4. `git fetch origin main`, then `git rev-list --count origin/main..HEAD` for local commits not yet pushed.
5. If the tree is clean and the count is 0, report "nothing to land" and stop.

Report the branch, the changed files and the unpushed commit count before going on.

## 2. Docs that list what changed

If a changed file is a source in the anchor table in `.claude/skills/sync-docs/SKILL.md` (a new skill, a root
script, an AppHost setting, ...), spawn the `doc-auditor` agent with those files and fix the gaps it reports with
Edit. Skip this when no anchor source changed.

## 3. Commit

Skip this step if the tree is clean.

1. Message: the format in `.claude/skills/commit/SKILL.md` §2 (conventional `type(scope): subject`, house style),
   with the attribution trailer your session instructions give. Drop a leading `full` from `$ARGUMENTS` first.
2. Write it with the Write tool to `.kangentic/COMMIT_MSG.tmp` (git-ignored).
3. `git add -A`, then `git restore --staged <path>` for each file set aside in step 1.
4. `git commit -F .kangentic/COMMIT_MSG.tmp`. If a hook fails, fix the cause and commit again. Never pass
   `--no-verify`.

## 4. Rebase onto origin/main

`git rebase origin/main`. The integration rule's "merge, not rebase" is for task branches. Here a merge commit on
`main` would only add noise to a linear history.

On a conflict (`git diff --name-only --diff-filter=U`):

- `pnpm-lock.yaml`, `packages/api-client/openapi.json`, `packages/api-client/src/generated/**`: take main's side
  with `git checkout --ours <file>` (in a rebase `--ours` is `origin/main`), regenerate as
  `.claude/rules/integration-conflicts.md` says, then `git add <file>`.
- Shared source files: keep both sides, as that rule says, then `git add <file>`.
- `git rebase --continue` until it finishes.
- A migration conflict, or one that needs a product decision: `git rebase --abort`, name the files, and stop
  with the ahead report below. Re-run `/merge-back` once it is sorted out.

**Ahead report.** Whenever this skill stops before the push, give `git rev-list --count origin/main..HEAD` and say
plainly that local `main` is ahead of `origin/main`. Until those commits land or are undone, the Merge column's
`git pull --ff-only` on this checkout (`/merge-pull-request` Step 5) fails on every merge.

## 5. Checks

List what will land: `git diff --name-only origin/main HEAD`. Run the checks the paths call for, in this order,
stopping at the first failure:

| Changed paths | Check |
|---|---|
| `src/**`, `apps/api/**`, `apps/worker/**`, `apps/apphost/**`, `tests/**`, `*.csproj`, `Directory.*.props`, `ScreenHire.slnx`, `global.json` | `pnpm verify:dotnet`, then `dotnet test --project tests/ScreenHire.UnitTests` |
| `apps/api/**`, `packages/api-client/**` | `pnpm verify:contract` (after `verify:dotnet`, which emits `openapi.json`) |
| `apps/dashboard/**`, `apps/careers/**`, `apps/marketing/**`, `packages/**`, `package.json`, `pnpm-lock.yaml` | `pnpm check`, then `pnpm -r --if-present test` |
| Anything else (`.claude/**`, `docs/**`, `*.md`, `kangentic.json`, ...) | none |

With `full`, run `pnpm verify` instead, with `run_in_background`, and wait for the notification.

On a failure, fix it, commit the fix (`fix: ...`), and run the failing check again. If it is not a quick fix, stop
with the error and the ahead report from step 4.

## 6. Push

`git push origin HEAD:main`. Never force.

If it is rejected because `origin/main` moved, run `git fetch origin main` and go back to step 4 once. If it is
rejected again, stop with the error and the ahead report.

## 7. Report

- `git log --oneline origin/main -n <count>` for the commits that landed.
- The checks that ran, or "none (tooling and docs only)".
- `git status -sb`, which must show `main...origin/main` with nothing ahead or behind.
