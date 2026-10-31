---
type: Reference
title: "ScreenHire design system"
description: "The one source of truth for every screen: principles, the review checklist the design-review agent enforces by ID, colour and status icons, type, layers, page templates, screen sizes, components, words, dates, icons, shortcuts, integrity UI, employer branding, and what v0.3 changed on each screen."
tags: [design-system, ui, checklist]
sources:
  - resource: packages/ui/src/tokens.css
  - resource: packages/ui/src/theme.css
  - resource: apps/dashboard/src/lib/status.ts
  - resource: apps/marketing/src/components/StatusIcon.astro
  - resource: apps/dashboard/src/routes/(dev)/style-guide/style-guide.svelte
  - resource: apps/dashboard/src/lib/table-sort.svelte.ts
  - resource: apps/dashboard/src/lib/preview-window.svelte.ts
  - resource: apps/dashboard/src/lib/form-builder.ts
  - resource: apps/dashboard/src/lib/form-sections.ts
  - resource: apps/dashboard/src/lib/templates.ts
---
# ScreenHire design system

> Status: **v0.3 (2026-10-25)**. Decisions: [ADR-0009](../adr/0009-design-system-warm-grid.md), amended by
> [ADR-0010](../adr/0010-design-system-v0-2.md), [ADR-0013](../adr/0013-design-system-v0-3.md) and
> [ADR-0016](../adr/0016-design-system-v0-3-review-amendments.md). Visual reference: the
> owner's "ScreenHire design v0.3 review" canvas; where it and this file differ, this file wins. The dashboard is
> built to v0.3: every screen, and every component in section 11 with its example in the style guide. Section 21
> records what v0.3 changed on each screen.

This file is the one source of truth for how ScreenHire looks, reads and behaves on screen. CLAUDE.md, the rules in
`.claude/rules/`, the page skills and the `design-review` agent point here and do not restate it. To change the
design, change this file (and its ADR when a decision changes); everything else follows.

**Scope.** Everything here applies to the recruiter dashboard. Candidate and marketing pages follow the principles
(section 1), the review checklist where it applies, colour (section 3.4), words (section 12) and employer branding
(section 18), and keep their own type scale (section 4.3) and the public-page rule (`.claude/rules/astro-public.md`).

## 1. Principles

1. **Keep it simple.** The fewest words, elements and styles that do the job. If the screen already says it, cut it.
2. **Scan first.** One glance answers "what needs me?". Calm states stay quiet.
3. **Readable by default.** Body 14px, nothing under 12px. Sentence case. Monospace only for codes and keys.
4. **Links go somewhere, buttons do something.** A link is underlined text that takes you to a page, tab or record. A
   button has a border or a fill and acts, or opens a sheet, menu or dialog. Badges and pills are never clickable.
5. **Status first, then facts.** The signal people scan for comes first, as a badge. Facts follow in a labelled facts
   row, or as plain text in a table.
6. **Clear layers.** Every section is a panel with its title inside, lifted off the page by a soft shadow. Panels in a
   row align.
7. **Same pattern, same place.** One page template per job and one component catalogue. New patterns join the
   catalogue first.
8. **Fluid at every size.** Pages fill the window; only text keeps a measure. Flex and grid, never fixed widths, from
   phone to wide desktop. Controls never wrap.
9. **Evidence, not verdicts** (ADR-0004). A level, its reasons and a next step. Never a score or a reject button.
10. **Works out of the box, customizable when needed.** Every area ships a default that needs no setup: templates,
    email, branding and roles. Customizing is opt-in, reversible and shown next to the default.
11. **Every fact has one home.** Mark the exception, never the default. Design for the count, not the example: a list
    that can grow shows how many.

## 2. Review checklist

Authors check their screens against this list, and the `design-review` agent reports findings by these ids.

**Structure**

- **S1** The page uses a template from section 7 (list, detail, read-only, form, builder).
- **S2** One header band: title, its status badges beside it, the primary action on the right, then the facts row or
  tabs. A description is one plain sentence. On anything people edit, Last updated is the last fact, with History
  beside it (T6).
- **S3** Every section is a panel with its title inside a fixed-height header. Nothing readable sits on the page
  background.
- **S4** Panels in a row share a top edge. No card inside a card.
- **S5** Insets only inside a surface. Panels and cards take the soft surface shadow; raised and overlay layers take
  theirs (section 6). Nothing else casts a shadow.
- **S6** Pages fill the window: a page's content container has no max-width, and tables and boards never stop short.
  Text keeps a measure: a long paragraph stops at 72 characters and a form at 688px, while short sentences, callouts
  and notices use the full width. Content elements get a max-width, never a fixed width.
- **S7** No side column beside a table or a board: secondary things move to a tab, a sheet or the header. A detail page
  puts the record's facts in its header band, above tabs in a fixed order that hold its content; nothing sits at the
  window's far edge, where a wide screen puts it out of sight (section 7). The one exception is the Settings menu, the
  200px column left of a settings page's content (section 11).
- **S8** Moving between sibling pages never shifts the frame. Pages in one set (the settings pages, a record's tabs)
  keep one header height, so the menu, tabs and content start at the same place on each: a header's title row is at
  least a button's height, and a fact like Updated and History sits on that row, never on a row only some pages have.
  A sentence about the page goes in its panel's header, not under the title.

**Actions**

- **A1** At most one primary (filled) button per page, sheet or dialog.
- **A2** Destructive actions live in a menu and confirm in a dialog. Section 10 says what is not destructive: an action
  the same screen can undo, and removing a row from an unsaved builder draft.
- **A3** A feature that does not exist yet is hidden, not shown disabled. Disabled is only for a control this screen
  can enable (Save until the form is valid), with the reason beside it.
- **A4** Icon-only buttons have an `aria-label` and a tooltip.
- **A5** Links go somewhere: another page, tab or record, as underlined text; navigation and a table row's name
  underline on hover. Buttons do something: act, or open a sheet, a menu or a dialog. A link never opens a sheet, and a
  button never navigates, with one exception: a page's own action that opens another page to do it (New job, Edit,
  Cancel on a form, Back) is an outline or primary button that renders as a link (`<a>`), because it starts that
  action. A menu item that opens a page is a link inside the menu. Badges, pills and counts are never clickable,
  except a count button in a header (T4).
- **A6** A button with a text label has a border or a fill, never bare coloured text, and its label starts with a verb
  (Mark done, not Complete). An icon button (More, Close) may be bare, with its label in a tooltip (A4). A sortable
  column header is the one bare text button, marked by its sort icon (L2). A toggle in a labelled group is a choice,
  not an action: its label names the choice, the pressed one takes `accent` and the others stay bare (section 11). A
  button never looks like a badge.
- **A7** Reorder by dragging a grip. Clicking the grip opens Move up, Move down, Move to top and Move to bottom, and
  Space with the arrow keys moves it. No up and down arrow buttons.
- **A8** An editor whose result someone else sees (a questionnaire, a scorecard, an email template, a careers page)
  shows the result beside the editor where both fit at their real sizes (the 688px editor and the result, about
  1,312px of content, so from roughly a 1,600px window), the preview staying in view while the editor scrolls.
  Narrower, an Edit / Preview switch at the top, with a pencil and an eye, shows one at a time. Preview shows the
  result at its real size and adds no sentence about itself; a window icon pops it out, beside the switch or in the
  preview's header, and the window follows the edits. Container queries choose the layout, so the first paint is
  already right (S8). The email composer keeps the switch and has no window: it is one email in a sheet, not wording
  others reuse.
- **A9** Anything that opens in place says so: a `chevron-down` at the end of the row, turning up when it is open, and
  the whole row is the control, with `aria-expanded`. A row with no chevron never expands. Expand all and Collapse all
  sit above a list of them.

**Words and type**

- **W1** Text sizes come from the type tokens (section 4). Nothing under 12px. No pixel font sizes in markup.
- **W2** Sentence case. No all-caps text except acronyms. Monospace only for codes, ids and key caps.
- **W3** No dot separators or strings of metadata. A record's header puts its facts in a meta line, each with its
  icon and its own gap (`MetaItem`), or in a labelled facts row where a value needs its label (section 7); a table's
  sit in their own columns (L1).
- **W4** Labels, not instructions: a control's label is one to three words. A description gets one line, or none if
  the title says it, and no sentence repeats what the screen already shows (W7).
