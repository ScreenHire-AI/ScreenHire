# ScreenHire design system

> Status: **v0.2 (2026-10-20)**. Decisions: [ADR-0009](../adr/0009-design-system-warm-grid.md), amended by
> [ADR-0010](../adr/0010-design-system-v0-2.md). Visual reference: the owner's "ScreenHire design v0.2 review" canvas;
> where it and this file differ, this file wins.

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
4. **Clickable looks clickable.** Buttons look pressable. Links underline. Badges and pills are never clickable.
5. **Status first, then facts.** The signal people scan for comes first, as a badge. Facts follow as pills.
6. **Clear layers.** Every section is a panel with its title inside. Panels in a row align.
7. **Same pattern, same place.** One page template per job and one component catalogue. New patterns join the
   catalogue first.
8. **Fluid at every size.** Flex and grid, never fixed widths, from phone to wide desktop. Controls never wrap.
9. **Evidence, not verdicts** (ADR-0004). A level, its reasons and a next step. Never a score or a reject button.

## 2. Review checklist

Authors check their screens against this list, and the `design-review` agent reports findings by these ids.

**Structure**

- **S1** The page uses a template from section 7 (list, detail, read-only, form, builder).
- **S2** One header band: title, the status badge beside it when there is one, the primary action on the right, then
  the facts row or tabs.
- **S3** Every section is a panel with its title inside a fixed-height header. Nothing readable sits on the page
  background.
- **S4** Panels in a row share a top edge. No card inside a card.
- **S5** Insets only inside a surface. Shadows only on raised and overlay layers.
- **S6** Each page's content container has a max-width of 1280px (lists may reach 1536px at 2xl). Content elements
  get a max-width, never a fixed width.

**Actions**

- **A1** At most one primary (filled) button per page, sheet or dialog.
- **A2** Destructive actions live in a menu and confirm in a dialog. Section 10 says what is not destructive: an action
  the same screen can undo, and removing a row from an unsaved builder draft.
- **A3** A feature that does not exist yet is hidden, not shown disabled. Disabled is only for a control this screen
  can enable (Save until the form is valid), with the reason beside it.
- **A4** Icon-only buttons have an `aria-label` and a tooltip.
- **A5** Only buttons and links look clickable. Links in text underline; navigation and names in lists underline
  on hover. Badges, pills and counts are never clickable.

**Words and type**

- **W1** Text sizes come from the type tokens (section 4). Nothing under 12px. No pixel font sizes in markup.
- **W2** Sentence case. No all-caps text except acronyms. Monospace only for codes, ids and key caps.
- **W3** No dot separators or strings of metadata. Facts are muted pills or a labelled facts row.
- **W4** Labels, not instructions. A description gets one line, or none if the title says it. A fact appears once
  per screen.
- **W5** Copy follows section 12 and `.claude/rules/writing-style.md`.

**Status, counts and time**

- **T1** Status is a badge: shape mark, colour and label, always all three, except AI assistance, which is
  information and has no mark (section 3.2). Status colours mean status only.
- **T2** Status comes first in its group: beside the title, or first in the row.
- **T3** In lists and tables, calm states are quiet (mark and muted text). Only states that need attention get a
  filled badge.
- **T4** Counts are wide pills right after their label. Steps are square markers on a line. They never share a shape.
- **T5** Times follow section 13: time ago in text, dates in tables, the browser's locale, the exact time on hover.

**Icons and shortcuts**

- **I1** Lucide only, at the sizes in section 14. Navigation and main buttons have an icon and a label.
- **I2** Status shapes appear only for status, never as decoration or for ordinary views.
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
- **E1** Loading shows skeletons of the same shape. Empty states say why and offer the one action. Errors use
  `QueryError`.

**Integrity and access** (the `integrity-guardrails` agent owns the first)

- **G1** No score or percentage, no reject control, AI assistance styled as information, and every result keeps its
  next step (section 16).
- **X1** WCAG 2.2 AA: visible focus ring, labels on inputs, landmarks and headings, text contrast at least 4.5:1, and
  a non-drag alternative for every drag (section 17).

