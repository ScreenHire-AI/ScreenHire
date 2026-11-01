---
description: Testing column skill. Commit, merge origin/main, sync docs, run the fast local checks and the branch's tests, push once, open and link the PR, then drive CI's Gate check to green (up to 3 fix rounds, one push each). Never merges. Ends on "PR: ready to merge" or "PR: blocked".
argument-hint: [commit message]
---

# Pull request (Testing column)

The test gate is CI: `.github/workflows/ci.yml` runs every suite on GitHub's Linux runners for the pull request,
behind one check, `Gate` (ADR-0020). This skill gets the branch ready, pushes it once, and watches that check. It never
merges; `/merge-pull-request` does that in the Merge column.

Every step that makes a commit happens before the first push, and all the fixes from one failed CI run go out in one
push. A push starts a new run and cancels the one in flight, so each extra push costs a whole run.

**User-provided commit message (if any):** $ARGUMENTS

## Rules

- Never `gh pr merge`, and never `--admin`.
- No Bash timeout above 600000 ms, and no `sleep`. Wait with `gh pr checks --watch`.
- Branch protection on `main` does not require `Gate` today, so this skill checks it itself. Never use
  `gh pr checks --required`: it lists nothing here.
- A failed `Lighthouse (report-only)` job is not a CI failure. Note it and keep watching until `Gate` finishes.

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
`git merge origin/<base> --no-edit` (a merge, never a rebase) and resolve any conflicts by
`.claude/rules/integration-conflicts.md`. If a conflict needs a decision you cannot make, abort the merge, explain it,
and end with `PR: blocked`.

## Step 3: Sync docs

Run `/sync-docs <base>` with the Skill tool. It audits the whole branch diff against the docs and ends with the scoped
`DocsBundleTests` run. Commit what it fixes as `docs: ...`.

## Step 4: Local checks