- **W5** Copy follows section 12 and `.claude/rules/writing-style.md`.
- **W6** Every fact has one home. Before a label goes beside a name, in a table, header, card or list, ask in order.
  Is it the default? Show nothing, and mark only the exception (Built in, never Custom). Does the screen already show
  it? Leave it there (an edited built-in shows in Last updated, by a person instead of ScreenHire). Is it a state? It
  goes in the column or fact it describes, as a badge (a draft beside the version). Is it something to do? It goes in
  the menu (Reset to original). Is it detail? A tooltip or the record's page. A label people have to ask about is the
  wrong label or in the wrong place.

**Status, counts and time**

- **W7** Say it once per screen. The same piece of information never shows twice on one screen: a status in the header
  and again in a panel, a tab's count and a heading's count. Keep it where people look first and let the other go,
  never repeat it for emphasis. The one exception is a builder's validation message, which shows in "Problems to
  fix" and again on the control or graph node it is about, because the panel says what is wrong and the control
  says where.
- **W8** Never ask people to type a code. Currencies, countries and languages are picked from a list by their names
  (British pound (£), Ireland); the ISO code is only the stored value. Times are entered and shown in the person's own
  time zone, and a label never names one (Closing date, not Closing date, UTC).
- **W9** A label only where the value cannot explain itself. A sentence never gets one ("Why", "Suggested next step");
  a date says what happened ("Applied 2 hours ago via the careers page"); a job title next to a briefcase is a job.
  Labels stay where the value alone means nothing: evidence ("Network country"), settings values, bare numbers. A
  label that goes stays for screen readers (an `sr-only` term).
- **W10** What to do comes first; audit details come last. When something was assessed, under which ruleset, a code
  or an id goes on a panel's last line in caption size, never above the next step. A calm result is shorter than a
  concern: one quiet line, not the full layout.
- **T1** Status is a badge: its icon, colour and label, always all three (section 3.2). The icon alone tells statuses
  apart. Status colours mean status only.
- **T2** Status comes first in its group: beside the title, or first in the row.
- **T3** In lists and tables, calm states are quiet (icon and muted text). Only states that need attention get a
  filled badge. Waiting states (Sent, Queued, Scanning) are grey: nobody needs to act.
- **T4** Design for the count, not the example: wherever a list can grow, show how many (2 hiring templates). In a
  header the count is a button that opens a sheet listing each item as a link, with search past 10. On a control (a
  tab, a filter chip, a button, a sidebar item) the count sits in the grey count badge after its label; in a table it
  is plain text. Steps are square markers on a line. Counts and steps never share a shape.