## 3. Colour

Semantic names are the API; raw values live only in `packages/ui` (`tokens.css`, mapped in `theme.css`) and reach
components as Tailwind classes. The token column is the name in code: `background` is `bg-background`,
`muted-foreground` is `text-muted-foreground`. Colours did not change in v0.2.

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

### 3.2 Status (shape, colour and label, always)

| Meaning | Mark | Tokens (`status-*`, `-soft`, `-mark`) | Text | Soft bg | Mark colour |
|---|---|---|---|---|---|
| No concerns, Live, a workflow with no problems found | circle | `status-clear` | `#0B5D4F` | `#E3F1EC` | `#0F766E` |
| Review suggested, Queued, a file being scanned | diamond | `status-review` | `#7A4205` | `#FBEFD3` | `#C77700` |
| Verification recommended, a file withheld by the scan, a workflow with problems to fix | square | `status-verify` | `#9A2F17` | `#F9E0D6` | `#B4461F` |
| AI assistance (information) | none | `status-ai` | `#2B2788` | `#ECEBFA` | none |
| Not started or ended: a draft, a closed job, a board the job is not on | ring | `muted-foreground` on `secondary` | `#5F5A52` | `#F4F1EB` | `#5F5A52` |

Status colours are never used for brand, decoration or facts. Counts use tabular figures. The stacked integrity bar
is `aria-hidden` and always has its numeric legend.

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

Dark status: no concerns `#4FD1B4` on `#15302A`; review `#F6CB76` on `#3A2C10` (mark `#F2B84B`); verify `#F7957A` on
`#3D1A10` (mark `#F27A55`); AI `#C7C8FA` on `#26264A`. In dark mode, higher layers get lighter (section 6).

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
`text-candidate-input` and `text-candidate-hint` in `packages/ui`. Marketing display goes up to 64px. The dashboard's
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
| Content widths | 1280px `page`, 1536px `page-wide`, 688px `form`, 320px `side` (`max-w-page`, `basis-side`, ...) |

## 6. Layers

Drawn from Material 3's elevation ("the fewer levels in your UI, the more power they have"), IBM Carbon's layering and
Atlassian's surfaces.

| Layer | What lives there | Looks like |
|---|---|---|
| Page | Layout only. Nothing readable sits on it. | `background` |
| Surface | Header band, panels, tables, sidebar, top bar | `card`, 1px `border`, no shadow |
| Inset | A well inside a surface: resume preview, board column | `secondary`, no edge |
| Raised | The one thing in hand: a dragged card, a toolbar stuck while scrolling | surface, edge, `0 4px 12px rgb(28 27 25 / .14)` |
| Overlay | Menus, popovers, tooltips, sheets, dialogs, toasts | surface, edge, `0 16px 48px rgb(28 27 25 / .18)`; dialogs dim the page |

- Every section is a panel with its title inside the panel header. Panels in a row share a top edge.
- Shadows only on raised and overlay. No card inside a card.
- Inside an inset, data and controls sit on `card` with a `border-subtle` edge (a quoted answer, a list of fact pills,
  a field), so they read as values in the well, not as another layer.

## 7. Page templates

| Template | For | Anatomy |
|---|---|---|
| List | Candidates, jobs, templates, My tasks; the team, API keys and webhooks in Settings | Header band (title, count, page actions); one table panel whose header holds its title, search and filters. A list with no search (My tasks) puts its toggles in a row under the panel header |
| Detail | A candidate, a job, a webhook endpoint | Header band (identity, status badge, actions, facts row or tabs); main column and a 320px side column |
| Read-only view | A ScreenHire template, anything you cannot edit | As detail; data as labels and values, never disabled inputs; Copy or Edit as the primary action |
| Form | Creating or editing a record; a settings page such as Organization, Careers branding or Integrity | One column, 688px wide, labels above fields, actions at the end |
| Builder | The workflow graph, the questionnaire builder | Header band, then a full-width work area |

