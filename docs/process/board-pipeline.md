# Board pipeline (Kangentic)

How a task travels from the board to `main` without anyone at the keyboard, what each notification means, and how to
unblock a task when one fires. Written for the owner. The sources of truth are [`kangentic.json`](../../kangentic.json)
(columns and automations), the pipeline skills in [`.claude/skills/`](../../.claude/skills/) and the "Board pipeline"
section of [`CLAUDE.md`](../../CLAUDE.md). If this page and those files disagree, the files win: fix this page.

## The route

```
To Do → (Planning) → Executing → Code Review → Testing → Merge → Done
                         ↑            │
                         └─ Blocked ──┘   (at most 3 times)
```

Every column between To Do and Done spawns an agent in the task's own git worktree (`.kangentic/worktrees/<n>/`).
When that agent ends a turn, the column reads **the last line of its final message** and compares it, in code, with the outcome lines
below (ignoring case, punctuation and markdown). The outcome lines are therefore contracts. An agent prints one only
when its condition holds, alone, with nothing after it.

| Column | Agent | What starts the work | Exit lines |
|---|---|---|---|
| To Do | — | — | You (or a workflow) move the task on |
| Planning | Opus, plan mode | Spawns on entry | Kangentic approves the plan (`planApproval: automatic`) → Executing. **Workflow tasks skip this column:** their brief is the plan. |
| Executing | Opus, auto mode | Spawns on entry with the brief | `Implementation: complete`, then `Findings: applied`; or `Blocked: dependency not on main` |
| Code Review | Opus, auto mode, **isolated session, new every time** | `/task-review <base>` | `Verdict: Ready` / `Verdict: Blocked` |
| Testing | Sonnet, auto mode | `/pull-request` | `PR: ready to merge` / `PR: blocked` |
| Merge | Sonnet, auto mode | `/merge-pull-request` | `PR: merged` / `PR: blocked` |
| Done | — | — | Worktree removed; dependents start |

### What each column does

- **Executing** implements the brief, adds the tests, and runs the gate (`pnpm verify`). Its first instruction
  ([unattended-runs](../../.claude/rules/unattended-runs.md)) is to fetch `origin/main` and confirm that the tasks it
  depends on have landed.
- **Code Review** ([`task-review`](../../.claude/skills/task-review/SKILL.md)) runs in a fresh conversation that never
  saw the implementation. It reviews three dimensions in parallel: correctness, the non-negotiables and security, and
  conventions. It fixes what it can, commits review work as `fix(review): …`, `test(review): …` or
  `docs(review): …`, and sends back only work that is task-sized or needs a decision.
- **Testing** ([`pull-request`](../../.claude/skills/pull-request/SKILL.md)) commits, merges `origin/main` into the
  branch, runs the full gate with up to three fix rounds, pushes to a public `<type>/<slug>` branch, and opens a PR
  linked to the card. It never merges.
- **Merge** ([`merge-pull-request`](../../.claude/skills/merge-pull-request/SKILL.md)) re-integrates `main` and re-runs
  the gate if `main` moved, squash-merges the PR, confirms the merge commit is on `origin/main`, resets the worktree to
  `origin/main`, and fast-forwards the root checkout when it is on `main`.
- **Done** removes the worktree and starts every task waiting on this one. Kangentic cuts new worktrees from
  `origin/main`, which is why `PR: merged` must mean the code is really there.

## Outcome lines and what they trigger