- **T5** Times follow section 13: time ago in text, dates in tables, the browser's locale, the exact time on hover.
- **T6** Anything people edit says who updated it last and how long ago: the last fact in its header ("2 hours ago by
  Avery Quinn") with History beside it, and a Last updated column in its list. ScreenHire's own templates and roles
  say "by ScreenHire". History opens a sheet with every change, newest first and grouped by day: who, when and what,
  with before and after on request. The one exception is a record whose row holds a secret (a webhook endpoint):
  change history never reads it, so its header says when it was added and when its secret was last rotated instead
  (ADR-0016).

**Icons and shortcuts**

- **I1** Lucide only, at the sizes in section 14. Navigation and main buttons have an icon and a label.
- **I2** Status icons appear only in status badges, never as decoration or for ordinary views (section 14).
- **K1** Shortcut hints appear in tooltips, menus, the `?` dialog and Ctrl K, as `Kbd` key caps. No always-on hint
  bar. Single-key shortcuts can be turned off.

**Sizes and states**

- **F1** The screen works at 360, 768, 1024, 1440 and 1920px. The page never scrolls sideways; wide tables scroll
  inside their panel.
- **F2** Layout wraps by content (flex-wrap with a basis, grid `auto-fit`). Components respond to their container
  (`@container`); only page layout uses breakpoints, and only Tailwind's.
- **F3** Navigation and actions follow section 8 at each size.
- **F4** Touch (`pointer: coarse`) gets 44px targets and 16px inputs. Anything shown on hover also opens on tap or
  focus.
- **F5** Controls never wrap. Long text truncates (`min-w-0`, ellipsis) with the full value in a tooltip.
- **F6** A field's message sits under the field, and the actions that go with the field (Save note, Add tag) share
  that row on the right, rather than starting a row of their own.
- **E1** Loading shows skeletons of the same shape. Empty states say why and offer the one action. Errors use
  `QueryError`.
- **E2** A problem says what is wrong and where to fix it, in words, in a notice on the page where people will see it.
  A tab shows its state in its count where one fits (Distribution 7/8), always in the same grey count badge. A state
  that needs a look is the record's status badge beside its title (T2), never a coloured tab, a dot or a lone icon (W7).
- **E3** Read-only says why and what to do. Editable shows its controls.

**Tables**

- **L1** Two text styles. The row's name is semibold on one line, with at most one badge after it (Built in, You).
  Every other cell is one line of body text, cut with an ellipsis and shown in full in a tooltip, so rows keep one
  height. No code font, pills or icons in cells except status badges and a person's avatar. A template variable reads
  as a muted word (job title). The row opens the record. A cell with nothing in it is blank, its word ("No draft")
  for screen readers only, never a filler word repeated down the column; a value that means something, such as
  Never used, keeps its words.
- **L2** Every table sorts. Each column header is a button: its text and a faint up-down icon, which becomes an arrow
  on the sorted column, and a second click reverses it. A table opens on one default sort: things people edit by Last
  updated, newest first; people, keys and endpoints by name; a job's candidates with those needing attention first. A
  table with a natural order (roles, by access) keeps it until someone sorts. The dashboard remembers each person's
  sort. On a phone a Sort chip beside the filters offers the same choices. A fixed grid, such as the permissions
  table, does not sort.

**Integrity and access** (the `integrity-guardrails` agent owns the first)

- **G1** No score or percentage, no reject control, AI assistance styled as information, and every result keeps its
  next step (section 16). The candidate's scorecard rating never draws on integrity output, AI signals or the stage
  (section 11, Rating).
- **X1** WCAG 2.2 AA: visible focus ring, labels on inputs, landmarks and headings, text contrast at least 4.5:1, and
  a non-drag alternative for every drag (section 17).

## 3. Colour

Semantic names are the API; raw values live only in `packages/ui` (`tokens.css`, mapped in `theme.css`) and reach
components as Tailwind classes. The token column is the name in code: `background` is `bg-background`,
`muted-foreground` is `text-muted-foreground`. Colours did not change in v0.2 or v0.3.

### 3.1 Recruiter app, light (default)

| Token | Value | Use |
|---|---|---|
| `background` | `#F7F5F0` | Page (paper) |
| `sidebar` | `#FBFAF6` | Sidebar |
| `card` | `#FFFFFF` | Header band, panels, tables |
| `muted` | `#FBFAF7` | Panel footers, table header rows |
| `secondary` | `#F4F1EB` | Insets, muted pills, counts |
| `border` | `#E6E1D8` | Surface edges |
| `border-subtle` | `#EFEBE4` | Rows and inner rules |
| `input` | `#DDD7CC` | Buttons, inputs, pill edges |
| `foreground` | `#1C1B19` | Primary text |
| `sidebar-foreground` | `#3A3630` | Secondary text, navigation (a general token for it is still to add) |
| `muted-foreground` | `#5F5A52` | Labels, captions (5.3:1 on `background`) |
| `primary` | `#4338CA` | The primary button, links, focus ring (`ring`) |
| `accent` | `#EEEBFB` | The selected item only (active navigation) |
| `accent-foreground` | `#2B2788` | Text on `accent` |
| `row-selected` | `#F3F1FC` | Selected table row |
| `destructive` | `#9A2F17` | Destructive menu items and their confirmations |
| `scrim` | `#1C1B19` | The dimming behind dialogs and sheets, at 10% (`bg-scrim/10`) |

### 3.2 Status (icon, colour and label, always)

Each status has its own Lucide icon. The icon alone tells statuses apart, so colour is never the only signal.

| Colour | Statuses and their icons | Tokens (`status-*`, `-soft`, `-mark`) | Text | Soft bg | Icon colour |
|---|---|---|---|---|---|
| Clear | No concerns, No problems found, Delivering, Done; Delivered, a webhook delivery; Active, an API key; Live, the live updates connection (`circle-check`). Open, a job taking applications; Live, a job showing on a board (`door-open`) | `status-clear` | `#0B5D4F` | `#E3F1EC` | `#0F766E` |
| Review | Review suggested (`eye`); Overdue by 2 days (`clock-alert`) | `status-review` | `#7A4205` | `#FBEFD3` | `#C77700` |
| Verify | Verification suggested (`id-card`). Needs fixes, for a refused posting or a workflow with problems, and for a posting a board did not answer (Error), a webhook Turned off after failures and a delivery that Failed (`wrench`). Unsafe file, flagged by the malware scan (`shield-x`) | `status-verify` | `#9A2F17` | `#F9E0D6` | `#B4461F` |
| AI (information) | No signs, Some signs or Strong signs of AI help (`sparkles`) | `status-ai` | `#2B2788` | `#ECEBFA` | `#2B2788` |
| Grey: waiting or not started | Sent, Queued, Submitted, Removing and Removal requested, waiting on a board; Pending, a webhook delivery waiting for its next try (`clock-4`). Scanning and Checks pending, ScreenHire at work; Connecting, the live updates connection (`loader-circle`). Closed (`door-closed`). Draft (`file-pen-line`). Archived, a template out of the pickers until someone restores it (`archive`). Not in effect: Not posted, a board the job is not on; Removed, a board that took the job down; Off, a webhook someone turned off; Skipped, a delivery not sent; Revoked, an API key; Offline, the live updates connection (`circle-minus`) | `muted-foreground` on `secondary` | `#5F5A52` | `#F4F1EB` | `#5F5A52` |

Status colours are never used for brand, decoration or facts. No status uses the warning triangle: it marks only the
integrity cards that fed a level (section 16). Counts use tabular figures.

In code, every status is a name in `statuses` (`#lib/status.ts`) with its icon and colour, and `StatusBadge` draws it.
The icon colour is the `status-*-mark` token (`status-ai-mark` for AI), and grey statuses use `muted-foreground`. In
lists and tables only the review and verify statuses get a filled badge (T3). AI assistance stays quiet in a list at
every level, because it is information.

**Overdue** is the hiring team's status, never the candidate's. An application is overdue once it has been in its stage
longer than the stage's time limit (`slaDays`), which measures how long the team has kept the candidate waiting. It
takes the review colour and the `clock-alert` icon because it asks a person to act. It never takes the verify colour:
that sits beside integrity results and would read as a concern about the candidate. The label says by how much, in the
largest whole unit: "Overdue by 2 days", "Overdue by 5 hours". The due date and time are in its tooltip (section 13). It
is a filled badge wherever it shows (T3), because it needs attention. An application within its time limit shows
nothing. A job board's estimate that has run out, the muted "Overdue" pill in a job's Distribution panel, is a fact
about the board and not this status.

### 3.3 Recruiter app, dark

| Token | Value | Token | Value |
|---|---|---|---|
| `background` | `#141311` | `foreground` | `#EDEAE4` |
| `sidebar` | `#181714` | `sidebar-foreground` | `#CFCAC1` |
| `card` | `#1C1B18` | `muted-foreground` | `#A39E95` |
| `muted` | `#181714` | `primary` | `#9B9CF8` (text on it `#15132E`) |
| `row-selected` | `#2A2722` | `accent` | `#26264A`, text `#C7C8FA` |
| `border` | `#2E2C27` | `border-subtle` | `#262420` |
| `input` | `#3A3731` | `destructive` | `#F7957A` |
| `scrim` | `#000000` | | |

Dark status: no concerns `#4FD1B4` on `#15302A`; review `#F6CB76` on `#3A2C10` (icon `#F2B84B`); verify `#F7957A` on
`#3D1A10` (icon `#F27A55`); AI `#C7C8FA` on `#26264A` (icon `#C7C8FA`); grey icons take `muted-foreground`. In dark mode,
higher layers get lighter (section 6), and the surface shadow is darker so it still shows on the dark page.

### 3.4 Candidate and marketing pages

| Token | Light | Dark |
|---|---|---|
| `candidate-background` | `#FBF6EE` | `#1A1714` |
| `candidate-surface` | `#FFFFFF` | `#23201C` |
| `candidate-border` / `candidate-input-border` | `#EDE3D3` / `#D9CCB8` | `#332E28` / `#4A443B` |
| `candidate-foreground` / `candidate-muted-foreground` | `#231F1A` / `#6A5D4D` | `#F2EDE6` / `#B3ABA0` |
| `candidate-chip` | `#F3E7D6` (text `#5A4630`) | `#2E2A25` |
| `candidate-cta` | `#C2462F` (white text, 4.98:1) | `#F08A6E` (text `#1A1310`) |

On tenant careers pages, `candidate-cta` and the header mark take the employer brand tokens (section 18).

## 4. Type

Fonts are self-hosted (`@fontsource-variable/*`, latin and latin-ext): Bricolage Grotesque for titles, Hanken Grotesk
for everything else, JetBrains Mono for codes, ids and key caps. The root font size is the browser's own (16px);
nothing sets a pixel root.

### 4.1 Dashboard scale

| Token | Size / line height | Weight | Use |
|---|---|---|---|
| `page-title` | 28 / 34 (24 / 30 on phones) | Bricolage 700, -0.02em | The page's `h1` |
| `panel-title` | 20 / 28 | Bricolage 650 | A record's name inside a panel or sheet |
| `metric` | 32 / 38 | Hanken 600 | A metric card's value, in the UI face (section 11) |
| `heading` | 16 / 24 | Hanken 600 | Panel and section titles |
| `body` | 14 / 20 | Hanken 400, 600 for emphasis | Everything else |
| `reading` | 16 / 26 | Hanken 400 | Resumes, notes, integrity explanations |
| `caption` | 12 / 16 | Hanken 500 or 600 | Fact labels, pills, badges, table headers |
| `code` | 12 / 16 | JetBrains Mono 400 | Ids, codes, key caps |

12px is the floor. Text never shrinks on phones; only `page-title` steps down, below `md`. The tokens are Tailwind
`--text-*` tokens in `packages/ui` (`text-page-title`, `text-body`, `text-caption`, ...), each with its line height,
weight and tracking. The family is its own class: `font-display` for the two titles, `font-mono` for `code`.

### 4.2 Rules

- Sentence case everywhere, headings and buttons included. No all-caps text except acronyms.
- One `h1` per page. Panel titles are `h2`.

### 4.3 Candidate and marketing pages

Base 15px, display 30px, inputs 16px (no zoom on iOS), hints under a field 13px: `text-candidate-body`,
`text-candidate-input` and `text-candidate-hint` in `packages/ui`. A form of several pages shows `PageProgress`
(`packages/ui/src/questions`) above its fields: a bar of one segment per step, then "Step 2 of 4" and "9 of 24
answered" in the hint size, the step semibold; each page's heading is its section's name. Marketing display goes up to 64px. The dashboard's
questionnaire preview shows the candidate base with `text-candidate-body`.

## 5. Space, shape and size

| Thing | Value |
|---|---|
| Spacing | 4px grid: 4, 8, 12, 16, 20, 24, 32, 40, 48 |
| Radius | 6 (key caps), 8 (buttons, inputs), 12 (panels), full (badges, pills, counts, avatars) |
| Controls | 36px high with a mouse; 44px on touch; 32px inside a panel header |
| Table rows | 48px default, 40px compact (a person's setting) |
| Panel header | 56px (52px on phones), 16 to 18px side padding |
| Sidebar | 248px; icon rail 64px |
| Top bar | 56px |
| Content widths | Pages fill the window: a page's content container has no max-width. Long paragraphs stop at 72ch (`max-w-measure`) and forms at 688px (`max-w-form`). If a layout needs a limit, the element gets a max-width, never the page |
| Board columns | 296px; an empty stage shrinks to 188px, and the stages that end a candidacy (Hired, Rejected, Withdrawn) share one 188px Closed column that counts each |

## 6. Layers

Drawn from Material 3's elevation ("the fewer levels in your UI, the more power they have"), IBM Carbon's layering and
Atlassian's surfaces.

| Layer | What lives there | Looks like |
|---|---|---|
| Page | Layout only. Nothing readable sits on it. | `background` |
| Surface | Header band, panels, tables, sidebar, top bar | `card`, 1px `border`; panels and cards add the soft surface shadow `shadow-surface`: `0 1px 2px rgb(28 27 25 / .06), 0 1px 3px rgb(28 27 25 / .04)`, and in dark mode `0 1px 2px rgb(0 0 0 / .5), 0 1px 3px rgb(0 0 0 / .3)` |
| Inset | A well inside a surface: a summary band, a list of reasons, a board column | `secondary`, no edge |
| Raised | The one thing in hand: a dragged card, a toolbar stuck while scrolling | surface, edge, `0 4px 12px rgb(28 27 25 / .14)` |
| Overlay | Menus, popovers, tooltips, sheets, dialogs, toasts | surface, edge, `0 16px 48px rgb(28 27 25 / .18)`; dialogs dim the page |

- Every section is a panel with its title inside the panel header. Panels in a row share a top edge.
- Shadows: the soft surface shadow on panels and cards (a board card, a workflow graph node, the graph's controls),
  then raised and overlay. Nothing else. No card inside a card.
- Inside an inset, data and controls sit on `card` with a `border-subtle` edge (a quoted answer, a list of fact pills,
  a field), so they read as values in the well, not as another layer.
- A summary band is an inset across the top of a panel, full width with no edge of its own. Summary cards, the next
  step and notices sit white on it, as the inset's values. Weight comes from the layer: white on warm grey stands out,
  white on white does not.

## 7. Page templates

| Template | For | Anatomy |
|---|---|---|
| List | Candidates, jobs, templates, My tasks; the team, roles, API keys and webhooks in Settings | Header band (title, count, page actions); one full-width table panel whose header holds its title, search and filters, with no side column (S7). A list with no search (My tasks) puts its toggles in a row under the panel header |
| Detail | A candidate, a job, a webhook endpoint | Header band (identity, status badges, a contact line for a person, actions, the facts row); tabs in a fixed order under it; the content in panels below, in a grid of two columns on a wide page and one on a narrow one (S7) |
| Read-only view | A ScreenHire template, anything you cannot edit | As detail; data as labels and values, never disabled inputs; it says why it is read only and what to do (E3); Copy or Edit as the primary action |
| Form | Creating or editing a record; a settings page such as Organization, Branding or Integrity | One column, 688px wide, labels above fields, actions at the end |
| Builder | The workflow graph; the questionnaire, scorecard and email template editors | Header band, then a full-width work area, with the Edit / Preview switch when someone else sees the result (A8). The graph's nodes edit through the same sheets and forms as the list view beside it, never a second editor, and nothing in it needs dragging: a node's buttons do every edit. A validation message shows on the node it is about, as it does under its form control (W7) |

Sign-in, onboarding and error pages sit outside the app shell. They have no sidebar: the brand bar takes the top
bar's place, then the form template. A title that changes in place, such as "Check your email" after a link is sent,
takes the focus.

- **Header band:** a white strip under the top bar. Title first, status badges beside it, actions on the right, then
  a labelled facts row (a short label 12px above each value 14px, a divider between them) or tabs on its bottom edge.
  On anything people edit, Last updated is the last fact, with History (T6). A description under the title is one
  plain sentence. An explanation goes where it applies: a tooltip on the fact, a hint by the control, the reason on a
  disabled item, or a notice for the people it affects, never a paragraph of loosely joined facts. One muted line
  under the title may hold a person's contact details, each an icon and its value, with no labels. On a form page the
  band's contents line up with the 688px column.
- **Panel:** header (title, optional count, actions on the right), body, optional footer inset.
- **Columns:** a detail page's panels sit in a grid that uses the width: two columns, gap 20px, once the content is
  1024px wide (`@5xl`), with the work (next steps, activity) on the left and the record's things (files, tags, answers)
  on the right; one column below that, in the same order. The record's facts are in the header, so every tab shows them
  and none sits at the window's far edge.

## 8. Screen sizes

Four size classes on Tailwind's breakpoints. No custom breakpoints.

| | Phone | Tablet | Laptop | Desktop |
|---|---|---|---|---|
| Window | under 768px | 768 to 1023px (`md`) | 1024 to 1535px (`lg`, `xl`) | 1536px and up (`2xl`) |
| Navigation | Sidebar as a sheet behind a menu button; Back on detail pages | Icon rail, labels in tooltips | Full sidebar | Full sidebar |
| Columns | One | One | A detail page's panels in two columns from 1024px of content | Same, filling the window |
| Header band | Facts in a two-column grid | Facts wrap | One line of facts where they fit; beside the full sidebar they may wrap | One line of facts |
| Actions | Primary in a bar at the bottom; the rest in More. A form keeps Cancel and its primary at its end (section 7) | In the header; extras fold into More | In the header | In the header |
| Tables | Rows become two-line list items, with a Sort chip beside the filters (L2) | Scroll inside the panel; first column sticks, and while it scrolls the row's link covers that cell only | Full; a table wider than its panel beside the full sidebar scrolls inside it, as on a tablet | Full, optional columns may show |

The sidebar is shadcn-svelte's `Sidebar`: `collapsible="icon"` gives the rail and its mobile mode gives the sheet.
On a phone the workflow graph is for reading: its nodes have no buttons, one line says to edit in Pipeline, and the
Pipeline view does the editing.
Checked at 360, 768, 1024, 1440 and 1920px.

## 9. Flow

| Move | How |
|---|---|
| Across areas | The sidebar. It is the only app navigation. Switching areas fades. |
| Forward | From a list to a record. The whole row is the link. Filters and scroll position survive the trip back. |
| Back | The breadcrumb goes up a level. Browser Back goes where you came from. |
| In place | A sheet for quick work: add a note, quick edit, filters, History, the items behind a count, Invite, an email. The record stays behind it. |
| Preview | An editor's Edit / Preview switch swaps the view in place; the window icon opens it in a second window that follows the edits (A8). |
| Files | A file opens in a full-window viewer from its file row. A page never embeds a preview. |
| Confirm | A dialog, only to confirm or decide. A menu is the gentler choice. |
| Create | A full page for anything with several steps, such as a new job. |
| Tabs | Peer views of one record, under its header. Never steps in a sequence. |

Forward and back slide (200ms); everything honours `prefers-reduced-motion`.

## 10. Actions

| Emphasis | Looks like | Use |
|---|---|---|
| Primary | Filled `primary` | One per page, sheet or dialog. It moves the work forward. |
| Secondary | White, `input` edge | Supporting actions, and Cancel |
| Quiet | Icon button | Row actions, usually the More menu. Every icon button has a tooltip and an `aria-label`. |
| Destructive | `destructive` item in a menu | Never the page's main action. Confirmed in a dialog. An action the same screen can undo (taking a job down from one board, which Post puts back) is not destructive, and neither is removing a row from an unsaved builder draft (a stage, a question, a condition): nothing changes until Save, and leaving asks first. |

Features that do not exist yet are hidden (A3). Links go somewhere and buttons do something (A5, A6).

## 11. Components

The catalogue. A screen is built from these; a new pattern joins this table before a second screen uses it. Code lives
in `apps/dashboard/src/lib/components` (shadcn-svelte under `ui/` is ours to edit). The style guide page, `/style-guide`
in a development build, shows every component in each state, and its test fails when a row here has no example.

| Component | Built on | Rules |
|---|---|---|
| Header band, Panel | Our components (`HeaderBand` with `MetaItem` for a record's meta line or `Fact` for a labelled facts row, `Panel`) | Section 7. A record's header (a candidate) has a meta line: each fact an icon and a value that reads on its own, the label only for screen readers (W9), and the stage a muted pill. Pages whose values need their labels (a job's salary, a template's version) keep the facts row |
| Status badge | `StatusBadge` on shadcn `Badge` (`clear`, `review`, `verify`, `ai`, `neutral`; `quiet` for calm states in lists) | Icon, colour, label (T1, T3): 24px high, the 14px icon in its icon colour, then the label in 12px semibold. It takes a status name from `#lib/status.ts`; each status's icon is in section 3.2 |
| Overdue badge | `OverdueBadge` on `StatusBadge` and `Hint` | Section 3.2: the review badge, "Overdue by" and how long, the due time in a tooltip that opens on hover, focus or tap. First in the stage cell of a candidate table and list item, first in a board card's status row, and beside the title on an application's page (S2, T2). Never on a hired, rejected or withdrawn stage |
| Fact pill | `Badge` muted variant | `secondary` fill, `input` edge, 12px; only on white surfaces. For tags, for facts in a list item or a sheet, and for the stage in a record's meta line and a policy beside an integrity level; never in a labelled facts row or a table cell (W3, L1) |
| Count | `Badge` count variant | The grey count badge (min 28px) after its label on a control (T4); plain text in a table |
| Toggle | shadcn `Toggle`, in a labelled `role="group"` | Filters with a few choices and peer views (Table, Board; the apply form and each follow-up questionnaire in the workflow builder). The pressed one takes `accent`; its count follows the label in the count badge, white on the pressed one (T4). One line that scrolls inside its panel, never wraps (F5). In a narrow container a few short toggles whose every choice matters at a glance (a job's authenticity levels) stack one per line instead |
| More menu | `MoreMenu` on shadcn `DropdownMenu` | An icon button with its label in a tooltip (A4). Holds extra actions, folded ones on phones and destructive items (A2, section 10) |
| Step marker | `StepMarkers` (across), `StepLine` (down) | 28px square, radius 7, white with an edge, joined by a line: across for progress, down for a workflow's stages |
| Summary list | `<dl>` | Labels and values for read-only data; one Edit per section when you can edit |
| Key cap | shadcn `Kbd` | In tooltips, menus and the `?` dialog |
| Tooltip | shadcn `Tooltip` | Labels for icon buttons, full values of truncated text, shortcuts |
| Icon button | `IconButton`: ghost `Button` and `Tooltip` | A quiet row action; its label is both the `aria-label` and the tooltip (A4) |
| Empty state | shadcn `Empty` | One line saying why, and the one action |
| Shortcuts dialog | shadcn `Dialog` | Opens on `?`; lists every shortcut; holds the single-key toggle |
| Sidebar | shadcn `Sidebar` | Section 8. The sections, the saved views, then one Settings item for anyone whose role has a settings permission, which opens the first Settings page they may use; nobody else sees it |
| Brand bar | Our component (`BrandBar`) | The top bar of a page outside the app shell: the logo mark and name only (section 7) |
| Table, list item | TanStack table; our list item | 48px rows; list items on phones. Two text styles and one-line cells (L1); every column sorts through the sort header (L2) |
| Search field | Our component | A list's search, in its table panel's header (section 7); 16px text on touch |
| Activity | `ActivityList`; on a candidate `ActivityPanel` | Icon in a circle, event, who did it, time ago; a note left with it is quoted below. People and API keys show by name ("by Robin Ashby", "by you", "by ATS sync"). A candidate's Activity lists everything newest first, filtered by kind (All, Notes, Emails, Scorecards, Stage and tags) with a count on each, and opens with the note box; Overview shows the last five and links to the rest. A team note shows its latest text through `NoteText`, with "Edited" after it and when in a tooltip; its More menu holds Edit for the author and Delete for the author, an Owner or an Admin, confirmed in a dialog, and anyone else sees no menu. Editing turns the note into its own box in place. A deleted note leaves one line, "Deleted a note" and who, never its text |
| Mention | `MentionField` on shadcn `Textarea`; `NoteForm` for the note box and editing in place; `NoteText` | @ opens the organization's members, without yourself, narrowing as you type: arrow keys move, Enter or Tab picks, Esc closes the list only. The list sits under the field, each member an avatar, name and role; the field points at it with `aria-controls` and `aria-activedescendant`, because a textarea cannot be a combobox. An @ button (Mention someone) starts one below `md`. Ctrl or Cmd Enter saves, hinted in the Save tooltip. A mention is stored as the member's user id and shows their current name ("@former teammate" once they leave). The person mentioned gets a notification card and an email that says who and where and never quotes the note; a mention an edit adds notifies only that person. Save is outline, because the page's primary is Move to stage (A1) |
| Tag list | `TagList` | Each tag a fact pill with its own Remove icon button beside it, never inside it (A5), then a field and Add tag. Removing is not destructive: adding the tag again undoes it (section 10). Without a role that changes hiring data, the pills only |
| Task list | `TaskList` | Icon in a circle, what to do, then its kind and who it is for as fact pills and time ago; a done task says when and by whom. A task is ticked off where its work is done: on the candidate's Next steps, for a role that changes hiring data, an open task has the task checkbox in its icon's place. On My tasks each whole row links to that work, with the candidate's name and the job as a pill, and has no checkbox |
| Task checkbox | shadcn `Checkbox` in `TaskList`; `createTaskCompletion` in `#lib/tasks.svelte.ts` | Ticking marks the task done on the server at once, so leaving the page keeps it done. The row stays where it is for 10 seconds, ticked and struck through, with an outline Undo; Undo, or unticking, opens the task again. Then a done task leaves an open list. Its label names the task. A tick that fails unticks and says why (`FailureText`). Its hit area is 44px on touch (F4) |
| Scorecard | `ScorecardList`; `ScorecardSheet` to fill one; `ScorecardBuilder` for a library scorecard's questions; `ScorecardPicker` in the workflow builder | A submitted scorecard is an icon in a circle, the recommendation in words and who gave it, the stage as a fact pill and time ago, then each question and its answer as a summary list ("Not answered" when left out) and the note quoted. Every scorecard ends with an overall recommendation, in words, and a note. `ScorecardFields` draws what a reviewer fills in, for the sheet and for the library scorecard's Preview, which shows it at the sheet's width for a sample candidate; `ScorecardQuestionList` reads a scorecard's questions as content. Questions are built in the form builder with the answer menu, on a library scorecard (built-in or the team's own); a stage picks one, and a job keeps its own copy of the questions; rating questions answer on one row of joined buttons and make the candidate's rating, the one average anything takes of scorecards (Rating, below). The sheet never shows integrity output. Fill scorecard (outline) is in the panel header for a role that changes hiring data, while a reached stage with a scorecard is left to score. Questions ask about the work and the skills the job needs, never personality, emotion or honesty |
| Notification tray | `NotificationTray` | Overlay cards in the bottom corner (above the action bar on phones), newest first, at most three: an icon for what raised it (`bell` for the team, `at-sign` for a mention), the record as a link, one line and Dismiss. They never time out, because each holds a link (WCAG 2.2.1), and a polite live region announces them |
| Sheet | shadcn `Sheet` (`MoveNoteSheet`, `ScorecardSheet`, the workflow builder's automations and scorecards) | Quick work in place (section 9): the title, one line of description, the form, then Cancel and the one primary action (A1). The record stays behind it. History (T6), the items behind a count (T4), Invite and the email composer are sheets too |
| History sheet | `HistorySheet` on shadcn `Sheet`; `LastUpdated` for the fact; `HistoryFact` puts both in a header that shows nothing else of the history (the settings pages) | T6: Last updated is the last fact of the header, time ago and who ("2 hours ago by Avery Quinn", "By ScreenHire" for an untouched built-in), then an outline History button that opens the sheet. The sheet's title is History and its one line the record's name; changes come newest first under a heading per day (Today, Yesterday, Oct 22). Each change is the person's avatar, their name starting the first line, one line per thing that changed ("changed **.NET and C#** from 1 to 5 to 1 to 10"), and time ago. Show the change is an outline button (A6) that reveals before, struck through, and after for each value, and "Unchanged" for the context. Show older changes loads the next page. A ScreenHire template has no History button, because nobody can change it |
| Action bar | Our component (`ActionBar`) | A phone's primary action in a bar fixed to the bottom, over the safe area (section 8) |
| Relative time, truncated text | Our components on `Hint` | Time ago, or text cut with an ellipsis (on one line, or on two as a workflow graph node's title); the exact time or full value in a tooltip that opens on hover, focus or tap (T5, F4, F5). A truncated link is one tab stop, with an optional icon before its text |
| Table date | `DateText` on `Hint` | A date in a table or list item, the exact time in its tooltip (T5, section 13) |
| Form field | shadcn `Field` (`Field.Error`); `FieldErrors` in the workflow builder | Label above the control, a description of one line at most, and each error under it with the problem icon and the verify colour (T1, section 14) |
| Move to stage | `MoveToStage` on shadcn `DropdownMenu` | The non-drag way to move a candidate (section 17): a button that opens the stages, the current one marked. On a detail page a last item, With a note, opens the move in a sheet with a note |
| Confirm dialog | shadcn `Dialog` (`ConfirmDialog`, `CloseJobDialog`, `LeaveDialog`) | Section 9: the question as its title, what happens in one line, the outline way back and the action, in `destructive` when it destroys (A2). A choice that goes with the action (also revoke a removed member's keys) sits under the line as a checkbox. A failure stays in the dialog as `FailureText` |
| Secret dialog | shadcn `Dialog` (`SecretDialog`) | A secret the API shows once (an API key, a webhook signing secret): "Copy it now. You will not see it again.", the value in a read-only monospace field with Copy, and Done. The value lives only while the dialog is open; never in the query cache, the address or storage |
| Email preview | `EmailPreview` | An email as the candidate receives it: the subject, then Email or Plain text (Toggle). The HTML renders in a sandboxed frame with no scripts, in an inset, never in the page, because a tenant wrote the template. The composer's preview, and a sent email opened in Messages (`SentEmail`), as it went |
| Email composer | `EmailComposer`; `EmailComposerSheet` on shadcn `Sheet` | Email one candidate or a selection: in a sheet from a list's selection bar and the candidate's header (section 9, in place), and inline at the top of the Messages tab, titled Email and the candidate's name. To, then Start from: Blank email, the default, or one of the organization's templates, which fills Subject and Message. The rejection and follow-up questions emails are never offered, because only their actions send them (`docs/architecture/email.md`), and a blank email never becomes a template. The Edit / Preview switch (A8, no window) and Insert variable, a menu of the variables that puts `{{ name }}` at the caret of the field last used. Changing a template's wording changes this email only, and one muted line under the message says so and that the template stays as it is. Preview is the first candidate's email, rendered by the API with their details ("Preview for" and their name in a selection); each candidate's own copy is rendered with theirs. Send is the sheet's one primary ("Send 3 emails" for a selection); inline on Messages it is outline, because the page's primary is Move to stage (A1), and empty fields say what they need when it is pressed. Each email lands in Activity and Messages as it went. No bulk email while an integrity filter or view picked the rows |
| Live indicator | `LiveIndicator` | The live updates status as a pill in the top bar, beside Search, on every page: icon, colour and label (T1), from section 3.2 (Live, Connecting, Offline). While live, its icon has a slow, soft pulse behind it, which stops for reduced motion. On a phone only the icon shows; screen readers hear the label |
| Failure text | `FailureText` with `ProblemIcon` | A failed action or form, in one line where it happened: the problem icon, the verify colour and the reason (T1, section 14). A section that cannot load uses `QueryError` |
| Integrity chip and panel | `IntegrityChip`, `SignalEvidence`, `ReviewLog`, `LevelScale`; `IntegrityPanel` is the candidate's Verification tab | Section 16. `LevelScale` draws a level on its three steps. A summary card per kind of check: Network, Identity, Automation and Content always, Files and Form activity (the telemetry aggregates) only when they found something, each named for what was looked at, never for what it means about the person. Reasons expand one by one or with Expand all. The review log is its own panel: dismissals and review notes as Activity, and Add review note, named apart from Activity's Add note |
| Tabs | `RecordTabs` on shadcn `Tabs` (`line`) | A record's peer views in a fixed order, each with its count in the count badge. A tab shows its state in its count where one fits (Distribution 7/8, posted on 7 of 8 boards, which screen readers hear in words), always in the same grey count badge; a state that needs a look is the record's status beside its title, never a coloured tab, a dot or a lone icon (E2). The list scrolls sideways, never wraps (F5) |
| Notice | `Notice` | A problem to fix: the verify status's soft colour, full width, the problem icon (section 14), what is wrong in words and a link to the fix (E2) |
| Expandable row | `ExpandableRow` | A list's row that opens in place to show more, such as an email on Messages: the whole row is the button, with `aria-expanded` and `chevron-down`, which turns up when it is open; what it shows follows the row. Its words wrap rather than truncate, since a tooltip has no place inside a button. The rows share one surface and its dividers (S4) |
| Summary card | shadcn `Card` as it is (`SummaryCard`), on a summary band (`SummaryBand`, section 6) | An icon in a neutral 32px chip and `Card.Title` (16/24 semibold); the count as a badge in `Card.Action`, saying what it counts (2 signals); `Card.Description` (14/20 muted, two lines at most, the rest in a tooltip). A link when it opens what it counts |
| Metric card | shadcn `Card` (`MetricCard`) | The value in the UI typeface (`metric`), a signed change against a named period (+7 vs last week), a 12-point line and a readout on hover, tap and focus; no description. The change is green when good and amber when it needs a look, decided per metric (fewer days to hire is good). It opens its report |
| File row and file viewer | `FileRow`; `FileViewer` on shadcn `Dialog`; `FileFrame` | A page shows a file row: type icon, name, size, View and Download, or the scan status when a file is withheld. View opens the file in a full-window dialog with Close and Download, never a new tab. Untrusted files render isolated in a sandboxed frame; it draws PDFs, and other types keep Download only |
| Edit / Preview switch | `EditPreviewLayout` (the editor and its preview side by side where both fit, else `EditPreviewSwitch`: a `Toggle` group and an icon button); `PreviewFrame`; `#lib/preview-window.svelte.ts` | A8: a pencil and an eye; the window icon (`app-window`, "Open in a new window") pops the preview out, and the window follows each edit over a same-origin `BroadcastChannel`. A candidate page previews in a phone, tablet or desktop frame at its real size. A read-only view of what an editor makes (a closed job's questionnaire) keeps the same switch, so content and preview never share the screen there either |
| Count button | `CountButton`: outline `Button` and `Sheet` | A count in a header ("2 hiring templates") that opens a sheet listing each item as a link, with search past 10 (T4). With nothing to list it is plain text, worded as its list column words it ("None") |
| Sort header | `SortHeader` in the TanStack table header; `SortMenu` on phones; `#lib/table-sort.svelte.ts` | L2: the header text and `chevrons-up-down`, which turns into `arrow-up` or `arrow-down` on the sorted column; `aria-sort` on that header; a second click reverses. Each table opens on its default (a job's candidates on Stage, those needing attention first: overdue, the longest first, then pipeline order; candidates across jobs on Applied, newest first), and each person's sort is remembered per table in the browser (`preferences`), so it does not follow them to another browser. The Sort chip offers the same choices on a phone, above the list items |
| Built in badge | `BuiltInBadge` on `Badge` muted variant | After the name of a template or role ScreenHire supplies; nothing marks your own (W6) |
| Reorder grip | `ReorderList` and `ReorderGrip` on svelte-dnd-action | A7: a drag handle whose click (or Enter) opens Move up, Move down, Move to top and Move to bottom, leaving out moves that do not apply; Space picks the item up and the arrow keys move it. Each list has its own drag zone, so lists on one page never trade items |
| Settings menu | `SettingsMenu`; its pages are `settingsSections` in `#lib/settings.ts` | Settings is one sidebar item; its pages share this menu: Organization, Team, Roles, Branding, Verification, API keys, Webhooks and MCP server. Email templates are on the Templates page (section 21), and their old Settings addresses redirect there. A surface under the header band: from `lg` a 200px column left of the page's content, the one side column S7 allows; below `lg` one row above the content that scrolls sideways and never wraps (F5). Each entry is a link with its icon; the current page takes `accent` and `aria-current`, and the row scrolls to show it. The menu lists only the pages the person's role may use, each behind its permission. A record under a settings page (a webhook endpoint, a role) is a page with no menu: a detail page when it has the details rail, the form template when it does not. On a settings page the header band fills the window and its title lines up with the menu, not with the form column |
| Form builder | `FormBuilder` and `AnswerMenu` on `Panel`, `ReorderList` and shadcn `Switch`; `#lib/form-builder.ts`, `#lib/form-sections.ts` | One builder for application questions, follow-up questionnaires and scorecards, modelled on Google Forms. The questions are rows in one panel with their count (S4), and every row starts closed. A closed row reads as text: the question, an asterisk when required, its answer kind's icon and name top right, the help line, and the options with the mark the person will see (a circle for Choice, a square for Multi-choice). The row is one button that opens it, beside its grip. Only one question is open at a time, with a `primary` bar down its left edge: its text, the answer kind button top right (the answer menu), the help line, the options with their own grip list ending in an outline Add option, then what the kind needs (a knockout, a number's range, a file's types). Its footer holds Duplicate and Delete icon buttons and the Required switch. Duplicate puts the copy straight after the original and opens it. Deleting changes nothing until Save, so it does not confirm (section 10). A problem shows under its question whether the row is open or closed (W7). Add a question, under the list, opens the answer menu. Questions and options reorder with a grip (A7). The editor that mounts it has the Edit / Preview switch at its top (A8). Changing a question never changes answers already given. A questionnaire (never a scorecard) may have named sections, each a page for the candidate: an outline Add a section beside Add a question, and each section a row in the same list, on the `muted` fill, with its grip, an inline name field, its question count and a Delete section icon button. A section row starts its section, so moving a question past one, by drag or the grip's Move up and Move down, moves it into that section; the list stays one panel with one open question. Deleting a section gives its questions to the section before it and does not confirm (section 10). The preview stacks the candidate's pages, each opening with `PageProgress` |
| Answer menu | `AnswerMenu` on shadcn `DropdownMenu` | Picks a question's answer kind, each group under its heading and each kind with its icon; the current kind is ticked, and Add a question opens the same menu with nothing ticked. General: Yes or no, Choice (pick one), Multi-choice (pick any), Short text, Long text. Application questions add More: Number, Link and File. A follow-up questionnaire offers the same without File, because its page takes no uploads. A scorecard offers General and Rating, each rating with a star: 1 to 5, 1 to 10, Yes or no |
| Rating | `Rating`; `#lib/rating-column.ts` for a job's table | The candidate's rating: one to five stars filled to the tenth, the number and how many scorecards it comes from (in words in the count's tooltip), opening, from a small outline button (A6), to each scorecard that counts with its own number and recommendation. It averages people's rating questions only, never integrity results, AI signals or the stage; a reviewer with a scorecard to give reads "Rating after your scorecard" ("After yours" in a table cell, the rest in its tooltip) until they submit their own; no automation acts on it. "Not rated yet" until a scorecard counts. A job's table has a Rating column, highest first, the unrated last |
| Rating answer | `RatingAnswer` on shadcn `ToggleGroup` | A rating or yes-or-no question on one row of joined buttons, with Low and High under the two ends of a number scale. Each button is a radio in the question's group; the chosen one takes `accent` (A6) and an inset ring in `accent-foreground`, so a chosen button is not told from a hovered one by a faint fill alone, and choosing it again clears an optional answer. A number scale never wraps: its buttons share the row equally, up to the control size, so 1 to 10 on a phone has buttons narrower than 44 px (still above WCAG 2.5.8's 24 px) and High stays under 10 |
| Choice card | `ChoiceCard`: a native radio in a labelled card | A choice of a few options in a `radiogroup` with its label: each a bordered card with its name and one muted line, and the whole card is the target. The chosen card takes the `primary` edge and `accent` fill. A member's jobs (All jobs or Only jobs they're on), the role in Invite, and the role to copy in New role |
| Filter menu | `FilterMenu` (one choice), `MultiFilterMenu` (several) | An outline button that names the filter and its choice ("Stage: All", "Job: 2 chosen"), `primary` while set, opening each choice with its count. `MultiFilterMenu` shows a box on every choice, ticked or not, and with two or more chosen each one is a pill with its own remove button beside the button. Filters sit in one row under a list panel's header that scrolls rather than wraps (F5); the page applies them to the rows |
| Picker | shadcn `Popover` and `Command` | An outline button with a verb ("Add a person", "Copy from another job") that opens a searchable list; typing filters by name, a pick closes it. The list keeps a 16px gutter from the window's edge. Nothing left to pick is said in the list, never by disabling the button (A3) |

## 12. Words

Keep it simple: say less, and say it once.

| Rule | Instead of | Write |
|---|---|---|
| Say it once | "Download resume" inside the Resume panel | Download |
| Labels, not instructions | "Enter the candidate's email address" | Email |
| Hide what cannot be used | A disabled "Request verification (phase 2)" | Nothing, until it works |
| One line, or none | "This candidate is currently in the Screening stage." | A Screening pill |
| Fewest words | An "Open preview in a new window" button | A window icon, with "Open in a new window" in its tooltip |
| Mark the exception | A ScreenHire pill on built-ins and a Custom pill on your own | Built in on built-ins, nothing on yours |
| Design for the count | "Used by Software engineer, Brightwater engineering" | 2 hiring templates, a button that lists them |

Helper text only when a label cannot say it. Copy follows `.claude/rules/writing-style.md`.

**Voice.**

- **Recruiters:** precise and neutral. "Review suggested", "Verification suggested" (never "Verify"), "3 reasons";
  never "suspicious", "fake", "fraudster" or "AI-generated candidate". Every automated observation says what was
  observed, not what it means about the person.
- **Candidates:** warm, plain, honest. Explain what we look at and what we never do, before collection; offer the
  assistive-tech toggle; welcome declared AI use where the employer allows it. "A person reads every application."
- **Marketing:** confident and specific; cite a source for every statistic; no invented numbers.

## 13. Dates and times

| Where | Show | Example |
|---|---|---|
| Text, headers, activity | Time ago, exact date and time on hover | 2 hours ago |
| Tables | The date, so the column lines up and sorts | Oct 20, 2026 |
| Older than 7 days | The date | Oct 3, 2026 |
| Last updated (T6) | Time ago and who, the exact time on hover | 2 hours ago by Avery Quinn |

Always the browser's locale (US English by default), through one shared formatter on `Intl.DateTimeFormat` and
`Intl.RelativeTimeFormat` (`#lib/dates.ts`). The API never sends formatted dates (`.claude/rules/utc-timestamps.md`).

## 14. Icons

Lucide (`@lucide/svelte`, per-icon imports). 16px beside 14px text, 20px in touch bars and the icon rail. Size 16 and
stroke 1.75 are set once with `setLucideProps` in the root layout. Navigation and main buttons pair an icon with a
label; decorative icons are `aria-hidden`. Status icons (section 3.2) appear only in status badges and in a status's
saved view in the sidebar (section 21), and no other control or decoration uses them. The warning triangle marks only
the integrity cards that fed a level (section 16). A problem in words (a failed action, a form error, a builder's
problem to fix) starts with `circle-alert` in the verify icon colour (`ProblemIcon`). It is not a status icon, so it
never stands in for a badge.

## 15. Shortcuts

Ctrl K or Cmd K opens the command palette. `?` opens the shortcuts dialog, and so does its command in the palette.
Hints appear as `Kbd` key caps in tooltips and menus, never as an always-on bar. Single-key shortcuts (the G chords, J
and K, X, V and `?`) can be turned off (WCAG 2.1.4), skip text fields, menus and dialogs, and work only while their
table or board has focus, except the G chords and `?`.

## 16. Integrity UI

Evidence, not verdicts (ADR-0004, CLAUDE.md non-negotiables 1 and 2):

- Each assessment shows its level as a status badge, a one-line summary, its reasons with evidence and
  counter-evidence, and its suggested next step. A calm result keeps its next step as one quiet line ("Suggested next
  step: No action needed."), not a boxed callout.
- AI assistance is information: its own colour and the `sparkles` icon at every level, quiet in lists, never a
  warning.
- No score, no percentage, no reject control. The panel ends with "No automated decision has been made. Every action
  is logged."
- Facts shown elsewhere on the page (the candidate's AI declaration) are not repeated in the panel (W7).
- On a candidate it is the Verification tab. When the level needs a look, the tab's count is the signals behind it, in
  the grey every tab's count has (E2), and the level is the candidate's status badge beside their name (T2). Each
  assessment is one panel with one skeleton and no labels over sentences (W9): its name, level badge (unless the page
  header already shows it, W7), a small meter of the three steps and, for AI assistance, the job's policy as a pill ("AI
  allowed with disclosure") in the header, with Expand all on the right. When the level needs a look the next step
  leads, semibold, in a callout edged in the level's colour with an arrow, the summary a muted line under it; a calm or
  dismissed result is one muted line, the summary then its next step (W10). Then the reasons, grouped by the kind of
  check that found them (Network, Identity, Automation, Content) and a last Context group for what does not count, each
  group's name and signal count beside its reasons, each reason opening (A9) to what was observed, its evidence as a
  facts row, any counter-evidence in its own card, and its code. The panel ends with Dismiss on the left and "Assessed 2
  hours ago" and "Ruleset" in caption size on the right. A calm result has no list of empty checks: its summary already
  says so.
- Labels keep their full names: Verification suggested, mirroring Review suggested. AI assistance reads No signs,
  Some signs or Strong signs of AI help.

## 17. Accessibility and motion

- WCAG 2.2 AA. A 2px `primary` focus ring with a 2px offset on every interactive element, never removed.
- Targets at least 24px with a mouse (2.5.8) and 44px on touch. Text at least 4.5:1; large text and boundaries 3:1.
- Every drag has a non-drag alternative (Move to stage, and the grip's Move menu, A7). Every shortcut has a visible
  control.
- A canvas of nodes (the workflow graph) makes each node a tab stop with the focus ring and a label that says what it
  is. Tab reaches the buttons inside a node; the arrow keys, Home and End move between nodes in reading order, and
  whatever takes the focus scrolls into view. Text in a node is clamped, with the full value in a tooltip that takes
  no tab stop of its own.
- Motion 120 to 180ms for hover and popovers, 200 to 240ms for sheets; `prefers-reduced-motion` turns it off.
- axe runs in component tests and Playwright; manual NVDA and VoiceOver passes per release; an external audit before
  general availability.

## 18. Employer branding (careers pages)

Tenants set a brand colour and logo. On save, the server derives and validates:

| Token | Purpose | Minimum |
|---|---|---|
| `brand` | Tenant colour | |
| `on-brand` | Text on brand fills | 4.5:1 |
| `brand-text` | Brand-coloured text on the page background | 4.5:1 |
| `brand-ui` | Borders, focus rings | 3:1 |

Failing colours are adjusted in tone (OKLCH lightness) or rejected with an explanation, and shipped as a hashed
per-tenant stylesheet (strict CSP, no inline styles). Brand never overrides status colours, the privacy notice or the
"Powered by ScreenHire" footer.

## 19. Assets

- Logo mark: an ink rounded square with "S" (a placeholder until brand identity work).
- No stock photography in the product; marketing imagery is real product UI.

## 20. Changing the system

- Change this file first. A change to a decision (anything in ADR-0010 or ADR-0013) also gets an ADR.
- Tokens live in `packages/ui` and reach components through Tailwind classes; this file names them.
- A new component or pattern joins section 11, with its example in the style guide, before a second screen uses it.
  The style guide's test reads section 11 and fails on a row with no example.
- The `design-review` agent reads section 2 on every run, so a new check here is enforced from the next review on.

## 21. Screens the v0.3 build changed

- **Job:** the work area first. The authenticity bar becomes filter chips with counts, the stages a Stage filter,
  and Distribution its own tab, whose count says 7/8. A refused posting is a notice that names the board and links to
  the fix. Calm rows say nothing. The board's empty and closed stages shrink so every stage fits, and the board
  scrolls inside itself. Workflow opens its own page from the header. Selected rows get a bar with Move and Email;
  with an authenticity filter on, Email hides and Rejected leaves Move (ADR-0004).
  Built: the chips and the Stage filter sit on a row under the pipeline panel's header (S3), as words and counts with
  no status icon (I2), and a phone has Stage, Authenticity and Sort chips above the list items. The tab is in the
  address (`?tab=distribution`), and the count is the boards the job is on or on its way to. A posting whose checks
  found issues is "did not take this job", never "refused": only a board's own answer is a refusal. On the board, the
  closed stages share one Closed column (section 5), whose rows take a dropped card, and a calm card shows no level.
  In the table a calm AI assistance cell is empty, with its words for screen readers, and authenticity keeps its quiet
  "No concerns" (T3).
  Hiring team: an outline button in the header with the team's count badge (T4), in More on a phone, opening a sheet
  of the people on the job, each with their role, "Only jobs they're on" for someone who sees only their own (W6), a
  remove button, and Add a person and Copy from another job (Pickers). Changes land on Save. Without Edit jobs the sheet lists the people and says why (E3). A hiring
  template's header has Default hiring team as a fact whose count opens the same sheet, without Copy.
- **Candidate:** the same tabs in the same order for everyone: Overview (what needs you now, the last five things
  anyone did with a box for a note, and the answers), Application, Messages (every email with the candidate, both
  ways, and the reply box), Scorecards, Integrity, Activity. Activity lists everything newest first, saying who and
  how long ago, and filters by kind. Only a note's author edits it; the author, or a role with Delete anyone's notes
  (Owners and Admins), deletes it, after a confirm. The header holds who they are and where they stand: the
  authenticity level as a status badge beside the name when it needs a look, contact on the line under it, then a meta
  line: the stage as a muted pill, the job, "Applied 2 hours ago via the careers page" and the rating. Overview
  is a grid of panels: Next steps and Activity on the left, Files, Tags and Answers on the right. Next steps is one row
  per step: a round to-do tick or the level's icon, the step, its facts, and its link on the right.
  Built: Messages lists every email to the candidate, about any of their applications, newest first, with the email
  composer on top for people who can send. Each row opens to the email as it went; one sent before ScreenHire kept a
  copy of each email says only its subject was kept. Replies wait for roadmap item 1, so the tab says nothing about
  them.
- **Templates:** one page with a tab per kind (Hiring, Scorecards, Emails). Each tab says in one sentence what its
  templates are for, each row says where it is used, and each editor has the Edit / Preview switch. A job keeps the
  copy of a template it started with. A built-in the team edited offers Reset to original in its menu.
  Built: the tab is in the address (`?tab=scorecards`), each with the count of templates that are not archived. The
  tab's sentence sits in its table panel's header, beside the search (S3). The filters are All, In use (something uses
  it now), Not used in 6 months (no job started from it, no stage used it and no email went out from it in 180 days,
  and the team has had it longer than that) and Archived, each with its count. The first two can overlap. Columns:
  Name with Built in, then for hiring What it is for, for scorecards Questions, for emails the Subject with each
  variable as a muted word; Used by (jobs that are not closed, hiring templates, automations); Last used, or Last sent;
  Version for hiring, the published number then the Draft badge; Last updated. The row menu holds Duplicate, which
  makes "<name> (copy)" as the team's own and opens it, Archive or Restore for the team's own, and Reset to original
  for an edited built-in email, which confirms. The built-in rejection and follow-up questions emails have no
  Duplicate: their variables only their own actions fill. Archive does not confirm: Restore on the same screen undoes
  it (section 10). An archived template leaves the pickers and reads only until restored; jobs keep their copy, and
  automations keep sending an archived email. New template is a menu of the kinds the person's role may make. Each
  editor's header has Used by as a count button, then its facts and Last updated with History; the hiring template's
  Edit / Preview switch is in its Questionnaire tab, the part someone else sees. A built-in hiring template or
  scorecard reads only, says why and offers Copy (E3), with no History. Built-in emails stay editable in place.
- **Settings:** one sidebar item with its own menu (section 11). Careers branding becomes Branding, and email
  templates move to Templates.
- **Team and Roles:** Team lists the members full width, each role by its name alone: a custom role has no Custom label
  (W6). Invite opens a sheet that describes each role, built-in or custom, starts at Viewer, the least access, and links
  to Compare roles. Roles is its own page on the list template: every role, the built-in ones marked Built in, with what
  it can do, how many people have it and Last updated ("By ScreenHire" for a built-in). Its header has Compare roles and
  New role. New role opens a sheet with a name and the role to copy, never Owner, then Create and edit opens the copy's
  page. Compare roles is the full-width table of what each role can do, read from the API's permission catalogue, a
  column per role. Each cell says Yes or No in words: L1 keeps icons out of cells, and the check is a status icon (I2).
  In a panel narrower than 896px (the full sidebar at 1024, and phones) it is a list instead: each permission, what it
  covers, and the roles that have it as badges. A custom role's page is the form template: Name, then What this role can
  do with a checkbox per permission and the count chosen ("4 of 16"), then Cancel and Save in that panel's footer; rows
  nobody can change here (See jobs and candidates, in every role; Manage Owners, Owner only; a permission the person's
  own role lacks) have a lock and say why. Its header's facts are Copied from and Last updated with History (the people
  are counted on their panel, W7); Copy role and Delete role are in More, and deleting a role somebody has is refused,
  saying how many. A built-in role's page is a read-only view with Built in beside its title and Copy role as the
  primary action. Both list the people with the role, each with the jobs they see ("All jobs", or a count button that
  lists them) and an Edit access on Team link. Job access (ADR-0015): Team has a Jobs column, plain text (T4): "All
  jobs", or "2 jobs" for someone on Only jobs they're on. A member's menu holds Edit access, which opens one sheet for
  their role (a select, with what it can do below) and their jobs: All jobs ("Including jobs added later") or Only jobs
  they're on, then the jobs whose hiring team they are on as links, read only, with one line saying each job's Hiring
  team changes them (W6). For an Owner or Admin the choice is a line saying they see all jobs. Invite asks the same,
  starting at All jobs. Someone who sees only their own jobs is offered only Only jobs they're on (A3).
- **Tasks:** Mine, For the team and Everyone's, each completed with a checkbox.
  Built: Mine is the tasks assigned to the person, For the team the tasks assigned to nobody in particular, and
  Everyone's every task the person can see, which job access limits (ADR-0015). Each is a toggle with its count, so the
  header band shows none (W7); Open and Done are the other toggle group (section 7). The task checkbox (section 11)
  completes a task on My tasks and on the candidate's Overview.
- **Saved views:** each shows its status icon and a short name (To verify), so nothing is cut off.
  Built: To review, To verify and Overdue show their status's icon; New today, Not screened and Interviewing an
  ordinary one. The short name is also the candidates page's title for the view.
- **API keys and webhooks:** each page says what it is for, how to call it and where the reference is. The API
  reference is served in production.
- **MCP server:** the values any MCP client needs (this deployment's server URL, the transport, the header), the
  key's access, the tools as pills, one generic config and a link to API keys. Never one client's setup steps.
