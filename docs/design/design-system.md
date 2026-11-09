---
type: Reference
title: "ScreenHire design system"
description: "The one source of truth for every screen: principles, the review checklist the design-review agent enforces by ID, colour and status icons, type, layers, page templates, screen sizes, components, words, dates, icons, shortcuts, verification UI, employer branding, the marketing site's bands, type and rules, and what v0.3 changed on each screen."
tags: [design-system, ui, checklist]
sources:
  - resource: packages/ui/src/tokens.css
  - resource: packages/ui/src/theme.css
  - resource: apps/dashboard/vite.config.ts
  - resource: apps/dashboard/src/app.css
  - resource: apps/dashboard/src/lib/status.ts
  - resource: apps/dashboard/src/lib/components/sign-in-card.svelte
  - resource: apps/dashboard/src/lib/components/provider-marks/google-mark.svelte
  - resource: apps/dashboard/src/lib/components/provider-marks/microsoft-mark.svelte
  - resource: apps/marketing/src/components/StatusIcon.astro
  - resource: apps/dashboard/src/routes/(dev)/style-guide/style-guide.svelte
  - resource: apps/dashboard/src/lib/components/rich-text-editor.svelte
  - resource: apps/dashboard/src/lib/components/rich-text-toolbar.svelte
  - resource: apps/dashboard/src/lib/rich-text.ts
  - resource: apps/dashboard/src/lib/table-sort.svelte.ts
  - resource: apps/dashboard/src/lib/preview-window.svelte.ts
  - resource: apps/dashboard/src/lib/components/edit-preview-layout.svelte
  - resource: apps/dashboard/src/lib/components/edit-preview-switch.svelte
  - resource: apps/dashboard/src/lib/components/preview-pending.svelte
  - resource: apps/dashboard/src/lib/components/template-list.svelte
  - resource: apps/dashboard/src/lib/components/workflow/pipeline-view.svelte
  - resource: apps/dashboard/src/lib/components/customize-posting-sheet.svelte
  - resource: apps/dashboard/src/lib/components/customize-posting-form.svelte
  - resource: apps/dashboard/src/lib/form-builder.ts
  - resource: apps/dashboard/src/lib/form-sections.ts
  - resource: apps/dashboard/src/lib/templates.ts
  - resource: apps/dashboard/src/lib/components/color-field.svelte
  - resource: apps/dashboard/src/lib/components/image-field.svelte
  - resource: packages/ui/src/questions/DropArea.svelte
  - resource: packages/ui/src/questions.css
  - resource: packages/ui/src/questions/FileField.svelte
  - resource: packages/ui/src/questions/files.ts
  - resource: apps/dashboard/src/lib/components/branding-preview.svelte
  - resource: apps/dashboard/src/lib/components/bar-chart.svelte
  - resource: apps/dashboard/src/lib/components/chart-panel.svelte
  - resource: apps/dashboard/src/lib/components/week-chart.svelte
  - resource: apps/dashboard/src/lib/components/report-stat.svelte
  - resource: apps/dashboard/src/lib/components/date-range-filter.svelte
  - resource: apps/dashboard/src/lib/report-range.ts
  - resource: apps/dashboard/src/lib/pipeline-tracker.ts
  - resource: apps/dashboard/src/lib/components/pipeline-tracker.svelte
  - resource: apps/dashboard/src/lib/components/mini-card.svelte
  - resource: apps/dashboard/src/lib/components/copy-button.svelte
  - resource: apps/dashboard/src/lib/components/candidate/header-cards.svelte
  - resource: apps/dashboard/src/lib/components/application-menu.svelte
  - resource: apps/dashboard/src/lib/components/workflow/workflow-tabs.svelte
  - resource: apps/dashboard/src/lib/components/workflow/stage-flow.svelte
  - resource: apps/dashboard/src/lib/components/workflow/guided-test.svelte
  - resource: apps/dashboard/src/lib/components/workflow/option-card.svelte
  - resource: apps/dashboard/src/lib/simulate.ts
  - resource: apps/dashboard/src/lib/components/workflow/pipeline-view.svelte
  - resource: apps/careers/src/components/JobFacts.astro
  - resource: apps/careers/src/components/Notice.astro
  - resource: apps/careers/src/components/Panel.astro
  - resource: apps/careers/src/styles/global.css
  - resource: apps/marketing/src/components/StatusBadge.astro
  - resource: apps/careers/src/lib/public-style.test.ts
  - resource: apps/marketing/src/styles/global.css
  - resource: apps/marketing/src/layouts/BaseLayout.astro
  - resource: apps/marketing/src/lib/features.ts
  - resource: apps/marketing/src/lib/contrast.test.ts
  - resource: apps/marketing/src/components/shell/SiteHeader.astro
  - resource: apps/marketing/src/components/shell/SiteFooter.astro
  - resource: apps/marketing/src/components/shell/CtaLink.astro
  - resource: apps/marketing/src/components/shell/Section.astro
  - resource: apps/marketing/src/components/shell/DarkBand.astro
  - resource: apps/marketing/src/components/shell/SellingPoint.astro
  - resource: apps/marketing/src/components/shell/ProductFrame.astro
  - resource: apps/marketing/src/components/shell/ApplicantPair.astro
  - resource: apps/marketing/src/components/shell/LevelChip.astro
  - resource: apps/marketing/src/components/shell/SourcedNumber.astro
  - resource: apps/marketing/src/components/shell/PlanRows.astro
  - resource: apps/marketing/src/components/shell/QuestionsList.astro
  - resource: apps/marketing/src/components/shell/ClosingBand.astro
  - resource: apps/marketing/src/components/shell/PageIntro.astro
  - resource: apps/marketing/src/components/shell/PageHero.astro
  - resource: apps/marketing/src/components/shell/RuledList.astro
  - resource: apps/marketing/src/components/shell/ProofList.astro
  - resource: apps/marketing/src/components/Claim.astro
  - resource: apps/marketing/src/components/SourceList.astro
---
# ScreenHire design system

> Status: **v0.3 (2026-10-25)**. Decisions: [ADR-0009](../adr/0009-design-system-warm-grid.md), amended by
> [ADR-0010](../adr/0010-design-system-v0-2.md), [ADR-0013](../adr/0013-design-system-v0-3.md) and
> [ADR-0016](../adr/0016-design-system-v0-3-review-amendments.md). Visual reference: the
> owner's "ScreenHire design v0.3 review" canvas; where it and this file differ, this file wins. The dashboard is
> built to v0.3: every screen, and every component in section 11 with its example in the style guide. The careers and
> marketing pages are built to it too, as sections 4.3 and 4.4 describe, and the marketing site has its own rules in
> section 22 ([ADR-0030](../adr/0030-marketing-site-direction.md)). Section 21 records what v0.3 changed on each screen.

This file is the one source of truth for how ScreenHire looks, reads and behaves on screen. CLAUDE.md, the rules in
`.claude/rules/`, the page skills and the `design-review` agent point here and do not restate it. To change the
design, change this file (and its ADR when a decision changes); everything else follows.

