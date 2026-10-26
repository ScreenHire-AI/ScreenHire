---
paths:
  - "package.json"
  - "apps/*/package.json"
  - "apps/apphost/**"
  - "ScreenHire.slnx"
  - ".claude/skills/**"
  - ".claude/agents/**"
  - ".claude/rules/**"
  - "docs/**"
---
# Rule: documentation tracks source

Some docs enumerate things the code defines: the root `package.json` scripts, the AppHost ports and settings, the
projects in the repo layout, the skills, agents and rules in `.claude/`, and the files in `docs/`. When one side
changes and the other does not, the next agent follows stale instructions. The `/sync-docs` skill owns the full
source-to-doc table; this rule is the reminder that fires when you touch one of those sources.

## The rule

When you add, rename or remove an enumerated item, update the doc that lists it in the same change:

- a root script or a changed command: CLAUDE.md "Commands";
- a pinned AppHost port: CLAUDE.md "Commands"; an AppHost setting: the header comment in `apps/apphost/AppHost.cs`;
- a project or top-level folder: the CLAUDE.md "Repo layout" table;
- a skill, agent or rule: `docs/process/dev-tooling.md`;
- a file under `docs/`: frontmatter (`type`, `title`, `description`, `sources:`) and a line with the same description
  in its folder's `index.md` (ADR-0012).

The mapping lives in `.claude/skills/sync-docs/SKILL.md`. Change it there, not here. The `/verify` skill describes
the gate's steps in prose, so a changed `verify*` script is a check for `/sync-docs`'s prose pass, not an anchor.

Code that a doc describes is listed in that doc's `sources:`. `node scripts/docs-bundle.mjs sources <file>` names the
docs to re-check after changing it. A file no doc lists is a gap to fill in the doc that describes it.

## Enforcement

- **Test.** `DocsBundleTests` (`tests/ScreenHire.UnitTests/Tooling/`) in `pnpm verify` fails on a doc without
  frontmatter, an index line that differs from its doc's title or description, a `sources:` path that no longer
  exists, a broken link or heading fragment (or a link whose case differs from its file), and a `docs/...` path
  cited from code, CLAUDE.md or `.claude/` that does not exist.
- **Agent.** `doc-auditor` compares each enumerated source with its doc and reports what is missing or extra.
- **Review.** `/task-review`'s conventions dimension checks that docs and CLAUDE.md changed with behaviour or
  commands.

## Scope

Enumerable source-to-doc pairs. Prose accuracy elsewhere is `/sync-docs`'s prose pass.
