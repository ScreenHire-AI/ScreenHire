# Log

## 2026-10-25

**Design system v0.3** ([ADR-0013](adr/0013-design-system-v0-3.md)), approved on the owner's "ScreenHire design v0.3
review" canvas. `design/design-system.md` keeps its section numbers and checklist ids: v0.2 checks were amended in
place and new ones added (S7, A6 to A8, W6, T6, E2, E3, L1, L2), so the `design-review` agent enforces v0.3 from its
next run. New section 21 lists the components the v0.3 build adds, outside section 11, which the style guide's test
reads; section 22 lists the screens it changes. CLAUDE.md, `svelte-dashboard.md`, the `add-dashboard-page` and
`task-review` skills and the `design-review` agent now say a status is an icon, its colour and its label.

## 2026-10-23

**Restructure** of `docs/` into an Open Knowledge Format v0.2 bundle (ADR-0012). Every doc is now a concept with
frontmatter: 37 concepts under 7 indexes, rooted at `index.md`, which alone carries `okf_version: "0.2"`. Each
concept's H1 is its title, so nine H1s lost an em-dash. Each concept's `description` is the line its folder index
shows, and `sources:` names the code it describes, so `node scripts/docs-bundle.mjs sources <file>` finds the docs a
change touches. `DocsBundleTests` checks all of it in `pnpm verify`.

No file was split or moved, so every `docs/...` path cited from code, skills, rules and migrations still holds. The
largest doc is about 690 lines, and the architecture docs and the design system are cited by section number.
`README.md` became the start-here guide by audience. Its MVP status (built, faked locally, owner actions, known gaps)
moved to `status.md`, and its decision log became `adr/index.md`. The roadmap and the status were brought up to date
with the "Platform: team, API access and webhooks" and "Team, collaboration and communication" workflows.