Run `/verify` (the default: `pnpm verify:fast` and the branch's added or changed tests, by class or file). If it fails,
fix the code or the test, whichever the brief says is wrong, commit (`fix(test): ...`), and run it again, up to three
rounds. After three failed rounds, go to Step 9b without pushing.

## Step 5: Push once

1. Public branch name `<type>/<slug>`:
   - `<type>`: the conventional prefix of the main change (`feat`, `fix`, `chore`, ...).
   - `<slug>`: the task title as 2 to 5 kebab-case words, using only `[a-z0-9-]`.
   - If this task already has a PR (`kangentic_get_current_task` shows `pr_number`), reuse its head branch.
2. Never rename the local branch. Kangentic keys the session to it.
3. `git push origin HEAD:<type>/<slug>`. A plain push: the branch only ever gains commits (merges, never rebases), so
   a rejected push means someone else pushed to it. Report that and end with `PR: blocked`; never force.

## Step 6: Open the PR and link it

1. Write the body to `.kangentic/PR_BODY.tmp` with the Write tool. The folder is ignored by git. Sections:
   - `## What`: the change and the components it touches.
   - `## Why`: the task (`#<number> <title>`) and the docs or ADRs it implements.
   - `## How`: the approach and the trade-offs worth a reviewer's attention.
   - `## Decisions`: choices made without a person, with the alternative each time; `None` if there were none.
   - `## Owner actions`: anything only a person can do (accounts, keys, legal); `None` if there is nothing.
   - `## Tests`: the local checks from Step 4 and the tests this task added; CI "running" for now (Step 8 fills it
     in).
   - The footer `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.
2. `gh pr create --base <base> --head <type>/<slug> --title "<conventional title>" --body-file .kangentic/PR_BODY.tmp`.
   If the PR already exists, `gh pr edit <pr> --body-file .kangentic/PR_BODY.tmp` instead. Opening the PR starts the
   CI run (a branch push without a PR starts none). Editing the body starts nothing.
3. Link it: `kangentic_get_current_task` (this directory and branch), then `kangentic_update_task` with `prUrl` and
   `prNumber`. The PR head differs from the local branch by design, so this link is the only connection. Retry if it
   fails; if the MCP server is gone for the rest of the run, report the PR number prominently.

## Step 7: Watch CI

Run `gh pr checks <type>/<slug> --watch --fail-fast --interval 30` with the Bash timeout at 600000 ms. Read the rows,
not only the exit code: it exits 8 while checks are pending.

- **Every row passed** (exit 0): go to Step 8.
- **"no checks reported"** right after the PR opened: the run has not registered yet. Confirm it with
  `gh run list --branch <type>/<slug> --workflow CI --limit 1`, then watch again.
- **The timeout fired with checks pending:** watch again. If two watches in a row end with no check finished in
  between, CI is stuck: go to Step 9b.
- **Only `Lighthouse (report-only)` failed:** watch again without `--fail-fast` until `Gate` finishes, and note the
  Lighthouse failure for the PR body.
- **Any other row failed:** a fix round, below.

### Fix round (at most three, one push each)

1. Start on the first failure at once: the failed row's `link` (from `gh pr checks <type>/<slug> --json name,state,link`)
   ends in `/job/<job-id>`; `gh run view --job <job-id> --log-failed` prints its failed steps.
2. Let the rest of the run finish before you push: `gh pr checks <type>/<slug> --watch --interval 30` (no
   `--fail-fast`). Pushing earlier cancels the run and hides the other failures.
3. Read every failure: `gh run view <run-id> --log-failed` (the run id is in each link, after `/runs/`). An
   `integration` or `e2e` shard that failed uploads its `TestResults` folder:
   `gh run download <run-id> -n integration-logs-<n>` or `-n e2e-results-<n>`, with `-D TestResults/ci-<run-id>`
   (ignored by git). Read the resource logs in `logs/` and the screenshots in `screenshots/`
   ([testing.md](../../../docs/process/testing.md#reading-a-failed-run)).
4. Classify each failure:
   - **The branch is wrong** (its code, or a test it added or changed): fix it.
   - **Code the branch did not touch:** look at `main`. `gh run list --branch main --workflow CI --limit 1 --json databaseId,conclusion`,
     and if that run failed, `gh run view <id> --log-failed`. The same test failing there means `main` is red: fix it
     in this branch and record it under Decisions in the PR body. A red `main` never blocks this skill.
   - **Neither** (the branch did not touch it and `main` passes it): suspect a flake. Rerun the failed jobs once,
     with no push: `gh run rerun <run-id> --failed`, then watch again. If they pass, it was a flake: list it in the
     PR body under Tests, and add a row to testing.md's Flake history in the next fix commit if a round pushes
     anyway. If it fails again, it is a defect: fix it.
5. Run each fixed test class or file locally, scoped (the table in `/verify`), then `/verify`. Commit
   (`fix(test): ...`, `fix: ...`).
6. Push once (`git push origin HEAD:<type>/<slug>`) and go back to the top of Step 7.

After the third round still fails, go to Step 9b.

## Step 8: Record the result

1. Confirm the green run is on the commit you pushed:
   `gh pr view <pr> --json headRefOid,statusCheckRollup --jq '{head: .headRefOid, gate: [.statusCheckRollup[] | select(.name == "Gate") | .conclusion]}'`.
   `head` must equal `git rev-parse HEAD`, and `gate` must read `["SUCCESS"]`.
2. Read Lighthouse: the `link` of `Lighthouse (report-only)` from `gh pr checks <pr> --json name,link`, then
   `gh run view --job <job-id> --log | grep "lighthouse:"`. It prints `N page(s) within budget` or the budget
   failures with each page's numbers. With no `lighthouse:` lines the job skipped the run: no marketing change.
3. Update the body's `## Tests` section: the CI run's URL, `Gate` passed on `<head sha>`, the jobs, the Lighthouse
   result ("N pages within budget", the misses, or "not run: no marketing change"; report-only either way), any
   flake from a rerun, and the local checks. `gh pr edit <pr> --body-file .kangentic/PR_BODY.tmp`. This starts no
   CI run.

## Step 9: Report

**9a. Success.** Give the PR URL, the commits, the CI run and its `Gate` result, the Lighthouse result, and the tests
added. Then, alone, as the last line:
`PR: ready to merge`

**9b. Blocked.** Push what you have if it is committed and the local checks pass, open or update the PR if
possible, and report:
- each still-failing check, with what each round tried and the root cause as far as you got;
- concrete next steps.

Then, alone, as the last line: `PR: blocked`

## Outcome line

The Testing column moves the task to Merge only when the last line reads `PR: ready to merge`. Print it only when
Step 8 confirmed `Gate` is SUCCESS on the PR's head and that head is the commit you pushed, the Lighthouse result is
in the PR body, and the PR exists and is linked. `PR: blocked` keeps the task in Testing and notifies the owner. Write
nothing after either line.