Sign-in, onboarding and error pages sit outside the app shell. They have no sidebar: the brand bar takes the top
bar's place, then the form template. A title that changes in place, such as "Check your email" after a link is sent,
takes the focus.

- **Header band:** a white strip under the top bar. Title first, status badge beside it, actions on the right, then
  a labelled facts row (label 12px above value 14px, thin rules between) or tabs on its bottom edge. One muted line
  under the title may hold a person's contact details, each an icon and its value, with no labels. On a form page
  the band's contents line up with the 688px column.
- **Panel:** header (title, optional count, actions on the right), body, optional footer inset.
- **Columns:** from `lg`, the main column takes the room the 320px side column leaves, gap 20px. Below `lg` they
  stack in one column in priority order, status first. The status panel comes first in the page's order and sits at
  the top of the side column from `lg`, so the order on screen and the reading order agree at every size.

## 8. Screen sizes

Four size classes on Tailwind's breakpoints. No custom breakpoints.

| | Phone | Tablet | Laptop | Desktop |
|---|---|---|---|---|
| Window | under 768px | 768 to 1023px (`md`) | 1024 to 1535px (`lg`, `xl`) | 1536px and up (`2xl`) |
| Navigation | Sidebar as a sheet behind a menu button; Back on detail pages | Icon rail, labels in tooltips | Full sidebar | Full sidebar |
| Columns | One, status first | One, status first | Main and side | Same, centred at 1280px |
| Header band | Facts in a two-column grid | Facts wrap | One line of facts | One line of facts |
| Actions | Primary in a bar at the bottom; the rest in More | In the header; extras fold into More | In the header | In the header |
| Tables | Rows become two-line list items | Scroll inside the panel; first column sticks | Full | Full, optional columns may show |

The sidebar is shadcn-svelte's `Sidebar`: `collapsible="icon"` gives the rail and its mobile mode gives the sheet.
Checked at 360, 768, 1024, 1440 and 1920px.

## 9. Flow

| Move | How |
|---|---|
| Across areas | The sidebar. It is the only app navigation. Switching areas fades. |
| Forward | From a list to a record. The whole row is the link. Filters and scroll position survive the trip back. |
| Back | The breadcrumb goes up a level. Browser Back goes where you came from. |
| In place | A sheet for quick work: add a note, quick edit, filters. The record stays behind it. |
| Confirm | A dialog, only to confirm or decide. A menu is the gentler choice. |
| Create | A full page for anything with several steps, such as a new job. |
| Tabs | Peer views of one record, under its header. Never steps in a sequence. |

Forward and back slide (200ms); everything honours `prefers-reduced-motion`.

## 10. Actions

| Emphasis | Looks like | Use |
|---|---|---|
| Primary | Filled `primary` | One per page, sheet or dialog. It moves the work forward. |
| Secondary | White, `input` edge | Supporting actions, and Cancel |
| Quiet | Text or icon button | Row actions. Every icon button has a tooltip and an `aria-label`. |
| Destructive | `destructive` item in a menu | Never the page's main action. Confirmed in a dialog. An action the same screen can undo (taking a job down from one board, which Post puts back) is not destructive, and neither is removing a row from an unsaved builder draft (a stage, a question, a condition): nothing changes until Save, and leaving asks first. |

Features that do not exist yet are hidden (A3).

## 11. Components

The catalogue. A screen is built from these; a new pattern joins this table before a second screen uses it. Code lives
in `apps/dashboard/src/lib/components` (shadcn-svelte under `ui/` is ours to edit). The style guide page, `/style-guide`
in a development build, shows every component in each state, and its test fails when a row here has no example.

