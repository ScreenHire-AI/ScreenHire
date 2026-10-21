---
description: Testing column skill. Commit, integrate origin/main, run the full local gate (/verify) and fix failures (up to 3 rounds), then push and open a PR linked to the task. Never merges. Ends on "PR: ready to merge" or "PR: blocked".
argument-hint: [commit message]
---

# Pull request (Testing column)

ScreenHire has no hosted CI yet. The gate runs here, locally, and a PR is opened only once it passes. The PR records
the change for a human reviewer. This skill never merges; `/merge-pull-request` does that in the Merge column.

**User-provided commit message (if any):** $ARGUMENTS

## Pre-flight

1. Worktree check: the current directory must be inside `.kangentic/worktrees/`. If not, report that this skill only
   runs from a task worktree and stop (no outcome line).
2. `git rev-parse --abbrev-ref HEAD`. If detached, report and stop.
3. Base branch: `git config kangentic.baseBranch`, else `main`.
4. `gh auth status`. If it fails, report and end with `PR: blocked`.
5. `git status --porcelain`. Changes you did not write are expected: the Code Review column works in this same
   worktree. They belong to this task, so commit them with yours and never ask whether to include them.

## Step 1: Commit

If the tree is dirty, `git add -A` and commit with a conventional message (`$ARGUMENTS` if given, otherwise one
derived from the diff).

## Step 2: Integrate main

`git fetch origin <base>`. If `git merge-base --is-ancestor origin/<base> HEAD` fails, `main` has moved: run
`git merge origin/<base> --no-edit` and resolve any conflicts by `.claude/rules/integration-conflicts.md`. If a
conflict needs a decision you cannot make, abort the merge, explain it, and end with `PR: blocked`.

## Step 3: Gate

Run the full gate as `/verify` describes (`pnpm verify`, in the background, waiting for the notification).

Then the marketing budgets, once per task and only here. If
`git diff --quiet origin/<base>...HEAD -- apps/marketing packages/ui pnpm-lock.yaml` exits non-zero, run
`pnpm verify:lighthouse` in the background against the build the gate just made. It may first wait for another
task's run (`/verify`, "Marketing performance budgets"). A budget failure is a gate failure.

If either fails, fix and run both again, up to **three rounds**:
- Diagnose from the output. Fix the code or the test, whichever the brief says is wrong.
- Commit each round: `fix(test): ...`.
- For a flaky test, fix the cause, never add a retry.

After three failed rounds, go to Step 6b.

## Step 4: Push

1. Public branch name `<type>/<slug>`:
   - `<type>`: the conventional prefix of the main change (`feat`, `fix`, `chore`, ...).
   - `<slug>`: the task title as 2 to 5 kebab-case words, using only `[a-z0-9-]`.
   - If this task already has a PR (`kangentic_get_current_task` shows `pr_number`), reuse its head branch.
2. Never rename the local branch. Kangentic keys the session to it.
3. `git push origin HEAD:<type>/<slug> --force-with-lease`.

## Step 5: Open the PR and link it

1. Write the body to `.kangentic/PR_BODY.tmp` with the Write tool. The folder is ignored by git. Sections:
   - `## What`: the change and the components it touches.
   - `## Why`: the task (`#<number> <title>`) and the docs or ADRs it implements.
   - `## How`: the approach and the trade-offs worth a reviewer's attention.
   - `## Decisions`: choices made without a person, with the alternative each time; `None` if there were none.
   - `## Owner actions`: anything only a person can do (accounts, keys, legal); `None` if there is nothing.
   - `## Tests`: the gate result with per-suite counts, the Lighthouse result ("N pages within budget", or "not run:
     no marketing change"), and the tests this task added.
   - The footer `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.
2. `gh pr create --base <base> --head <type>/<slug> --title "<conventional title>" --body-file .kangentic/PR_BODY.tmp`.
   If the PR already exists, `gh pr edit` it with the new body instead.
3. Link it: `kangentic_get_current_task` (this directory and branch), then `kangentic_update_task` with `prUrl` and
   `prNumber`. The PR head differs from the local branch by design, so this link is the only connection. Retry if it
   fails.

## Step 6: Report

**6a. Success.** Give the PR URL, the commits, the gate summary, and the tests added. Then, alone, as the last line:
`PR: ready to merge`

**6b. Blocked.** Push what you have, open or update the PR if possible, and report:
- each still-failing check, with what each round tried and the root cause as far as you got;
- concrete next steps.

Then, alone, as the last line: `PR: blocked`

## Outcome line

The Testing column moves the task to Merge only when the last line reads `PR: ready to merge`. Print it only when the
full gate (and Lighthouse, when Step 3 ran it) passed on the commit you pushed and the PR exists and is linked.
`PR: blocked` keeps the task in Testing and notifies the owner. Write nothing after either line.
