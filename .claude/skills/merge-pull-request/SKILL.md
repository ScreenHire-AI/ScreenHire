---
description: Merge column skill. Re-integrate origin/main and re-run the gate if main moved, squash-merge the task's PR, confirm it landed on origin/main, realign the worktree, fast-forward the root checkout, and end on "PR: merged" (or "PR: blocked").
---

# Merge pull request (Merge column)

By the time a task reaches Merge, the decision to ship is made: Testing ran the full local gate and opened the PR.
This skill lands it on `origin/main`. Kangentic cuts every new worktree from `origin/main`, and a task's dependents
start when it reaches Done, so `PR: merged` must mean the code really is on `origin/main`.

Fix small problems here and merge. Do not hand them back.

## Pre-flight

1. The current directory must be inside `.kangentic/worktrees/<slug>/`. If not, report and stop (no outcome line).
2. Project root: the path above `.kangentic/worktrees/<slug>`.
3. Base branch: `git config kangentic.baseBranch`, else `main`.
4. `gh auth status`. If it fails, report and end with `PR: blocked`.
5. Resolve the PR: `kangentic_get_current_task`, then take its `pr_number` and task id. If there is no PR number,
   find the PR with `gh pr list --state open --search "head:<type>/<slug>"`. If there is still none, run the
   `/pull-request` steps first.

## Step 1: Make sure the PR head contains current main

1. Commit anything left in the tree (`git add -A`, a conventional message).
2. `git fetch origin <base>`.
3. If `git merge-base --is-ancestor origin/<base> HEAD` fails:
   - `git merge origin/<base> --no-edit`, resolving conflicts by `.claude/rules/integration-conflicts.md`;
   - run the full gate (`pnpm verify`, in the background) and fix failures for up to two rounds. Do not run
     `pnpm verify:lighthouse` again. Testing measured the branch's own change, and Merge does not re-measure it
     together with the new `main` (`docs/process/testing.md`, "Why Lighthouse runs once, and one at a time").

   If it is still red, push, explain, and end with `PR: blocked`.
4. Push: `git push origin HEAD:<pr head branch> --force-with-lease`.
5. Confirm `gh pr view <pr> --json headRefOid` equals `git rev-parse HEAD`.

## Step 2: Merge

`gh pr merge <pr> --squash --delete-branch --match-head-commit <HEAD sha>`. The squash lands one commit per task,
titled with the PR title.

There are no hosted CI checks yet, so an empty check list is expected and is not a reason to stop.

A non-zero exit does not prove the merge failed. Read `gh pr view <pr> --json state,mergeCommit`:
- **MERGED:** continue.
- **Not merged because `main` moved or the branch conflicts:** go back to Step 1. Do this at most three times.
- **Anything else:** report it and end with `PR: blocked`.

## Step 3: Confirm it landed

1. `git fetch origin <base>`.
2. `git merge-base --is-ancestor <mergeCommit oid> origin/<base>` must succeed.
3. Call `kangentic_link_pr` with the task id, so the card shows the merged PR at once.

## Step 4: Realign the worktree

The squash commit does not match the branch's commits, so the move to Done would report them as unmerged unless the
branch is realigned.

1. `git status --porcelain` must be empty. If it is not, report the paths, never discard them, and end with
   `PR: blocked`.
2. `git reset --hard origin/<base>`. This is safe only because the tree is clean and Step 3 proved the content is on
   `origin/<base>`.

## Step 5: Fast-forward the root checkout

The root checkout is where the owner runs `pnpm dev`.
1. `git -C <projectRoot> rev-parse --abbrev-ref HEAD`.
2. If it is on `<base>`, run `git -C <projectRoot> pull --ff-only`.

A failure here does not undo the merge, and dependents read `origin/main`, not the root. So report it prominently
(the branch, ahead and behind counts from `git -C <projectRoot> status -sb`, and any local commits), but do not
block on it, and never stash, reset or rebase the root.

## Step 6: Report

Give the PR URL, the merge commit, the root checkout status, and anything the owner should know (the PR's Decisions
and Owner actions sections). Then, only when Step 3 confirmed the merge commit is on `origin/<base>` and Step 4 left
the worktree clean and realigned, write alone as the last line:

`PR: merged`

The Merge column moves the task to Done on that line, which removes the worktree and starts the tasks waiting on
this one. Every other stop ends with `PR: blocked`, which keeps the task in Merge and notifies the owner. Write
nothing after either line.
