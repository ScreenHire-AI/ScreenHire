---
description: Make a local commit of the current changes with a conventional-commit message. No push, no merge, no gate. Use for "commit", "commit my changes", "save my work". Pushing and PRs are the board's Testing column (/pull-request).
allowed-tools: Read, Glob, Grep, Bash(git:*), Write
argument-hint: [commit message]
---

# Commit

A fast, local snapshot of the working tree on the current branch. It never pushes, fetches, merges, rebases,
amends or runs the gate. The board's Testing column (`/pull-request`) does those.

**Message (optional):** $ARGUMENTS

## 1. Inspect

1. `git rev-parse --abbrev-ref HEAD`. If it prints `HEAD` (detached), report it and stop.
2. If the branch is `main`, stop: work reaches `main` only through the Merge column.
3. `git status --porcelain`. If it is empty, report "nothing to commit" and stop.
4. `git diff --stat` and `git diff --cached --stat` to see what will go in.

Do not commit secrets, `.env` files, real applicant data or machine paths (`.claude/rules/no-personal-info.md`). If
the diff contains any, leave those files out and say so.

## 2. Message

Conventional commits: `type(scope): subject`, subject in the imperative, lower case, no trailing period, at most
72 characters.

- Types: `feat`, `fix`, `refactor`, `perf`, `test`, `docs`, `chore`, `build`, `ci`, `style`. Add `!` after the
  type for a breaking change (a renamed route or `WithName`, a removed contract field).
- Scope is the area: `api`, `dashboard`, `careers`, `marketing`, `worker`, `domain`, `db`, `auth`, `integrity`,
  `workflow`, `ui`, `api-client`, `tooling`, `process`.
- Review fixes use the `(review)` scope suffix the Code Review column expects: `fix(review): ...`.
- If `$ARGUMENTS` already has a conventional prefix, use it as is. If it has none, pick the type from the diff and
  prepend it. With no argument, write the message from the diff.
- The body, when needed, says why in a sentence or two. Follow `.claude/rules/writing-style.md`.
- End with the attribution trailer your session instructions give, if any.

## 3. Commit

1. Write the message with the Write tool to `.kangentic/COMMIT_MSG.tmp` (relative to the worktree root;
   `.kangentic/` is git-ignored).
2. `git add -A`. Uncommitted changes in a task worktree belong to the task, including those another column made.
3. `git commit -F .kangentic/COMMIT_MSG.tmp`.

If a hook fails, fix the cause and make a new commit. Never pass `--no-verify`, and never amend.

## 4. Report

The short hash and subject (`git log --oneline -1`) and the number of files. This is a local commit only.
