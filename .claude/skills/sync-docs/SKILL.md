---
description: Find where docs/, CLAUDE.md and .claude/ have drifted from the code in a diff, and fix them. Owns the source-to-doc anchor table and maps a changed file to its docs with docs-bundle.mjs. Use after a change to commands, AppHost resources, projects, skills/agents/rules, docs files or code a doc describes, or when asked to "sync docs".
allowed-tools: Read, Edit, Write, Glob, Grep, Bash(git:*), Bash(node scripts/docs-bundle.mjs:*), Bash(dotnet test:*), Agent
argument-hint: [base-branch | all]
---

# Sync docs

Docs drift in two ways: a list that no longer matches what the code defines (an anchor), and prose that describes
behaviour that changed. This skill fixes both for the current branch. It edits docs only, never code.

**Scope:** `$ARGUMENTS` is a base branch (default `main`) to diff against, or `all` to check every anchor.

`docs/` is an Open Knowledge Format bundle (ADR-0012). Every doc but `index.md` and `log.md` is a concept whose
frontmatter names the code it describes in `sources:`, so a changed file maps to its docs by command, not by a
hand-kept table:

```
node scripts/docs-bundle.mjs sources <changed file>...
```

A folder argument prints the concepts that list any file in it, which helps after a change across a whole folder.

## Anchors

Each row is a list in a doc that must match a source exactly.

| Anchor | Source | Doc (canonical list) |
|---|---|---|
| Root scripts | every key of `scripts` in `package.json` | CLAUDE.md "Commands" (each script named) |
| Pinned ports | every `PortFor("<Name>", <port>)` in `apps/apphost/AppHost.cs` | CLAUDE.md "Commands" (the `pnpm dev` ports line) |
| AppHost settings | every `ScreenHire:*` key read in `AppHost.cs` | the header comment in `AppHost.cs` |
| Projects | every folder directly under `apps/`, `packages/` and `src/` | CLAUDE.md "Repo layout" (one row each; `tests/` is one row for all test projects) |
| Skills, agents, rules | `.claude/skills/*/SKILL.md`, `.claude/agents/*.md`, `.claude/rules/*.md` | `docs/process/dev-tooling.md` |
| Docs files and ADRs | `docs/**/*.md` but `index.md` and `log.md` | the `index.md` of the doc's folder (one `* [Title](file.md) - description` line each); `DocsBundleTests` also enforces it |

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
3. **Prose.** Run `node scripts/docs-bundle.mjs sources` on the changed source files. Read each concept it names
   and decide whether the text is still true. Also grep ADRs, `.claude/rules/*.md` and skills for the file name, the
   type name and the command, since they cite code without `sources:`. A changed file that no concept lists is a gap:
   decide which concept documents it and add the file to that concept's `sources:` in the same change.
4. **Fix.** Edit the docs so they match the code. Keep each doc's structure, section numbers and voice. New prose
   follows `.claude/rules/writing-style.md`; do not restyle text you are not otherwise changing. A changed decision is
   a new or amended ADR, never a silent edit of an accepted one: if the code contradicts an accepted ADR, report it
   rather than editing either side. When a change alters what a concept holds, update its `description` and the
   matching line in its folder's `index.md` together.
5. **New docs.** A new subsystem or process gets its own concept under `docs/architecture/` or `docs/process/`: the
   frontmatter below, and a line in that folder's `index.md`. A new folder gets an `index.md` and a line in its
   parent's. A change to the bundle's layout (a folder, a split, a move) gets a dated entry at the top of
   `docs/log.md`.
6. **Check.** `dotnet test --project tests/ScreenHire.UnitTests --filter-class "*DocsBundleTests"` checks frontmatter,
   index lines, links and heading fragments, and every `docs/...` path cited from code, CLAUDE.md and `.claude/`.
7. **Report.** The anchors checked with their status, the docs you changed and why, and anything you could not
   settle (an ADR conflict, a behaviour you could not confirm from the code).

## Conventions

- A concept's frontmatter: `type` (Guide, Reference, Design, Decision, Audit or Research), `title` (the same text as
  its H1), a one-line `description`, optional `tags: [...]`, and `sources:` as `- resource: <repo path>` lines.
- The `description` is how an agent picks the concept from the index. Name what only this concept holds: the flags,
  files, failure modes and decisions someone would ask about. No two concepts share one.
- `sources:` stay narrow: the files the concept describes, never a folder (`DocsBundleTests` refuses one). A broad
  entry flags the concept on every unrelated change, so leave out tests and helpers the concept does not describe.
- Docs link with relative paths (`../../CLAUDE.md`). Code references use `path:line` or a backticked path. Files cited
  by section number (the architecture docs, the design system) keep their numbering.
- CLAUDE.md is the index for agents: one line per rule or command, linking to the detail. Detail lives in
  `.claude/rules/`, `docs/` or a skill, never in two places.
- Tables for enumerations; code blocks for commands.
