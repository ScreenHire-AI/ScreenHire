# Unattended board runs

Workflow tasks run overnight with nobody at the keyboard. A question, a prompt or a missing credential parks the
task, and every task that waits on it, until morning.

## Never wait for a person

- Do not call AskUserQuestion and do not end a turn with a question. Take the option you would recommend, name the
  alternative, and record the choice under **Decisions** in the PR body.
- If the brief conflicts with the code on `main` or with an ADR, follow the ADR, note the conflict under
  **Decisions**, and carry on.
- If something is truly impossible without a person (a credential, a paid account, a legal call), build everything
  around it behind configuration with a local fake, list it under **Owner actions** in the PR body, and finish.

## Stay local

- No deploys, cloud resources, DNS, domains, purchases or third-party sign-ups. Hosting is deferred.
- No real credentials exist. Every external integration (OIDC providers, email, LLMs, job boards, IP intelligence)
  sits behind an interface with a local adapter: a container in the AppHost (mock OIDC server, Mailpit) or an
  in-process fake. A provider with no configuration is hidden, never an error, and no test needs a key or the
  internet.
- Never push to `main` directly. Code reaches `main` only through the Merge column's `/merge-pull-request`.
  `/merge-back` is the owner's escape hatch from the root checkout. Run it only when a person asks; it refuses
  inside a task worktree.

## Leave the process alone

- Do not edit `kangentic.json`, `.kangentic/`, `.claude/settings*.json`, `scripts/guard-processes.mjs` or the
  pipeline skills (`task-review`, `pull-request`, `merge-pull-request`, `verify`) unless the task is about them.
- Processes: never stop anything by name, port or process tree (the guard hook refuses it). Run long commands with
  `run_in_background` and stop them with TaskStop, except the gate: let a running `pnpm verify` finish (about 3.5
  minutes) and run it again after further edits. On Windows TaskStop ends the Bash tool's shell and leaves
  `pnpm verify` and its whole tree running, out of reach and holding the worktree folder (`/verify` has the details).
- Docker down: run `pnpm docker:start`, and report infrastructure only if it fails. Never launch Docker Desktop
  yourself (the guard refuses it): it would belong to the task and keep the worktree from being deleted.
- To run the stack, use `/preview`: it starts it detached with
  `dotnet aspire start --isolated` (random ports, nothing to reserve) and stops it with `dotnet aspire stop`. Prefer
  the Aspire test host over a dev server.

## Start from what is merged

- First step of every task: `git fetch origin main` and confirm the code your brief builds on (the tasks it waits
  for) is on `origin/main`. If the worktree is behind, `git merge origin/main` before writing code.
- If a task you wait for is **not** on `origin/main` (its PR is unmerged, or its code is missing), do not build on a
  guess. Stop, say which task and what is missing, and end the turn with this line alone as the last line:
  `Blocked: dependency not on main`. The board notifies the owner.
- Read the docs your brief names before designing anything.

## Finish in a state the gate accepts

- Tests ship with the code: unit tests for logic, Aspire integration tests for endpoints and data rules, Playwright
  for user journeys. A brief's acceptance criteria are the minimum.
- Run targeted tests while you work and `pnpm verify` before you call implementation done.
- Commit with conventional messages (`feat(auth): ...`). Keep commits focused; the merge squashes them.
- Board outcome lines are contracts. Print one only when its condition holds, alone, as the last line of the
  message (see CLAUDE.md, "Board pipeline").
