# Log

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