| Column | Last line | Automation | Effect |
|---|---|---|---|
| Executing | `Implementation: complete` | Apply findings | Does **not** move the task. Sends the agent a message asking it to apply any findings or follow-ups from its last message, re-run checks and commit. |
| Executing | `Findings: applied` | On to Code Review (max 4) | Moves the task to Code Review. |
| Executing | `Blocked: dependency not on main` | Dependency missing | Desktop notification. The task stays in Executing. |
| Code Review | `Verdict: Ready` | On to Testing (max 3) | Moves the task to Testing. |
| Code Review | `Verdict: Blocked` | Back for revisions (max 3) | Moves the task back to Executing with the review's numbered steps. The implementer does them and ends on `Findings: applied`, which brings the task back to Code Review. |
| Code Review | `Verdict: Blocked`, with the 3 returns used up | Review stuck | Desktop notification. The task stays in Code Review. |
| Testing | `PR: ready to merge` | On to Merge (max 3) | Moves the task to Merge. |
| Testing | `PR: blocked` | Testing blocked | Desktop notification. The task stays in Testing. |
| Merge | `PR: merged` | Finish (max 3) | Moves the task to Done. |
| Merge | `PR: blocked` | Merge blocked | Desktop notification. The task stays in Merge. |

Two rules about how these rows run (from Kangentic's automation reference, `kangentic_set_automations`):

- **A move ends the list.** When a `move_to_column` row runs, the rows after it in that column do not. That is why
  `Verdict: Blocked` only notifies once "Back for revisions" has used its 3 runs. `PR: blocked` matches only a notify
  row, so it notifies every time.
- **Max runs count per task, across every visit** to the column since the task last left Done. Moving a card by
  hand does not reset the counters. Once a move row is used up, its line falls through to the next matching row, or
  to nothing.

### The silent stall

If the last line matches no row (a progress note, a question, a typo in the outcome line, or a crash), **nothing
happens and nothing notifies you.** The task sits in its column with an idle agent. None of the four notifications
covers this case, so when you check the board, an idle agent in Executing through Merge with no outcome line on its
last message is a stall. The unattended rules forbid ending a turn with a question for exactly this reason.

## Notifications and how to unblock

All four notifications leave the task where it is, with its worktree and session intact. The general recipe:

1. **Pause the workflow** (Kangentic, or `kangentic_pause_workflow`). Every task's agent pauses at once and nothing
   starts or moves, so sibling tasks cannot pile onto the problem while you fix it.
2. **Read the agent's last message.** Each blocked outcome ends with the reason and concrete next steps.
3. **Fix the cause** (see the table below). Work in the task's worktree, or tell its agent what to do.
4. **Resume the workflow** (`kangentic_resume_workflow`). Each agent resumes in the column it is in, and waiting
   tasks start as their blockers finish.
5. **Get the right last line.** Resuming does not replay the column's entry message. If the agent is idle, ask it to
   re-run its column's skill (`/task-review`, `/pull-request`, `/merge-pull-request`), or to finish its brief, so
   that its turn ends on the outcome line. If you fixed everything yourself, you can instead move the card to the
   next column. That fires the next column's entry message as usual.

| Notification | Meaning | Unblock |
|---|---|---|
| **Blocked on a dependency: #N title** | Executing found that a task this one waits for is not on `origin/main` (its PR is unmerged, or its code is missing). The agent wrote nothing on a guess. | Get the dependency to Done first: check its own column and notifications. Then tell this task's agent to `git fetch origin main`, merge it, and carry on with the brief. |
| **Review blocked: #N title** | Code Review sent the task back 3 times and still finds blocking work, so the review loop is exhausted. | Read the numbered steps. Either do them yourself in the worktree and move the card to Testing, or settle the disputed decision in the brief and send the task back to Executing by hand. By now Executing's "On to Code Review" row has used all 4 runs (the first entry plus 3 returns), so `Findings: applied` will not move it again: move the card to Code Review yourself when the work is done. |
| **Testing blocked: #N title** | `/pull-request` could not finish. Common causes: the gate is still red after 3 fix rounds, `gh` is not authenticated, a merge conflict needs a product decision, Docker is down, or Playwright's Chromium is not installed (`pnpm e2e:install`). | Fix the named cause (for example `gh auth login`, start Docker, or decide the conflict), then ask the agent to run `/pull-request` again. It reuses the existing PR and branch. |
| **Merge blocked: #N title** | `/merge-pull-request` could not land the PR. Common causes: the re-gate failed after `main` moved, `main` moved more than 3 times during the merge, uncommitted files were found before the realign, or `gh` failed. | Fix the cause, then ask the agent to run `/merge-pull-request` again (if the PR is already merged, see [Troubleshooting](#troubleshooting) instead). Do not merge the PR on GitHub by hand and leave the card: Done is what removes the worktree and starts the dependents. If you did merge by hand, move the card to Done yourself. |

If a root checkout fast-forward fails during Merge, the task still reaches Done. The final message reports it
prominently (branch, ahead/behind, local commits). Sort out the root checkout yourself; the agents never stash, reset
or rebase it.

## Troubleshooting

After pausing the workflow, check this first. Run the commands in the task's worktree.

| Column | Notification | First thing to check |
|---|---|---|
| Executing | Blocked on a dependency | The dependency's card. If it is not in Done, the problem is there: unblock that task first. If it is in Done, run `git fetch origin main` and `git log --oneline origin/main -- <a file the dependency adds>`. A result means the dependency did land: tell this task's agent to merge `origin/main` and carry on. An empty result means its card reached Done without its code on `main` (moved by hand, or a bad merge), so check the dependency's PR with `gh pr view <pr> --json state,mergeCommit`. |
| Code Review | Review blocked | Whether the numbered steps repeat from one return to the next (earlier verdicts are in the Executing session, in the "Code Review sent this back" messages). The same step three times is a disagreement between the brief and the reviewer, not unfinished code, so settle it in the brief. Different steps each time mean the work is still moving: do the remaining steps yourself. |
| Testing | Testing blocked | The environment, before the code: `gh auth status` and `docker info`. Either one failing explains the block on its own. If both pass, read the last message for the first failing gate step, or for a merge conflict that needs a decision. |
| Merge | Merge blocked | `git status`, then `gh pr view <pr> --json state,mergeCommit`. Uncommitted files stop the realign, and Done deletes the worktree, so commit them or move them out first; never discard them. A PR that is already `MERGED` means the block came after the merge: ask the agent to carry on from "Confirm it landed" in [`merge-pull-request`](../../.claude/skills/merge-pull-request/SKILL.md), not to re-run the whole skill. An open PR means it stopped before or during the merge (re-gate, `main` moving, `gh`): read the reason in the last message. A closed, unmerged PR needs reopening first. |

No notification at all, but an idle agent? That is the [silent stall](#the-silent-stall): read the last line of its
final message.

## Where the rules live

| What | Where |
|---|---|
| Columns, automations, outcome lines, models | [`kangentic.json`](../../kangentic.json) |
| The gate (`/verify`): what runs, in what order, how to read failures | [`.claude/skills/verify/SKILL.md`](../../.claude/skills/verify/SKILL.md); scripts `verify`, `verify:dotnet`, `verify:contract`, `verify:test` in [`package.json`](../../package.json) |
| Column skills | [`task-review`](../../.claude/skills/task-review/SKILL.md), [`pull-request`](../../.claude/skills/pull-request/SKILL.md), [`merge-pull-request`](../../.claude/skills/merge-pull-request/SKILL.md) |
| Conflict playbook for integrating `main` (merge, not rebase; regenerate lockfile, API client and migrations) | [`.claude/rules/integration-conflicts.md`](../../.claude/rules/integration-conflicts.md) |
| Unattended-run rules (never wait for a person, stay local, start from what is merged) | [`.claude/rules/unattended-runs.md`](../../.claude/rules/unattended-runs.md) |
| Process guard: refuses killing processes by name, port or tree, stopping a PID started outside this worktree, Docker prunes and sub-command removals, and stopping services, WSL or the machine | [`scripts/guard-processes.mjs`](../../scripts/guard-processes.mjs), wired as a `PreToolUse` hook in [`.claude/settings.json`](../../.claude/settings.json) |
| Auto-mode allowances for the pipeline (push to the task branch, `gh pr merge`, local builds) | `autoMode` in [`.claude/settings.json`](../../.claude/settings.json) |

Agents may not edit `kangentic.json`, `.kangentic/`, `.claude/settings*.json`, the guard or the pipeline skills unless
the task is about them. Changing the pipeline is a task of its own.
