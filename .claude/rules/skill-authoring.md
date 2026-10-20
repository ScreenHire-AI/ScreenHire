---
paths:
  - ".claude/skills/**"
  - ".claude/agents/**"
  - ".claude/rules/**"
---
# Rule: authoring skills, agents and rules

Agents run these files with nobody watching. A command that does not exist, a path that moved, or a step that asks
a question parks a task overnight.

## The rule

- **Grounded.** Every command and path in a skill or agent exists on `main` and was run once when the file was
  written. When a step depends on something not merged yet, word it by what exists ("if
  `tests/ScreenHire.E2E` exists"), not by task number, and say what to do otherwise.
- **Unattended.** No step asks the user to confirm or choose. Take the recommended option and record it
  (`.claude/rules/unattended-runs.md`).
- **Short.** A skill is a procedure: numbered steps, the commands, and what done looks like. Background belongs in a
  rule or a doc it links to. Do not copy CLAUDE.md into a skill; point at it.
- **Outcome lines are contracts.** Never change the board outcome lines (`Implementation: complete`,
  `Verdict: Ready`, `PR: merged`, ...) or the pipeline skills (`verify`, `task-review`, `pull-request`,
  `merge-pull-request`) unless the task is about them.
- **Agents.** Reviewers and auditors are read-only: `tools: Read, Glob, Grep, Bash`, with Bash limited to read-only
  commands in the body. An agent that writes (`test-builder`) says so in its description. Never point a writing
  task at the built-in `Explore` or `Plan` agents; they are read-only and skip CLAUDE.md.
- **Do not fork a mutating skill.** A skill that commits, pushes or edits code runs in the main conversation, and
  heavy review work goes to subagents through the Agent tool. `context: fork` is only for a self-contained,
  read-only skill that ends in a summary.
- **Rules** have a `paths:` list when they only matter for some files, and end with Enforcement and Scope sections.
- Every new skill, agent or rule gets a row in `docs/process/dev-tooling.md`.

## Enforcement

- **Review.** `/task-review`'s conventions dimension and the `doc-auditor` agent (the dev-tooling anchor).

## Scope

Files under `.claude/`. Product code is out of scope.