| Component | Built on | Rules |
|---|---|---|
| Header band, Panel | Our components (`HeaderBand` with `Fact` for the facts row, `Panel`) | Section 7 |
| Status badge | `StatusBadge` on shadcn `Badge` (`clear`, `review`, `verify`, `ai`, `neutral`; `quiet` for calm states in lists) | Mark, colour, label (T1, T3) |
| Fact pill | `Badge` muted variant | `secondary` fill, `input` edge, 12px; only on white surfaces |
| Count | `Badge` count variant | Wide pill (min 28px), muted text, after its label (T4) |
| Toggle | shadcn `Toggle`, in a labelled `role="group"` | Filters with a few choices and peer views (Table, Board). The pressed one takes `accent`; a count follows its label (T4). One line that scrolls inside its panel, never wraps (F5). In a narrow container a few short toggles whose every choice matters at a glance (a job's authenticity levels) stack one per line instead |
| More menu | `MoreMenu` on shadcn `DropdownMenu` | An icon button with its label in a tooltip (A4). Holds extra actions, folded ones on phones and destructive items (A2, section 10) |
| Step marker | `StepMarkers` (across), `StepLine` (down) | 28px square, radius 7, white with an edge, joined by a line: across for progress, down for a workflow's stages |
| Summary list | `<dl>` | Labels and values for read-only data; one Edit per section when you can edit |
| File tile | Our component | Type icon, name, size and page count as fact pills, Download; scan status only when a file needs attention |
| Resume preview | Our component, in an inset | The file shown inline; untrusted files render isolated |
| Key cap | shadcn `Kbd` | In tooltips, menus and the `?` dialog |
| Tooltip | shadcn `Tooltip` | Labels for icon buttons, full values of truncated text, shortcuts |
| Icon button | `IconButton`: ghost `Button` and `Tooltip` | A quiet row action; its label is both the `aria-label` and the tooltip (A4) |
| Empty state | shadcn `Empty` | One line saying why, and the one action |
| Shortcuts dialog | shadcn `Dialog` | Opens on `?`; lists every shortcut; holds the single-key toggle |
| Sidebar | shadcn `Sidebar` | Section 8. The sections, the saved views, then Settings for owners and admins only; nobody else sees the group |
| Brand bar | Our component (`BrandBar`) | The top bar of a page outside the app shell: the logo mark and name only (section 7) |
| Table, list item | TanStack table; our list item | 48px rows; list items on phones |
| Search field | Our component | A list's search, in its table panel's header (section 7); 16px text on touch |
| Activity | Our component | Icon in a circle, event, who did it, time ago; a note left with it is quoted below. People show by name ("by Robin Ashby", "by you") |
| Tag list | `TagList` | Each tag a fact pill with its own Remove icon button beside it, never inside it (A5), then a field and Add tag. Removing is not destructive: adding the tag again undoes it (section 10). Without a role that changes hiring data, the pills only |
| Task list | `TaskList` | Icon in a circle, what to do, then its kind and who it is for as fact pills and time ago; a done task says when and by whom. An open task has Complete (outline) for a role that changes hiring data. On My tasks the candidate's name links to the application and the job is a pill |
| Notification tray | `NotificationTray` | Overlay cards in the bottom corner (above the action bar on phones), newest first, at most three: an icon, the record as a link, one line and Dismiss. They never time out, because each holds a link (WCAG 2.2.1), and a polite live region announces them |
| Sheet | shadcn `Sheet` (`NoteSheet`, `MoveNoteSheet`, the workflow builder's automations) | Quick work in place (section 9): the title, one line of description, the form, then Cancel and the one primary action (A1). The record stays behind it |
| Action bar | Our component (`ActionBar`) | A phone's primary action in a bar fixed to the bottom, over the safe area (section 8) |
| Relative time, truncated text | Our components on `Hint` | Time ago, or text cut with an ellipsis; the exact time or full value in a tooltip that opens on hover, focus or tap (T5, F4, F5). A truncated link is one tab stop, with an optional icon before its text |
| Table date | `DateText` on `Hint` | A date in a table or list item, the exact time in its tooltip (T5, section 13) |
| Form field | shadcn `Field` (`Field.Error`); `FieldErrors` in the workflow builder | Label above the control, a description of one line at most, and each error under it with the verify mark and colour (T1) |
| Move to stage | `MoveToStage` on shadcn `DropdownMenu` | The non-drag way to move a candidate (section 17): a button that opens the stages, the current one marked. On a detail page a last item, With a note, opens the move in a sheet with a note |
| Confirm dialog | shadcn `Dialog` (`ConfirmDialog`, `CloseJobDialog`, `LeaveDialog`) | Section 9: the question as its title, what happens in one line, the outline way back and the action, in `destructive` when it destroys (A2). A choice that goes with the action (also revoke a removed member's keys) sits under the line as a checkbox. A failure stays in the dialog as `FailureText` |
| Secret dialog | shadcn `Dialog` (`SecretDialog`) | A secret the API shows once (an API key, a webhook signing secret): "Copy it now. You will not see it again.", the value in a read-only monospace field with Copy, and Done. The value lives only while the dialog is open; never in the query cache, the address or storage |
| Live indicator | `LiveIndicator` | The live updates status in the sidebar's footer: mark, colour and label (T1); the rail shows the mark with a tooltip |
| Failure text | `FailureText` on the status mark | A failed action or form, in one line where it happened: the verify mark, its colour and the reason (T1). A section that cannot load uses `QueryError` |
| Integrity chip and panel | `IntegrityChip`, `SignalEvidence`, `ReviewLog` | Section 16. The review log is its own panel: dismissals and review notes as Activity, and Add review note, named apart from Activity's Add note |

## 12. Words

Keep it simple: say less, and say it once.

| Rule | Instead of | Write |
|---|---|---|
| Say it once | "Download resume" inside the Resume panel | Download |
| Labels, not instructions | "Enter the candidate's email address" | Email |
| Hide what cannot be used | A disabled "Request verification (phase 2)" | Nothing, until it works |
| One line, or none | "This candidate is currently in the Screening stage." | A Screening pill |

Helper text only when a label cannot say it. Copy follows `.claude/rules/writing-style.md`.

**Voice.**

- **Recruiters:** precise and neutral. "Review suggested", "Verification recommended", "3 reasons"; never
  "suspicious", "fake", "fraudster" or "AI-generated candidate". Every automated observation says what was observed,
  not what it means about the person.
- **Candidates:** warm, plain, honest. Explain what we look at and what we never do, before collection; offer the
  assistive-tech toggle; welcome declared AI use where the employer allows it. "A person reads every application."
- **Marketing:** confident and specific; cite a source for every statistic; no invented numbers.

## 13. Dates and times

| Where | Show | Example |
|---|---|---|
| Text, headers, activity | Time ago, exact date and time on hover | 2 hours ago |
| Tables | The date, so the column lines up and sorts | Oct 20, 2026 |
| Older than 7 days | The date | Oct 3, 2026 |

Always the browser's locale (US English by default), through one shared formatter on `Intl.DateTimeFormat` and
`Intl.RelativeTimeFormat` (`#lib/dates.ts`). The API never sends formatted dates (`.claude/rules/utc-timestamps.md`).

## 14. Icons

Lucide (`@lucide/svelte`, per-icon imports). 16px beside 14px text, 20px in touch bars and the icon rail. Size 16 and
stroke 1.75 are set once with `setLucideProps` in the root layout. Navigation and main buttons pair an icon with a
label; decorative icons are `aria-hidden`. Status shapes (section 3.2) are not icons and appear only for status.

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
- AI assistance is information: its own colour, no mark, never a warning.
- No score, no percentage, no reject control. The panel ends with "No automated decision has been made. Every action
  is logged."
- Facts shown elsewhere on the page (the candidate's AI declaration) are not repeated in the panel (W4).

## 17. Accessibility and motion

- WCAG 2.2 AA. A 2px `primary` focus ring with a 2px offset on every interactive element, never removed.
- Targets at least 24px with a mouse (2.5.8) and 44px on touch. Text at least 4.5:1; large text and boundaries 3:1.
- Every drag has a non-drag alternative (Move to stage). Every shortcut has a visible control.
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

- Change this file first. A change to a decision (anything in ADR-0010) also gets an ADR.
- Tokens live in `packages/ui` and reach components through Tailwind classes; this file names them.
- A new component or pattern joins section 11 before a second screen uses it.
- The `design-review` agent reads section 2 on every run, so a new check here is enforced from the next review on.