**Scope.** Everything here applies to the recruiter dashboard. Candidate and marketing pages follow the principles
(section 1), the review checklist where it applies, colour (section 3.4), the radii and spacing of section 5, layers
(section 6), words (section 12) and employer branding (section 18), and keep their own type scale (section 4.3), their
own page widths (S6 is the dashboard's: a careers page is one centred reading column) and the public-page rule
(`.claude/rules/astro-public.md`). The marketing site follows section 22 for its bands, type, space and the checklist's
Public pages group, and [the marketing messaging doc](marketing-site.md) for what it says.

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

- **S1** The page uses a template from section 7 (list, detail, read-only, form, report, builder).
- **S2** One header band: title, its status badges beside it, the primary action on the right, then the facts row or
  tabs. A description is one plain sentence. On anything people edit, Last updated is the last fact, with History
  beside it (T6).
- **S3** Every section is a panel with its title inside a fixed-height header. Nothing readable sits on the page
  background.
- **S4** Panels in a row share a top edge. No card inside a card.
- **S5** Insets only inside a surface. Panels and cards take the soft surface shadow; raised and overlay layers take
  theirs (section 6). Nothing else casts a shadow.
- **S6** Pages fill the window: a page's content container has no max-width, and tables and boards never stop short.
  Text keeps a measure: a long paragraph stops at 72 characters and a form at 688px, except an editor whose panels sit
  in rows (Branding, section 18), while short sentences, callouts and notices use the full width. Content elements get a max-width, never a fixed width.
- **S7** No side column beside a table or a board: secondary things move to a tab, a sheet or the header. A detail page
  puts the record's facts in its header band, above tabs in a fixed order that hold its content; nothing sits at the
  window's far edge, where a wide screen puts it out of sight (section 7). The one exception is the Settings menu, the
  200px column left of a settings page's content (section 11).
- **S8** Moving between sibling pages never shifts the frame. Pages in one set (the settings pages, a record's tabs)
  keep one header height, so the menu, tabs and content start at the same place on each: a header's title row is at
  least a button's height, and a fact like Updated and History sits on that row, never on a row only some pages have.
  A sentence about the page goes in its panel's header, not under the title. The window keeps room for its scrollbar
  (`scrollbar-gutter: stable` on `html`), so a page that scrolls lines up with one that does not.
- **S9** Design for how many there could be, not how many there are today. Before choosing a layout, ask what happens at
  20 or 50 items. Tabs, toggles and cards side by side are for a set that cannot grow, such as a record's peer views.
  Anything that can grow (integrations, boards, templates, settings pages) is a list, table or grid that takes more with
  no redesign, with search past about 10, so adding one never changes the layout. The Integrations directory is the worked
  example: a row per integration, filters by kind, and a twentieth integration is one more entry in a list.

**Actions**

- **A1** At most one primary (filled) button per page, sheet or dialog.
- **A2** Destructive actions live in a menu and confirm in a dialog. Section 10 says what is not destructive: an action
  the same screen can undo, and removing a row from an unsaved builder draft.
- **A3** A feature that does not exist yet is hidden, not shown disabled. Disabled is only for a control this screen
  can enable (Save until the form is valid), with the reason beside it.
- **A4** Icon-only buttons have an `aria-label` and a tooltip.
- **A5** Links go somewhere: another page, tab or record, as underlined text; navigation and a table row's name
  underline on hover. Buttons do something: act, or open a sheet, a menu or a dialog. A link never opens a sheet on the
  page it sits on, and a button never navigates, with one exception: a page's own action that opens another page to do it (New job, Edit,
  Cancel on a form, Back) is an outline or primary button that renders as a link (`<a>`), because it starts that
  action. A sign-in round trip to another service (Add to Slack, Link my Slack account) is a button too: the server
  must create the address first, so the button leaves for the other site once it answers. A link to a record may
  arrive with the sheet for its work open there, as a requested scorecard's does. A
  menu item that opens a page is a link inside the menu. Badges, pills and counts are never clickable,
  except a count button in a header (T4).
- **A6** A button with a text label has a border or a fill, never bare coloured text, and its label starts with a verb
  (Mark done, not Complete). An icon button (More, Close) may be bare, with its label in a tooltip (A4). A sortable
  column header is the one bare text button, marked by its sort icon (L2). A toggle in a labelled group is a choice,
  not an action: its label names the choice, the pressed one takes `accent` and the others stay bare (section 11). A
  button never looks like a badge.
- **A7** Reorder by dragging a grip. Clicking the grip opens Move up, Move down, Move to top and Move to bottom, and
  Space with the arrow keys moves it. No up and down arrow buttons.
- **A8** An editor whose result someone else sees (a questionnaire, a scorecard, an email template, a careers page)
  shows one thing at a time, at every width: an Edit / Preview switch at the top, with a pencil and an eye. The editor
  gets the room and the person's attention, and the preview is one press away, because showing both at once competes
  for both (ADR-0026). A read-only view keeps the switch and names its first choice for what it shows, Questions with a
  list icon, because nothing there can be edited (E3). Preview shows the result at its real size and adds no sentence
  about itself, unless it follows unsaved edits to something candidates already see (Branding), and then one line says so and that nothing is live until Save; a window icon beside the switch pops it out for someone who wants both, and the window follows the edits. The email composer keeps the switch and has no window: it is one email in a sheet, not wording
  others reuse. The Customize sheet does the same: its preview is the API's answer for one board's text, fetched when
  the preview opens, and it does not follow the edits.
- **A9** Anything that opens in place says so: a `chevron-down` at the end of the row, turning up when it is open, and
  the whole row is the control, with `aria-expanded`. A row with no chevron never expands. Expand all and Collapse all
  sit above a list of them.
- **A10** Anything that can be pressed shows the hand cursor, and a disabled control shows not-allowed. That covers
  buttons, tabs, switches, checkboxes, menu items and options, whichever component draws them, and a disabled text
  field or the label of a disabled control. It is one rule in the
  base layer of `packages/ui/src/theme.css`, so no component sets a cursor. A new kind of control joins that rule.
- **A11** An action says how far it reaches: what changes for others says who or what it touches, on its button or in
  its notice ("Published version 2. Jobs created from now on start from it.").
- **A12** Any file input takes a dropped file as well as Choose file, and is the Drop area (section 11), never a bare
  `<input type="file">`: the dashboard's `Input` has no file type. Every uploaded file shows its name and size with
  Remove, and a wrong type or a file too large is refused in the field's error, in words, before it is sent.

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
  A page that starts work outside ScreenHire, such as adding DNS records, says what will happen before anyone starts, in a
  How it works panel with its steps.
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
  is plain text. Steps are square markers on a line. Counts and steps never share a shape. The
  pipeline tracker is a bar of stages rather than markers, because where a person stands is a header's main fact.
- **T5** Times follow section 13: time ago in text, dates in tables, the browser's locale, the exact time on hover.
- **T6** Anything people edit says who updated it last and how long ago: the last fact in its header ("2 hours ago by
  Avery Quinn") with History beside it, and a Last updated column in its list. ScreenHire's own templates and roles say
  "by ScreenHire". Something nothing has changed since it was created says so: the fact is Created, "2 hours ago by
  Avery Quinn", never Updated with nobody, and no second Created fact repeats it (W7). History opens a sheet with every
  change, newest first and grouped by day: who, when and what, with before and after on request. A record whose row
  holds a secret gets a history only once the secret (or what derives it) moves to a row of its own, as a webhook
  endpoint's did (ADR-0016), and the History never shows it. A webhook endpoint's header also keeps Secret rotated
  beside Last updated.

**Icons and shortcuts**

- **I1** Lucide only, at the sizes in section 14, except the Google and Microsoft marks on the sign-in buttons, which
  their branding guidelines require (section 14). Navigation and main buttons have an icon and a label.
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
  A tab's count is a plain number in the same grey count badge (Job postings 7), never a fraction, which reads as a score
  and can claim more than the boards confirm. A state that needs a look is the record's status badge beside its title (T2), never a coloured tab, a dot or a lone icon (W7).
- **E3** Read-only says why and what to do. Editable shows its controls.

**Tables**

- **L1** Two text styles. The row's name is semibold on one line, with at most one badge after it (Built in, You).
  Every other cell is one line of body text, cut with an ellipsis and shown in full in a tooltip, so rows keep one
  height. A row never grows: what it needs to say goes in a notice above the table, a tooltip or its menu. No code font, pills or icons in cells except status badges and a person's avatar. A template variable reads
  as a muted word (job title). The row opens the record. A cell with nothing in it is blank, its word ("No draft")
  for screen readers only, never a filler word repeated down the column; a value that means something, such as
  Never used, keeps its words.
- **L2** Every table sorts. Each column header is a button: its text and a faint up-down icon, which becomes an arrow
  on the sorted column, and a second click reverses it. A table opens on one default sort: things people edit by Last
  updated, newest first; people, keys and endpoints by name; a job's candidates with those needing attention first. A
  table with a natural order (roles, by access) keeps it until someone sorts. The dashboard remembers each person's
  sort. On a phone a Sort chip beside the filters offers the same choices. A fixed grid, such as the permissions
  table, does not sort.
- **L3** The same button repeated down a list belongs in the row's menu (Message, Invite to apply, Remove), not as a
  button down a column. It holds for a builder's rows too: a workflow stage's Edit automations, Choose scorecard and
  Remove stage are one menu on the row, and a job's candidate row and board card have one menu with Move to.
- **L4** A table in a flush panel starts and ends at the panel's own padding (18px, 16px on a phone): its first and last
  columns line up with the panel's title and never sit closer to the edge than it. `Panel` sets this for any table in a
  flush body.

**Verification and access** (the `verification-guardrails` agent owns the first)

- **G1** No score or percentage, no reject control, AI assistance styled as information, and every result keeps its
  next step (section 16). The candidate's scorecard rating never draws on verification output, AI signals or the stage
  (section 11, Rating).
- **X1** WCAG 2.2 AA: visible focus ring, labels on inputs, landmarks and headings, text contrast at least 4.5:1, and
  a non-drag alternative for every drag (section 17).

**Public pages** (marketing pages, section 22. W11, F7 and X2 to X4 hold for careers pages too)

- **M1** Boldness is spent in one place. The real product, shown big and sharp, is the one memorable element on a page, and
  the home page opens with the real-versus-flagged applicants, not a generic dashboard. Everything around it stays quiet.
- **M2** No gradient washes and no purple-to-blue gradients.
- **M3** No small label above every heading and no icon tile above every heading.
- **M4** No identical card grid with one radius and one shadow, and no card inside a card. A layout follows its content: a
  list, a sentence, a table, the product frame.
- **M5** No meta strings joined with middle dots (W3) and no all-caps labels (W2).
- **M6** No numbered 01 / 02 markers. Structure (a border, a number, a label) appears only when it carries information.
- **M7** At most one deliberate motion moment a page, none with reduced motion, and no fade-up on every section.
- **M8** A transition names its properties. Never `transition-all`.
- **M9** No one word in a headline set apart by colour, italic or bold.
- **M10** No grey text on a coloured band and no bright single accent on a near-black band. Each band's text, links, focus
  ring and chips keep the ratios in the section 22 table.
- **M11** Every number has a source the page links, with its year. No testimonial, customer logo or award, and no claim for
  a feature that is not built.
- **M12** Copy follows `docs/design/marketing-site.md`: none of its AI fluff words, AI named only where it is a feature, no
  reason code, rule name, threshold, score or percentage of AI, and never that ScreenHire rejects anyone.
- **W11** Prices, counts and every figure in a comparison use `tabular-nums`. Headings take `text-balance`, so none ends on
  one word, and a number and its unit, or the name ScreenHire, never break across lines (a no-break space).
- **F7** Every image has `width`, `height` and `alt` (empty for a decorative one), loads lazily below the fold, and the one
  hero image has `fetchpriority="high"`. The product frame takes its size from the file.
- **X2** A public page starts with a skip link to `main` that shows on focus, and a sticky header never covers a focused
  control or an anchor's target (`scroll-padding-top`).
- **X3** Focus uses `:focus-visible`. `outline: none` appears only beside a ring that replaces it.
- **X4** A decorative icon is `aria-hidden`, an icon that carries meaning has its text beside it (T1), and the viewport
  never disables zoom.

## 3. Colour

Semantic names are the API; raw values live only in `packages/ui` (`tokens.css`, mapped in `theme.css`) and reach
components as Tailwind classes. The one exception is the Google and Microsoft marks on the sign-in buttons, which keep
their companies' own colours (section 14). The token column is the name in code: `background` is `bg-background`,
`muted-foreground` is `text-muted-foreground`. Colours did not change in v0.2 or v0.3, except that Reports added the two
chart colours (section 3.5).

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
| Clear | No concerns, No problems found, Delivering, Done; Hired, a candidate who got the job; Delivered, a webhook delivery; Active, an API key; Live, the live updates connection; Connected, a Slack workspace; Verified, a sending domain, and Found, one of its DNS records; Explained, a Verification reason a person marked explained (`circle-check`). Booked, an interview the candidate picked a time for (`calendar-check`). Completed, a data request the team answered (`circle-check`). Open, a job taking applications; Live, a job showing on a board (`door-open`) | `status-clear` | `#0B5D4F` | `#E3F1EC` | `#0F766E` |
| Review | Review suggested (`eye`); Overdue by 2 days (`clock-alert`) | `status-review` | `#7A4205` | `#FBEFD3` | `#C77700` |
| Verify | Verification suggested (`id-card`). Needs fixes, for a refused posting or a workflow with problems, and for a posting a board did not answer (Error), a webhook Turned off after failures, a delivery that Failed, a Slack workspace that is Not posting or Disconnected, a sending domain with Records to add and a record that is Not found or has a Different value, and a candidate whose address Email bounced or was Marked as spam (`wrench`). Unsafe file, flagged by the malware scan (`shield-x`) | `status-verify` | `#9A2F17` | `#F9E0D6` | `#B4461F` |
| AI (information) | No signs, Some signs or Strong signs of AI help (`sparkles`) | `status-ai` | `#2B2788` | `#ECEBFA` | `#2B2788` |
| Grey: waiting or not started | Sent, Queued, Submitted, Removing and Removal requested, waiting on a board; Pending, a webhook delivery waiting for its next try; Waiting for the candidate, an interview offered and not yet booked, and Waiting for a new time, a booked interview whose new times the team offered (`clock-4`). Scanning and Checks pending, ScreenHire at work; Preparing copy, a data request's file being built; Connecting, the live updates connection (`loader-circle`). Closed (`door-closed`). Draft (`file-pen-line`). Archived, a template out of the pickers until someone restores it (`archive`). Saved, a candidate who left the job without a rejection, kept for future roles (`bookmark`). Not in effect: Not posted, a board the job is not on; Removed, a board that took the job down; Off, a webhook someone turned off; Skipped, a delivery not sent; Revoked, an API key; Rejected and Withdrawn, how a candidate left a job; Offline, the live updates connection; Cancelled and Expired, an interview that ended; Declined, a data request the team turned down; Turned off and Trusted, a group of verification checks the organization turned off and a domain or network it trusts, both listed under Not checked on the candidate's verification (`circle-minus`) | `muted-foreground` on `secondary` | `#5F5A52` | `#F4F1EB` | `#5F5A52` |

Status colours are never used for brand, decoration or facts. No status uses the warning triangle: it marks only the
verification cards that fed a level (section 16). Counts use tabular figures.

In code, every status is a name in `statuses` (`#lib/status.ts`) with its icon and colour, and `StatusBadge` draws it.
The icon colour is the `status-*-mark` token (`status-ai-mark` for AI), and grey statuses use `muted-foreground`. In
lists and tables only the review and verify statuses get a filled badge (T3). AI assistance stays quiet in a list at
every level, because it is information.

**Overdue** is the hiring team's status, never the candidate's. An application is overdue once it has been in its stage
longer than the stage's time limit (`slaDays`), which measures how long the team has kept the candidate waiting. It
takes the review colour and the `clock-alert` icon because it asks a person to act. It never takes the verify colour:
that sits beside verification results and would read as a concern about the candidate. The label says by how much, in the
largest whole unit: "Overdue by 2 days", "Overdue by 5 hours". The due date and time are in its tooltip (section 13). It
is a filled badge wherever it shows (T3), because it needs attention. An application within its time limit shows
nothing. A data request past its due date (the team's own target, in Organization, Data requests) takes the same status and badge; one within its due date shows no badge.
A job board's estimate that has run out, said in a posting's status tooltip, is a fact about the board
and not this status.

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
higher layers get lighter (section 6), and the surface shadow is darker so it still shows on the dark page. The theme also sets
`color-scheme` (light on `:root`, dark on `.dark`), so the browser's own scrollbars, date pickers and native controls follow
it instead of showing white on a dark page.

### 3.4 Candidate and marketing pages

| Token | Light | Dark |
|---|---|---|
| `candidate-background` | `#FBF6EE` | `#1A1714` |
| `candidate-surface` | `#FFFFFF` | `#23201C` |
| `candidate-border` / `candidate-input-border` | `#EDE3D3` / `#D9CCB8` | `#332E28` / `#4A443B` |
| `candidate-foreground` / `candidate-muted-foreground` | `#231F1A` / `#6A5D4D` | `#F2EDE6` / `#B3ABA0` |
| `candidate-chip` | `#F3E7D6` (text `#5A4630`) | `#2E2A25` |
| `candidate-cta` | `#C2462F` (white text, 4.98:1) | `#F08A6E` (text `#1A1310`) |

On tenant careers pages, `candidate-cta` and the header mark take the employer brand tokens, and `candidate-header`
and `candidate-header-foreground` (the header band, default the page background and ink) take the second colour
(section 18).

### 3.5 Charts

Charts take their own colours, never a status colour (T1): one blue, away from every status hue (teal, amber,
red-orange, indigo).

| Token | Light | Dark | Use |
|---|---|---|---|
| `chart-1` | `#1E5BA1` | `#5D94DA` | The series of a one-series chart, or the part in focus (Hires) |
| `chart-2` | `#6AA7F4` | `#2E64A6` | The whole that part belongs to (Applications) |

- A chart with one series uses `chart-1` for every bar, and the panel's title names it, so it has no legend. A funnel is
  one series: the bars' order already says the order of the stages. Two series have a legend above the bars. The weekly
  chart's two series are Applications in `chart-2`, the whole, and Hires in `chart-1`, the part in focus.
- Text never takes a chart colour: names, values and legends are text tokens, and a swatch beside them carries the
  colour.
- `chart-2` is under 3:1 against the card, so every bar has its value written at its tip and every chart has its table
  view. The one exception is the weekly chart, where fifty columns leave no room for a value at each tip: the week
  pointed at, tapped or reached with the arrow keys is read out above the columns, and the table view holds every number. The pair passes the dataviz palette validator's checks (lightness band, chroma, colour-blind and normal-vision
  separation) on `card` in both themes; check it again after changing either value.
- A third series needs a third token checked the same way. Never a generated or status colour.

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
| `reading` | 16 / 26 | Hanken 400 | Resumes, notes, verification explanations |
| `caption` | 12 / 16 | Hanken 500 or 600 | Fact labels, pills, badges, table headers |
| `code` | 12 / 16 | JetBrains Mono 400 | Ids, codes, key caps |

12px is the floor. Text never shrinks on phones; only `page-title` steps down, below `md`. The tokens are Tailwind
`--text-*` tokens in `packages/ui` (`text-page-title`, `text-body`, `text-caption`, ...), each with its line height,
weight and tracking. The family is its own class: `font-display` for the two titles, `font-mono` for `code`.

### 4.2 Rules

- Sentence case everywhere, headings and buttons included. No all-caps text except acronyms.
- One `h1` per page. Panel titles are `h2`.

### 4.3 Candidate and marketing pages

Titles take the employer's heading font where one is set (section 18), otherwise Bricolage Grotesque. Base 15px, display 30px, inputs 16px (no zoom on iOS), hints under a field 13px: `text-candidate-body`,
`text-candidate-input` and `text-candidate-hint` in `packages/ui`. A form of several pages shows `PageProgress`
(`packages/ui/src/questions`) above its fields: a bar of one segment per step, then "Step 2 of 4" and "9 of 24
answered" in the hint size, the step semibold; each page's heading is its section's name. Marketing type is section 22.4. The dashboard's
questionnaire preview shows the candidate base with `text-candidate-body`.

A careers page's `h1` is `font-display text-candidate-title` (30 / 36, weight 700, -0.02em), so every title takes the
employer's heading font. A `Panel` title is `font-display text-heading` (16 / 24), and only the privacy notice keeps its
title in the body font (section 18). A card's or step's title is `font-display text-panel-title` (20 / 28), and a reading
line is `text-candidate-body`. Careers markup uses no Tailwind size
(`text-sm`, `text-lg`, `text-xl`, `text-3xl`). Until section 22 restyles a marketing page, its titles use Tailwind's scale in this order: the `h1` at
`text-4xl` (`text-5xl` on the home page) and `sm:text-6xl`, a section's `h2` at `text-3xl` (`text-4xl` on the home page), a
band's `h2` at `text-2xl`, a card's `h3` at `text-xl`, a home page step at `text-lg`, a lead at `text-xl`, body at
`text-base`, small text at `text-sm` and captions at `text-xs`.

### 4.4 Rules for public pages

The careers and marketing pages follow W1, W2, W3, W7 and A10 as well, with these additions. `public-style.test.ts`
(`apps/careers/src/lib`) fails on the first three.

- Nothing under 12px, and no pixel font size in markup: use `text-candidate-body` (15px) and `text-candidate-hint`
  (13px) on careers pages, and Tailwind's scale on marketing pages.
- No capitals and no code font for ordinary words. A small line above a title or a card's heading is a kicker
  (marketing pages have none since section 22, M3): sentence case, body face, 14px. The code font is for codes only: reason codes, a
  notice version.
- No cursor class on a component. Buttons, summaries, selects and switch labels get the hand from `theme.css` (A10).
- A kicker adds to the breadcrumb and never repeats it (W7): a page whose kicker would be its own name has none.
- A job's facts (where, how, contract, pay) are chips from one component, `JobFacts`, on the board, the job and the apply
  form. The workplace type is left out when the location already says it. Chips are never links or buttons (A5).
- A message about what just happened is `Notice`: an icon, the message, and one quieter line for what to do next. A page
  never builds its own bordered block for it.
- Every step of a form names itself in a visible heading, so "Step 2 of 3" is never the only clue. The review step says
  who receives the answers and when.
- A page that cannot open (an unknown, expired or closed link) says what happened in one sentence and who to contact:
  the hiring team at the employer, by the address they write from. "Open it again" is never the only advice.
- A long page of prose (the privacy page) starts with an "On this page" list of links to its headings.
- Header, breadcrumb and footer links are 44px tall on a coarse pointer and at least 24px with a mouse (WCAG 2.5.8):
  `pointer-coarse:min-h-11` on marketing pages. Careers pages use a plain `min-h-11`, so they are 44px on any pointer.
- Shape and space come from section 5. Radius is a token: 8px for buttons and inputs (`rounded-lg`), 12px for panels
  (`rounded-panel`), full for chips and badges, and never `rounded-[...]`. Margins, padding and gaps sit on the 4px grid
  (4, 8, 12, 16, 20, 24, 32, 40, 48, which is `1`, `2`, `3`, `4`, `5`, `6`, `8`, `10` and `12`): no half steps, no `7`,
  `9` or `11`. A button is 44px high (`min-h-11` on careers pages, `h-11` on marketing pages), and a careers input or
  choice is 48px (`min-h-12`).
- A section is a panel (S3, section 6): the card colour, a 1px edge, `rounded-panel` and `shadow-surface`, with its
  title inside a 56px header. Careers pages draw one with `Panel.astro` (`title` and `id`, or only a screen-reader
  `label`, an `action` slot for a Change button, a `footer` slot) in the neutral candidate tokens, so employer colours
  never restyle it. Its title takes the employer's heading font, except in the privacy notice (`neutral`). A job card on the board, `Notice` and the error summary take the same surface and shadow. Marketing
  cards are `rounded-panel border border-border bg-card shadow-surface` until section 22 restyles the page, which draws bands, lists
  and the product frame instead. A list of reasons inside a panel is an inset
  (`bg-secondary`, no edge). The shared question controls stay unpanelled in their form. `public-style.test.ts` fails on
  a panel without the shadow.
- Employer colours sit on the page background and on the white panel, and the contrast check covers both
  (`BrandPalette`): the brand colour used as text and for borders and focus rings is checked on `#fbf6ee` and on `#ffffff`
  before it saves.
- A status on a marketing page is `StatusBadge.astro` (T1): the 24px badge with its icon in the mark colour and the
  label in 12px semibold, or `quiet` for a calm state in a list (T3), the same as the dashboard's. A page never builds
  its own chip from `bg-status-*`. Careers pages show no status badge: each page names its state in its title and in
  `Notice`, and the verification result is never shown to a candidate.
- A marketing call to action is `CtaLink` (22.9), 44px high with an 8px radius. A careers button is `.button-primary` or `.button-secondary` in the careers stylesheet, the same size, filled with the
  employer's brand colour.
- A careers form with steps has one row of buttons: the primary button first in the document and at the right on a wide
  screen, Back beside it, and Save and finish later at the left. A page with one action leaves it at the left, under its
  content.

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
| Content widths | Pages fill the window: a page's content container has no max-width. Long paragraphs stop at 72ch (`max-w-measure`, 60ch on the marketing site, 22.5) and forms at 688px (`max-w-form`). If a layout needs a limit, the element gets a max-width, never the page |
| Board columns | 296px; an empty stage shrinks to 188px, and the stages that end a candidacy (Hired, Rejected, Withdrawn) share one 188px Closed column that counts each; each of its rows (and Saved) with someone in it is a button with a chevron (A9) that opens in place to that stage's candidates as plain cards linking to the candidate, collapsed to start |

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
| List | Candidates (Current and Saved), jobs, templates, My tasks; the team, roles, API keys and webhooks in Settings | Header band (title, count, page actions); one full-width table panel whose header holds its title, search and filters, with no side column (S7). A list with no search (My tasks) puts its toggles in a row under the panel header |
| Detail | A candidate, a job, a webhook endpoint | Header band (identity, status badges, a contact line for a person, actions, the facts row); tabs in a fixed order under it; the content in panels below, in a grid of two columns on a wide page and one on a narrow one (S7) |
| Read-only view | A ScreenHire template, anything you cannot edit | As detail; data as labels and values, never disabled inputs; it says why it is read only and what to do (E3); Copy or Edit as the primary action |
| Form | Creating or editing a record; a settings page such as Organization or Verification (Branding is a wide editor, section 18) | One column, 688px wide, labels above fields, actions at the end |
| Report | Reports | Header band (title, the report's one action such as Export CSV, then one row of filters on its bottom edge: the date range first, then Job, then Compare; the row wraps by content on a phone rather than cutting a control off), a row of headline numbers, then the report's panels in a grid of two columns from 1024px of content and one below, the main chart across both. Each chart is a Chart panel (section 11). Filters scope every panel, never one panel alone |
| Builder | The workflow graph; the questionnaire, scorecard and email template editors | Header band, then a full-width work area, with the Edit / Preview switch when someone else sees the result (A8). A form in that area keeps the 688px column (S6). The graph's nodes edit through the same sheets and forms as the list view beside it, never a second editor, and nothing in it needs dragging: pressing a node and the plus buttons do every edit. See Workflow graph in section 11. A validation message shows on the node it is about, as it does under its form control (W7) |

Sign-in, onboarding and error pages sit outside the app shell, with no sidebar. A title that changes in place, such as
"Check your email" after a link is sent, takes the focus.

- **Sign-in card:** signing in and finishing an email link (`SignInCard`). One card, 416px at most, centred on the page
  background with a 16px gutter on a phone, and nothing outside it (S3). There is no brand bar, because the card carries
  the mark. The card holds, in order:
  - the 40px logo mark;
  - the page's h1, "Sign in to ScreenHire";
  - one muted line, "New here? Signing in creates your account.";
  - any error from a provider that sent the person back;
  - each configured provider as a full-width outline button, "Continue with Google". Google and Microsoft carry their
    own marks (section 14). Any other provider, such as Dev SSO or an organization's own OIDC, has `log-in`;
  - an "or" divider, only when both providers and email links are offered;
  - the Work email field and the one primary button, "Email me a sign-in link".

  "Check your email", "Finish signing in" and "This link doesn't work" are the same card with their own title. There is
  no footer line, because the app has no Terms or Privacy addresses yet. Add the line with the addresses when they exist.
- **Onboarding, invitations, connecting an assistant and error pages:** the brand bar takes the top bar's place, then
  the form template.

- **Header band:** a white strip under the top bar. Title first, status badges beside it, actions on the right, then
  a labelled facts row (a short label 12px above each value 14px, a divider between them) or tabs on its bottom edge.
  A record with a pipeline (a candidate) puts the pipeline tracker and its mini cards between the title and the tabs.
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
| Navigation | Sidebar as a sheet behind a menu button; Back on detail pages | Icon rail, labels in tooltips; a button in the top bar opens the full sidebar, which holds the Views menus | Full sidebar | Full sidebar |
| Columns | One | One | A detail page's panels in two columns from 1024px of content | Same, filling the window |
| Header band | Facts in a two-column grid | Facts wrap | One line of facts where they fit; beside the full sidebar they may wrap | One line of facts |
| Actions | Primary in a bar at the bottom; the rest in More. A form keeps Cancel and its primary at its end (section 7) | In the header; extras fold into More | In the header | In the header |
| Tables | Rows become two-line list items, with a Sort chip beside the filters (L2); a long list shows 50 at a time and Show more adds the next 50, saying how many are shown of how many there are (S9) | Scroll inside the panel; first column sticks, and so does a row menu at the end (the Jobs table's), so it stays reachable. While it scrolls the row's link covers that cell only | Full; a table wider than its panel beside the full sidebar scrolls inside it, as on a tablet | Full, optional columns may show |

The sidebar is shadcn-svelte's `Sidebar`: `collapsible="icon"` gives the rail and its mobile mode gives the sheet.
On a phone the workflow graph is for reading: its nodes cannot be pressed and have no plus buttons, one line says to edit in Pipeline, and the
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
| Confirm | A dialog, only to confirm or decide, or to name something (Save view, Rename view). A menu is the gentler choice. |
| Create | A full page for anything with several steps, such as a new job. |
| Tabs | Peer views of one record, under its header. Never steps in a sequence. |

Forward and back slide (200ms); everything honours `prefers-reduced-motion`.

## 10. Actions

| Emphasis | Looks like | Use |
|---|---|---|
| Primary | Filled `primary` | One per page, sheet or dialog. It moves the work forward. |
| Secondary | White, `input` edge | Supporting actions, and Cancel |
| Quiet | Icon button | Row actions, usually the More menu. Every icon button has a tooltip and an `aria-label`. |
| Destructive | `destructive` item in a menu | Never the page's main action. Confirmed in a dialog. An action the same screen can undo (taking a job down from one board, which Post puts back; deleting a saved view, with an Undo notice) is not destructive and needs no dialog, and neither is removing a row from an unsaved builder draft (a stage, a question, a condition): nothing changes until Save, and leaving asks first. |

Features that do not exist yet are hidden (A3). Links go somewhere and buttons do something (A5, A6).

## 11. Components

The catalogue. A screen is built from these; a new pattern joins this table before a second screen uses it. Code lives
in `apps/dashboard/src/lib/components` (shadcn-svelte under `ui/` is ours to edit). The style guide page, `/style-guide`
in a development build, shows every component in each state, and its test fails when a row here has no example.

| Component | Built on | Rules |
|---|---|---|
| Header band, Panel | Our components (`HeaderBand` with `MetaItem` for a meta line, `Fact` for a labelled facts row, or its `summary` slot for a pipeline tracker and mini cards; `Panel`) | Section 7. A candidate's header is the name and one status badge, the actions on the right, the pipeline tracker, then three mini cards. A meta line, where a page still has one, is icons and values that read on their own, the label only for screen readers (W9). Pages whose values need their labels (a job's salary, a template's version) keep the facts row. A panel with only a screen-reader `label` and nothing for its header draws no header band (Reports' empty and error states). The pages of a set whose menu or tabs sit under the band (the settings pages of Organization and Hiring setup) pass `oneRow`: the title's row never wraps, at any width, so the menu starts at the same place on every page of the set (S8). The title keeps its width and the actions give way: Last updated truncates beside the title with an ellipsis, and History keeps its icon with its name for screen readers once the band is narrower than 448px. On a phone a page's other actions move to the bar at the bottom (section 8), as Email domain's Check records and More do, so the row holds only the title, its count or status and Last updated. |
| Status badge | `StatusBadge` on shadcn `Badge` (`clear`, `review`, `verify`, `ai`, `neutral`; `quiet` for calm states in lists) | Icon, colour, label (T1, T3): 24px high, the 14px icon in its icon colour, then the label in 12px semibold. It takes a status name from `#lib/status.ts`; each status's icon is in section 3.2 |
| Overdue badge | `OverdueBadge` on `StatusBadge` and `Hint` | Section 3.2: the review badge, "Overdue by" and how long, the due time in a tooltip that opens on hover, focus or tap. First in the stage cell of a candidate table and list item, first in a board card's status row, and beside "Here 3 days" in an application's pipeline tracker (S2, T2). Never on a hired, rejected or withdrawn stage |
| Fact pill | `Badge` muted variant | `secondary` fill, `input` edge, 12px; only on white surfaces. For tags, for facts in a list item or a sheet, and a policy beside a verification level; never in a labelled facts row or a table cell (W3, L1) |
| Count | `Badge` count variant | The grey count badge (min 28px) after its label on a control (T4); plain text in a table |
| Toggle | shadcn `Toggle`, in a labelled `role="group"`, or the rich text editor's `role="toolbar"` | Filters with a few choices and peer views (Table, Board; the apply form and each follow-up questionnaire in the workflow builder), and the formatting toggles of the rich text editor. The pressed one takes `accent`; its count follows the label in the count badge, white on the pressed one (T4). One line that scrolls inside its panel, never wraps (F5). In a narrow container a few short toggles whose every choice matters at a glance (a job's authenticity levels) stack one per line instead |
| More menu | `MoreMenu` on shadcn `DropdownMenu` | An icon button with its label in a tooltip (A4). Holds extra actions, folded ones on phones and destructive items (A2, section 10) |
| Step marker | `StepMarkers` (across), `StepLine` (down) | 28px square, radius 7, white with an edge, joined by a line: across for progress, down for steps to do in order (a domain's setup). A workflow's stages are the Stage flow, below |
| Workflow graph | `WorkflowGraph` (`workflow/workflow-graph.svelte`), `StepNode`, `RoutedEdge`, laid out by `toGraph` | The Graph tab of a workflow: the stages run left to right as the backbone, after a "Whole workflow" lane, and each one's automations hang beneath it, one column each, read down as When, If, Then. Every line is orthogonal, has an arrowhead and runs one way, never through a node; a move to a stage rises beside its column and runs along a lane above the stages, labelled "Moves to", the only words a line carries. A node has its kind's icon and word and one look per kind, in tokens. Where the editor can change it, the whole node is one button with the hover, the focus ring and the hand, and opens its form; a plus where a condition, an action or an automation would go adds one. Hovering or focusing a node lights its path and quietens the rest by edge alone, text keeping its contrast, with no motion for reduced motion; a test's path is lit the same way. An empty workflow says what to do, with one button, under the stages. It opens at full size, because node text is 12px there and W1 sets that as the floor: centred when the workflow is narrower than the canvas, otherwise from the top left, with the rest a pan away and Fit for an overview, so 20 stages and 50 automations read the same (S9). End stages stand after the open stages, set apart and named End stage, and no line of the backbone reaches them, because an application can finish at any of them from any stage |
| Stage flow | `StageFlow` (`workflow/stage-flow.svelte`), in the Stages panel of `WorkflowSummary`, on a template's page and a job's workflow page wherever the workflow reads only (E3) | A workflow's stages in order, as a row of columns where every stage fits and the vertical list where they do not, so 5 stages and 20 read alike and the page never scrolls sideways (S9, F1). A stage is its step marker and name, then what is attached as short facts in one order: the scorecard's name (the word Scorecard for screen readers only, W9), the time limit ("3-day limit") and the automations that fire on it ("2 automations"), which leaves out any that are off; each is an icon and a muted line that wraps, in one style. A stage with nothing attached shows only its name, and its kind is not repeated (W6). Columns are 12rem at least. The choice is made in CSS from the container's width and the number of stages, so the first paint is right and nothing is measured by script (S8, F2); the vertical list draws the line down between markers, the row a hairline between columns. Keyboard and screen reader order is the stages' order in both, with no reordering. The stages that end a candidacy stay in the panel's footer. The panel has a row of its own, so the columns have the page's width; the builder's Pipeline view keeps its editable rows |
| Guided test | `GuidedTest` (`workflow/guided-test.svelte`) in the Simulate tab's panel, with `OptionCard` and `StepMarkers`; the rules are in `#lib/simulate.ts` | A test of a workflow as three questions, one decision on the screen at a time, with the step markers above (T4) and the question as the h3, which takes the focus when it appears. 1. What happened: a card for each moment some automation that is on reacts to (such as A candidate applied, A candidate moved to a stage, A scorecard came in, A time limit passed), with how many automations react. A second screen asks which stage or questionnaire only when automations react in more than one. 2. Who is the candidate: a card for each ready-made candidate, built from what those automations read (Meets every requirement, Fails a knockout, Has verification signals, Has a reviewer against them, Asked for an accommodation), each present only where an automation reads what it changes. A card is `OptionCard`, a button: pressing it chooses and runs, so a test is one press from the moment. It looks like a Choice card, but a `ChoiceCard` is a radio in a group that waits for a separate submit. Customize, an outline button, opens a form of only the facts those automations read (W6); a fact nothing reads never appears. Verification levels are words in that form, never a number, and a result never rejects anyone (G1). 3. What would happen: the moment and the candidate in two lines, then a numbered line (`StepLine`) of the automations that would run, each with what it would do in order, and under it a plain list headed Would not run (not numbered, because nothing happens) with each other automation of that moment and one line on why (a condition that did not hold, an accommodation, switched off, waiting for another stage). Nothing running says so in one line. An edit after the test leaves the answer as it was, with a line saying the workflow changed and Run it again in place of Show this path in the graph. Change the candidate and Try another moment keep what was chosen: another moment runs the same kind of candidate without asking again. Back is an outline button with an arrow on every screen but the first and the last. One line on the first screen says once that the test runs on the workflow as it is on screen and never touches a real candidate or sends anything. A workflow with no automation is an empty state, Nothing to test yet, with Add an automation where it can be edited (A5: it adds one, so it is a button); one whose automations are all off says so, with Open Pipeline where it can be edited. Show this path in the graph stays in the editor, beside the two. The tabs stay mounted, so when the workflow changes after a test the result says so in one line and offers Run it again, which tests the same moment and candidate on the workflow as it is now, in place of the path button |
| Pipeline tracker | `PipelineTracker`, from `trackerFor` (`#lib/pipeline-tracker.ts`) | A candidate's place in the job's workflow, under the first line of their header: a bordered bar with one block per stage, left to right, so nobody scans for where they stand (ADR-0025). Rejected and Withdrawn are ways out, not steps, so they are not drawn. The current stage is the solid `primary` block, in semibold, with "Here 3 days" and the Overdue badge once its time limit passed. Passed stages are tinted `accent`, with a check and the date they reached them ("Oct 20"). Later stages are plain. Once the candidate left, the stage they left from is the solid `foreground` block with how and when ("Saved Oct 31", "Rejected Oct 31", "Hired Nov 1"). It replaces the stage pill. Screen readers hear "Screening, step 2 of 6". In a narrow container only that stage shows, with "2 of 6". When no stage can be marked, a line under the bar says where they stand |
| Mini card | `MiniCard`; a candidate's header has Contact, Application and a third card in `candidate/header-cards.svelte` | A small bordered card: a caption title and two lines, a `role="group"` named by its title. It is not a panel (S3), is never a link as a whole (A5), and sits in a record's header. Contact: the email and the phone, each a link with a Copy button (`CopyButton`, an `IconButton` that shows a check and says "Copied"), and the email-issue badge on the email. For a saved candidate the second line is "Last contacted 3 days ago" in place of the phone, which stays on their profile. Application: the job as a link, then "Applied 6 days ago via the careers page". The third is Also applied to, from the one-candidate profile, or Rating once someone scored them; it never says "Not rated yet", and either way its second line links to the candidate's profile. A fourth, Interview, is there only while an interview is booked: the time in the reader's own zone, then its length and the number of interviewers. The cards take as many columns as hold 16rem each: three across in a wide container, two at a laptop (F2). In a container narrower than 36rem, a phone's, they sit in one row of 16rem cards that scrolls sideways and snaps, the next card's edge showing, because stacked they take about 580px before the tabs |
| Summary list | `<dl>` | Labels and values for read-only data; one Edit per section when you can edit |
| Key cap | shadcn `Kbd` | In tooltips, menus and the `?` dialog |
| Tooltip | shadcn `Tooltip` | Labels for icon buttons, full values of truncated text, shortcuts |
| Icon button | `IconButton`: ghost `Button` and `Tooltip`; `CopyButton` copies one value | A quiet row action; its label is both the `aria-label` and the tooltip (A4). `CopyButton` swaps its icon for a check for two seconds and says "Copied" to screen readers |
| Empty state | shadcn `Empty` | One line saying why, and the one action. A list that has never held anything has an icon, a short title and that line (No candidates yet, Nothing to do); a list a filter emptied has only its line and Clear filters. A loading state draws skeletons of the page's own shape (header, facts, tabs, a panel of rows) in `secondary`, because `muted` on a white panel is almost invisible |
| Shortcuts dialog | shadcn `Dialog` | Opens on `?`; lists every shortcut; holds the single-key toggle |
| Sidebar | shadcn `Sidebar` | Section 8. The sections (Jobs, Candidates, Templates, My tasks, Reports; Saved candidates is the Saved choice on the Candidates page, not an item, though G then S still goes to it), the Views (built in and saved, each with a menu on hover and focus: Rename, Share with the team or Make private, Delete view, and Hide from sidebar for a built-in one, which only that person stops seeing; a Show hidden views button ends the list while any are hidden, and each hidden view's menu has Show in sidebar), then up to three items at the bottom, Organization, Hiring setup and Integrations, each for anyone whose role has a permission for at least one page in it and each opening the first page they may use (Integrations opens its directory); nobody else sees one. On a phone they sit in the sheet, and in the icon rail they keep their icon and a tooltip. The person's block at the foot shows their name and their role, never the organization again (W7: the workspace above names it). The person's block is set apart from the navigation above it by a hairline divider in `sidebar-border` (shadcn `Sidebar.Separator`), so where to go and who you are read as two things; the divider holds in the icon rail and the sheet. The person's menu there holds the keyboard shortcuts, the single-key switch, Slack account (while the organization's Slack workspace is connected, opening `SlackAccountSheet`, where each person links their own account and picks their direct messages), Calendar (while this ScreenHire has a calendar to connect, opening `CalendarAccountSheet`, where each person connects or disconnects their own: the Connected status first, then which calendar, and Disconnect says that interviews already booked stay on it; not in the style guide because it reads the signed-in person's connection) and Sign out |
| Interview panel | `candidate/interview-panel.svelte` | On a candidate's Overview, under Next steps, and not drawn while no interview was ever offered. A status badge first: Waiting for the candidate (`waiting`), Booked (`booked`), Cancelled or Expired (`notInEffect`), then the days offered, the interviewers and the meeting link, or one line on how it ended. A booked interview with new times offered adds Waiting for a new time (`waiting`) and a line that the booked time stays until the candidate picks. Its menu has Offer new times (booked only, a sheet) and Cancel, a destructive item behind a dialog. The time of a booked interview is the header's Interview card and is not said again (W7). The details are in the style guide; the panel with its menu is not, because it reads the application's interviews |
| Brand bar | Our component (`BrandBar`) | The top bar of a page outside the app shell: the logo mark and name only (section 7). The sign-in pages have none, because their card carries the mark |
| Table, list item | TanStack table; our list item | 48px rows; list items on phones. Two text styles and one-line cells (L1); every column sorts through the sort header (L2). A list item that takes a date puts its name on its own line under the status, so a draft with no date is as tall as a published job. A list whose actions live only on its rows (saved candidates) ends each list item with the row's More menu, which stays clickable above the item's link. A job's candidate table and the board's cards do the same (`ApplicationMenu`, named Actions for the candidate): a Move to heading over the job's stages, the current one marked, which is the non-drag way to move a card (A7). The detail page keeps its Move to stage button, because it is one page's own action |
| Search field | Our component | A list's search, in its table panel's header (section 7); 16px text on touch |
| Activity | `ActivityList`; on a candidate `ActivityPanel` | Icon in a circle, event, who did it, time ago; a note left with it is quoted below. People and API keys show by name ("by Robin Ashby", "by you", "by ATS sync"). A candidate's Activity lists everything newest first, opens with the note box, then filters by kind (All, Notes, Emails, Scorecards, Interviews, Stage and tags) with a count on each kind but All, whose count is the Activity tab's (W7); Overview shows the last five and links to the rest. A team note shows its latest text through `NoteText`, with "Edited" after it and when in a tooltip; its More menu holds Edit for the author and Delete for the author, an Owner or an Admin, confirmed in a dialog, and anyone else sees no menu. Editing turns the note into its own box in place. A deleted note leaves one line, "Deleted a note" and who, never its text |
| Mention | `MentionField` on shadcn `Textarea`; `NoteForm` for the note box and editing in place; `NoteText` | @ opens the organization's members, without yourself, narrowing as you type: arrow keys move, Enter or Tab picks, Esc closes the list only. The list sits under the field, each member an avatar, name and role; the field points at it with `aria-controls` and `aria-activedescendant`, because a textarea cannot be a combobox. An @ button (Mention someone) starts one below `md`. Ctrl or Cmd Enter saves, hinted in the Save tooltip. A mention is stored as the member's user id and shows their current name ("@former teammate" once they leave). The person mentioned gets a notification card and an email that says who and where and never quotes the note; a mention an edit adds notifies only that person. Save is outline, because the page's primary is Move to stage (A1) |
| Rich text editor | `RichTextEditor` and `RichTextToolbar` on Tiptap 3, with shadcn `Toggle`, `Tooltip` and `Popover` | A job's description (New job, Edit details), an email template's message and the email composer's (docs/architecture/rich-text.md). A box with the input edge, a toolbar across its top and the text under it; the label above it names the text area (`aria-labelledby`, because a contenteditable has no `for`), and clicking the label focuses it. The toolbar holds Bold, Italic, Heading, Subheading, Bulleted list, Numbered list and Link, in that order: one tab stop, the arrow keys, Home and End move between them, each an icon toggle with its name and key caps in a tooltip (A4, K1), the pressed ones `accent` (A6), one line that scrolls rather than wraps (F5), 44px on touch (F4). Link opens a popover with the address field, Apply and, on a link, Remove link; a bare host gets https, an email address mailto, and anything else says what to enter. Headings are two levels (the careers page's h1 is the job title), lists do not nest, and Tab always leaves the editor. In an email, each variable is a chip: a muted word with its label (First name), kept whole, and Insert variable adds one at the caret; End goes past a chip that ends a line. An email template's Subject shows its variables as the same chips in `SubjectEditor`: the size and edge of a text field around one line of plain text that scrolls sideways, where Enter does nothing; it stores the Liquid text as the message does. Read-only, the same text without the toolbar. No hint under it: the toolbar says what it does (W4) |
| Tag list | `TagList` | Each tag a fact pill with its own Remove icon button beside it, never inside it (A5), then a field and Add tag. Removing is not destructive: adding the tag again undoes it (section 10). Without a role that changes hiring data, the pills only. The same list holds the sites that can embed the careers page (Hiring setup, Careers site, Careers page panel), with Add site and Remove site: each saves at once, and "No sites yet. Add one to get the embed code." while there are none |
| Task list | `TaskList` | Icon in a circle, what to do, then its kind and who it is for as fact pills and time ago; a done task says when and by whom. A task is ticked off where its work is done: on the candidate's Next steps, for a role that changes hiring data, an open to-do or rejection review has the task checkbox in its icon's place. A review and a requested scorecard (kind Scorecard) have no checkbox: submitting the scorecard finishes them, and for the person they are for (anyone, for a review for the whole team) their row ends in a link to the Scorecards tab, Open scorecards, or for a requested scorecard Fill in, with its sheet open. My tasks is a table, not this list (section 21) |
| Task checkbox | shadcn `Checkbox` in `TaskList`; `createTaskCompletion` in `#lib/tasks.svelte.ts` | Ticking marks the task done on the server at once, so leaving the page keeps it done. The row stays where it is for 10 seconds, ticked and struck through, with an outline Undo; Undo, or unticking, opens the task again. Then a done task leaves an open list. Its label names the task. A tick that fails unticks and says why (`FailureText`). Its hit area is 44px on touch (F4) |
| Undo notice | `UndoNotice`; `UndoOffer` in `#lib/undo.svelte.ts` | The offer to undo a change that already happened on the server, such as a deleted view: one card at the bottom of the window (above the action bar on a phone) with what was done in a sentence and an outline Undo button, for 10 seconds. Undo reverses it on the server; if that fails the card says why. A second offer replaces the first. The wrapper is a polite live region that is always in the page. Undo takes focus when the card appears, without stopping the clock, so the card goes for someone who does nothing. The 10 seconds wait while the pointer is on the card or the person moves focus to it, and start again when they leave (WCAG 2.2.1). Escape closes it. When the focused Undo goes with the card, focus moves to the page's title. Used where the change is not destructive because the same screen undoes it (section 10), and where a row cannot stay in place the way a ticked task does |
| Scorecard | `ScorecardList`; `ScorecardSheet` to fill one; `AskScorecardSheet` to ask a teammate for one; `ScorecardBuilder` for a library scorecard's questions; `ScorecardPicker` in the workflow builder | A submitted scorecard is an icon in a circle, the recommendation in words and who gave it, the scorecard's name (when it is not the stage's) and the stage as fact pills and time ago, then each question and its answer as a summary list ("Not answered" when left out) and the note quoted. Every scorecard ends with an overall recommendation, in words, and a note. `ScorecardFields` draws what a reviewer fills in, for the sheet and for the library scorecard's Preview, which shows it at the sheet's width for a sample candidate; `ScorecardQuestionList` reads a scorecard's questions as content. Questions are built in the form builder with the answer menu, on a library scorecard (built-in or the team's own); a stage picks one, and a job keeps its own copy of the questions; rating questions answer on one row of joined buttons and make the candidate's rating, the one average anything takes of scorecards (Rating, below). The sheet never shows verification output. The panel header has two outline buttons: Fill in, for a role that fills in scorecards, and Ask for one, for a role that creates tasks. Fill in opens the sheet with a Scorecard select first: the reached stages' own scorecards the reader has not given (the current stage first, which it starts on), then the built-ins and the team's own, which are given at the stage the candidate is in now; an archived one only when a link names it. Ask for one opens a sheet with Teammate (the people who see the job and whose role fills in scorecards, never yourself; with nobody, one line says why), Scorecard (the current stage's own first, then the library without archived ones) and Ask, with no note, because the request is the message; the teammate gets a Scorecard task, done when they submit it. Questions ask about the work and the skills the job needs, never personality, emotion or honesty |
| Notification tray | `NotificationTray`; `Notifications` in `#lib/notifications.svelte.ts` | Overlay cards in the bottom corner (above the action bar on phones), newest first, at most three in all: an icon for what raised it (`bell` for the team, `at-sign` for a mention, `reply` for a candidate's reply, `file-lock` for a new data request, which only people who may erase candidates and see every job get), the record as a link, one line and Dismiss. A card never times out, because it holds a link (WCAG 2.2.1), and a polite live region announces each one. A teammate's change to something the person works on is a toast in the same tray: the icon Activity gives that change, one sentence naming who did what with the record as its link ("Jordan Ellis moved **Taylor Brook** to Interview"), the job as a muted line, and Dismiss. Changes one person makes together on one job read as one toast about the job ("moved 5 candidates on **Platform Engineer**"). A toast closes after 8 seconds and counts down only while nothing holds it: the pointer or the focus on the tray, a hidden tab, or a sheet or dialog over it. A new card or toast pushes out the oldest toast before any card, and a toast does not show while three cards are open. Notifications in the account menu opens a sheet (`NotificationSettingsSheet`, not in the style guide because it reads the signed-in person's settings) where each person turns toasts off, picks the kinds, and turns off Close automatically, which keeps each toast until dismissed: that is the time limit WCAG 2.2.1 lets people turn off, and every toast's change is also in Activity or History. Mentions and automation cards are not toasts and always show |
| Sheet | shadcn `Sheet` (`MoveNoteSheet`, `ScorecardSheet`, `AskScorecardSheet`, `ScheduleInterviewSheet`, `OfferNewTimesSheet`, the workflow builder's automations and scorecards) | Quick work in place (section 9): the title, one line of description, the form, then Cancel and the one primary action (A1). The record stays behind it. History (T6), the items behind a count (T4), Invite and the email composer are sheets too |
| History sheet | `HistorySheet` on shadcn `Sheet`; `LastUpdated` for the fact; `HistoryFact` puts both in a header that shows nothing else of the history (the settings pages) | T6: Last updated is the last fact of the header, time ago and who ("2 hours ago by Avery Quinn", "By ScreenHire" for an untouched built-in), then an outline History button that opens the sheet. While nothing has changed the object since it was created, the fact's label is Created. On a page whose one editable thing is a single panel (Record retention, on Data requests), the fact is in that panel's header actions, not the page's title row. On a settings page's title row, where no label shows, the fact starts with its verb ("Created 2 hours ago by Avery Quinn", "Updated yesterday by you"). The sheet's title is History and its one line the record's name; changes come newest first under a heading per day (Today, Yesterday, Oct 22). Each change is the person's avatar, their name starting the first line, one line per thing that changed ("changed **.NET and C#** from 1 to 5 to 1 to 10"), and time ago. Show the change is an outline button (A6) that reveals before, struck through, and after for each value, and "Unchanged" for the context. Show older changes loads the next page. A ScreenHire template has no History button, because nobody can change it |
| Action bar | Our component (`ActionBar`) | A phone's primary action in a bar fixed to the bottom, over the safe area (section 8) |
| Relative time, truncated text | Our components on `Hint` | Time ago, or text cut with an ellipsis (on one line, or on two as a workflow graph node's title); the exact time or full value in a tooltip that opens on hover, focus or tap (T5, F4, F5). A truncated link is one tab stop, with an optional icon before its text |
| Table date | `DateText` on `Hint` | A date in a table or list item, the exact time in its tooltip (T5, section 13) |
| Form field | shadcn `Field` (`Field.Error`); `FieldErrors` in the workflow builder | Label above the control, a description of one line at most, and each error under it with the problem icon and the verify colour (T1, section 14) |
| Move to stage | `MoveToStage` on shadcn `DropdownMenu` | The non-drag way to move a candidate (section 17): a button that opens the stages, the current one marked. On a candidate's page, Save candidate follows the stages, beside Rejected, for a candidate whose permission for future roles holds; it opens a sheet (`SaveCandidateSheet`) with a note and Save candidate as its primary. On a detail page a last item, With a note, opens the move in a sheet with a note. A saved candidate has no current stage: moving them brings them back to the job |
| Confirm dialog | shadcn `Dialog` (`ConfirmDialog`, `CloseJobDialog`, `LeaveDialog`) | Section 9: the question as its title, what happens in one line, the outline way back and the action, in `destructive` when it destroys (A2). A choice that goes with the action (also revoke a removed member's keys, also save a closing job's candidates who allowed it) sits under the line as a checkbox. A failure stays in the dialog as `FailureText`. An action that cannot be undone and takes a person's data with it (`EraseCandidateDialog`) sets `confirmText`: a field "Type <name> to confirm" keeps the action off until the typed text matches, ignoring case and the spaces at the ends. A name is not a code, so W8 holds |
| Secret dialog | shadcn `Dialog` (`SecretDialog`) | A secret the API shows once (an API key, a webhook signing secret): "Copy it now. You will not see it again.", the value in a read-only monospace field with Copy, and Done. The value lives only while the dialog is open; never in the query cache, the address or storage |
| Code block | `CodeBlock` | Code someone pastes elsewhere: the careers widget's embed code (Hiring setup, Careers site, and a job's Job postings tab for that job), an MCP client's config (Integrations, MCP server), the example request (Integrations, API keys) and each DNS record's Host and Value, under a caption saying which is which (Hiring setup, Email domain). The code in monospace on the `secondary` fill, wrapping at spaces first rather than scrolling, then a large outline Copy that turns to Copied, with a status line for screen readers. Without a clipboard, Copy selects the code |
| Email preview | `EmailPreview` | An email as the candidate receives it: the subject, then Email or Plain text (Toggle). The HTML renders in a sandboxed frame with no scripts, in an inset, never in the page, because a tenant wrote the template. The frame takes the email's own height, so a short email leaves no gap under it. The composer's preview, a sent email opened in Messages (`SentEmail`), as it went, and a reply opened there (`ReceivedEmail`), under who it is from and above its files |
| Email composer | `EmailComposer`; `EmailComposerSheet` on shadcn `Sheet` | Email one candidate or a selection: in a sheet from a list's selection bar and the candidate's header (section 9, in place), and in its own panel under the emails on the Messages tab, titled Email and the candidate's name, so what was sent is the first thing the tab shows. To, then Start from: Blank email, the default, or one of the organization's templates, which fills Subject and Message. The rejection and follow-up questions emails are never offered, because only their actions send them (`docs/architecture/email.md`), and a blank email never becomes a template. The Edit / Preview switch (A8, no window) and Insert variable, a menu of the variables that puts one at the caret of the field last used: a chip in the message, `{{ name }}` in the subject. Changing a template's wording changes this email only, and one muted line under the message says so and that the template stays as it is. Preview is the first candidate's email, rendered by the API with their details ("Preview for" and their name in a selection); each candidate's own copy is rendered with theirs. Send is the sheet's one primary ("Send 3 emails" for a selection); inline on Messages it is outline, because the page's primary is Move to stage (A1), and empty fields say what they need when it is pressed. Each email lands in Activity and Messages as it went. No bulk email while a verification filter or view picked the rows |
| Add candidate sheet | `AddCandidateSheet` on shadcn `Sheet` | A recruiter adds a candidate by hand: Job (only the drafts and open jobs, and left out on a job's page, where the job is preset), First name, Last name, Email, Phone and Resume, then Cancel and Add candidate. Last name, Phone and Resume say "(optional)", the exception here (W6). With no draft or open job the select reads "No job to choose" and a line under it says why, and links to New job for someone with Edit jobs or says who can create one for anyone else (A3, E3). A job list that cannot load is `QueryError` under the select. No Source field: the API sets Recruiter (W6). No notice, telemetry or declarations: the Verification tab says which checks had nothing to look at (section 16). The resume goes straight to storage, as on the careers form. The page's primary on Candidates (in the action bar on a phone); outline on a job's page, where Publish is the primary, in More on a phone, and absent on a closed job; never without Add candidates (A3). A refusal (a closed job, someone who already applied, someone on a job the person does not see) is `FailureText` in the sheet; on success the page's status line names the candidate as a link to their page |
| Customize sheet | `CustomizePostingSheet`, `CustomizePostingForm` on shadcn `Sheet` | What one job board receives in place of the job's title and description. Each field starts as what the board gets now, which is the job's text until it is customized; "Use the job's title" or "Use the job's description" shows under the field only while it differs, and an empty or equal field follows the job. The Edit / Preview switch (A8, no window) shows the API's preview, with the board's failing checks as `FailureText`. When the job changed since the board was customized, a block above the fields shows each field's old and new job text. Cancel and one primary, Save and post; a failing check stays in the sheet and nothing is sent. Where it opens from, and where it is never offered, is in section 21 |
| Live indicator | `LiveIndicator` | The live updates status as a pill in the top bar, beside Search, on every page: icon, colour and label (T1), from section 3.2 (Live, Connecting, Offline). While live, its icon has a slow, soft pulse behind it, which stops for reduced motion. On a phone only the icon shows; screen readers hear the label |
| Failure text | `FailureText` with `ProblemIcon` | A failed action or form, in one line where it happened: the problem icon, the verify colour and the reason (T1, section 14). A section that cannot load uses `QueryError` |
| Verification chip and panel | `VerificationChip`, `SignalEvidence`, `ReviewLog`, `LevelScale`; `VerificationPanel` is the candidate's Verification tab, with `VerificationExplainDialog` and `VerificationDismissDialog` | Section 16. `LevelScale` draws a level on its three steps. A summary card per kind of check: Network, Identity, Automation and Content always, Files and Form activity (the telemetry aggregates) only when they found something, each named for what was looked at, never for what it means about the person. Reasons expand one by one or with Expand all, and each can be marked explained, with Undo for its author. The review log is its own panel: dismissals, explained reasons, undos and review notes as Activity, and Add review note, named apart from Activity's Add note |
| Tabs | `RecordTabs` on shadcn `Tabs` (`line`) | A record's peer views in a fixed order, each with its count in the count badge. The workflow page's tabs (Pipeline, Graph, Questionnaire, Simulate) are this strip too, so a job and its workflow keep one frame (S8). A count is a plain number, never a fraction (Job postings 7), always in the same grey count badge; a state that needs a look is the record's status beside its title, never a coloured tab, a dot or a lone icon (E2). The list scrolls sideways, never wraps (F5) |
| Notice | `Notice` | A problem to fix: the verify status's soft colour, full width, the problem icon (section 14), what is wrong in words and a link to the fix, or the button that fixes it from there (E2) |
| Expandable row | `ExpandableRow` | A list's row that opens in place to show more, such as an email on Messages: the whole row is the button, with `aria-expanded` and `chevron-down`, which turns up when it is open; what it shows follows the row. Its words wrap rather than truncate, since a tooltip has no place inside a button. The rows share one surface and its dividers (S4) |
| Summary card | shadcn `Card` as it is (`SummaryCard`), on a summary band (`SummaryBand`, section 6) | An icon in a neutral 32px chip and `Card.Title` (16/24 semibold); the count as a badge in `Card.Action`, saying what it counts (2 signals); `Card.Description` (14/20 muted, two lines at most, the rest in a tooltip). A link when it opens what it counts |
| Bar chart | `BarChart` | A horizontal bar chart in HTML (section 3.5): each row's name, then its bars from one baseline, at most 24px thick, square at the baseline and rounded 4px at the end, with the value written at each tip in text colours, then an optional muted share (62%) after the first value and an optional muted note (84% moved on); the one row with the biggest drop has its note in semibold with a small down-right arrow, and its summary says "the biggest drop". A zero draws nothing. Two series sit in pairs with a 2px gap and a legend above. The bars and note are one image that reads out the row's summary on hover, focus and tap (F4); the row's name beside it is a link when the row opens something (a funnel stage opens its candidates), so the link stays outside the image. In a narrow container the name goes above the bars |
| Chart panel | `ChartPanel` on `Panel`, `Toggle` and the sorted table (L2) | A report's chart: the title, Chart and Table toggles on the header's right, then the chart or the same numbers as a table, whose first column is the row's name (a link when the row opens something). In a panel narrower than 24rem the toggles show their icons only, with the word as the tooltip and for screen readers, so the title keeps its room. No sentence sits under the title (W9): what the numbers count is for screen readers only. A panel with no chart is its table alone, without the toggles. The table opens on the chart's own order unless a default sort says otherwise. While new numbers load it keeps the old ones, dimmed, rather than a skeleton. An empty state replaces both views |
| Date range filter | `DateRangeFilter` on shadcn `Popover`; `#lib/report-range.ts` | A filter menu's outline button for dates (Dates: Last 30 days), `primary` once it is not the default. It opens the presets as ticked rows (Last 30, 90 and 365 days), which apply at once, then a custom range of two date fields under a rule, which applies with Apply and says what is wrong in words. Dates are the person's own days (W8); the API gets the local midnights |
| Metric card | shadcn `Card` (`MetricCard`) | The value in the UI typeface (`metric`), a signed change against a named period (+7 vs last week), a 12-point line and a readout on hover, tap and focus; no description. The change is green when good and amber when it needs a look, decided per metric (fewer days to hire is good). It opens its report |
| Report stat | shadcn `Card` (`ReportStat`) | A headline number on Reports: an icon chip and title, the value in the UI typeface (`metric`) as the one thing to read, the change from the previous period as a small grey pill under it (an arrow and the amount, never a status colour, its sentence in a tooltip), and an optional muted share under that (62% of applications). Each has its own line, starting at the card's left edge, so the pill is in the same place on every card however wide the value is. No sparkline and no link |
| Week chart | `WeekChart` | Applications and hires by week as paired columns from one baseline, Applications in `chart-2` and Hires in `chart-1`, with a legend above and a readout line of its own above the columns, so reading a week never moves them. One tab stop: pointing at or tapping a week, or the arrow, Home and End keys, reads that week out (politely, to screen readers). Past about a year the columns keep a minimum width and the chart scrolls sideways inside its card. Its panel has the table view |
| File row and file viewer | `FileRow`; `FileViewer` on shadcn `Dialog`; `FileFrame` | A page shows a file row: type icon, name, size, View and Download, or the scan status when a file is withheld. View opens the file in a full-window dialog with Close and Download, never a new tab. Untrusted files render isolated in a sandboxed frame; it draws PDFs, and other types keep Download only |
| Edit / Preview switch | `EditPreviewLayout` on `EditPreviewSwitch` (a `Toggle` group and an icon button), one at a time at every width; `PreviewFrame`; `#lib/preview-window.svelte.ts` | A8: a pencil and an eye; the window icon (`app-window`, "Open in a new window") pops the preview out, and the window follows each edit over a same-origin `BroadcastChannel`. A candidate page previews in a phone, tablet or desktop frame at its real size. A read-only view of what an editor makes (a closed job's questionnaire) keeps the same switch, so content and preview never share the screen there either, and its first choice is Questions with a list icon, because nothing there can be edited (E3). The edit pane keeps the 688px form column (S6), unless the editor sets `wide` because its panels sit in rows (Branding): then the pane and the switch's row take the work area's width, and `actions` sit at the row's right end, which stays in view while the page scrolls, with what takes focus in the panes kept clear of it (WCAG 2.4.11). The preview pane is always the work area's full width |
| Count button | `CountButton`: outline `Button` and `Sheet` | A count in a header ("2 hiring templates") that opens a sheet listing each item as a link, with search past 10 (T4). With nothing to list it is plain text ("None"): a header fact is never blank, though the list's column is (L1) |
| Sort header | `SortHeader` in the TanStack table header; `SortMenu` on phones; `#lib/table-sort.svelte.ts` | L2: the header text and `chevrons-up-down`, which turns into `arrow-up` or `arrow-down` on the sorted column; `aria-sort` on that header; a second click reverses. Each table opens on its default (a job's candidates on Stage, those needing attention first: overdue, the longest first, then pipeline order; candidates across jobs on Applied, newest first), and each person's sort is remembered per table in the browser (`preferences`), so it does not follow them to another browser. The Sort chip offers the same choices on a phone, above the list items |
| Built in badge | `BuiltInBadge` on `Badge` muted variant | After the name of a template or role ScreenHire supplies; nothing marks your own (W6) |
| Reorder grip | `ReorderList` and `ReorderGrip` on svelte-dnd-action | A7: a drag handle whose click (or Enter) opens Move up, Move down, Move to top and Move to bottom, leaving out moves that do not apply; Space picks the item up and the arrow keys move it. Each list has its own drag zone, so lists on one page never trade items |
| Settings menu | `SettingsMenu`; its pages are `settingsSections` in `#lib/settings.ts`, its sidebar items `settingsAreas` | Settings is three sidebar items (Organization, Hiring setup, Integrations). The pages of Organization (General, Team, Roles, Data requests, and Billing for an Owner) and of Hiring setup (Careers site, Branding, Verification, Email domain) share this menu, which lists only the pages of the area the person is in, so it stays the same size however many areas there are. A page's breadcrumb leads with its sidebar item (Organization / Team), never with the word Settings, which names nothing in the sidebar. Integrations has no menu: its pages (Slack, MCP server, Webhooks, API keys) are records of the directory (Integration row), which a menu could not hold at twenty (S9), and each has the directory in its breadcrumb. Slack is there only where a Slack app is set up (`GET /v1/slack` says `available`): elsewhere the page and its entry are hidden (A3), and an address to the page goes to the first page the person may use. Email domain is hidden the same way where the installation has no email provider (`available` is false); it shows until the shell hears that, so the entry does not wait for a query. Billing is hidden the same way where no payment provider is set up (`GET /v1/organization/billing` says `available`), and only an Owner (Manage billing) sees it. General holds the organization's name, data region and when it was created; Careers site its careers address, the apply form's questions per page and the sites that may embed it. The menu's name for screen readers is its area. Never indented entries. Email templates are on the Templates page (section 21), and their old Settings addresses redirect there. A surface under the header band: from `lg` a 200px column left of the page's content, the one side column S7 allows; below `lg` one row above the content that scrolls sideways and never wraps (F5). Each entry is a link with its icon; the current page takes `accent` and `aria-current`, and the row scrolls to show it. The menu lists only the pages the person's role may use, each behind its permission. A record under a settings page (a webhook endpoint, a role) is a page with no menu: a detail page when it has the details rail, the form template when it does not. On a settings page the header band fills the window and its title lines up with the menu, not with the form column |
| Candidates list switch | `CandidatesListSwitch`: links drawn with `toggleVariants` in a `role="group"` (label List) | Current, Saved and All beside the Candidates title, in the same place on every list so the frame does not shift (S8). Current is the people in a hiring process now (on a job that is not closed, not saved, not at an end stage), Saved the people kept for future roles, All every application. Each choice carries its own count, and the header band carries none: a count beside the title changed width between the lists and slid the switch. The table panel's title counts what the filters show. Three choices that cannot grow, so a toggle's look fits (S9). The choice is the address, `?list=saved` or `?list=all` (Current has none), so each choice is a link (A5, A6) with `aria-current`, and a link and a reload keep it; the current one takes `accent`, as a pressed toggle does. `/saved` and `/talent-pool` redirect there. Choosing one starts the other clean: the address drops the view and filters of the first |
| Integration row | `IntegrationRow` (the state from `IntegrationEntry`); `integrations` in `#lib/integrations.ts` | A row of the Integrations directory: icon, name, one line on what it does, and a quiet state badge, Connected or Not connected, where the integration can tell, under the text when the row is narrow so the one line keeps its width; an integration with no way to tell (the MCP server) shows none (W6). The whole row links to the integration's page. The directory is the list template: a count in the header band, one panel, a Toggle group of kinds (All first, each with its count) when there is more than one kind, and a search field past ten integrations. Personal connections, such as a member's calendar or their Slack account, are not integrations of the organization and stay in the account menu |
| Setup checklist | `SetupChecklist` and `SetupSteps` on `Panel` and `StepMarker` | Set up your organization, on Jobs above the list, for whoever can do the steps and see every job (Manage the team, Edit settings and all jobs: Owners and Admins). Five steps, each a square marker (a check once done) and a link to the page where it is done: Invite your team, Set your branding, Set up your careers site, Add your email domain, Publish your first job. Each ticks from the organization's own data, never a click: Set up your careers site once a site may embed it or the questions per page leave the default, so anyone keeping the defaults dismisses the panel; Add your email domain is not offered where the installation has no email provider (A3). It shows nothing until every answer is in, goes by itself once the last step is done, and Dismiss (an icon button) hides it in this browser for the organization |
| Form builder | `FormBuilder` and `AnswerMenu` on `Panel`, `ReorderList` and shadcn `Switch`; `#lib/form-builder.ts`, `#lib/form-sections.ts` | One builder for application questions, follow-up questionnaires and scorecards, modelled on Google Forms. The questions are rows in one panel with their count (S4), and every row starts closed. A closed row reads as text: the question, an asterisk when required, its answer kind's icon and name top right, the help line, and the options with the mark the person will see (a circle for Choice, a square for Multi-choice). The row is one button that opens it, beside its grip. Only one question is open at a time, with a `primary` bar down its left edge: its text, the answer kind button top right (the answer menu), the help line, the options with their own grip list ending in an outline Add option, then what the kind needs (a knockout, a number's range, a file's types). Its footer holds Duplicate and Delete icon buttons and the Required switch. Duplicate puts the copy straight after the original and opens it. Deleting changes nothing until Save, so it does not confirm (section 10). A problem shows under its question whether the row is open or closed (W7). Add a question, under the list, opens the answer menu. Questions and options reorder with a grip (A7). The editor that mounts it has the Edit / Preview switch at its top (A8). Changing a question never changes answers already given. A questionnaire (never a scorecard) may have named sections, each a page for the candidate: an outline Add a section beside Add a question, and each section a row in the same list, on the `muted` fill, with its grip, an inline name field, its question count and a Delete section icon button. A section row starts its section, so moving a question past one, by drag or the grip's Move up and Move down, moves it into that section; the list stays one panel with one open question. Deleting a section gives its questions to the section before it and does not confirm (section 10). The preview stacks the candidate's pages, each opening with `PageProgress` |
| Answer menu | `AnswerMenu` on shadcn `DropdownMenu` | Picks a question's answer kind, each group under its heading and each kind with its icon; the current kind is ticked, and Add a question opens the same menu with nothing ticked. General: Yes or no, Choice (pick one), Multi-choice (pick any), Short text, Long text. Application questions add More: Number, Link and File. A follow-up questionnaire offers the same without File, because its page takes no uploads. A scorecard offers General and Rating, each rating with a star: 1 to 5, 1 to 10, Yes or no |
| Rating | `Rating`; `#lib/rating-column.ts` for a job's table | The candidate's rating: one to five stars filled to the tenth, the number and how many scorecards it comes from (in words in the count's tooltip), opening, from a small outline button (A6), to each scorecard that counts with its own number and recommendation. It averages people's rating questions only, never verification results, AI signals or the stage; a reviewer with a scorecard to give reads "Rating after your scorecard" ("After yours" in a table cell, the rest in its tooltip) until they submit their own; no automation acts on it. Until a scorecard counts a table cell is blank, its words ("Not rated yet") for screen readers only (L1). A job's table has a Rating column, highest first, the unrated last |
| Rating answer | `RatingAnswer` on shadcn `ToggleGroup` | A rating or yes-or-no question on one row of joined buttons, with Low and High under the two ends of a number scale. Each button is a radio in the question's group; the chosen one takes `accent` (A6) and an inset ring in `accent-foreground`, so a chosen button is not told from a hovered one by a faint fill alone, and choosing it again clears an optional answer. A number scale never wraps: its buttons share the row equally, up to the control size, so 1 to 10 on a phone has buttons narrower than 44 px (still above WCAG 2.5.8's 24 px) and High stays under 10 |
| Choice card | `ChoiceCard`: a native radio in a labelled card | A choice of a few options in a `radiogroup` with its label: each a bordered card with its name and one muted line, and the whole card is the target. The chosen card takes the `primary` edge and `accent` fill. A member's jobs (All jobs or Only jobs they're on), the role in Invite, and the role to copy in New role |
| Filter menu | `FilterMenu` (one choice), `MultiFilterMenu` (several) | An outline button that names the filter and its choice ("Stage: All", "Job: 2 chosen"), `primary` while set, opening each choice with its count. `MultiFilterMenu` shows a box on every choice, ticked or not, and with two or more chosen each one is a pill with its own remove button beside the button. Filters sit in one row under a list panel's header that scrolls rather than wraps (F5); the page applies them to the rows |
| Save view button | `SaveViewButton`; its dialog is `SaveViewDialog` on shadcn `Dialog`, `Field`, `Input` and `Switch` | An outline button with a plus icon, "Save view", at the end of a candidate table's filter row. It is always there, so people can see that views can be saved: disabled until the filters or sort differ from the view the table is on, with the reason in its tooltip (A3). A phone shows it only once it works. It is the row's only button of its kind (A1). It opens a small dialog: Name (required, 80 characters at most) and a Share with the team switch, off by default; the dialog's one primary button is Save view, and the new view is opened at once, so the button goes away. The dialog is in the page only while it is open |
| Rename view dialog | `RenameViewDialog` on shadcn `Dialog`, `Field` and `Input` | Opened from a view's menu in the sidebar: one Name field and a primary Rename view button. A name the person already uses for a view of that kind shows under the field (W7) |
| Picker | shadcn `Popover` and `Command` | An outline button with a verb ("Add a person", "Copy from another job") that opens a searchable list; typing filters by name, a pick closes it. The list keeps a 16px gutter from the window's edge. Nothing left to pick is said in the list, never by disabling the button (A3) |
| Country picker | `CountryPicker` on bits-ui `Combobox`; the codes and names in `#lib/countries.ts` | Several countries for one field (a remote job's countries, W8): a search field labelled by the form that finds countries by name in the person's languages, ignoring case and accents, and each chosen country a fact pill with its own Remove button beside it (A5). The arrow keys move through the matches, Enter or a click adds one and the list stays open for the next; a ticked country in the list is already chosen, and picking it again takes it away. Focus goes to the next pill's Remove button after a removal, and a live region says what was added or removed. One country is a native select of the same names. The value is always the ISO 3166-1 alpha-2 codes |
| Colour field | `ColorField` on shadcn `Field` and `Input` | A colour for Branding: the native colour picker beside a hex field (16px on touch, F4), empty for its default (its placeholder names it: "ScreenHire's" for the brand colour, "None" for the second colour), with one line saying where the colour goes. As someone types a whole colour, the API checks it (section 18); each pair it fails is a line under the field in words ("Links in #ffd400 are hard to read on the page: 1.4:1, and they need 4.5:1."), and the nearest passing shade is an outline button on that row (F6), "Use #8a6d00", which fills the field. A refused save's messages take the same place |
| Drop area | `DropArea` in `@screenhire/ui/questions` (`packages/ui/src/questions/DropArea.svelte`), shared by the dashboard and the careers apply form; `FileField` is the question that uses it | The one way to choose a file (A12). A dashed area with 12px padding (`rounded-lg` in both: `border-input` on `secondary` in the app, a 2px edge in the candidate tokens on the careers pages) with a real file input inside it, so the keyboard (Tab to the input, Enter or Space), screen readers and phones work. Empty, it says "Drop a file here or" and shows Choose file; with a file it shows the name and size with Replace file and Remove (outline buttons), and it keeps a polite live region for "Uploading...". It answers to its own width (F2): under 448px the words sit above full-width buttons, so nothing wraps. In the app tone, which is a tile in a row of tiles, the buttons stay in one row at any width (they wrap only when two of them cannot fit) and the gaps are 8px, which keeps a set tile short. Sizes are 1024-based (`describeSize`), as every upload limit is, so "10 MB" means the same in the limit and in the name. A refused area has a destructive edge beside the words. The whole area is the target: a click anywhere opens the picker, and while a file from outside the page is over it the edge turns solid in the ring colour on the accent fill (dragging text or a link does nothing). The first dropped file is taken when several are, because a field holds one. A type or size the field does not take is refused in words ("Upload a PDF or DOCX file.", "This file is larger than 10 MB.") through the field's own error, never as a toast, and the refused file is not kept. Replace file and Choose file look like buttons but are not: the input is the one control, so assistive technology finds one picker, and Remove is a real button. On a careers page rendered without JavaScript it is only the named input in its dashed frame and posts with the form; once the page's script runs the area takes over, and a field that posts with its form gets a dropped file put into its input. Tokens only, no inline styles, and the hand cursor comes from the theme (A10) |
| Image field | `ImageField` on shadcn `Field` and the Drop area; `BrandImageSlot` in `#lib/brand-images.svelte.ts` for the upload | A brand image as a tile showing it where it is used, at its real proportions: a logo on a light checkerboard (the candidate surfaces, light in both themes), a favicon at 32px inside a small browser-tab mock, a sharing image as a small card, its picture beside the title and description a shared link shows (the Branding preview draws the full card). A set tile is about 200px tall, so the three sit in one row of the wide Branding editor and the page does not scroll at 1440 by 900 with all of them set. The tile is the Drop area, so the picture is part of the target: empty, it says "No logo yet" and "Drop a file here or Upload logo"; set, it gives the pixel size, names the file and its file size with Replace and Remove in one row (their nouns are short: "Replace icon", "Remove image"), and a click or a dropped file replaces the image (nothing changes for anyone until Save). A file that is not a PNG or JPEG, or is over the kind's limit (the API's `BrandImageRules`), is refused in the field's error. While the malware scan runs it shows the grey Scanning badge (T3) and the page's Save waits, saying why (A3); a file the scan blocked says so with `FailureText`. The upload goes straight to storage, as a candidate's does |
| Branding preview | `BrandingPreview` in `PreviewFrame` | Branding's result, one press from its form (A8), with a line saying it follows unsaved changes: the careers page (the tab with the favicon, the header band, a heading and a job in the heading font, a brand link and the Apply button), the shared link card with the sharing image, and the top of an email. It draws the form's values as typed, failing ones included, so the person sees the problem the check names. Its screen-size frame opens on Desktop once the pane has room for a tablet frame (800px with the inset's padding), else on Phone, so a phone frame never leaves a blank half; a size the person picks stays |

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
| Last updated (T6) | Time ago and who, the exact time on hover; Created until the first change | 2 hours ago by Avery Quinn; Created 2 hours ago by Avery Quinn |

Always the browser's locale (US English by default), through one shared formatter on `Intl.DateTimeFormat` and
`Intl.RelativeTimeFormat` (`#lib/dates.ts`). The API never sends formatted dates (`.claude/rules/utc-timestamps.md`).

## 14. Icons

Lucide, imported per icon as `~icons/lucide/<name>` (unplugin-icons with `@iconify-json/lucide`, which keeps the whole
set in one file; `@lucide/svelte` ships three files per icon and is not used). 16px beside 14px text, 20px in touch bars
and the icon rail. Size 16 is the default from the icon customizer in `apps/dashboard/vite.config.ts`, and stroke 1.75
is set on `svg[data-icon]` in `app.css`, where the shapes inherit it, so a `fill-*` or `stroke-[...]` class on the icon
reaches them. Navigation and main buttons pair an icon with a label; decorative icons are `aria-hidden`. Status icons (section 3.2) appear only in status badges and in a status's
saved view in the sidebar (section 21), and no other control or decoration uses them. The warning triangle marks only
the verification cards that fed a level (section 16). A problem in words (a failed action, a form error, a builder's
problem to fix) starts with `circle-alert` in the verify icon colour (`ProblemIcon`). It is not a status icon, so it
never stands in for a badge.

The one exception to Lucide and to tokens-only colour is the Google and Microsoft marks on the sign-in page's provider
buttons (section 7). Both companies' sign-in branding guidelines require their own logo, unchanged and in its own
colours, on a button that signs in with them, so the button is recognisably theirs. Each mark is a small inline SVG
component, `GoogleMark` and `MicrosoftMark` (`#lib/components/provider-marks`), 16px like any icon, decorative
(`aria-hidden`, because the label names the provider) and chosen by provider id. `src/tokens-only.test.ts` exempts these
two files from the hex rule and fails if a third file joins them. Every other provider keeps a Lucide icon, and nothing
else uses the marks.

## 15. Shortcuts

Ctrl K or Cmd K opens the command palette. From two characters it searches on the server once typing pauses
(`GET /v1/search`): candidates by name or email, each on a job, and jobs by title, within the jobs the person sees. A
list page's own search still filters the rows it loaded. A row matches when its words contain what was typed (no fuzzy
matching, which made "sen" list every Settings page), and what the server found comes first, above the pages and actions. `?` opens the shortcuts dialog, and so does its command in the
palette.
Hints appear as `Kbd` key caps in tooltips and menus, never as an always-on bar. Single-key shortcuts (the G chords, J
and K, X, V and `?`) can be turned off (WCAG 2.1.4), skip text fields, menus and dialogs, and work only while their
table or board has focus, except the G chords and `?`. The rich text editor's shortcuts all have a modifier (Ctrl or
Cmd B and I, with Alt 2 and 3 for the headings, with Shift 8 and 7 for the lists) and Link has none, because Ctrl K is
the palette. Typing "- ", "1. " or "## " starts a list or a heading only while single-key shortcuts are on: for speech
input those sequences act like single-key shortcuts.

## 16. Verification UI

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
  allowed with disclosure") in the header, with Expand all on the right. AI assistance's pills (level, meter, policy and
  Informational) do not fit a fixed-height header on a narrow panel, so below a 48rem container they move to the body's
  first line and the header keeps the name and Expand all (F1, F5). A calm AI result (No signs of AI help) keeps only the level
  and the policy: the meter's lowest step and Informational are dropped (W10). When the level needs a look the next step
  leads, semibold, in a callout edged in the level's colour with an arrow, the summary a muted line under it; a calm or
  dismissed result is one muted line, the summary then its next step (W10). Then the reasons, grouped by the kind of
  check that found them (Network, Identity, Automation, Content) and a last Context group for what does not count, each
  group's name and signal count beside its reasons, each reason opening (A9) to what was observed, its evidence as a
  facts row, any counter-evidence in its own card, and its code. The panel ends with Dismiss on the left and "Assessed 2
  hours ago" and "Ruleset" in caption size on the right. A calm result has no list of empty checks: its summary already
  says so. The summary never repeats the job's policy, which the pill shows (W7).
- A candidate added by hand came through no careers form, so the authenticity result says so in one quiet line ("Added
  by hand, so the network, automation and form activity checks had nothing to look at."), and Network and Automation
  read "Not checked" rather than "Nothing found" when the reasons show. It goes by the source alone; a careers
  applicant's checks never say this, so nothing tells an assistive-technology opt-out apart.
- Each reason of an assessment that needs a look and is not dismissed has Mark explained, an outline button on the row
  of its code (F6). It opens a dialog: a reason from a list (Confirmed with the candidate, Expected for this role, A
  known false positive, Other) and a note, optional unless the reason is Other. An explained reason closes, shows a
  quiet Explained badge on its row (W6, T3), and under the row one muted line starts with who explained it, then when, why and
  the note (the badge already says Explained, W7), with an outline Undo for its author only. Explaining decides nothing: the level, the candidate's status badge,
  "To review", filters, automations and webhooks never read it. Once every reason that counts is explained, Dismiss
  reads Dismiss all; it is the same button, never a second one (W7), and it opens the dismiss dialog with the explained
  reasons and their notes listed above the dismissal's own reason. A dismissed assessment keeps its explanations, read
  only. The review controls show only to a role that reviews Verification; any other role reads one line saying it can
  read the checks and should ask a reviewer to explain or dismiss them (E3). The dismiss dialog scrolls when the list
  is taller than the window, so its button stays reachable.
- Labels keep their full names: Verification suggested, mirroring Review suggested. AI assistance reads No signs,
  Some signs or Strong signs of AI help.

## 17. Accessibility and motion

- WCAG 2.2 AA. A 2px `primary` focus ring with a 2px offset on every interactive element, never removed.
- Targets at least 24px with a mouse (2.5.8) and 44px on touch. Text at least 4.5:1; large text and boundaries 3:1.
- Every drag has a non-drag alternative (Move to stage, and the grip's Move menu, A7). Every shortcut has a visible
  control.
- A canvas of nodes (the workflow graph) makes each node one tab stop with the focus ring and a label that says what it
  is: the node is itself a button where it can be pressed. The arrow keys, Home and End move between nodes, and
  whatever takes the focus scrolls into view. Text in a node is clamped, with the full value in a tooltip that takes
  no tab stop of its own.
- Motion 120 to 180ms for hover and popovers, 200 to 240ms for sheets; `prefers-reduced-motion` turns it off.
- axe runs in component tests and Playwright; manual NVDA and VoiceOver passes per release; an external audit before
  general availability.

## 18. Employer branding

Hiring setup, Branding (ADR-0018, `docs/architecture/branding.md`) sets a brand colour, a second colour, a heading font,
a logo, a favicon and a sharing image, each optional and shown next to ScreenHire's default (principle 10). They apply
to the careers pages, the apply flow, the embeddable widget and the top of every email to a candidate.

| Part | Where | Check before it saves |
|---|---|---|
| Brand colour | The Apply button and every filled button, links, focus rings | White or ink on it at 4.5:1 ("Text on buttons"); it on the page background at 4.5:1 ("Links") |
| Second colour | The careers header band, the email's top band | White or ink on it at 4.5:1 ("Text on a header") |
| Heading font | Every careers title (`font-display`) | One of six self-hosted OFL fonts: Bricolage Grotesque (ScreenHire's), Inter, Montserrat, Nunito, Fraunces, Playfair Display |
| Logo | The careers header in place of the letter mark and name; the email's top | PNG or JPEG, up to 1 MB, malware scan |
| Favicon | The browser tab | Square PNG or JPEG, at least 32px, up to 256 KB, malware scan |
| Sharing image | `og:image` when a link is shared | PNG or JPEG, at least 600 by 315px, up to 5 MB, malware scan |

- A colour is used as it is given. One that fails a pair is refused, never adjusted: the field says which pair fails
  in words, with its ratio cut down so a failing one never reads as its minimum, and offers the nearest passing shade
  as a button that fills the field (Colour field, section 11). The page checks as someone types; Save checks again.
- Everything inside the careers header, its links and focus ring included, takes `candidate-header-foreground`, the
  white or ink the check chose, so a dark brand focus ring never sits on a dark band.
- The palette ships as the hashed per-tenant stylesheet (strict CSP, no inline styles), and images from the careers
  origin, so `img-src 'self'` holds. Brand never overrides status colours, the privacy notice or the "Powered by
  ScreenHire" footer.
- The email's logo is a plain image at one address for every recipient, never a tracking pixel. Emails load no web
  font.
- The editor is wide (A8, Edit / Preview switch in section 11), so at 1440 by 900 it fits without scrolling: Colours
  and Heading font side by side (the two colours side by side inside Colours), the three images in a row, even with all
  three set (Image field, section 11). Colours and Heading font stack below 52rem of editor width; the images are three across
  from 52rem, two from 34rem and one below. Save sits in
  the switch's row, which stays in view in Edit and Preview, and a field a Save refuses brings Edit back, where its
  message is. The preview is the full width of the work area (the careers page, then the
  shared link and the email's top side by side, Branding preview in section 11). Nothing changes for candidates until
  Save, which waits for any image still being scanned and says so.

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
  and Job postings (once Distribution: where the job is posted) its own tab, whose count says how many boards the job went to. A refused posting is a notice that names the board and links to
  the fix. Calm rows say nothing. The board's empty and closed stages shrink so every stage fits, and the board
  scrolls inside itself. Workflow opens its own page from the header. Selected rows get a bar with Move and Email;
  with an authenticity filter on, Email hides and Rejected leaves Move (ADR-0004).
  Built: the chips and the Stage filter sit on a row under the pipeline panel's header (S3), as words and counts with
  no status icon (I2), and a phone has Stage, Authenticity and Sort chips above the list items. The tab is in the
  address (`?tab=postings`; the old `?tab=distribution` still opens it), and the count is the boards the job is on or on its way to. Job postings is a small table, one row per
  board: Status, Board, Posted, Last updated and a row menu. The menu has View posting and Copy link where we know the
  live address, which today is only the careers page: a board that reads our feed never says where its listing landed.
  Then comes what a person can do there: Edit details and Customize (every board but the Careers page) and Retry on a
  posting that needs fixes, Post or Post again, and Take down last. A board with text of its own carries one Customized
  badge after its name (L1). A board that only closes with the job says why it has no Take down. The board's estimate is in its
  status tooltip. A posting that needs fixes is a notice above the tabs, and the row's menu holds its two fixes, each
  saying how far it reaches: Customize for that board and Edit details for every board. A customized board whose job
  changed since has the badge Job changed instead of Customized, with the reason in its tooltip. A draft or closed job has no
  menus: its header publishes or closes it. A posting whose checks
  found issues is "did not take this job", never "refused": only a board's own answer is a refusal. On the board, the
  closed stages share one Closed column (section 5), whose rows take a dropped card, and a calm card shows no level.
  In the table a calm AI assistance cell is empty, with its words for screen readers, and authenticity keeps its quiet
  "No concerns" (T3).
  Hiring team: an outline button in the header with the team's count badge (T4), in More on a phone, opening a sheet
  of the people on the job, each with their role, "Only jobs they're on" for someone who sees only their own (W6), a
  remove button, and Add a person and Copy from another job (Pickers). Changes land on Save. Without Edit jobs the sheet lists the people and says why (E3). A hiring
  template's header has Default hiring team as a fact whose count opens the same sheet, without Copy.
- **Jobs, Edit details and Workflow (page audit):** the Jobs list leaves a draft's Published cell blank (L1) and its row
  menu, for someone who may edit jobs (A3), opens Edit details (a link, A5) before Publish to every board or Close and remove everywhere; with a filter on,
  its empty state is the title and Clear filters, with no sentence. A job's candidate table ends each row, and the board each card, with one
  menu holding Move to (L3). On a job's header, Edit details, Publish and Close and remove everywhere are left out for someone who may not edit jobs (A3), Opened is left out while Last updated says the same minute (W7), and on a phone the More
  menu's item is Edit hiring team. The Jobs search matches the title and location, the columns a person sees (W6). Job postings has the Sort chip on a phone (L2). Edit details is headed with the job's
  name and its status, titles its panel Edit details as the crumb and the button do, and on an open job says before
  Save how far it reaches: the careers page changes now and boards that already have the job keep what they were sent until someone updates them (E3). Back on the job, a notice above the tabs says the boards do not have the change yet, with Update boards, which sends the new version to every board the job is on and checks refused ones again. It shows only to someone who may edit jobs. The
  workflow page's header is "<job title> workflow" with the job's status first and the check on the workflow second
  (S2, T2), and what saving did sits in the band's line under the title, as on the job page, so it never pushes the
  actions to a second row (S8). Its stages keep one row menu for Edit automations, Choose scorecard and Remove stage; a stage with no time limit shows "No limit"; a stage's scorecard is its name beside
  an icon, the word "Scorecard" for screen readers only (W9). Where the workflow reads only (a closed job's), its Pipeline tab is the Stage flow, not these rows. Simulate is the Guided test (section 11): three questions one at a time, a multi-choice answer picked from the options'
  names, never typed ids (W8), and one line saying no real candidate is touched.
- **Candidates:** every candidate table (Candidates and a job's) has Last activity after Applied: the date anything
  last happened to the application, from the candidate (applied, answered a questionnaire) or the team (moved, noted,
  tagged, emailed, scored, a task given or done), never the checks. It sorts, newest first on the first click, and live
  events keep it current; the tables keep their default sorts (L2). Add candidate opens the Add candidate sheet (section
  11), and Ctrl K searches on the server (section 15).
- **Candidate profile:** `/candidates/{id}`, on the detail template without tabs: the header band holds the name and the contact on the
  line under it. Two panels sit side by side from a wide window, one column when narrow: Applications, a sortable table
  that opens oldest first (a Sort chip and list items on a phone), each row the job as a link, then Stage (how it ended:
  Hired, Rejected, Withdrawn, Saved, or the stage it is in), Applied, Got as far as (the last stage before it ended) and
  Ended, the dates in their own columns and a cell with nothing in it blank; and History, every application's activity
  newest first, each line saying which job once the candidate has more than one application. When the candidate used the
  stop link in an invitation from Saved candidates, a notice says so and names Erase candidate, which sits in the More
  menu as a destructive item and shows only to people with Erase candidates who see every job (A2, A3). Once it is done the Candidates list says who was erased. An application's
  header links here from its third mini card.
- **Candidate:** the same tabs in the same order for everyone: Overview (what needs you now, the last five things
  anyone did with a box for a note, and the answers), Application, Messages (every email with the candidate, both
  ways, and the reply box under them), Scorecards, Verification, Activity. Activity lists everything newest first, saying who and
  how long ago, and filters by kind. Only a note's author edits it; the author, or a role with Delete anyone's notes
  (Owners and Admins), deletes it, after a confirm. The header holds who they are and where they stand. The
  first line is the name and one status badge, with the actions on the right: Email and Move to stage, or Message and
  Invite to another job for a saved candidate. The badge is Verification suggested (or another level that needs a look)
  while they are in the pipeline, and Saved, Rejected, Withdrawn or Hired once they left. Under it the pipeline tracker
  shows every stage of the job's workflow, then three mini cards: Contact, Application, and Also applied to or Rating
  (section 11). The source is part of the Application card's "via the careers page", and the only meta item left is the
  link to Saved candidates while they are on that list. On a phone the cards stack and the tracker shows the current
  stage with "2 of 6". The Answers panel lists the job's questions, then the ones every application asks (AI use,
  adjustments to the process, future roles), each under the question as the apply form words it; only what the candidate
  declared is a row (W6). A question is muted body text and its answer medium weight, so the two never read as one; in a
  panel 48rem wide or more the question has a column up to 30rem wide, so most questions fit on one line, and in a
  narrower one the answer sits under it. Overview
  is a grid of panels: Next steps and Activity on the left, Files, Tags and Answers on the right. Next steps is one row
  per step: a round to-do tick or the level's icon, the step, its facts, and its link on the right. A review or a
  requested scorecard has no tick; each one's link is Fill in, with the sheet open on the requested scorecard, or for a review on
  the scorecard for the stage the candidate is in (`?fill=stage`), which is where My tasks takes a review too. Until a
  reviewer gives theirs, the Scorecards tab says they are asked for one, with a Fill in your scorecard button.
  Built: Messages lists every email to the candidate, about any of their applications, newest first, with the email
  composer in its own panel under them for people who can send. Each row opens to the email as it went; one sent before ScreenHire kept a
  copy of each email says only its subject was kept. A candidate's reply is in the same list, with the reply arrow and
  who it is from, and opens to the sender, the message and its files; a file shows once its malware scan allows it. A
  flagged address puts a status beside the name, a notice at the top of the tab and Allow email again, and takes the
  composer and the Email button away ([email](../architecture/email.md)).
- **Templates:** one page with a tab per kind (Hiring, Scorecards, Emails). Each row says where it is used, and each
  editor has the Edit / Preview switch. A job keeps the copy of a template it started with. A built-in the team edited
  offers Reset to original in its menu.
  Built: the tab is in the address (`?tab=scorecards`), each with the count of templates that are not archived. The
  table panel's header is its title (Hiring templates) and the search, with no sentence about the tab (W9). The
  filters are All, In use (something uses it now), Not used in 6 months (no job started from it, no stage used it and
  no email went out from it in 180 days, and the team has had it longer than that) and Archived, each with its count.
  The first two can overlap. Columns: Name with Built in, then for hiring What it is for, for scorecards Questions,
  for emails the Subject with each variable as a muted word; Used by (jobs that are not closed, hiring templates,
  automations), blank while nothing uses it; Last used, or Last sent; for hiring Version, the published number, blank
  until one is published, and Draft, the version being drafted; Last updated. On a phone each list item ends with the
  same row menu. The row menu holds Duplicate, which makes "<name> (copy)" as the team's own and opens it, Archive or Restore for the team's own, and Reset to original
  for an edited built-in email, which confirms. The built-in rejection and follow-up questions emails have no
  Duplicate: their variables only their own actions fill. Archive does not confirm: Restore on the same screen undoes
  it (section 10). An archived template leaves the pickers and reads only until restored; jobs keep their copy, and
  automations keep sending an archived email. New template is a menu of the kinds the person's role may make. Each
  editor's header has Used by as a count button, then its facts and Last updated with History; the hiring template's
  Edit / Preview switch is in its Questionnaire tab, the part someone else sees. A built-in hiring template or
  scorecard reads only, says why and offers Copy (E3), with no History. Built-in emails stay editable in place. A
  publish or save says how far it reaches in the notice beside it ("Published version 2. Jobs created from now on
  start from it."). In the hiring template's Pipeline tab each stage's Edit automations, Choose scorecard and Remove
  stage are its row's More menu (L2); a hiring template that reads only (built in, archived, a published version, or one you may not edit) has the Stage flow there instead. An empty Emails tab offers New template as a
  link to the new-email page. A pop-out preview window with no editor behind it (a reload, an editor that closed) says
  so in one line instead of loading for ever.
- **Settings:** three sidebar items, Organization, Hiring setup and Integrations (section 11), the first two with a menu of their own pages and the third a directory. Careers branding becomes Branding, and email
  templates move to Templates.
- **Team and Roles:** Team lists the members full width, each role by its name alone: a custom role has no Custom label
  (W6). Its title's row has Last updated and History (S8) for every change to the team: invitations sent and revoked,
  joins, role and job access changes, and removals. Invite opens a sheet that describes each role, built-in or custom, starts at Viewer, the least access, and links
  to Compare roles. Roles is its own page on the list template: every role, the built-in ones marked Built in, with what
  it can do, how many people have it and Last updated ("By ScreenHire" for a built-in). Its header has Compare roles and
  New role. New role opens a sheet with a name and the role to copy, never Owner, then Create and edit opens the copy's
  page. Compare roles is the full-width table of what each role can do, read from the API's permission catalogue, a
  column per role. A role that has the permission says Yes in words (L1 keeps icons out of cells, and the check is a status
  icon, I2); a role without it is a blank cell with its No for screen readers only, so the Yes values stand out.
  In a panel narrower than 896px (the full sidebar at 1024, and phones) it is a list instead: each permission, what it
  covers, and the roles that have it as badges. A custom role's page is the form template: Name, then What this role can
  do with a checkbox per permission and the count chosen, a plain number in the panel's header (E2: never "4 of 16"), then Cancel and Save in that panel's footer; rows
  nobody can change here (See jobs and candidates, in every role; Manage Owners, Owner only; a permission the person's
  own role lacks) have a lock and say why. Its header's facts are Copied from and Last updated with History (the people
  are counted on their panel, W7); Copy role and Delete role are in More, and deleting a role somebody has is refused,
  saying how many. A built-in role's page is a read-only view with Built in beside its title and Copy role as the
  primary action, and the panel's footer says why it is read only, offering a copy only where the button is. Both list the people with the role in a sortable table: Name, Email, Jobs ("All jobs", or a count button that
  lists them) and Joined, with Edit access in the row menu, which opens that person's sheet on Team, for anyone the viewer may change. Job access (ADR-0015): Team has a Jobs column, plain text (T4): "All
  jobs", or "2 jobs" for someone on Only jobs they're on. A member's menu holds Edit access, which opens one sheet for
  their role (a select, with what it can do below) and their jobs: All jobs ("Including jobs added later") or Only jobs
  they're on, then the jobs whose hiring team they are on as links, read only, with one line saying each job's Hiring
  team changes them (W6). For an Owner or Admin the choice is a line saying they see all jobs. Invite asks the same,
  starting at All jobs. Someone who sees only their own jobs is offered only Only jobs they're on (A3).
- **Saved candidates:** the Saved choice on the Candidates page, beside its title (G then S goes to `/candidates?list=saved`;
  `/saved` and `/talent-pool` redirect there), on the list template: the people the team saved, with search, Saved from and Tag filters, and columns Candidate, Rating, Note,
  Tags, Saved from, Saved and Last contacted, sorted by name. They have no end date, so there is no column for one.
  Rating is the same read as the candidate page, with the reviewer gate ("After yours"). Last contacted is a date, or Never. The note is the
  team's, and pressing it opens the Edit note sheet, which has a History button; a person with no note has a blank cell.
  A row's menu has Invite to apply, Add to a job, Message (for a reader who may email candidates, opening the email
  composer), Edit note and Remove from saved (destructive, with "They asked to be removed" as its checkbox); a
  selection gets Invite to apply and Add to a job. Invite to apply is a sheet with the job and the email's preview; it
  has no wording to change, because only Templates rewords the invitation. No Verification column or filter
  (ADR-0004). Save candidate sits in Move to stage on the candidate's page, beside Move on a job's selection bar
  (hidden under a Verification filter, as Email is) and in the close dialog. A saved candidate's stage reads "Saved" (a
  bookmark icon, the word and a colour) in every list and header, and they count in a board's Closed column as "Saved".
  The candidate's Overview lists their other applications as links, so an application from the saved candidates and the
  one it came from link to each other ([saved candidates](../architecture/talent-pool.md)).
- **Tasks:** Mine, For the team and Everyone's, a sortable table with each whole row a link to where the task is done.
  Built: Mine is the tasks assigned to the person, For the team the tasks assigned to nobody in particular, and
  Everyone's every task the person can see, which job access limits (ADR-0015). Each is a toggle with its count, so the
  header band shows none (W7); Open and Done are the other toggle group (section 7). The columns are Task, Candidate,
  Job, Type and Created, with For in Everyone's only (Mine and For the team already say it, W7) and Done and Done by
  on Done; the dates are dates (T5). Open tasks start with the one that has waited longest, done tasks with the newest
  finished; a phone gets list items and a Sort chip. A row has no checkbox (ADR-0017
  decision 11): a requested scorecard opens the candidate's Scorecards tab with the sheet open on it, a review opens it
  with the sheet on the scorecard for the candidate's current stage, and every other task the candidate's Overview, where Next steps ticks a to-do or a rejection review. A review and a requested
  scorecard are done when the scorecard is submitted.
- **Reports:** a sidebar item after My tasks (G R) for every member, since every role sees jobs and candidates; job
  access narrows the numbers to the jobs a person sees. The Report template (section 7): Dates, Job and the Compare
  periods switch in the header band (on by default), and Export CSV as the band's action (not offered while the report is empty, and its tooltip says it holds every panel for these dates and jobs). Then a row of
  five headline cards (Applications, Reached interview with its share, Offers, Hires, Median time to hire), each with its
  change from the previous period as an arrow and amount, its sentence in a tooltip; Open jobs as a table, because what is open now comes first;
  Applications and hires by week beside Hiring funnel where the page is wide enough, so neither stretches across a large
  monitor, each row of the funnel with its count, its share of applicants and the share that moved on, the stage name
  linking to the candidates behind it and the biggest drop marked in its own row (no callout under the funnel); and Time to
  hire (median days in each stage, then by job) beside Sources (applications, hires and the hire rate; the careers widget
  is Your website and a job board's apply link is that board). The overall median shows only in the headline
  (W7), and no panel has a sentence under its title (W9). No verification data: no levels, signals or counts by level, and
  no row for a verification stage (ADR-0004, `docs/architecture/reports.md`). With nothing in the dates the page shows one
  empty card, not a panel with an empty header; an organization with no jobs yet is told what fills Reports and linked to
  Jobs, instead of being offered a longer range that cannot help.
- **Views:** the sidebar group is headed Views, so Saved means saved candidates only. Each shows its status icon and a short name
  (To verify), so nothing is cut off. Built: To review, To verify and Overdue show their status's icon; New today, Not
  screened and Interviewing an ordinary one. A view people save shows the list-filter icon (the users icon when a teammate
  shared it), never a status icon (I2). The short name is also the candidates page's title for the view. A Save view
  outline button ends the filter row of the Candidates table and of a job's table once the filters or sort differ from the
  view the table is on; its dialog asks for a name and Share with the team, off by default (`docs/architecture/saved-views.md`).
- **API keys and webhooks:** each page says what it is for, how to call it and where the reference is. The API reference
  is served in production. API keys has Last updated and History on its title's row: each key created and revoked, with
  its name, access and who. A webhook endpoint's page has Last updated and History (its address by host, its events, its
  switch and each rotation of its secret) beside Secret rotated (T6).
- **MCP server:** the values any MCP client needs (this deployment's server URL, the transport, the header), the
  key's access, the tools as pills, one generic config and a link to API keys. Never one client's setup steps.

## 22. Marketing pages

The marketing site (`apps/marketing`, ADR-0030) follows principles 1 and 9, sections 5 and 12, the checklist's Public pages
group (M1 to M12, W11, F7, X2 to X4) and this section. Where this section and 4.3 or 4.4 differ for a marketing page,
this section wins; 4.3 and 4.4 still describe the careers pages and the marketing pages not yet restyled. What the site
says is `docs/design/marketing-site.md`.

### 22.1 Where the rules come from

The target is the owner's "B + C, second pass" board: a dark hero band, a white page, the real product as the one bold
element, and nothing that reads as a generated page. The never-list and the one-bold-element rule draw on Anthropic's
frontend-design skill (github.com/anthropics/skills, Apache-2.0) and on Impeccable's anti-pattern rules
(github.com/pbakaus/impeccable, Apache-2.0). X2 to X4, W11 and F7 come from Vercel's Web Interface Guidelines
(github.com/vercel-labs/web-interface-guidelines, MIT), keeping the rules that apply to a static page with no forms. The
rules are written here in our own words. No third-party design skill is installed in the repository, and the
`design-review` agent enforces them (ADR-0030).

### 22.2 Spend boldness in one place

The real product, shown big and sharp, is the memorable element, and everything around it stays quiet. The home page
opens with the most characteristic thing in hiring: two applicants for the same job, one with no concerns and one with a
verification suggested, with its why, what speaks for the candidate and the next step (`ApplicantPair`). It is never a
generic dashboard. Structure (a border, a number, a label) appears only where it carries information.

Never, as decoration:

- identical card grids with one radius and one shadow, and a card inside a card;
- an icon tile above every heading, or a small label above every heading (the kicker);
- a gradient wash, or any purple-to-blue gradient;
- one word in a headline set apart by colour, italic or bold;
- all-caps labels, meta strings joined with middle dots, or numbered 01 / 02 markers;
- a fade-up on every section;
- grey text on a coloured band, or a bright single accent on a near-black page.

### 22.3 Bands

A page is a stack of full-bleed bands with the content column inside each. A band is `Section` (`tone`), and a dark one
is `DarkBand`.

| Band | Classes | Used for |
|---|---|---|
| White page | `bg-page text-foreground` | Most sections, and the header on a page with no dark hero |
| Warm band | `bg-background text-foreground` | An alternate section, between two white ones. Never a whole page, never two in a row |
| Dark band | `dark bg-background text-foreground` | The hero (with the header over it, `headerTone="dark"`), pricing, the close and the footer |

- `.dark` re-themes every token inside it, so a component never names a band. Text, borders, rings and chips take the
  band's tokens by themselves. Never nest a dark band in a dark band.
- The product frame is always the light theme (`light` on `ProductFrame`), also inside a dark band, because that is how
  the product looks. The status chips inside it are the dashboard's, in their light colours.
- Call-to-action buttons are the foreground colour (`bg-foreground text-background`): light on a dark band and ink on a
  light one, with no per-band class. The brand indigo (`bg-primary`) is for a light band only.
- Text is `foreground` for reading and headings, `muted-foreground` for sources and captions, and never lighter than
  that. Links in text are underlined: `primary` on a light band and `foreground` on a dark one. The header, the phone
  menu, the footer and the breadcrumb are lists of links, so they are not underlined until hovered or current, and they
  never turn `primary`, which a dark band does not use for links. Focus is the 2px `ring` outline
  with a 2px offset, which each band's tokens colour.

Contrast, computed from `tokens.css` by `apps/marketing/src/lib/contrast.test.ts` (text needs 4.5:1, a focus ring or the
edge of an outline button 3:1):

| Pair | White page | Warm band | Dark band |
|---|---|---|---|
| `foreground` on the band | 17.2 | 15.8 | 15.5 |
| `muted-foreground` on the band | 6.8 | 6.3 | 7.0 |
| `primary` (the link on light bands) | 7.9 | 7.3 | 7.5 (not used for links) |
| `accent-foreground` | 12.0 | 11.0 | 11.6 |
| `ring` on the band | 7.9 | 7.3 | 7.5 |
| Call to action, `background` on `foreground` | 15.8 | 15.8 | 15.5 |

| Status chip (light theme, on the product frame) | Label on its fill | Icon on its fill |
|---|---|---|
| No concerns | 6.7 | 4.7 |
| Review suggested | 7.0 | 3.0 |
| Verification suggested | 6.0 | 4.3 |
| Some signs of AI help | 10.2 | 10.2 |

### 22.4 Type

Bricolage Grotesque (`font-display`) for headlines, Hanken Grotesk for everything else. Headlines are short and plain,
set with `text-balance`. The display sizes are tokens that step up at `md` (48rem) and `xl` (80rem), like `page-title`,
so markup never holds a pixel size:

| Token | Phone | md | xl | Weight | Use |
|---|---|---|---|---|---|
| `text-display` | 40 / 40 | 56 / 56 | 76 / 76 | 800, -0.025em | The `h1` of the home page and of a selling-point page, up to 40 characters; a longer one takes `text-section` |
| `text-section` | 32 / 34 | 40 / 42 | 52 / 55 | 800, -0.015em | A section's `h2`, the closing band, and the `h1` of the pricing, compare, laws and blog pages |
| `text-band` | 32 / 34 | 44 / 46 | 44 / 46 | 800, -0.015em | The heading beside a list, such as Questions |
| `text-figure` | 40 / 40 | 48 / 48 | 52 / 52 | 800 | A sourced number |
| `text-price` | 28 / 28 | 32 / 32 | 32 / 32 | 800 | A plan's price |

At 76px a headline of a full sentence fills the first screen, so `PageHero` sets one over 40 characters at
`text-section` by itself (`LONG_TITLE` in `PageHero.astro`).

A lead is `text-xl`, body `text-base`, small text and sources `text-sm`, captions `text-xs`. A figure takes
`tabular-nums` (W11). Text never goes under 12px, and the root size is the browser's.

### 22.5 Space and width

- A section has 64px of space above and below on a phone, 96px from `md`, and 112px from `xl` (`py-16 md:py-24
  xl:py-28`, built into `Section`). Between a heading and its words 16px (`gap-4`), between blocks 40px (`gap-10`),
  between columns 64px (`gap-x-16`).
- The content column is `max-w-site` (1240px) and centred, with gutters of 16px on a phone, 24px from `md` and 32px
  from `lg`. Bands run edge to edge. A paragraph stops at 60ch, about 75 characters a line in Hanken Grotesk: the
  marketing stylesheet sets `--container-measure` to 60ch, so `max-w-measure` and long-form text follow it there, where
  the token's 72ch ran to about 90. The dashboard and careers pages keep 72ch.
- Radius is the brand's: 8px for buttons (`rounded-lg`), 12px for the product frame (`rounded-panel`), full for chips and
  avatars. Spacing sits on the 4px grid (section 4.4).

### 22.6 From 390px to 1920px (S9)

- A phone is one column with the header's links in a `details` menu, which needs no script (X2). From `md` the links
  are in the bar. Sections that sit side by side wrap by content (`flex-wrap` with a basis, or grid `auto-fit`), so no
  breakpoint hides anything.
- Nothing is a fixed width, and nothing scrolls sideways at 390px. The hero's two applicants stack, with the second
  indented from `sm`.
- From 1280px the content stops growing at 1240px and the bands carry on to the edge of the window, up to 1920px and
  beyond. The display type is the only thing that grows past `lg`.
- Anything that can grow is a list, not a row of tiles: integrations are a `dl`, plans are rows, questions are a list. A
  twentieth integration is one more row. A set that cannot grow (the four chips, the three examples) may sit side by side.

### 22.7 The product frame

`ProductFrame` shows the real product, as an image of the dashboard (`src`, with its size taken from the file, so the page
does not shift) or as markup built from the same components. One 1px edge, the 12px radius and the surface shadow, nothing
else: no browser chrome, no tilt, no glow, no second shadow. It is the light theme in any band. A caption says the people
are fictional ("Sample applicants. A person on your team decides."). The site shows levels, in plain words, and never a
reason code, a rule name, a threshold, a score or a percentage of AI.

An image is served as AVIF with a WebP fallback. Its `phoneSrc` adds the screen laid out for a phone as sources taken below
`md`, with their own size, so a phone downloads that file and never the wide one. A frame that would run long takes a
`max-h-*` class and shows the top of the screen.

### 22.8 A selling point

`SellingPoint` (`layout` `split`, `reverse` with the picture first from `lg`, or `stacked`) is one section: the buyer's pain, our promise, then the proof, in that order, with the product or a list
beside or under it. The layout changes with the content, so neighbouring sections never repeat one shape:

| Point | Proof is shown as |
|---|---|
| Real people | Two lists side by side: what you see and what ScreenHire never does |
| Never guessing | The team's activity beside the candidate's own page, in a stacked layout |
| Automate anything | One automation written out as When, If, Then, then three examples from one step to a whole process |
| Integrations | A plain list of what each one does |
| Built your way | A hiring template with its own stages |

The pain may carry a sourced number (`SourcedNumber`: the number, what it says and a link to where it comes from). A
pain with no verified number has none. Selling points come from `apps/marketing/src/lib/features.ts`, so home, product and
pricing say the same thing.

### 22.9 Calls to action

`CtaLink` is a solid link with an 8px radius, 44px high (52px at `size="lg"`), never a pill. `primary` is the foreground
colour, `indigo` is the brand colour for a light band, `secondary` is an outline in `muted-foreground`. One primary call
to action a band, not counting the header's Start free trial, which is the same action as the hero's. The marketing site has no `.button-*` classes.

### 22.10 The shell and the other components

| Component | Job |
|---|---|
| `SiteHeader` | The brand, the links, Sign in and Start free trial. `tone` is `dark` over a dark hero and `light` elsewhere. Product is a native `details` menu of the overview and the six product pages (`productNav`), so it opens and closes with its summary only: it does not close on Escape or an outside click, because that would need script. Below `md` a `details` menu holds the same links, with the product pages nested under Product |
| `SiteFooter` | A dark band: links, RSS, the in-development note |
| `Section`, `DarkBand` | A full-bleed band with the content column inside |
| `SellingPoint` | One selling point (22.8) |
| `ProductFrame`, `ApplicantPair` | The real product (22.7), and the hero's real-versus-flagged pair. A screenshot takes a `phoneSrc`, the same screen at phone width, which a phone downloads instead of the wide picture |
| `PageHero` | The dark band that opens a product page: the `h1` at `text-display` (`text-section` past 40 characters) across the full width, then the lead and call to action beside the page's one bold element (a screenshot, the applicant pair, a sentence). The page passes `headerTone="dark"` |
| `RuledList`, `ProofList` | A `dl` of terms and what each does with a rule between rows, which grows by a row and never by a tile (22.6); and a plain bulleted list: a selling point's proof points beside a picture, or any short list on a product page |
| `LevelChip` | The dashboard's four labels and icons: No concerns, Review suggested, Verification suggested, Some signs of AI help |
| `SourcedNumber` | A number with its claim and source link |
| `PlanRows` | The plans as rows, read from the plan catalogue (`packages/plans`): name and open-job limit, then the price. A plan with no price shows no amount, and one with no limit shows none |
| `QuestionsList` | Questions as a list with rules between them |
| `ClosingBand` | The dark band that ends a page. Without an `href` its call to action goes where the header's Start free trial goes |
| `PageIntro` | The opening of a compare, law, blog or 404 page: the `h1` at `text-section`, a lead and a slot for dates or a notice. White, or `tone="dark"` for a page that sells, with the header's `headerTone="dark"` |
| `CtaLink` | The call to action (22.9) |

A claim about a competitor is `Claim`: the words, the numbers of the sources it rests on (linked to `SourceList`'s anchors) and the day the vendor's pages were read. A compare page shows no claim without them. Index pages take a different shape each: compare a heading beside a ruled list, laws a ruled table, blog a date column and a larger first post.

A status on a marketing page is `LevelChip` (or `StatusBadge` for a state), never a chip built from `bg-status-*` (T1).

### 22.11 Motion

At most one deliberate moment on a page, and none when the person asks for reduced motion (`motion-safe:`). No fade-up on
every section, no looping animation, and a transition lists its properties.
