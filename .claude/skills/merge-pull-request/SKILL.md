---
description: Merge column skill. Check that CI's Gate is green on the PR's head and the PR is mergeable, squash-merge it (CI runs again only after a conflict), confirm it landed on origin/main, realign the worktree, fast-forward the root checkout, and end on "PR: merged" (or "PR: blocked").
---

# Merge pull request (Merge column)

By the time a task reaches Merge, the decision to ship is made: Testing drove CI's `Gate` check to green on the PR's
head. This skill lands it on `origin/main`. Kangentic cuts every new worktree from `origin/main`, and a task's
dependents start when it reaches Done, so `PR: merged` must mean the code really is on `origin/main`.

A pull request run tests `refs/pull/<n>/merge`: the head merged with `main` as it was when the run started. So a PR
whose `Gate` passed still merges when `main` has moved since; CI does not run again. CI on every push to `main` is the
backstop for anything two green branches break together, and the next task whose CI fails on it fixes it (ADR-0020).
Only a conflict needs a new run.

Fix small problems here and merge. Do not hand them back.

## Rules

- Never `--admin`, and no Bash timeout above 600000 ms. No `sleep`: re-read, or wait with `gh pr checks --watch`.
- Branch protection on `main` does not require `Gate` today, so this skill checks it itself, by name, on the head
  SHA. Never use `gh pr checks --required` (it lists nothing here) and never decide on `mergeStateStatus`.
- A red `main` never blocks a merge. Read `main`'s CI only inside a fix round, to classify a failing job as
  `/pull-request`'s fix round does.
- No Lighthouse here: Testing reported it.

## Pre-flight

1. The current directory must be inside `.kangentic/worktrees/<slug>/`. If not, report and stop (no outcome line).
2. Project root: the path above `.kangentic/worktrees/<slug>`.
3. Base branch: `git config kangentic.baseBranch`, else `main`.
4. `gh auth status`. If it fails, report and end with `PR: blocked`.
5. Resolve the PR: `kangentic_get_current_task`, then take its `pr_number` and task id. If there is no PR number,
   find the PR with `gh pr list --state all --search "head:<type>/<slug>"`. If there is still none, run the
   `/pull-request` steps first.
6. `gh pr view <pr> --json state,headRefName`. Record `<prHead>`. If `state` is `MERGED`, an earlier turn merged it:
   skip to Step 4.

## Step 1: Check the PR

1. Commit anything left in the tree (`git add -A`, a conventional message). A new commit needs a push and a CI run:
   go to Step 2, item 3.
2. Read `gh pr view <pr> --json headRefOid,mergeable,statusCheckRollup --jq '{head: .headRefOid, mergeable, gate: [.statusCheckRollup[] | select(.name == "Gate" and .workflowName == "CI") | .conclusion]}'`.
3. `head` must equal `git rev-parse HEAD`.
   - Local HEAD has commits the PR lacks: push them (`git push origin HEAD:<prHead>`) and go to Step 2, item 4.
   - The PR head has commits the worktree lacks: someone else pushed. Report it and end with `PR: blocked`.
4. `mergeable`. Read it before `gate`, because GitHub starts no pull request run while the PR conflicts.
   - `MERGEABLE`: go on.
   - `CONFLICTING`: go to Step 2.
   - `UNKNOWN`: GitHub is still computing it. Read item 2 again, up to three times. Still unknown: report and end
     with `PR: blocked`.
5. `gate`:
   - `["SUCCESS"]`: go to Step 3.
   - Empty or pending: wait with `gh pr checks <pr> --watch --interval 30` (Bash timeout 600000 ms), as
     `/pull-request` Step 7 describes, then read item 2 again. If two waits in a row report no checks or no check
     finishes, read `gh run list --branch <prHead> --workflow CI --limit 1`, report it and end with `PR: blocked`.
   - `FAILURE` (or cancelled): fix it as `/pull-request` Step 7's fix round does, at most two rounds, one push each.
     Still red after that: report and end with `PR: blocked`.

## Step 2: Resolve a conflict (only when the PR conflicts)

1. `git fetch origin <base>`, then `git merge origin/<base> --no-edit`. Resolve by
   `.claude/rules/integration-conflicts.md`. If a conflict needs a decision you cannot make, abort the merge, explain,
   and end with `PR: blocked`.
2. `pnpm verify:fast`. Fix and commit what it finds.
3. Push: `git push origin HEAD:<prHead>`. A plain push: the branch only gains commits.
4. Wait for the one CI run this push started (`/pull-request` Step 7), then go back to Step 1, item 2.

Resolve conflicts at most three times in one run (`main` can move again). After that, report and end with
`PR: blocked`.

## Step 3: Merge

`gh pr merge <pr> --squash --delete-branch --match-head-commit <head sha>`, with the SHA Step 1 confirmed. The squash
lands one commit per task, titled with the PR title. `--match-head-commit` refuses the merge if the head moved since
the check.

A non-zero exit does not prove the merge failed. Read `gh pr view <pr> --json state,mergeCommit`:
- **MERGED:** continue.
- **Not merged because the head moved or the PR now conflicts:** go back to Step 1. Do this at most three times.
- **Anything else:** report it and end with `PR: blocked`.

## Step 4: Confirm it landed

1. `git fetch origin <base>`.
2. `git merge-base --is-ancestor <mergeCommit oid> origin/<base>` must succeed (`gh pr view <pr> --json mergeCommit`
   gives the oid).
3. Call `kangentic_link_pr` with the task id, so the card shows the merged PR at once.

## Step 5: Realign the worktree

The squash commit does not match the branch's commits, so the move to Done would report them as unmerged unless the
branch is realigned.

1. `git status --porcelain` must be empty. If it is not, report the paths, never discard them, and end with
   `PR: blocked`.
2. `git reset --hard origin/<base>`. This is safe only because the tree is clean and Step 4 proved the content is on
   `origin/<base>`.

## Step 6: Fast-forward the root checkout

The root checkout is where the owner runs `pnpm dev`.
1. `git -C <projectRoot> rev-parse --abbrev-ref HEAD`.
2. If it is on `<base>`, run `git -C <projectRoot> pull --ff-only`.

A failure here does not undo the merge, and dependents read `origin/main`, not the root. So report it prominently
(the branch, ahead and behind counts from `git -C <projectRoot> status -sb`, and any local commits), but do not
block on it, and never stash, reset or rebase the root.

## Step 7: Report

Give the PR URL, the merge commit, the CI run whose `Gate` the merge relied on, the root checkout status, and anything
the owner should know (the PR's Decisions and Owner actions sections). Then, only when Step 4 confirmed the merge
commit is on `origin/<base>` and Step 5 left the worktree clean and realigned, write alone as the last line:

`PR: merged`

The Merge column moves the task to Done on that line, which removes the worktree and starts the tasks waiting on
this one. Every other stop ends with `PR: blocked`, which keeps the task in Merge and notifies the owner. Write
nothing after either line.
