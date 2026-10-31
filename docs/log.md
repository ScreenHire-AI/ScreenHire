# Log

## 2026-10-31

**Rich text for job descriptions and email templates** ([rich text](architecture/rich-text.md)). New job, Edit
details, the email template editor and the composer have a small editor with a toolbar (bold, italic, a heading and a
subheading, bulleted and numbered lists, links) in place of the plain-text hints. Both are stored as sanitized HTML
limited to those elements, which the server writes in one canonical form, so a save that changes nothing records
nothing. Email bodies became HTML with Liquid in their text: values are encoded, the result is sanitized again, and the
plain-text part prints lists and link addresses. In an email, each variable is a chip. A migration converted every
plain-text body. The design system gains a Rich text editor row, and its shortcuts join the `?` dialog; typed
formatting stops while single-key shortcuts are off.

## 2026-10-30

**The owner reviewed the v0.3 build** ([ADR-0017](adr/0017-design-system-v0-3-owner-review.md)) and the dashboard
changed with it. The candidate page has no details rail: who they are and where they stand is one meta line in the
header, with the authenticity level as their status beside the name, and Overview is a grid of panels. The feature
people see is Verification, not Integrity: the tab, the Settings page (`/settings/verification`, the old addresses
redirect), the filter, the workflow trigger and facts, the role permission and the guardrail's message; code, API
routes, events and tables keep "integrity" for now. Each assessment leads with its next step in a callout, groups its
reasons under the check that found them and puts when it was assessed last. My tasks links each task to its candidate,
where it is ticked off. Candidates filter by several jobs and stages at once. Settings gains an MCP server page with the
values any client needs, and every settings header is one row, so nothing jumps between them. The Live indicator moved
to the top bar. Scorecard templates, email templates and questionnaires show the editor and its preview side by side
where both fit. The job form picks a currency by name and a closing date in the person's own time zone.

New rules in `design/design-system.md`: S8 (moving between sibling pages never shifts the frame), A9 (expandable rows
have a chevron), F6 (a field's actions share its message's row), W7 (say it once per screen), W8 (never ask people to
type a code), W9 (a label only where the value cannot explain itself) and W10 (what to do first, audit details last); A8
now lets an editor and its preview share the screen, and L1 says an empty cell is blank. The roadmap moves the MCP
server to Done and adds rich text, Connect to Slack and teammates' changes as toasts.

## 2026-10-25

**The dashboard is built to v0.3.** The last screens landed (Tasks with Mine, For the team and Everyone's, each ticked
done with Undo for 10 seconds, which reopens the task on the server; saved views with short names), and the
`design-review` agent ran over the whole dashboard, code and screenshots at 390, 1024, 1440 and 1920px. Its findings
were fixed: workflow stages and automation actions reorder with a grip (A7), the candidate tables fill the window
(S6), the webhook delivery log sorts (L2), and the Organization, Branding and Integrity settings pages have Last
updated and History (T6). Four findings were rules the rest of the system contradicts; ADR-0016 settles them in the
design system: a page's action that opens another page is a button rendered as a link (A5), a webhook endpoint has no
history because its row holds a secret (T6), a form keeps its actions at its end on a phone, and a table wider than
its panel beside the full sidebar scrolls inside it (section 8). Section 21 of the design system, the components the
build still added, is gone: Task checkbox and Choice card joined section 11, and the screens section is now 21. A
second review pass added Expandable row to section 11 (an email on Messages), put a webhook endpoint's events on the
details rail, and made the Settings menu list only the pages a role may use. The roadmap records the workflow as
done, and CLAUDE.md now says long, complex tasks go through Planning with automatic approval.

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
