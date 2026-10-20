---
description: Find where docs/, CLAUDE.md and .claude/ have drifted from the code in a diff, and fix them. Owns the source-to-doc anchor table. Use after a change to commands, AppHost resources, projects, skills/agents/rules or docs files, or when asked to "sync docs".
allowed-tools: Read, Edit, Write, Glob, Grep, Bash(git:*), Agent
argument-hint: [base-branch | all]
---

# Sync docs

Docs drift in two ways: a list that no longer matches what the code defines (an anchor), and prose that describes
behaviour that changed. This skill fixes both for the current branch. It edits docs only, never code.

**Scope:** `$ARGUMENTS` is a base branch (default `main`) to diff against, or `all` to check every anchor.

## Anchors

Each row is a list in a doc that must match a source exactly.

| Anchor | Source | Doc (canonical list) |
|---|---|---|
| Root scripts | every key of `scripts` in `package.json` | CLAUDE.md "Commands" (each script named) |
| Pinned ports | every `PortFor("<Name>", <port>)` in `apps/apphost/AppHost.cs` | CLAUDE.md "Commands" (the `pnpm dev` ports line) |
| AppHost settings | every `ScreenHire:*` key read in `AppHost.cs` | the header comment in `AppHost.cs` |
| Projects | every folder directly under `apps/`, `packages/` and `src/` | CLAUDE.md "Repo layout" (one row each; `tests/` is one row for all test projects) |
| Skills, agents, rules | `.claude/skills/*/SKILL.md`, `.claude/agents/*.md`, `.claude/rules/*.md` | `docs/process/dev-tooling.md` |
| Docs files and ADRs | `docs/**/*.md` | `docs/README.md` (decision log and section lists) |

The process docs mirror two more sources: `kangentic.json` columns and outcome lines in
`docs/process/board-pipeline.md` and the CLAUDE.md "Board pipeline" table. Those change only in pipeline tasks;
check them if `kangentic.json` is in the diff.

Not anchored on purpose: API routes (`packages/api-client/openapi.json` is the list, generated from code) and
design tokens (`packages/ui/src/tokens.css` is the source; `docs/design/design-system.md` explains it).

## Workflow

1. **Scope.** `git fetch origin <base>`, then `git diff --name-only origin/<base>...HEAD` plus
   `git status --porcelain`. With `all`, every anchor is in scope.
2. **Anchors.** Map the changed files to the rows above. Spawn the `doc-auditor` agent with the list of anchors to
   check (or `all`). It reports items missing from or extra in each doc, with `file:line`.
3. **Prose.** For each changed source file, find the docs that describe it: `docs/architecture/*.md` for the
   integrity, workflow, distribution and messaging subsystems, ADRs that name it, `.claude/rules/*.md` that name its paths,
   and skills whose steps run it. Use Grep for the file name, the type name and the command. Read each hit and
   decide whether the text is still true.
4. **Fix.** Edit the docs so they match the code. Keep each doc's structure and voice. New prose follows
   `.claude/rules/writing-style.md`; do not restyle text you are not otherwise changing. A changed decision is a new
   or amended ADR, never a silent edit of an accepted one: if the code contradicts an accepted ADR, report it
   rather than editing either side.
5. **New docs.** A new subsystem or process gets its own page under `docs/architecture/` or `docs/process/`, plus a
   line in `docs/README.md`.
6. **Report.** The anchors checked with their status, the docs you changed and why, and anything you could not
   settle (an ADR conflict, a behaviour you could not confirm from the code).

## Conventions

- Docs link with relative paths (`../../CLAUDE.md`). Code references use `path:line` or a backticked path.
- CLAUDE.md is the index: one line per rule or command, linking to the detail. Detail lives in `.claude/rules/`,
  `docs/` or a skill, never in two places.
- Tables for enumerations; code blocks for commands.
