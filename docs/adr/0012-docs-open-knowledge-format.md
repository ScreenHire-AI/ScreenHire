---
type: Decision
title: "ADR-0012: Docs as an Open Knowledge Format bundle"
description: "Why docs/ is an Open Knowledge Format v0.2 bundle with an index per folder, a routing description and narrow sources per concept, why no file was split, and that Kangentic measured no reading benefit at its size, so the reasons are growth and drift detection."
tags: [adr, docs, okf, agents]
sources:
  - resource: scripts/docs-bundle.mjs
  - resource: tests/ScreenHire.UnitTests/Tooling/DocsBundle.cs
  - resource: tests/ScreenHire.UnitTests/Tooling/DocsBundleTests.cs
---
# ADR-0012: Docs as an Open Knowledge Format bundle

- **Status:** Accepted (2026-10-23)
- **Deciders:** Owner
- **Follows:** Kangentic's own rollout ([PR #639](https://github.com/Kangentic/kangentic/pull/639) and its write-up,
  [Open Knowledge Format for agent-read docs](https://github.com/Kangentic/kangentic/blob/main/docs/research/okf-knowledge-format.md))

## Context

Agents do most of the work in this repository, and they find docs by grepping. `docs/README.md` was a hand-kept list
of every doc next to the MVP status and the decision log, and nothing checked that the list, the links or the
`docs/...` paths cited from code still matched the files. `/sync-docs` mapped a changed source file to its docs by
judgment: grep for the file name and read the hits.

The [Open Knowledge Format](https://github.com/GoogleCloudPlatform/open-knowledge-format/blob/main/SPEC.md) (OKF)
v0.2 is a folder of markdown files with YAML frontmatter, where each folder's `index.md` lists its files so an agent
can read one line per doc before it opens any. Google publishes no benchmark for it.

Kangentic, the board that runs this repository's tasks, converted its own docs first and measured the result. An
agent answering narrow questions read about the same number of bytes before and after (inside run-to-run noise),
because it already greps and reads a line range. A cheap model routed to the right doc from the index slightly more
often than from a heading outline (92% against 88% in its top three picks), short of the bar Kangentic set in
advance. No test showed a gain that cleared its bar, and none showed harm. Kangentic adopted the format anyway, for
two reasons: an index keeps working as the docs grow past what grep handles well, and each doc's `sources:` lets a
code change name the docs to re-check.

## Decision

Adopt OKF v0.2 for `docs/`, for the same two reasons. **We expect no reading benefit at today's size**: ScreenHire's
docs are about 5,900 lines in 37 docs, much smaller than Kangentic's, where the measurement found none.

1. Every markdown file under `docs/` except `index.md` and `log.md` is a concept with frontmatter: `type` (Guide,
   Reference, Design, Decision, Audit or Research), `title` (the concept's H1), a one-line `description`, optional
   `tags`, and `sources:`, the files the concept describes.
2. `docs/index.md` is the root. Its frontmatter is only `okf_version: "0.2"`. It and each folder's `index.md` list
   every concept as `* [Title](file.md) - description` and link each subfolder's index.
3. A description is a routing key. It names what only that concept holds: the flags, files, failure modes and
   decisions someone would ask about. No two descriptions match.
4. `sources:` stay narrow: the files a concept describes, never a folder, so a change to one file flags only the
   concepts that describe it. `node scripts/docs-bundle.mjs sources <path>` prints the concepts that list a file, and
   `/sync-docs` starts from it.
5. No file was split. A split pays only where questions split, and the largest file here is about 690 lines. The
   architecture docs, the design system and CLAUDE.md's non-negotiables are cited by section number (§10.9, S1), so
   they keep their numbering, and the design system's review checklist stays whole for the `design-review` agent.
6. `docs/README.md` became the start-here guide. The MVP status moved to `docs/status.md`, and the decision log is
   `docs/adr/index.md`.
7. `docs/log.md` records changes to the layout, newest first.

## Consequences

- `DocsBundleTests` in `pnpm verify` holds the layout: frontmatter and types, unique descriptions, an index per folder
  with exact entries, `okf_version` on the root only, every relative link and heading fragment, no file named like an
  agent instruction file (CLAUDE.md, AGENTS.md and the like), log dates newest first, and every `docs/...` path cited
  from CLAUDE.md, `README.md`, `.claude/` and code. Each check also runs against a fixture with one defect, so a check
  that cannot fail fails the build.
- A new concept costs a frontmatter block and an index line. A changed description has to change in the index too;
  the test says which line.
- Whether the format earns its keep is a question for later, when the docs are several times their size. Kangentic's
  scripts for measuring it (`eval-docs-reads.mjs`, `measure-docs-structure.mjs`) are not copied here.
