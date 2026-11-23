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
  - resource: apps/dashboard/src/lib/dates.ts
  - resource: apps/dashboard/src/lib/verification-area.ts
  - resource: apps/dashboard/src/lib/shell.ts
  - resource: apps/dashboard/src/routes/(app)/verification/+layout.svelte
  - resource: apps/dashboard/src/lib/components/sign-in-card.svelte
  - resource: apps/dashboard/src/lib/components/provider-marks/google-mark.svelte
  - resource: apps/dashboard/src/lib/components/provider-marks/microsoft-mark.svelte
  - resource: apps/marketing/src/components/StatusIcon.astro
  - resource: apps/dashboard/src/routes/(dev)/style-guide/style-guide.svelte
  - resource: apps/dashboard/src/lib/components/plan-card.svelte
  - resource: apps/dashboard/src/lib/components/settings-frame.svelte
  - resource: apps/dashboard/src/lib/components/overview-columns.svelte
  - resource: apps/dashboard/src/lib/components/header-band.svelte
  - resource: apps/dashboard/src/lib/components/plan-comparison.svelte
  - resource: apps/dashboard/src/lib/components/rich-text-editor.svelte
  - resource: apps/dashboard/src/lib/components/rich-text-toolbar.svelte
  - resource: apps/dashboard/src/lib/rich-text.ts
  - resource: apps/dashboard/src/lib/table-sort.svelte.ts
  - resource: apps/dashboard/src/lib/row-link.ts
  - resource: apps/dashboard/src/lib/components/sort-header.svelte
  - resource: apps/dashboard/src/lib/preview-window.svelte.ts
  - resource: apps/dashboard/src/lib/components/edit-preview-layout.svelte
  - resource: apps/dashboard/src/lib/components/edit-preview-switch.svelte
  - resource: apps/dashboard/src/lib/components/preview-pending.svelte
  - resource: apps/dashboard/src/lib/components/template-list.svelte
  - resource: apps/dashboard/src/lib/components/customize-posting-sheet.svelte
  - resource: apps/dashboard/src/lib/components/customize-posting-form.svelte
  - resource: apps/dashboard/src/lib/form-builder.ts
  - resource: apps/dashboard/src/lib/components/workflow/form-cards.svelte
  - resource: apps/dashboard/src/lib/components/workflow/add-form-sheet.svelte
  - resource: apps/dashboard/src/lib/components/workflow/form-send-moment.svelte
  - resource: apps/dashboard/src/lib/components/candidate/send-form-sheet.svelte
  - resource: apps/dashboard/src/lib/components/equality-report-panel.svelte
  - resource: apps/dashboard/src/lib/components/workflow/ai-use-row.svelte
  - resource: apps/dashboard/src/lib/form-sections.ts
  - resource: apps/dashboard/src/lib/templates.ts
  - resource: apps/dashboard/src/lib/components/versions-sheet.svelte
  - resource: apps/dashboard/src/lib/components/count-button.svelte
  - resource: apps/dashboard/src/lib/components/color-field.svelte
  - resource: apps/dashboard/src/lib/components/image-field.svelte
  - resource: packages/ui/src/questions/DropArea.svelte
  - resource: packages/ui/src/questions/FilesField.svelte
  - resource: packages/ui/src/questions.css
  - resource: packages/ui/src/questions/FileField.svelte
  - resource: packages/ui/src/questions/files.ts
  - resource: apps/dashboard/src/lib/components/branding-preview.svelte
  - resource: apps/dashboard/src/lib/components/bar-chart.svelte
  - resource: apps/dashboard/src/lib/components/chart-panel.svelte
  - resource: apps/dashboard/src/lib/components/week-chart.svelte
  - resource: apps/dashboard/src/lib/components/expandable-row.svelte
  - resource: apps/dashboard/src/lib/components/info-button.svelte
  - resource: apps/dashboard/src/lib/components/alert-grid.svelte
  - resource: apps/dashboard/src/lib/components/email-preview.svelte
  - resource: apps/dashboard/src/lib/components/candidate/latest-message-panel.svelte
  - resource: apps/dashboard/src/lib/components/report-stat.svelte
  - resource: apps/dashboard/src/lib/components/date-range-filter.svelte
  - resource: apps/dashboard/src/lib/report-range.ts
  - resource: apps/dashboard/src/lib/pipeline-tracker.ts
  - resource: apps/dashboard/src/lib/components/stage-rail.svelte
  - resource: apps/dashboard/src/lib/components/level-mark.svelte
  - resource: apps/dashboard/src/lib/components/ai-mark.svelte
  - resource: apps/dashboard/src/lib/components/candidate/header-meta.svelte
  - resource: apps/dashboard/src/lib/components/application-menu.svelte
  - resource: apps/dashboard/src/lib/components/workflow/workflow-tabs.svelte
  - resource: apps/dashboard/src/lib/components/workflow/stage-flow.svelte
  - resource: apps/dashboard/src/lib/components/workflow/stage-chips.svelte
  - resource: apps/dashboard/src/lib/components/workflow/try-it.svelte
  - resource: apps/dashboard/src/lib/components/workflow/option-card.svelte
  - resource: apps/dashboard/src/lib/components/workflow/try-it-start.svelte
  - resource: apps/dashboard/src/lib/components/workflow/try-it-end.svelte
  - resource: apps/dashboard/src/lib/components/workflow/stage-cards.svelte
  - resource: apps/dashboard/src/lib/components/reorder-grip.svelte
  - resource: apps/dashboard/src/lib/components/reorder-list.svelte
  - resource: apps/dashboard/src/lib/components/leave-dialog.svelte
  - resource: apps/dashboard/src/lib/components/move-confirm-dialog.svelte
  - resource: apps/dashboard/src/lib/components/switch-template-sheet.svelte
  - resource: apps/dashboard/src/lib/components/team-views-sheet.svelte
  - resource: apps/dashboard/src/lib/components/webhook-endpoint-sheet.svelte
  - resource: apps/dashboard/src/lib/components/secret-field.svelte
  - resource: apps/dashboard/src/routes/(dev)/style-guide/template-builder-demo.svelte
  - resource: apps/dashboard/src/routes/(dev)/style-guide/automation-demo.svelte
  - resource: apps/dashboard/src/routes/(dev)/style-guide/move-confirm-demo.svelte
  - resource: apps/dashboard/src/lib/components/workflow/stage-sheet.svelte
  - resource: apps/dashboard/src/lib/components/workflow/stage-fact-list.svelte
  - resource: apps/dashboard/src/lib/components/workflow/try-it-stage.svelte
  - resource: apps/dashboard/src/lib/components/workflow/try-it-email.svelte
  - resource: apps/dashboard/src/lib/components/workflow/problem-notice.svelte
  - resource: apps/dashboard/src/lib/components/workflow/validation-badge.svelte
  - resource: apps/dashboard/src/lib/components/starting-point-card.svelte
  - resource: apps/dashboard/src/lib/stage-facts.ts
  - resource: apps/dashboard/src/lib/try-it.ts
  - resource: apps/careers/src/components/JobFacts.astro
  - resource: apps/careers/src/components/JobCard.astro
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
  - resource: apps/marketing/src/components/shell/ActivityFeed.astro
  - resource: apps/marketing/src/components/shell/AssistantAsks.astro
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
   row align, and an overview's columns stack on their own, so panels never have to match in height across a row.
7. **Same pattern, same place.** One page template per job and one component catalogue. New patterns join the
   catalogue first.
8. **Fluid at every size.** Lists, boards and overviews fill the window, settings sit in one centred frame, and text
   keeps a measure. Flex and grid, never fixed widths, from
   phone to wide desktop. Controls never wrap.
9. **Evidence, not verdicts** (ADR-0004). A level, its reasons and a next step. Never a score or a reject button.
10. **Works out of the box, customizable when needed.** Every area ships a default that needs no setup: templates,
    email, branding and roles. Customizing is opt-in, reversible and shown next to the default.
11. **Every fact has one home.** Mark the exception, never the default. Design for the count, not the example: a list
    that can grow shows how many.

## 2. Review checklist

Authors check their screens against this list, and the `design-review` agent reports findings by these ids.

**Structure**

- **S1** The page uses a template from section 7 (list, detail, read-only, settings, form, overview, area, builder).
- **S2** One header band: title, its status badges beside it, the primary action on the right, then the facts row or
  tabs. A description is one plain sentence. On anything people edit, Last updated is the last fact, with History
  beside it (T6).
- **S3** Every section is a panel with its title inside a fixed-height header. Nothing readable sits on the page
  background, except a record page's closing audit line (W10).
- **S4** Panels in a row share a top edge; an overview's two columns share their first panels' top edge and then stack on
  their own (S10). No card inside a card. A set of bordered choices inside a panel, with no
  shadow, is not a card in a card: the Choice card and Billing's Plan comparison cards (section 11).
- **S5** Insets only inside a surface. Panels and cards take the soft surface shadow; raised and overlay layers take
  theirs (section 6). Nothing else casts a shadow.
- **S6** Pages fill the window: a page's content container has no max-width, except the settings frame (S10), and tables
  and boards never stop short.
  Text keeps a measure: a long paragraph stops at 72 characters and a form at 688px, except an editor whose panels sit
  in rows (Branding, section 18), while short sentences, callouts and notices use the full width. Content elements get a max-width, never a fixed width.
- **S7** No side column beside a table or a board: secondary things move to a tab, a sheet or the header. A detail page
  puts the record's facts in its header band, above tabs in a fixed order that hold its content; nothing sits at the
  window's far edge, where a wide screen puts it out of sight (section 7). There are two exceptions: the Settings menu, the
  200px column left of a settings page's content (section 11), and an overview's side column (S10), which holds panels
  beside panels, never beside a list page's table or a board.
- **S8** Moving between sibling pages never shifts the frame. Pages in one set (the settings pages, a record's tabs)
  keep one header height, so the menu, tabs and content start at the same place on each: a header's title row is at
  least a button's height, and a fact like Updated and History sits on that row, never on a row only some pages have.
  A sentence about the page goes in its panel's header, not under the title. The window keeps room for its scrollbar
  (`scrollbar-gutter: stable` on `html`), so a page that scrolls lines up with one that does not.
- **S9** Design for how many there could be, not how many there are today. Before choosing a layout, ask what happens at
  20 or 50 items. Tabs, toggles and cards side by side are for a set that cannot grow, such as a record's peer views.
  Anything that can grow (integrations, boards, templates, settings pages) is a list, table or grid that takes more with
  no redesign, with search past about 10, so adding one never changes the layout. The Settings menu is the worked
  example: an entry per setting under its group's name, a search field at the top, and a twentieth integration is one more entry
  in the Integrations group.
- **S10** Width follows the template (section 7). Lists and boards fill the window. A page with the Settings or account menu
  sits in the settings frame: the menu and one 960px column, centred in the content area so spare width splits evenly on
  both sides, the same on every such page so the menu never moves (S8). Panels fill the column and a form's fields keep
  688px inside them. An overview (Reports, the Verification overview, Billing) is a main column of about two thirds and a
  side column of one third from about a 1415px window with the full sidebar (69rem of content, 55rem in the settings column;
  a 1440px window clears both, and it is not an exact breakpoint), each stacking its own panels, never a row of panels that must
  match in height; one column below that, main then side. A row of headline numbers keeps its equal-width row. The owner's
  walk-through, 2026-11-22: half of a 2560px screen was empty, and panels of different heights side by side looked broken.

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
  column header is the one bare text button, marked by its sort icon (L2). Two places draw a button as underlined text
  instead: Count button's `link` (a header fact that opens its sheet) and Try it's small buttons under its one decision
  (section 11), which bordered buttons would crowd. A toggle in a labelled group is a choice,
  not an action: its label names the choice, the pressed one takes `accent` and the others stay bare (section 11). A
  button never looks like a badge.
- **A7** Reorder by dragging a grip. Clicking the grip opens Move up, Move down, Move to top and Move to bottom (Move left, Move right, Move to start and Move to end in a row of cards), and
  Space with the arrow keys moves it. No up and down arrow buttons.
- **A8** An editor whose result someone else sees (a questionnaire, a scorecard, an email template, a careers page)
  shows one thing at a time, at every width: an Edit / Preview switch at the top, with a pencil and an eye. The editor
  gets the room and the person's attention, and the preview is one press away, because showing both at once competes
  for both (ADR-0026). A read-only view keeps the switch and names its first choice for what it shows, Questions with a
  list icon, because nothing there can be edited (E3). Preview shows the result at its real size and adds no sentence
  about itself, unless it follows unsaved edits to something candidates already see (Branding), and then one line says so and that nothing is live until Save; a window icon beside the switch pops it out for someone who wants both, and the window follows the edits (the icon is left out on a phone, which has no room for a second window). The email composer keeps the switch and has no window: it is one email in a sheet, not wording
  others reuse. The Customize sheet does the same: its preview is the API's answer for one board's text, fetched when
  the preview opens, and it does not follow the edits.
- **A9** Anything that opens in place says so before it is hovered: a `chevron-down` beside the row's label, turning up
  when it is open, a hover background and the hand cursor, and the whole row is the control, with `aria-expanded`. A
  table row that opens keeps a button with the label and chevron in its cell for keyboards, and its detail is the next
  row; the row itself carries `data-row-opens`, which the theme gives the hand cursor (A10). A row with no chevron never
  expands. Expand all and Collapse all sit above a list of them (the owner's walk-through, 2026-11-21: a chevron at the far
  end of the row read as decoration).
- **A10** Anything that can be pressed shows the hand cursor, and a disabled control shows not-allowed. That covers
  buttons, tabs, switches, checkboxes, menu items and options, whichever component draws them, and a disabled text
  field or the label of a disabled control. It is one rule in the
  base layer of `packages/ui/src/theme.css`, so no component sets a cursor. A new kind of control joins that rule.
- **A11** An action says how far it reaches: what changes for others says who or what it touches, on its button or in
  its notice ("Published version 2. Jobs created from now on start from it.").
- **A12** Any file input takes a dropped file as well as Choose file, and is the Drop area (section 11), never a bare
  `<input type="file">`: the dashboard's `Input` has no file type. Every uploaded file shows its name and size with
  Remove, and a wrong type or a file too large is refused in the field's error, in words, before it is sent. A field that
  takes several files (a candidate's reply on their own page) uses the Drop area's `multiple` mode: one row per file with
  its name, size and its own Remove, and one refusal line per refused file in the field's error.
- **A13** Anything that answers a click looks like it will before it is hovered: a button or link style, or an icon that
  says what it does beside the words. A heading, a plain line of text or a chevron far from its label never hides a control
  ("What was checked" with its chevron 1,700px away; a stage name that opened a menu: the owner's walk-through,
  2026-11-21).
- **A14** Words that name a control are that control, or sit beside it. Copy never sends people looking for a button
  elsewhere on the page ("To change your card or cancel, use Manage billing" while Manage billing was in the header): the
  named words open it.
- **A15** Integrations stay provider-neutral. An integration shows in the product only once someone connects it (Slack's
  "By Slack message" beside "By email"); until then nothing mentions it outside Settings, Integrations. Where a task cannot
  work without one of a kind, the prompt names the kind ("Connect a calendar"), never one provider.
- **A16** A short create form (a handful of fields, no live preview) opens in a Sheet over its list, and the new row
  appears behind it on save. A long editor with a live preview (a job, a hiring template, an email template) has its own
  page. A dialog only confirms.
- **A17** What changes in place moves a little: a row opening or closing, a sheet, a tab's content. 150 to 200ms,
  ease-in-out, on named properties (M8), and none with reduced motion. Snapping read as abrupt (the owner's walk-through,
  2026-11-21).

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
  says where. The other is the Welcome line (section 11), which names the workspace and role the sidebar shows, once,
  to confirm what accepting an invitation did. Billing's plan cards are a third (section 11, Plan card and Plan
  comparison cards): what every plan includes repeats on the plan card and on each plan's card, as on a pricing page,
  so each card reads alone (the owner's walk-through, 2026-11-22).
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
  or an id goes on a panel's last line in caption size, never above the next step. On a candidate's Verification tab, when
  the checks ran is "Checked 19 minutes ago" in the Verification panel's header, the ruleset is in that word's tooltip, and
  "No automated decision has been made. Every action is logged." is the panel's closing line, inside it; the ruleset is not a
  line on the page, and stays in the activity log and its export (section 16). A clear result reassures without a verdict: a
  check icon and "Nothing unusual found", never "Verified" or "Genuine", and the same six group tiles a concern gets, each
  saying "Nothing found", "Off" or "Did not run", so it shows what was looked at.
- **W12** One decision per screen. A screen shows the few things its next decision needs, and everything else is one
  click away (a row that opens, a sheet, a tooltip). A screen that makes people read paragraphs before they can act fails,
  whoever it is for: a plumber setting up a first job reads no more than a recruiter does.
- **W13** One word for one thing. A job's hiring process is its pipeline, made of stages: never a journey or a workflow
  in what people read ("workflow" is the engine's word in the code, and the address `/jobs/{id}/workflow` keeps it). The
  job's button is Edit pipeline. A stage's time limit reads "Respond within 3 days" wherever it shows.
- **T1** Status is a badge: its icon, colour and label, always all three (section 3.2). The icon alone tells statuses
  apart. Status colours mean status only.
- **T2** Status comes first in its group: beside the title, or first in the row.
- **T3** In lists and tables, calm states are quiet (icon and muted text). Only states that need attention get a
  filled badge. Waiting states (Sent, Queued, Scanning) are grey: nobody needs to act.
- **T4** Design for the count, not the example: wherever a list can grow, show how many (2 hiring templates). In a
  header the count is a button that opens a sheet listing each item as a link, with search past 10. On a control (a
  tab, a filter chip, a button, a sidebar item) the count sits in the grey count badge after its label; in a table it
  is plain text. Steps are square markers on a line. Counts and steps never share a shape. The
  stage rail is a line of those markers in a header, because where a person stands is a header's main fact.
- **T5** Times follow section 13: time ago in text, dates in tables, the reader's locale (the browser's on the dashboard,
  the request's `Accept-Language` on careers pages), the exact time on hover.
- **T6** Anything people edit says who updated it last and how long ago: the last fact in its header ("2 hours ago by
  Avery Quinn") with History beside it, and a Last updated column in its list. ScreenHire's own templates and roles say
  "by ScreenHire". Something nothing has changed since it was created says so: the fact is Created, "2 hours ago by
  Avery Quinn", never Updated with nobody, and no second Created fact repeats it (W7). History opens a sheet with every
  change, newest first and grouped by day: who, when and what, with before and after on request. A record whose row
  holds a secret gets a history only once the secret (or what derives it) moves to a row of its own, as a webhook
  endpoint's did (ADR-0016), and the History never shows it. A webhook endpoint's header also keeps Secret rotated
  beside Last updated.
- **T7** A page that sums up activity (an overview, a report, a usage page) says how recent its numbers are: when the
  newest thing it counts happened, as time ago with the exact time on hover (T5), in one muted line under the headline it
  belongs to ("Last checked 2 hours ago" under Applications checked on the Verification Overview, "Last application 3 hours ago"
  under Applications on Reports, "Last call 5 minutes ago" under Model calls in Billing's AI use section). It is the newest event,
  never when the page loaded, because live updates keep a page's data current and "Updated just now" says nothing. With
  nothing to count yet, the line is left out (L1), except in a row of headline cards: there every card has the same rows
  (what it counts, the number with its change, the line), so a card with nothing yet says so ("No offers yet"), and on
  Reports each card says when its own number last moved (Last application, Last reached interview, Last offer, Last hire,
  and Last hire again under the median time to hire, which moves only with a hire).

**Icons and shortcuts**

- **I1** Lucide only, at the sizes in section 14, except the Google and Microsoft marks on the sign-in buttons, which
  their branding guidelines require. Navigation and main buttons have an icon and a label. The one letter that stands in
  for an icon is a saved view's square in the icon rail, the first letter of its name with the name in its tooltip
  (section 21); two views that start with the same letter are told apart by that tooltip.
- **I2** Status icons, the level icons included, appear only in status badges and where a status is the fact (a table cell, a
  panel's step), never as decoration. The one other place is a built-in view of a status, whose sidebar icon is that status's
  mark (To review `eye`, To verify `id-card`, Overdue `clock-alert`), uncoloured, so the icon rail tells the views apart
  (section 21). No two built-in views share an icon. The AI mark is information, not a status.
- **K1** Shortcut hints appear in tooltips, menus, the `?` dialog and Ctrl K, as `Kbd` key caps. No always-on hint
  bar. Single-key shortcuts can be turned off.

**Sizes and states**

- **F1** The screen works at 360, 768, 1024, 1440, 1920 and 2560px. The page never scrolls sideways; wide tables scroll
  inside their panel.
- **F2** Layout wraps by content (flex-wrap with a basis, grid `auto-fit`). Components respond to their container
  (`@container`); only page layout uses breakpoints, and only Tailwind's.
- **F3** Navigation and actions follow section 8 at each size.
- **F4** Touch (`pointer: coarse`) gets 44px targets and 16px inputs. Anything shown on hover also opens on tap or
  focus.
- **F5** Controls never wrap, except an Overview stat's title, the whole label of a card-sized link (section 11). Long text truncates (`min-w-0`, ellipsis) with the full value in a tooltip.
- **F6** A field's message sits under the field, and the actions that go with the field (Save note, Add tag) share
  that row on the right, rather than starting a row of their own.
- **E1** Loading shows skeletons of the same shape. Empty states say why and offer the one action. A list with no rows at all
  shows its empty state under the panel's title and nothing else: no search, no filter row and no Save view, because there is nothing to
  search or filter, and they return with the first row. A filter or search that hides every row keeps them and says what hides the rows. Errors use
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
  Never used, keeps its words. A recommendation is words in ink, never a status colour.
- **L2** Every table sorts. Each column header is a button: its text and a faint up-down icon, which becomes an arrow
  on the sorted column, and a second click reverses it. A header of a right-aligned column puts the icon before its
  text, so the text ends where the column's values do; every header lines up with its cells at every width. A table
  opens on one default sort: things people edit by Last updated, newest first; people, keys and endpoints by name; a job's candidates with those needing attention first. A
  table with a natural order (roles, by access) keeps it until someone sorts. The dashboard remembers each person's
  sort. On a phone a Sort chip beside the filters offers the same choices. A fixed grid, such as the permissions
  table, does not sort.
- **L3** The same button repeated down a list belongs in the row's menu (Message, Invite to apply, Remove), not as a
  button down a column. It holds for a builder's rows too: a workflow stage's Edit stage, Add an automation, Choose scorecard and
  Remove stage are one menu on its card, and a job's candidate row and board card have one menu with Move to.
- **L4** A table in a flush panel starts and ends at the panel's own padding (18px, 16px on a phone): its first and last
  columns line up with the panel's title and never sit closer to the edge than it. `Panel` sets this for any table in a
  flush body.
- **L5** A click anywhere on a table row opens its record, on every table (the owner's walk-through, 2026-11-21). The
  record's own control stays in the name cell and is the row's one tab stop: a link to its page, or the button that opens
  its sheet (a team, a tag's Rename, a member's Edit access, a scorecard, a board's Customize), marked `data-row-link`.
  A name that is a button is `TruncatedText` with `onclick`, so a cut name keeps its tooltip (F5).
  `rowLink` (`#lib/row-link.ts`, `{@attach rowLink}` on the `<tr>`) follows it from the rest of the row and leaves alone
  a click on a button, a menu, a checkbox, a text field or another link, a click that ends a text selection or closes a
  menu, and a tap on cut text, which opens its tooltip (F4); text that fits opens the record on a tap. Ctrl, Cmd, Shift
  or a middle click opens a link's record in a new tab. The row has a hover background, and the theme gives a
  `data-row-click` row the hand cursor while it holds the control. The checkbox's cell is the whole target for selecting: its label fills the cell (`selectCellLabel`),
  at least 48px tall and 70px wide on touch. A row with nothing to open (an API key, an invitation, a scorecard still
  hidden from the reader) has no control, no hand and no row click. A row that opens in place keeps A9 and expands.

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
| `input` | `#DDD7CC` | Button, badge, key cap and pill edges: they carry a label, so the edge is quiet |
| `field-edge` | `#8A8579` | The edge of a text field, search box, select, editor and the dashboard's drop area: 3.4:1 on `background`, 3.7:1 on `card` and `popover`, 3.5:1 on `muted` (WCAG 1.4.11) |
| `foreground` | `#1C1B19` | Primary text |
| `sidebar-foreground` | `#3A3630` | Secondary text, navigation (a general token for it is still to add) |
| `muted-foreground` | `#5F5A52` | Labels, captions (5.3:1 on `background`) |
| `primary` | `#4338CA` | The primary button, links, focus ring (`ring`) |
| `accent` | `#EEEBFB` | The selected item only (active navigation) |
| `accent-foreground` | `#2B2788` | Text on `accent` |
| `row-selected` | `#F3F1FC` | Selected table row |
| `destructive` | `#9A2F17` | Destructive menu items and their confirmations |
| `scrim` | `#1C1B19` | The dimming behind dialogs and sheets, at 10% (`bg-scrim/10`) |

A field has no label inside its edge, so the edge is what shows where to type: it takes `field-edge`, which is why a search box
beside filter buttons is deliberately the stronger of the two. A button, badge or pill names itself in text, and a choice card or
checkbox shows its state with the primary edge or fill, so those keep `input`.

### 3.2 Status (icon, colour and label, always)

Each status has its own Lucide icon. The icon alone tells statuses apart, so colour is never the only signal.

| Colour | Statuses and their icons | Tokens (`status-*`, `-soft`, `-mark`) | Text | Soft bg | Icon colour |
|---|---|---|---|---|---|
| Clear | Clear, a verification state with nothing to look at (`circle-check`, quiet beside a calm name); Delivering, Done; Hired, a candidate who got the job; Delivered, a webhook delivery; Active, an API key; Connected, the live updates connection and a Slack workspace; Verified, a sending domain, and Found, one of its DNS records; Explained, a Verification reason a person marked explained (`circle-check`); Nothing found, a group of verification checks that ran and found nothing (`circle-check`). Booked, an interview the candidate picked a time for (`calendar-check`). Completed, a data request the team answered (`circle-check`). Open, a job taking applications; Live, a job showing on a board; Posted by you, a listing the team says it put on a board itself (`door-open`). Published, a hiring template version new jobs can start from (`badge-check`) | `status-clear` | `#0B5D4F` | `#E3F1EC` | `#0F766E` |
| Review | Review, a verification state that needs a look, with the status's own icon, `eye` (screen readers hear "Review suggested"); the same icon marks a review-coloured badge that is not a verification state, such as the sign-in screen's note about an assistant on your computer. Overdue by 2 days (`clock-alert`). Still posted by you, a listing the team posted themselves that is still up after the job closed or its end date passed (`megaphone`). Found, a group of verification checks with findings that count toward the level, at any level (`eye`). The level's own icon would read as a check of its own, and the header already shows the level | `status-review` | `#7A4205` | `#FBEFD3` | `#C77700` |
| Verify | Broken, a setting that needs fixing (`circle-x`, a bare mark in the Settings menu); Verify, a verification state that needs a closer look, with the status's own icon, `id-card` (screen readers hear "Verification suggested"). Needs fixes, for a refused posting or a workflow with problems, and for a posting a board did not answer (Error), a webhook Turned off after failures, a delivery that Failed, a Slack workspace that is Not posting or Disconnected, a sending domain with Records to add and a record that is Not found or has a Different value, and a candidate whose address Email bounced or was Marked as spam (`wrench`). Unsafe file, flagged by the malware scan (`shield-x`) | `status-verify` | `#9A2F17` | `#F9E0D6` | `#B4461F` |
| AI (information) | Some signs or Strong signs of AI help: a bare `sparkles` mark with a tooltip, never in a badge. No signs shows nothing. On the Verification tab, the sparkles tile of a group of checks whose notes are all AI help ("1 note") and the sparkles mark on an AI finding's row, which says "Information only" to screen readers only. The sparkles mean AI help and nothing else | `status-ai` | `#2B2788` | `#ECEBFA` | `#2B2788` |
| Grey: waiting or not started | Sent, Queued, Submitted, Removing and Removal requested, waiting on a board; Pending, a webhook delivery waiting for its next try; Waiting for the candidate, an interview offered and not yet booked, and Waiting for a new time, a booked interview whose new times the team offered (`clock-4`). Scanning and Pending, a candidate whose verification checks have not finished, ScreenHire at work; Preparing copy, a data request's file being built; Connecting, the live updates connection (`loader-circle`). Closed (`door-closed`). Draft (`file-pen-line`). Archived, a template out of the pickers until someone restores it (`archive`). Saved, a candidate who left the job without a rejection, kept for future roles (`bookmark`). Not in effect: Not posted, a board the job is not on; Removed, a board that took the job down; Taken down, a listing the team posted on a board itself and marked taken down; Off, a webhook someone turned off; Skipped, a delivery not sent; Revoked, an API key; Rejected and Withdrawn, how a candidate left a job; Offline, the live updates connection; Cancelled and Expired, an interview that ended; Declined, a data request the team turned down; Dismissed, a verification state a person set aside, its tooltip saying who and why (`circle-minus`); Turned off and Trusted, a group of verification checks the organization turned off and a domain or network it trusts, both listed under Not checked on the candidate's verification (`circle-minus`); Not checked, a group of verification checks a candidate added by hand gave nothing to read (`circle-minus`). Information only, a group of verification checks whose notes never count and are not all AI help (a VPN alone), with the tile's words "1 note", and such a finding's bare mark (`info`, "Information only" for screen readers) | `muted-foreground` on `secondary` | `#5F5A52` | `#F4F1EB` | `#5F5A52` |

Status colours are never used for brand, decoration or facts. No status uses the warning triangle: it marks only the
verification cards that fed a level (section 16). Counts use tabular figures.

In code, every status is a name in `statuses` (`#lib/status.ts`) with its icon and colour, and `StatusBadge` draws it.
The icon colour is the `status-*-mark` token (`status-ai-mark` for AI), and grey statuses use `muted-foreground`. In
lists and tables only the review and verify statuses get a filled badge (T3). AI assistance is a bare sparkles mark with a
tooltip, shown only for Some and Strong signs, because it is information: it never sorts, filters or counts.

**A setting's mark** (`SettingMark`) is the one bare status icon outside a badge besides the AI mark and a status view's sidebar icon (I2): in the Settings menu, a green `circle-check` (Done) once a setting works and a red `circle-x` (Broken) while it needs fixing, with its words for screen readers and on hover. An optional setting nobody set up shows no mark, so nothing waits to be looked at for ever.

**Verification states** are one word everywhere a level shows (the candidate header, tables, the board, filters, views):
Clear, Review, Verify, then the muted Pending and Dismissed. The long names ("Review suggested", "Verification suggested")
stay only as screen-reader text, and a level's tooltip in the header says "Verification suggested" and nothing more. A calm
candidate shows a quiet Clear (circle-check and a muted word) beside their name. Review and Verify draw their status's own
icon, `eye` for Review and `id-card` for Verify, so the icon tells them apart without colour (T1). `LevelMark`
(`#lib/components/level-mark.svelte`) draws it in the status's mark colour and `VerificationChip` puts it beside the word,
with the badge's usual gap. The owner found the earlier glyph, three small squares filled to the level, cramped against the
word and hard to read as an icon (2026-11-09). Dismissed's tooltip says who set the result aside and why. A verification column sorts Verify, Review,
Dismissed, Clear, then Pending.

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
| `field-edge` | `#767168` (3.6:1 on `card` and `popover`, 3.8:1 on `background`, 3.7:1 on `muted`) | | |
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
| `candidate-border` / `candidate-input-border` | `#EDE3D3` / `#8C7F6B` | `#332E28` / `#807768` |
| `candidate-foreground` / `candidate-muted-foreground` | `#231F1A` / `#6A5D4D` | `#F2EDE6` / `#B3ABA0` |
| `candidate-chip` | `#F3E7D6` (text `#5A4630`) | `#2E2A25` |
| `candidate-cta` | `#C2462F` (white text, 4.98:1) | `#F08A6E` (text `#1A1310`) |

`candidate-input-border` is the edge of a field, a file control and the drop area, and of a secondary button, at least 3:1 on `candidate-surface` and `candidate-background` in both themes (3.9:1 and 3.6:1 light, 3.7:1 and 4.0:1 dark). `candidate-border` stays the quiet edge of cards.

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
(`packages/ui/src/questions`) above its fields: one bar and "40% done" in the hint size, semibold, where sending counts as
the last step, so the review never reads 100% (apply-flow.md section 7); each page's heading is its section's name. An
apply form without sections asks four questions a page by default. Marketing type is section 22.4. The dashboard's
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
- A job's facts are at most three chips (place, contract, pay) from one component, `JobFacts`, on the board, the job and the
  apply form. The workplace type goes into the place chip ("London, England, United Kingdom (Hybrid)"), and is left out when the job is
  on-site or the location already says it. Chips are never links or buttons (A5).
- A job card on the board (`JobCard`) reads title, the job's short summary clamped to three lines with `line-clamp-3`
  (CSS only, no script), then the chips. The summary is at most 160 characters, about the job rather than the company; until
  a person writes one it is the description's first sentence. The job page does not repeat it: the description is right below, and
  until someone writes a summary the two start with the same sentence. The card takes no employer colour. The branding
  preview's sample card starts with the same title, summary and chips, then adds the job page's link and Apply button,
  because those are what the brand colour paints.
- The board's heading is "Open roles", because the header already names the employer, over a lede of one sentence.
- A message about what just happened is `Notice`: an icon, the message, and one quieter line for what to do next. A page
  never builds its own bordered block for it.
- Every step of a form names itself in a visible heading, so the progress line ("40% done") is never the only clue. The review step says
  who receives the answers and when.
- A page that cannot open (an unknown, expired or closed link) says what happened in one sentence and who to contact:
  the hiring team at the employer, by the address they write from. "Open it again" is never the only advice.
- A long page of prose (the privacy page) starts with an "On this page" list of links to its headings.
- Header, breadcrumb and footer links are 44px tall on a coarse pointer and at least 24px with a mouse (WCAG 2.5.8):
  `pointer-coarse:min-h-11` on marketing pages. Careers pages use a plain `min-h-11`, so they are 44px on any pointer.
- Shape and space come from section 5. Radius is a token: 8px for buttons and inputs (`rounded-lg`), 12px for panels
  (`rounded-panel`), full for chips and badges, and never `rounded-[...]` or another size such as `rounded-2xl` (`public-style.test.ts` fails on any radius
  class but those three and `rounded-none`; a step marker on a candidate's own page is square, with no radius class). A card that is not a `Panel` still takes `rounded-panel` and `shadow-surface`. Margins, padding and gaps sit on the 4px grid
  (4, 8, 12, 16, 20, 24, 32, 40, 48, which is `1`, `2`, `3`, `4`, `5`, `6`, `8`, `10` and `12`): no half steps, no `7`,
  `9` or `11`. A button is 44px high (`min-h-11` on careers pages, `h-11` on marketing pages), and a careers input or
  choice is 48px (`min-h-12`). A time on the interview page is the one exception: a chip 44px high (`min-h-11`), because its radio is hidden under it and the whole chip is the target.
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
  content. Enter in a line presses the primary, except on the last step, where only the button sends. A page of
  questions opens with the focus on its first question, and a text field left wrong shows its check line under it
  (`:user-invalid`, no script).
- The job page reads title, chips, the description, then the Apply button with "Takes about 4 minutes" under it (a
  `clock` icon and hint text), then one muted line about the checks with the privacy link. It has no notice panel: the
  notice is on the form, where collection starts.
- The page after Send is a warm finish: a check mark in a soft circle above "Application sent", one line of thanks,
  then "What happens next" as two steps each led by when (Next, Any time), the last linking the candidate's own page.
  The heading and the thanks already say the employer has the application, so no step repeats it (W7). It names no date and never says who reads the application.
- A candidate's own page shows an application's four steps as square markers on a line (T4), in one row at 390px, each
  with its name under it: a step done is filled with a tick, the step it is at is outlined with a filled centre and a bold
  name, and the steps to come are empty. A done or current marker is in `candidate-brand-text`, an empty one and the line
  to it in `candidate-input-border`, so each keeps 3:1, and the line runs under the markers through their centres. They are
  a list named "Where your application is", never radio-like circles in a grid, which read as choices.
- The footer carries "What we collect and why" and "Your applications" (not on the candidate's own pages, which it opens), so a page does not repeat either
  in its body (W7). The exceptions are the notice on the apply form and the job page's one line about the checks, which
  stands in for the notice there: both link the privacy page because they are what it explains. `public-style.test.ts` also
  fails on candidate copy, and the two emails that once carried it, that says a person reads everything or your answer, that
  every application or yours is reviewed, or that no software decides.
- A claim about who reads or decides must hold for every employer's setup. An eligibility question can end an application
  with no person involved when the employer chose that, so candidate copy says what the checks never do ("our checks never
  reject anyone") and never that no software, or always a person, decides, or that the employer reviews every application.

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
| Content widths | Pages fill the window: a page's content container has no max-width, except the settings frame (`max-w-settings-frame`: 74rem of content, the 200px menu, a 24px gap and a 960px column, plus the page's gutters, centred, section 7). Long paragraphs stop at 72ch (`max-w-measure`, 60ch on the marketing site, 22.5) and forms at 688px (`max-w-form`). If a layout needs a limit, the element gets a max-width, never the page |
| Board columns | Every stage column shares the board's width equally, at most 384px, so two busy stages never stretch their cards across half a wide screen; when the stages do not fit, a column narrows to its least, 264px with candidates and 188px empty, so the empty ones give way first, and past that the board scrolls inside itself (the owner's walk-through, 2026-11-21). A card is at most three rows: the name, then on the same row any status that needs a look, with the card's menu at the row's end (shown on hover and focus, always on touch); the rating and up to three tags and how many more; when they applied and, once they have moved on, how long they have been in the stage. A calm card with no rating or tags is two rows. The stages that end a candidacy (Hired, Rejected, Withdrawn) share one 188px Closed column that counts each; each of its rows (and Saved) with someone in it is a button with a chevron (A9) that opens in place to that stage's candidates as plain cards linking to the candidate, collapsed to start |

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
| List | Jobs, Candidates, Templates and My tasks; the team, roles, API keys and webhooks in Settings | The top-level lists fill the window; a list in Settings fills the settings column (Settings, below), because the frame is the same on every settings page. The same controls in the same places on every list page, so moving between them nothing has to be found again (the owner's walk-through, 2026-11-18). The header band holds the title, with no count beside it, and the page's main action on the right (New job, Add candidate, New template; My tasks has none). When the page holds separate collections they are tabs under the title (`RecordTabs`), each with its count: Templates' Hiring, Scorecards and Emails, Candidates' Current, Saved and All, My tasks' Mine, For the team and Everyone's, and Team's Members and Teams (at the top of its content rather than on the band, so the band keeps the one height of the settings pages, S8; its Invitations panel keeps its own count, being a second panel on the Members tab); Jobs is one collection and has none; the other settings lists (Roles, API keys, Webhooks) and Passkeys have no tabs or pills, so a count lives in the grey count badge of the list panel's header, shown only once there is a row, and `HeaderBand` has no count to give. A tab's main action is the page's: Team's is Invite on Members and New team on Teams. Then one full-width table panel, with no side column (S7), named after what it shows (Open jobs, Hiring templates, Current candidates, Open tasks), with its search at the right of its header (`placeholder="Search"`, labelled with what it searches). Under the header, one row: the state pills first, each with its count, All included (Jobs' All, Open, Draft and Closed; Templates' All, In use and Archived; My tasks' Open and Done, which have no count because Done is each scope's newest 200), then any filter menus, and Save view at the row's right end. A page with no rows at all keeps the panel's title and shows only its empty state, with no search, filter row or column headers. That means Jobs with no job, Candidates with no candidate, and a Templates tab with nothing on it, archived included; a job's candidate table follows the same rule. A filter that hides every row is not an empty page, and keeps its row. My tasks keeps Open and Done even when empty, because Done is asked for only when it is chosen, so an empty Open list cannot say whether Done has anything |
| Detail | A candidate, a job, a webhook endpoint | Header band (identity, status badges, a meta line for a person, actions, the facts row); tabs in a fixed order under it; the content in panels below, in a grid of two columns on a wide page and one on a narrow one (S7) |
| Read-only view | A ScreenHire template, anything you cannot edit | As detail; data as labels and values, never disabled inputs; it says why it is read only and what to do (E3); Copy or Edit as the primary action |
| Settings | Every page with the Settings menu or the Your account menu: General, Team, Billing, Branding, Profile and the rest | The settings frame (`SettingsFrame`, S10): the menu, a 24px gap and one column of at most 960px, 74rem of content with the page's gutters on top (`max-w-settings-frame`), centred in the content area so a wide window's spare width splits evenly on both sides, as Linear, GitHub and Stripe do. The frame is the same on every such page, whatever the page holds, so the menu starts at the same place on each (S8), and the header band's contents sit on the same box (`HeaderBand frame`): the title above the menu, Last updated and History at the column's right edge. Panels fill the column; a form's fields keep 688px inside them, labels above fields, actions at the panel's end. A list fills the column. Billing is an overview inside it (Overview, below). Below `lg` the menu is a row above the column and the frame fills the window. The column is a container (`@container/column`) |
| Form | Creating or editing a record outside Settings: onboarding, an invitation, a role, a page someone's role may not open | One column, 688px wide, centred, labels above fields, actions at the end. A form longer than a window, with one Save for all of it (Verification's Checks), holds that Save in a bar stuck to the window's bottom edge, so a switch near the top never needs a long scroll to keep |
| Overview | Reports, the Verification overview, Billing | A row of headline numbers in their equal-width row, then two columns (`OverviewColumns`): a main column of about two thirds and a side column of one third, each stacking its own panels, so heights never have to match across a row. Two columns from 69rem of content on a page and 55rem in the settings column, which a 1440px window with the full sidebar gives both; one column below, main then side. The main column holds the work the page is for, the side column what supports it. Reports: the weekly chart, Open jobs and Time to hire, then the Hiring funnel, Sources and any equality monitoring totals. The Verification overview: Findings by group, then AI credits. Billing: the plan card, This month so far, Invoices and AI use with what the checks used, then Payment method, AI credits, Auto-reload and Plans, with Purchases and Deductions under both, because a long table never pushes the side column down on a phone. Reports' header band holds the report's one action (Export CSV) and one row of filters on its bottom edge: the date range first, then Job, then Compare; the row wraps by content on a phone rather than cutting a control off. Each chart is a Chart panel (section 11). Filters scope every panel, never one panel alone |
| Area | The Verification area (Overview, Activity log, Checks) | One header band for the whole area (title, the tab strip on its bottom edge, Last updated and History on the title's row on the tabs that show saved settings). Each tab then uses one of the other templates under it: Overview for the Overview, List (full width) for the Activity log, and compact rows at full width for Checks, whose groups put their settings beside what they are. An area holds what its pages are about: money goes to Billing, an integration to Settings and a person's notifications to Your account, never into an area's own tab (the owner's walk-through, 2026-11-22). The band keeps one height on every tab (S8). A tab a role cannot use is left out of the strip and its address returns to the Overview (A3) |
| Builder | The workflow graph; the questionnaire, scorecard and email template editors | Header band, then a full-width work area, with the Edit / Preview switch when someone else sees the result (A8). A form in that area keeps the 688px column (S6). The graph's nodes edit through the same sheets and forms as the list view beside it, never a second editor, and nothing in it needs dragging: pressing a node and the plus buttons do every edit. See Workflow graph in section 11. A validation message shows on the node it is about, as it does under its form control (W7) |

Sign-in, onboarding and the root error page sit outside the app shell, with no sidebar. An unknown address and a page that failed to load inside the app keep the shell (`(app)/+error.svelte`): the sidebar, the top bar, then the page's title and a panel with the way back. A title that changes in place, such as
"Check your email" after a link is sent, takes the focus.

- **Sign-in card:** signing in and finishing an email link (`SignInCard`). One card, 416px at most, centred on the page
  background with a 16px gutter on a phone, and nothing outside it (S3). There is no brand bar, because the card carries
  the mark. The card holds, in order:
  - the 40px logo mark;
  - the page's h1, "Sign in to ScreenHire";
  - one muted line, "New here? Signing in creates your account.";
  - any error from a provider that sent the person back, or from a passkey that did not work ("That passkey did not
    work. Use another way to sign in."), above the ways to try again;
  - the Work email field, then the one primary full-width button, "Continue", which sends the sign-in link. The field has
    `autocomplete="email webauthn"`, so a browser that can offer one shows a saved passkey inside it;
  - one "or" divider, only when the email field and at least one other way are offered, so the passkey and the providers
    read as one list of other ways in;
  - "Sign in with a passkey", a full-width outline button with `key-round`, first in that list and only in a browser that
    has WebAuthn. Closing the browser's own prompt says nothing;
  - each configured provider as a full-width outline button, "Continue with Google". Google and Microsoft carry their
    own marks (section 14). Any other provider, such as Dev SSO or an organization's own OIDC, has `log-in`;
  - at the foot of the card, in a muted caption, the agreement line: "By continuing, you agree to ScreenHire's Terms of
    Service, Privacy Policy and Data Processing Agreement." Each document is a link to its configured address
    (`ScreenHire:Legal`), opening in a new tab, and the line names only the documents that have one. With none
    configured the line is left out.

  When email links are not configured the field, Continue and the divider are left out and the buttons stand alone. With
  no provider and no passkey support there is no divider.

  "Check your email", "Finish signing in" and "This link doesn't work" are the same card with their own title and no
  agreement line, which belongs to the page where the person signs in. Trello's "Remember me" (the session lasts 14 days
  and renews while in use), Apple and Slack buttons and "Can't log in?" (the emailed link is the recovery) are left out.
- **Onboarding, invitations, connecting an assistant and the root error page:** the brand bar takes the top bar's place, then
  the form template.

- **Top bar:** the breadcrumb shows where a page sits under another (Jobs, then the job; Settings, the group, the page) and is silent on a top-level page, whose name is its header band's title (W7). Live, Search, the bell, the shortcuts and the theme switch are always there.
- **Header band:** a white strip under the top bar. Title first, status badges beside it, actions on the right, then
  a labelled facts row (a short label 12px above each value 14px, a divider between them) or tabs on its bottom edge.
  A record with a pipeline (a candidate) puts a meta line, its rating and tags, and the stage rail between the title and the tabs.
  On anything people edit, Last updated is the last fact, with History (T6). A description under the title is one
  plain sentence. An explanation goes where it applies: a tooltip on the fact, a hint by the control, the reason on a
  disabled item, or a notice for the people it affects, never a paragraph of loosely joined facts. One muted line
  under the title may hold a person's contact details, each an icon and its value, with no labels. On a form page the
  band's contents line up with the 688px column, and on a settings page with the settings frame.
- **Panel:** header (title, optional count, actions on the right), body, optional footer inset.
- **Columns:** a detail page's panels sit in a grid that uses the width: two columns, gap 20px, once the content is
  1024px wide (`@5xl`), with the work (what needs you, scorecards) on the left and the record's things (the interview, the latest message)
  on the right; one column below that, in the same order. The record's facts are in the header, so every tab shows them
  and none sits at the window's far edge.

## 8. Screen sizes

Four size classes on Tailwind's breakpoints. No custom breakpoints.

| | Phone | Tablet | Laptop | Desktop |
|---|---|---|---|---|
| Window | under 768px | 768 to 1023px (`md`) | 1024 to 1535px (`lg`, `xl`) | 1536px and up (`2xl`) |
| Navigation | Sidebar as a sheet behind a menu button; Back on detail pages | Icon rail, labels in tooltips; a button in the top bar opens the full sidebar, which holds the Views menus; Browse team views is an icon in the rail's fixed group above Settings | Full sidebar | Full sidebar |
| Columns | One | One | A detail page's panels in two columns from 1024px of content; an overview's main and side columns from about 1440px (section 7) | Same, filling the window; Settings stays in its centred frame |
| Header band | Facts in a two-column grid | Facts wrap | One line of facts where they fit; beside the full sidebar they may wrap | One line of facts |
| Actions | Primary in a bar at the bottom; the rest in More. A form keeps Cancel and its primary at its end (section 7) | In the header; extras fold into More | In the header | In the header |
| Tables | Rows become two-line list items, with a Sort chip beside the filters (L2); a long list shows 50 at a time and Show more adds the next 50, saying how many are shown of how many there are (S9) | Scroll inside the panel; first column sticks, and so does a row menu at the end (the Jobs table's), so it stays reachable. While it scrolls the row's link covers that cell only | Full; a table wider than its panel beside the full sidebar scrolls inside it, as on a tablet | Full, optional columns may show |

The sidebar is shadcn-svelte's `Sidebar`: `collapsible="icon"` gives the rail and its mobile mode gives the sheet.
On a phone the Automations tab is the List view: the map needs room, so it is not drawn, and the rows open the same sheets that edit it.
Checked at 360, 768, 1024, 1440, 1920 and 2560px.

## 9. Flow

| Move | How |
|---|---|
| Across areas | The sidebar. It is the only app navigation. Switching areas fades. |
| Forward | From a list to a record. A click anywhere on the row opens it (L5). Filters and scroll position survive the trip back. |
| Back | The breadcrumb goes up a level. Browser Back goes where you came from. |
| In place | A sheet for quick work: add a note, quick edit, filters, History, the items behind a count, Invite, an email. The record stays behind it. |
| Preview | An editor's Edit / Preview switch swaps the view in place; the window icon opens it in a second window that follows the edits (A8). |
| Files | A file opens in a full-window viewer from its file row. A page never embeds a preview. |
| Confirm | A dialog, only to confirm or decide, or to name something (Save view, Rename view). A menu is the gentler choice. |
| Create | A16: a short form (a handful of fields, no live preview) is a sheet over its list: Invite, New role, Add candidate, Add endpoint, Create key. A long editor with a live preview (a job, a hiring template, an email template) has its own page. Never a dialog. |
| Tabs | Peer views of one record, or the pages of one area (Verification), under its header. Never steps in a sequence. |

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
| Header band, Panel | Our components (`HeaderBand` with `MetaItem` for a meta line, `Fact` for a labelled facts row, or its `summary` slot for a candidate's rating, tags and stage rail; `Panel`) | Section 7. A candidate's header is the name and the verification state, the actions on the right, one meta line of fact lines, the rating and tags, then the stage rail (section 21). A meta line, where a page still has one, is icons and values that read on their own, the label only for screen readers (W9). Pages whose values need their labels (a job's salary, a template's version) keep the facts row. A panel with only a screen-reader `label` and nothing for its header draws no header band (Reports' empty and error states). The pages of a set whose menu or tabs sit under the band (the settings pages) pass `oneRow`: the title's row never wraps, at any width, so the menu starts at the same place on every page of the set (S8). The title keeps its width and the actions give way: Last updated truncates beside the title with an ellipsis, and History keeps its icon with its name for screen readers once the band is narrower than 448px. On a phone a page's other actions move to the bar at the bottom (section 8), as Email domain's Check records and More do, so the row holds only the title, its status and Last updated. A queue's status can be words: Data requests says "3 open" in muted text beside its title while a request is open, never as a count pill, and nothing when none is. |
| Status badge | `StatusBadge` on shadcn `Badge` (`clear`, `review`, `verify`, `ai`, `neutral`; `quiet` for calm states in lists) | Icon, colour, label (T1, T3): 24px high, the 14px icon in its icon colour, then the label in 12px semibold. It takes a status name from `#lib/status.ts`; each status's icon is in section 3.2 |
| Overdue badge | `OverdueBadge` on `StatusBadge` and `Hint` | Section 3.2: the review badge, "Overdue by" and how long, the due time in a tooltip that opens on hover, focus or tap. First in the stage cell of a candidate table and list item, first in a board card's status row, and beside "Here 3 days" in an application's stage rail (S2, T2). Never on a hired, rejected or withdrawn stage |
| Fact pill | `Badge` muted variant | `secondary` fill, `input` edge, 12px; only on white surfaces. For tags, for facts in a list item or a sheet, and a policy beside a verification level; never in a labelled facts row or a table cell (W3, L1) |
| Count | `Badge` count variant | The grey count badge (min 28px) after its label on a control (T4); plain text in a table |
| Toggle | shadcn `Toggle`, in a labelled `role="group"`, or the rich text editor's `role="toolbar"` | Filters with a few choices and peer views (Table, Board), and the formatting toggles of the rich text editor. The pressed one takes `accent`; its count follows the label in the count badge, white on the pressed one (T4). One line that scrolls inside its panel, never wraps (F5). In a narrow container a few short toggles whose every choice matters at a glance (a job's authenticity levels) stack one per line instead |
| More menu | `MoreMenu` on shadcn `DropdownMenu` | An icon button with its label in a tooltip (A4). Holds extra actions, folded ones on phones and destructive items (A2, section 10) |
| Step marker | `StepMarkers` (across), `StepLine` (down) | 28px square, radius 7, white with an edge, joined by a line: across for progress, down for steps to do in order (a domain's setup). A workflow's stages are the Stage flow, below |
| Workflow graph | `WorkflowGraph` (`workflow/workflow-graph.svelte`), `StepNode`, `RoutedEdge`, laid out by `toGraph` | The Automations tab of a pipeline: the stages run left to right as the backbone, and each one's automations hang beneath it, one column each, read down as When, If, Then. An application arriving and the checks finishing hang under Applied, and a stage's time limit under that stage. What no stage holds (a run by hand, any stage's time limit, a form answered, an interview booked) hangs in a thin "Any stage" band below the deepest stage chain, its columns standing on the stages' columns, joined to no stage, and left out while it is empty. Every line is orthogonal, has an arrowhead and runs one way, never through a node; a move to a stage rises beside its column and runs along a lane above the stages, labelled "Moves to", the only words a line carries. A node has its kind's word (When, If, Then, Lane, Stage) and one look per kind, in tokens, and no icon: the word is the label, and the text under it is short and plain. A When node says what starts it ("Someone applies", "Moves into Phone call") with the automation's name under it, an If node its conditions ("Right to work is No", "Verification is Review or Verify") and a Then node the step. Where the editor can change it, the whole node is one button with the hover, the focus ring and the hand, and opens the automation in its sheet (Automation sheet), on the tab chosen last; the Any stage band or a stage opens its automations as rows; a plus where a condition, an action or an automation would go adds one. Hovering or focusing a node lights its path and quietens the rest by edge alone, text keeping its contrast, with no motion for reduced motion. An empty workflow says what to do, with one button, under the stages. It opens at full size, because node text is 12px there and W1 sets that as the floor: centred when the workflow is narrower than the canvas, otherwise from the top left, with the rest a pan away and Fit for an overview, so 20 stages and 50 automations read the same (S9). The tab's panel header has a List | Map switch (Toggle) and Add automation, which opens the Recipe list; the empty map keeps its one button under the stages, so the header's is left out then (W7). A phone shows only the list. End stages stand after the open stages, set apart and named End stage, and no line of the backbone reaches them, because an application can finish at any of them from any stage |
| Automation sheet | `workflow-editor.svelte` on shadcn `Sheet`, `RecordTabs`, `AutomationName`, `AutomationSentence` and `AutomationForm`; the choice remembered in `preferences.automationView` | One automation in a sheet titled with its name, the only place the name shows as a heading, and with no subtitle. Under the title, two tabs, Sentence and Rules, both editing the same automation, so switching loses nothing; under the tabs, the one Name field and the one On switch both tabs share, then the tab's content, and Done as the sheet's one primary (A1). The sheet is one width in every view (a stage's automations, the recipes, one automation, either tab), so switching changes only what is in it. Sentence is the default; the tab the person picks last is remembered in this browser, as the pipeline's table or board is, and every automation opens on it. An automation too complex for a sentence (it turns someone down, whose review choice only Rules has, or a condition checks something its trigger does not offer: `sentenceLimit`) opens on Rules, and its Sentence tab says why in one line rather than hiding; opening it does not change the remembered choice. Rules is When, If and Then, each a well like the sentence's with its controls in rows of the standard 36px height, a remove icon button on every condition and action, and Add condition and Add action as the sentence's dashed chips with a plus; Remove automation sits at its foot |
| Automation sentence | `AutomationSentence` and `AutomationBlank` (`workflow/automation-sentence.svelte`, `workflow/automation-blank.svelte`), the words and choices from `#lib/automation-sentence.ts` | The Sentence tab of the automation sheet: "When [someone applies] and [their answer to Right to work] [is] [No], [ask] [the team] to review and [add the tag] [Not eligible]". A blank is a bordered chip with a chevron (A6, A9) that opens a short list of plain choices: a single choice closes the list; a condition's values tick and stay open, and one value is stored as is, two or more as is one of, so "Verification is Review or Verify" needs no operator to pick; a text or number blank is a field that fills as it is typed. The facts are grouped as Their answers, The application and ScreenHire's checks, and a list past about ten choices has a search (S9). "and if" and "then also" are dashed chips that add a condition or a step; a condition or a step is removed from its own list. A step left with no text, such as a new tag, is dropped with Undo (section 10). The validator's messages sit once under the sentence and mark their blank (W7). The guardrail is in the choices (ADR-0004): once the sentence reads a check, the step list offers only asking a teammate to review, adding a tag and moving into a review stage whose own automations are also allowed after a check, with one line at the end of the list, "A check can only ask for a closer look. A person decides."; a check is not offered to an automation that has a step a check cannot start (an email, an interview, turning someone down), and the list says choices are left out. The sentence never adds a rejection, never offers AI help for a new condition, and has no accommodation or AI-use answer to offer, because neither is a fact. Adding a rejection and removing the automation are on the Rules tab |
| Recipe list | `AutomationRecipes` (`workflow/automation-recipes.svelte`), the recipes from `#lib/automation-recipes.ts` | What every Add automation opens, in a sheet titled Add an automation: ready-made automations written for this template's own questions and stages, in four groups (Keep candidates in the loop, Get the team to act, Check the must-haves, Look closer when the checks suggest it). A recipe is its name, one line saying what it does, and an outline Add. An added recipe is hidden, as is one the workflow already does under another name, so nothing is sent twice; a status line says what was added, with Edit. A recipe asks "the team", never a person by name, and none turns anyone down on a check or sends anything after one. Opened from a stage, only that stage's recipes show. Write your own, an outline button above the groups, adds an automation that is complete from the start (it asks the team to review, which every trigger and every condition allows) and opens its sentence. There is no card explaining the guardrail (S4): the choices keep it |
| Automation list | `AutomationList` (`workflow/automation-list.svelte`) | The automations as rows grouped by the stage they belong to, as the map hangs them (Applied holds an arriving application and the checks), with Any stage last: the name (", off" after it while switched off) and its sentence, muted. The whole row is the button that opens the automation in its sheet, on the tab chosen last (A5: it opens a sheet); where the workflow only reads it is text. It is the List view of the Automations tab, the whole tab on a phone, and what a stage's or Any stage's sheet holds, with Add automation under it |
| Stage flow | `StageFlow` (`workflow/stage-flow.svelte`), in the Stages panel of `WorkflowSummary`, on a template's page and a job's pipeline page wherever the pipeline reads only (E3) | A pipeline's open stages in order, as a row of columns where every stage fits and the vertical list where they do not, so 5 stages and 20 read alike and the page never scrolls sideways (S9, F1). A stage is its step marker and name, then its Stage chips, read only: the count chips still open their lists, whose items are text with no links, the time limit and the scorecard (by its name) are icon and muted words with no border (the word Scorecard for screen readers only, W9). A stage with nothing on it says "Nothing happens automatically", muted. Columns are 12rem at least. The choice is made in CSS from the container's width and the number of stages, so the first paint is right and nothing is measured by script (S8, F2); the vertical list draws the line down between markers, the row a hairline between columns. Keyboard and screen reader order is the stages' order in both, with no reordering. The panel's footer has the Any stage row (its automations chip) and the Ends row (each end's name and its chips), as the cards have. The panel has a row of its own, so the columns have the page's width |
| Stage cards | `StageCards` (`workflow/stage-cards.svelte`), with `StageChips`, in the Pipeline tab of the builder, its first tab | The pipeline as a flow that reads without reading sentences (the owner's walk-through, 2026-11-22: the generated sentences in narrow columns were rejected twice). The open stages are cards that share the panel's width in equal columns, as the board's columns do: one row while every card gets at least 11rem, and rows of equal columns at 11rem when they do not, never one stretched last row; the row stops at 32rem a stage, so a short pipeline on a wide screen keeps its cards in proportion. The choice is CSS, from container units and the number of stages, as the Stage flow's is (S8, F2). One card a line on a phone. A connector runs from each card to the next, a line with an arrow: across the gap in a row, down it when the cards stack, and none from the last card of a row that wraps. A card is its step marker, the stage's name and one row of Stage chips; nothing else. A long name wraps rather than cuts, since the name is a button and a tooltip has no place inside one. The name is a button that looks like one: a hover fill, a pencil after it, the tooltip "Edit <stage>", and it opens the Stage sheet (A5). A stage with nothing on it says "Nothing happens automatically", muted, in place of the chips. The grip and the More menu (Edit stage, Add an automation, Choose scorecard, Remove stage) show on hover, focus and touch (F4). The grip reorders the open stages by A7 in its horizontal form. A plus on the connector adds a stage there and opens its sheet to name it; the last card's plus sits on its bottom edge; a new stage goes before the stages that end a candidacy. Under the flow, the automations that belong to no stage (a run by hand, any stage's time limit) are one Any stage row with its automations chip, and the stages that end a candidacy are the Ends row: quiet outline buttons, each its name and what it sends counted (an automations and an emails count with their icons), opening the stage's sheet, which holds Add an automation and Remove stage. Stage numbers stay; the panel shows no count |
| Stage chips | `StageChips` (`workflow/stage-chips.svelte`), counts from `stageChips` in `#lib/stage-facts.ts` | What happens at a stage, read at a glance: small outline buttons in one order, at most the time limit on one line and the counts on the next in a narrow card. The time limit (`clock`, "Respond within 3 days", W13) opens the stage's sheet; it is the widest chip, so only at the narrowest row of cards (about 11rem a card) it cuts with its words in a tooltip (F5) rather than run out of the card. Automations (`zap`), emails (`mail`) and questions (`list`) are an icon and a count, with the words ("2 automations") in the tooltip and in the accessible name with the stage ("2 automations at Screening"); each opens a short titled list in a popover. Automations list by their own names, which people give them and edit, never a sentence made from their conditions; emails by their template's name; questions by form, each form with its question count as a count badge, the application form on the first stage and each form sent on entering the stage. Each item links to its place: an automation or an email to the automation on the Automations tab (`?tab=automations&show=<id>`), a form to the Forms tab with it chosen (`?tab=forms&form=<id>`). A plain click opens the item in the page, and a modified click follows the link. The scorecard (`clipboard-list`) opens its sheet; it is an icon like the counts, with "Scorecard" in the tooltip and the accessible name, and the word shows beside the icon on touch, where there is no hover. They are buttons with a border, never badges (principle 4, A6). The stage's own reminder is its time limit, so it is not counted again (W7), and an automation that is off is not counted. Where the pipeline reads only, the lists are text, and the time limit and the scorecard (by its name) are its icon and muted words, with no border or fill, so they never look like the count buttons beside them |
| Stage sheet | `StageSheet` (`workflow/stage-sheet.svelte`) | A stage's quick work in place: no subtitle and no "Stage 3 of 4". Name; Respond within as a group of plain choices (None, 2 days, 3 days, 5 days, 1 week, Other, which asks for the days), described by the line under it: "How long a candidate should wait here before your team moves them on. After that they show as Overdue. Nothing happens to the candidate."; once a limit is set, "When overdue" with the short labels of what runs when it passes (the stage's reminder is "Tell the team"), each a link to it, or, where a reminder may run, "When overdue: Nothing yet." with an outline Add a reminder, which adds a reminder for that stage's time limit and opens it on the Automations tab. A stage a check moves candidates into says neither, because the guardrail forbids a reminder there. Scorecard as an inset row with Choose or Change; What the meeting is called, one text field with "interview" as its placeholder and its own field error, on every stage but Applied, Offer and the ones that end the application, and on any stage that already has a name (`offersMeetings`); under it, on the same stages, Interview scheduling: an outline Set a default, then how candidates pick a time (choice cards), who interviews (A team, with its select, or People, as boxes), Length, Days ahead, the hours (weekdays 9 to 5 until Set the hours opens the Weekly hours editor, with a box for interview blocks) and its own field errors, and an outline Remove the default; Automations, the stage's emails and automations as the card says them, each a link, an outline Add an automation and a link, Open in Automations; More options, a disclosure (A9) with the stage's kind, which follows the name until set by hand. Footer: the More menu (Remove stage, not on the first stage) and Done, the one primary (A1) |
| Start from card | `StartingPointCard` (`starting-point-card.svelte`) on `/templates/new` | A choice of how a new hiring template starts, as a radio card: the starting point's name, one example line ("Plumber, electrician, driver") and its stages as plain text. No icon tile, no arrows between stages, no third caption. The page is a form (688px): the template's name, then the cards, with Use this starting point the one filled button and Start from nothing an outline button in the panel header |
| Try it | `TryIt` (`workflow/try-it.svelte`) with `TryItStart`, `TryItStage`, `TryItEnd`, `TryItEmail`, `OptionCard`, `StageRail` and `ProblemNotice`; the rules are in `#lib/try-it.ts` | The Try it tab (the owner's walk-through, 2026-11-21: the old Test tab showed far too much to read): one made-up candidate, Jordan (the API's sample, Jordan Lee, so the emails and the buttons say the same name), clicked through the pipeline one stage at a time, so the owner of a small plumbing business can follow it (W12). Nothing real is created and no one else is emailed. 1. Who is applying?: four `OptionCard`s, each its name and one line, and pressing one starts. A good fit; Can't work here (says no to a must-have question, present only where the form has one, since without it the candidate would be a good fit); Something to check (the checks flag something); Fill in the form yourself, which opens the application form a page at a time with Jordan Lee's details filled in and hands its answers back. One muted line says nothing real is created. 2. One stage: the `StageRail` with the stage they are in lit, the stage's name as the h3, which takes the focus, then at most three things, each drawn as the real thing: an email as an inbox lists it (the organization's logo or initial, its name, the subject with Jordan's name filled in, the start of the text, muted), a button that opens the whole email in a sheet; a task chip (icon and words, never who is asked, W6); and the line that says who decides, always last and always shown: "You decide when Jordan moves on", the review list for someone who said no ("nobody is turned down until you decide"), or, for the flagged candidate, only that the checks flagged something and a person decides (G1: never a level, a score or a reject control). More than three puts the rest behind an outline "2 more". An automation that moves them on by itself is one line ("Jordan moved on to Interview by itself"), up to three moves a click; a move to a way out, or a decline, ends the walk. A form the stage sends is answered with the candidate's answers, so its follow-up runs too. One primary button, Move Jordan to <next stage>, or See the whole process on the last, where the closing line says it is the last stage instead of who decides when Jordan moves on. It is the page's one primary (A1): while the Try it tab is open, the page's own action (Publish, Save pipeline, and the phone's action bar) is outline. Under it, small buttons drawn as underlined text, as Count button's link is, because bordered buttons would crowd the one decision: Let 3 days pass (the stage's time limit, or its first time-passed automation, in days; it shows the Overdue badge and the reminder), Send me these emails (to the signed-in person only, each marked [Test], a few a person each ten minutes, and only where the organization can send), and Start again. 3. That's your hiring process: one line ("Jordan went through 4 stages and got 3 emails"), Looks good, publish (on a hiring template's draft; Looks good, save on a job's pipeline with unsaved changes), Change something, a menu of the stages whose choice opens that stage's sheet, and Start again. A version that reads only has neither button. An edit anywhere starts Try it again. While a problem blocks the workflow the tab shows only a notice with the fix inline (a blank Add a tag step: a Tag name field, Add the tag, Remove that step; any other problem: a Fix button) and nothing else |
| Fix button | `ValidationBadge` (`workflow/validation-badge.svelte`) | Beside a workflow's title: the Needs fixes status badge only when there is a problem, and an outline button, Fix 2 things, with no coloured icon, that brings the focus to the first problem. The Test tab leaves the button out, because its notice holds the fix. A workflow with nothing wrong shows no badge (W4, W7) |
| Stage rail | `StageRail`, from `trackerFor` (`#lib/pipeline-tracker.ts`) | A candidate's place in the job's workflow, a line of square stage markers under their rating and tags, left to right, so nobody scans for where they stand (ADR-0025). Rejected and Withdrawn are ways out, not steps, so they are not drawn. Passed stages have a filled marker with a check, the current stage is a larger marker with its name in semibold and "Here 3 days" under it (and the Overdue badge once its time limit passed), later stages are plain muted names. Only the current stage says anything under its name, and no date is drawn (W7: the meta line says when they applied). Once the candidate left, the status beside their name says how. Screen readers hear "Screening, step 2 of 6". In a narrow container only the current stage shows, with "2 of 6" |
| Fact lines | `HeaderMeta` in `candidate/header-meta.svelte` | A candidate's meta line, under their name: the email and the phone, each a link (the email is also the Email action, so there are no copy buttons), the email-issue badge beside the email, the job as a link, and "Applied 4 days ago via the careers page" (the date once it is older than 7 days, section 13). For a saved candidate "Last contacted 3 days ago" replaces the phone. Icons and values read on their own, the labels for screen readers (W9). The meta line wraps by content (F2) |
| Summary list | `<dl>` | Labels and values for read-only data; one Edit per section when you can edit |
| Key cap | shadcn `Kbd` | In tooltips, menus and the `?` dialog |
| Tooltip | shadcn `Tooltip` | Labels for icon buttons, full values of truncated text, shortcuts. Opens on hover and on keyboard focus (Tab, or the arrows a toolbar moves by), never when code moves the focus to its trigger: a sheet's first field, a view that settles, the focus coming back as a menu or sheet closes (`focus-intent.ts`), so no sheet opens with a stray tooltip |
| Icon button | `IconButton`: ghost `Button` and `Tooltip` | A quiet row action; its label is both the `aria-label` and the tooltip (A4) |
| Empty state | shadcn `Empty` | One line saying why, and the one action. A list that has never held anything has an icon, a short title and that line (No candidates yet, Nothing to do), and Jobs adds one more line and a secondary button, Open the setup checklist, while setup steps remain for the person (Setup checklist); a list a filter emptied has only its line and Clear filters. A loading state draws skeletons of the page's own shape (header, facts, tabs, a panel of rows) in `secondary`, because `muted` on a white panel is almost invisible |
| Shortcuts dialog | shadcn `Dialog` | Opens on `?`; lists every shortcut; holds the single-key toggle |
| Sidebar | shadcn `Sidebar` | Section 8. The sections (Jobs, Candidates, Templates, My tasks with the count of its open tasks in the count badge (T4), Reports, and Verification for everyone who can see verification (its tabs then check their own permissions); Saved candidates is the Saved choice on the Candidates page, not an item, though G then S still goes to it), the Views (built in, the person's own and the shared views they added, each a name with an icon and no count: a built-in view its own icon, a status view its status's mark (I2), a saved view list-filter, or users for a teammate's, and in the icon rail a saved view is the first letter of its name in a small rounded square, with its name in the tooltip; in the rail a hairline divider sets the views apart from the sections above them, because the Views heading hides there, and every rail item has a tooltip with its name; each with a menu on hover and focus: Rename, Share with the team or Make private, Delete view, Hide from sidebar for a built-in one, which only that person stops seeing, and Remove from sidebar, first, for a teammate's view they added, which stays shared; a Views heading has a menu (Views options) for what acts on the views rather than being one: Browse team views, and Show hidden views with their count while any are hidden, so the list holds only views (the owner's walk-through, 2026-11-21); a built-in view's menu says Built-in view, because it can be hidden but not deleted, and offers Hide, or Show on a hidden one; Browse team views opens the Team views sheet, which on a phone opens over the sidebar's sheet; in the icon rail the button is an icon with a tooltip in the fixed group above Settings, outside the Views list that scrolls), then one Settings item at the bottom, which opens the first settings page, for anyone whose role has a permission for at least one setting; nobody else sees it. On a phone it sits in the sheet, and in the icon rail it keeps its icon and a tooltip. The person's block at the foot shows their name and their role, never the organization again (W7: the workspace above names it). For an Owner while the free trial runs, one quiet muted line sits above that block, "Trial ends December 5" (the day and month in the browser's locale order, section 13, so en-GB reads 5 December), an underlined link to Billing (A5); nobody else has it, it is gone once a plan is paid or where billing is off, and it is left out of the icon rail. The person's block is set apart from the navigation above it by a hairline divider in `sidebar-border` (shadcn `Sidebar.Separator`), so where to go and who you are read as two things; the divider holds in the icon rail and the sheet. The block shows the person's photo once they add one on Your account, and their initials until then. The person's menu there is short (the owner's walk-through, 2026-11-21): their name and email as its label, then Your account (a link to `/account`, section 21, where everything that is theirs lives: profile, notifications, calendar and accounts, sign-in and security, preferences), Keyboard shortcuts, Billing (a link to Settings > Billing for the people who see it in Settings: an Owner, where a payment provider is set up) and, after a separator, Sign out. The Calendar sheet (`CalendarAccountSheet`: the Connected status first, then which calendar, and Disconnect says that interviews already booked stay on it; not in the style guide because it reads the signed-in person's connection) is the shell's, for a page that needs a calendar connected; Your account > Calendar and accounts shows the same connection as a section |
| Interview panel | `candidate/interview-panel.svelte` | On a candidate's Overview, above Latest message, and not drawn while no interview was ever offered. A status badge first: Waiting for the candidate (`waiting`), Booked (`booked`), Cancelled or Expired (`notInEffect`), then the days offered (or the times suggested, by how the candidate picks), the interviewers and the meeting link, or one line on how it ended. When the next step is the team's, the badge is Times to book or Wants another time (`review`): the candidate's times as a list, each with who is free in words (Free: Avery. Busy: Robin.) and an outline Book button, and Ask for more times (a short message in a sheet); or the candidate's note in quotes. A booked interview with a move pending adds Waiting for a new time (`waiting`) and a line that the booked time stays until a new one is booked. Its menu has Change how they pick (open) or Move the interview (booked), both opening the same three ways in a sheet, and Cancel, a destructive item behind a dialog. A booked interview's time, in the reader's zone, and its length come first after the badge, since the header no longer carries it. The details are in the style guide; the panel with its menu is not, because it reads the application's interviews |
| Brand bar | Our component (`BrandBar`) | The top bar of a page outside the app shell: the logo mark and name only (section 7). The sign-in pages have none, because their card carries the mark |
| Table, list item | TanStack table; our list item; `rowLink` | 48px rows; list items on phones. Two text styles and one-line cells (L1); every column sorts through the sort header (L2); a click anywhere on a row opens its record through `rowLink`, and the checkbox's whole cell selects (L5). A list item that takes a date puts its name on its own line under the status, so a draft with no date is as tall as a published job. A list whose actions live only on its rows (saved candidates) ends each list item with the row's More menu, which stays clickable above the item's link. A job's candidate table and the board's cards do the same (`ApplicationMenu`, named Actions for the candidate): a Move to heading over the job's stages, the current one marked, which is the non-drag way to move a card (A7). The detail page keeps its Move to stage button, because it is one page's own action |
| Search field | Our component | A list's search, in its table panel's header (section 7); 16px text on touch |
| Activity | `ActivityList`; on a candidate `ActivityPanel` | Icon in a circle, event, who did it, time ago; a note left with it is quoted below. People and API keys show by name ("by Robin Ashby", "by you", "by ATS sync"). Every entry is a link to exactly where its change is (`activityHref`), the same in the bell's Activity: an email to its row on Messages, a scorecard to its sheet, a follow-up form to its answers, an interview to its panel, the checks to Verification, a note to itself, and a move or a tag to the Overview, under the stage rail. A candidate's Activity lists everything newest first, opens with the note box, then filters by kind (All, Notes, Emails, Scorecards, Interviews, Stage and tags, Verification) with a count on each kind but All, whose count is the Activity tab's (W7); the Verification kind holds when the checks ran and what people and API keys did to the candidate's verification (dismissals, explanations, undos, notes, evidence a re-check changed, and reads through an API key, a run by one key as one line) (section 16); Overview shows the last five and links to the rest. A team note shows its latest text through `NoteText`, with "Edited" after it and when in a tooltip; its More menu holds Edit for the author and Delete for the author, an Owner or an Admin, confirmed in a dialog, and anyone else sees no menu. Editing turns the note into its own box in place. A deleted note leaves one line, "Deleted a note" and who, never its text |
| Mention | `MentionField` on shadcn `Textarea`; `NoteForm` for the note box and editing in place; `NoteText` | @ opens the organization's members, without yourself, narrowing as you type: arrow keys move, Enter or Tab picks, Esc closes the list only. The list sits under the field, each member an avatar, name and role; the field points at it with `aria-controls` and `aria-activedescendant`, because a textarea cannot be a combobox. An @ button (Mention someone) starts one below `md`. Ctrl or Cmd Enter saves, hinted in the Save tooltip. A mention is stored as the member's user id and shows their current name ("@former teammate" once they leave). The person mentioned gets a notification card and an email that says who and where and never quotes the note; a mention an edit adds notifies only that person. Save is outline, because the page's primary is Move to stage (A1) |
| Rich text editor | `RichTextEditor` and `RichTextToolbar` on Tiptap 3, with shadcn `Toggle`, `Tooltip` and `Popover` | A job's description (New job, Edit details), an email template's message and the email composer's (docs/architecture/rich-text.md). A box with the input edge, a toolbar across its top and the text under it; the label above it names the text area (`aria-labelledby`, because a contenteditable has no `for`), and clicking the label focuses it. The toolbar holds Bold, Italic, Heading, Subheading, Bulleted list, Numbered list and Link, in that order: one tab stop, the arrow keys, Home and End move between them, each an icon toggle with its name and key caps in a tooltip (A4, K1), the pressed ones `accent` (A6), one line that scrolls rather than wraps (F5), 44px on touch (F4). Link opens a popover with the address field, Apply and, on a link, Remove link; a bare host gets https, an email address mailto, and anything else says what to enter. Headings are two levels (the careers page's h1 is the job title), lists do not nest, and Tab always leaves the editor. In an email, each variable is a chip: a muted word with its label (First name), kept whole, and Insert variable adds one at the caret; End goes past a chip that ends a line. An email template's Subject shows its variables as the same chips in `SubjectEditor`: the size and edge of a text field around one line of plain text that scrolls sideways, where Enter does nothing; it stores the Liquid text as the message does. Read-only, the same text without the toolbar. No hint under it: the toolbar says what it does (W4) |
| Tag list | `TagList` | Each tag a fact pill with its own Remove icon button beside it, never inside it (A5), then a field and Add tag. In a candidate's header (`compact`) the Remove button shows on hover, focus and tap only, and Add tag is a small outline button (A6) that opens its field in place. Removing is not destructive: adding the tag again undoes it (section 10). Without a role that changes hiring data, the pills only. In a candidate's header the field suggests the organization's tags (`suggestions`, `GET /v1/tags`) as a combobox: a list under the field of the tags containing the text, ignoring case, most used first and without the candidate's own, the tag the text names in full first, and Create "the text" last, only for a name no tag has in any case, so "senior" picks "Senior" and never makes a second tag. The list opens as they type, or with the down arrow in an empty field, which shows the most used; an empty field opens nothing, so Esc still closes the header's field. The arrow keys move, Enter picks the highlighted one (the first, until moved), a pointer picks on press, and Esc closes the list only. The same list holds the sites that can embed the careers page (Settings, Careers site, Careers page, the Embed on your website panel), with Add site and Remove site: each saves at once, and "No sites yet. Add one to get the embed code." while there are none |
| Task list | `TaskList` | Icon in a circle, what to do, then its kind and who it is for as fact pills and time ago; a done task says when and by whom. A task is ticked off where its work is done: on the candidate's Needs you, for a role that changes hiring data, an open to-do or rejection review has the task checkbox in its icon's place. A review and a requested scorecard (kind Scorecard) have no checkbox: submitting the scorecard finishes them, and for the person they are for (anyone, for a review for the whole team) their row ends in a link to the Scorecards tab, Open scorecards, or for a requested scorecard Fill in, with its sheet open. A review reads "Give your scorecard" and its kind "Scorecard", so "Review" only ever means the verification state. My tasks is a table, not this list (section 21) |
| Task checkbox | shadcn `Checkbox` in `TaskList`; `createTaskCompletion` in `#lib/tasks.svelte.ts` | Ticking marks the task done on the server at once, so leaving the page keeps it done. The row stays where it is for 10 seconds, ticked and struck through, with an outline Undo; Undo, or unticking, opens the task again. Then a done task leaves an open list. Its label names the task. A tick that fails unticks and says why (`FailureText`). Its hit area is 44px on touch (F4) |
| Undo notice | `UndoNotice`; `UndoOffer` in `#lib/undo.svelte.ts` | The offer to undo a change that already happened on the server, such as a deleted view: one card at the bottom of the window (above the action bar on a phone) with what was done in a sentence and an outline Undo button, for 10 seconds. Undo reverses it on the server; if that fails the card says why. A second offer replaces the first. The wrapper is a polite live region that is always in the page. Undo takes focus when the card appears, without stopping the clock, so the card goes for someone who does nothing. The 10 seconds wait while the pointer is on the card or the person moves focus to it, and start again when they leave (WCAG 2.2.1). Escape closes it. When the focused Undo goes with the card, focus moves to the page's title. Used where the change is not destructive because the same screen undoes it (section 10), and where a row cannot stay in place the way a ticked task does |
| Scorecard | `ScorecardsTab` and `ScorecardRoster` for the roster, `ScorecardDetailSheet` to read one; `ScorecardSheet` to give one; `AskScorecardSheet` to ask a teammate for one; `ScorecardBuilder` for a library scorecard's questions; `ScorecardPicker` in the workflow builder | The Scorecards tab is a roster: a table with one row per reviewer and every header sorting (L2, newest first). The columns are Reviewer (the name only), Stage, Recommendation (words in ink, semibold, never a status colour, so a Strong no never reads like a verification result), Rating (the number only) and Submitted. The row's name is a button that opens the scorecard in a read-only side sheet: the scorecard's name is the title, the reviewer, their role and the time submitted are the subtitle, and below are each question with its answer ("Not answered" when left out) and the note quoted, with no label (W9). The sheet does not repeat the row's recommendation, rating or stage (W7) and has no filled button (A1); it ends with Previous and Next, which step through the roster in the order the table shows, over the scorecards that can be read. The scorecards asked for that are not in yet sit under a muted "Asked for, not in yet" heading in the same table: the teammate, a neutral Waiting badge (the scorecard's name in its tooltip) and "Asked Nov 5 by you". Such a row opens nothing. A reviewer with a scorecard of their own still to give (ADR-0013) sees who gave the others and when: their cells for the recommendation and rating are blank, with "Hidden until you submit yours" for screen readers only (no lock icons, no repeated caption), the row opens nothing, and one line reads "Submit yours to see the others." beside Fill in your scorecard, which is filled only when the reader cannot move candidates (A1). A phone gets list items and a Sort chip. Every scorecard ends with an overall recommendation, in words, and a note. `ScorecardFields` draws what a reviewer fills in, for the sheet and for the library scorecard's Preview, which shows it at the sheet's width for a sample candidate; `ScorecardQuestionList` reads a scorecard's questions as content. Questions are built in the form builder with the answer menu, on a library scorecard (built-in or the team's own); a stage picks one, and a job keeps its own copy of the questions; rating questions answer on one row of joined buttons and make the candidate's rating, the one average anything takes of scorecards (Rating, below). The sheets never show verification output. The panel header has Fill in, for a role that fills in scorecards who has not given one (A3) or is still held back by the gate, since a scorecard given at another stage does not answer the one they owe, and Ask for one, for a role that creates tasks, both outline. Fill in opens the sheet titled "Give your scorecard". Its subtitle is the scorecard's name when only one fits, and a Scorecard select (the reached stages' own scorecards the reader has not given, the current stage first, which it starts on, then the built-ins and the team's own, given at the stage the candidate is in now; an archived one only when a link names it) appears only when more than one does (A3). A task's link (My tasks, Needs you) fits only the scorecard the task asks for, so the sheet opens on it with its name as the subtitle; Fill in on the panel offers them all. A scorecard someone asked for says "Asked by Avery Quinn on Nov 5, 2026." in muted text with no icon (a workflow's review has no one to name, so it reads "Asked on Nov 5, 2026."; a review is named only on the scorecard for the stage it was made in). Low and High show once for a run of number scales. After Submit the sheet stays open on "Scorecard submitted." and, when the reader has another open task, an outline "Open next task" link to it (a page action rendered as a link, A5); Submit scorecard is the only filled button. Ask for one opens a sheet with Teammate (the people who see the job and whose role fills in scorecards, never yourself; with nobody, one line says why), Scorecard (the current stage's own first, then the library without archived ones) and Ask, with no note, because the request is the message; the teammate gets a Scorecard task, done when they submit it. Remind, for a row that is not in yet, is proposed and not built: it would raise a notification for the teammate in the bell (`RaiseNotification`). Questions ask about the work and the skills the job needs, never personality, emotion or honesty |
| Notification tray | `NotificationTray`; `Notifications` in `#lib/notifications.svelte.ts` | Overlay cards in the bottom corner (above the action bar on phones), newest first, at most three in all: an icon for what raised it (`bell` for the team, `at-sign` for a mention, `reply` for a candidate's reply, `file-lock` for a new data request, which only people who may erase candidates and see every job get), the record as a link, one line and Dismiss. A card never times out, because it holds a link (WCAG 2.2.1), and a polite live region announces each one. A teammate's change to something the person works on is a toast in the same tray: the icon Activity gives that change, one sentence naming who did what with the record as its link ("Jordan Ellis moved **Taylor Brook** to Interview"), the job as a muted line, and Dismiss. Changes one person makes together on one job read as one toast about the job ("moved 5 candidates on **Platform Engineer**"). A candidate applying is the one toast with nobody acting, for the job's hiring team only: "**Taylor Brook** applied to Senior .NET Engineer", and more within a minute grow one toast, "**3 new applications** to Senior .NET Engineer", linking to the job's Not screened candidates. These and the grouped toasts name the job in the sentence, so they have no muted line. A toast closes after 8 seconds and counts down only while nothing holds it: the pointer or the focus on the tray, a hidden tab, or a sheet or dialog over it. A new card or toast pushes out the oldest toast before any card, and a toast does not show while three cards are open. What the candidate does is the other toast with no teammate acting, for the job's hiring team too: an interview booked, moved or cancelled ("**Taylor Brook** booked Tue, Nov 25, 9:00 AM for Senior .NET Engineer", linking to the interview, with the `calendar-check` icon) and a follow-up form answered ("**Taylor Brook** answered Screening questionnaire", `list-checks`); each is its own toast. Each person chooses what shows in the tray on Your account > Notifications (section 21): the In the app column turns each row's toasts on or off (the cards for a mention and a candidate's reply included; the bell keeps both either way), Pause toasts stops them all, and Close toasts automatically off keeps each toast until dismissed: that is the time limit WCAG 2.2.1 lets people turn off, and every toast's change is also in Activity or History. Automation cards and data request cards are not toasts and always show |
| Sheet | shadcn `Sheet` (`MoveNoteSheet`, `ScorecardSheet`, `AskScorecardSheet`, `ScheduleInterviewSheet`, `ChangeOptionSheet`, the workflow builder's automations and scorecards) | Quick work in place (section 9): the title, one line of description, the form, then Cancel and the one primary action (A1). The record stays behind it. History (T6), the items behind a count (T4), Invite, the email composer and Team views are sheets too, and so is every short create or edit form (A16): New role, Add candidate, Add endpoint over the Webhooks list and Edit over an endpoint's page (`WebhookEndpointSheet`), Create key over API keys. A sheet that makes something the API shows once ends on that step in the same sheet, titled for it, with the value in `SecretField` and Done (Create key's "Your new API key"); closing it any way forgets the value. A sheet that asks a few decisions in order, such as Switch template on a job (`SwitchTemplateSheet`: the template, where each stage's candidates go, then what changes), shows the step markers above one decision at a time, the step's question as its heading, Back as an outline button, and the one primary action on the last step. An address that used to open such a form on a page of its own opens the page behind the sheet with the sheet open (`/settings/webhooks/new` is `/settings/webhooks?add=endpoint`), and the parameter leaves the address once read. A sheet that only reads a record, such as a scorecard in the roster, has no form and no primary action: Close, and Previous and Next when it steps through a list. A sheet that lists things to act on, such as Team views, has outline actions on each row and none in a footer |
| History sheet | `HistorySheet` on shadcn `Sheet`; `LastUpdated` for the fact; `HistoryFact` puts both in a header that shows nothing else of the history (the settings pages) | T6: Last updated is the last fact of the header, time ago and who ("2 hours ago by Avery Quinn", "By ScreenHire" for an untouched built-in), and the fact itself opens the sheet: a dotted underline, a pointer, and a tooltip with the exact time and "See every change"; its accessible name ends "History". A separate History button beside it was the one button in a row of facts, so the last change's time is the link to its history (the owner's walk-through, 2026-11-21). While nothing has changed the object since it was created, the fact's label is Created. On a page whose one editable thing is a single panel (Record retention, on Data requests), the fact is in that panel's header actions, not the page's title row. On a settings page's title row, where no label shows, the fact starts with its verb ("Created 2 hours ago by Avery Quinn", "Updated yesterday by you"). The sheet's title is History and its one line the record's name; changes come newest first under a heading per day (Today, Yesterday, Oct 22). Each change is the person's avatar, their name starting the first line, one line per thing that changed ("changed **.NET and C#** from 1 to 5 to 1 to 10"), and time ago. Show the change is an outline button (A6) that reveals before, struck through, and after for each value, and "Unchanged" for the context. Show older changes loads the next page. A ScreenHire template has no History button, because nobody can change it. The list is `HistoryEntries`, which the Versions sheet holds too |
| Versions sheet | `VersionsSheet` on shadcn `Sheet` and `Tabs` (`RecordTabs`), with `HistoryEntries` | A hiring template's versions and its history in one sheet, titled Versions and changes, with two tabs: Versions, every version newest first, each a link to it (A5) with its status badge (Published or Draft, quiet), when it was published, "(live)" on the one new jobs get and a check on the one shown; and All changes, the History sheet's list and its note. The header's Versions button opens the first tab and Last updated's History the second; a phone's More menu has Versions. A ScreenHire template has only Versions. It replaced a Versions dropdown beside a separate History sheet (the owner's walk-through, 2026-11-21) |
| Action bar | Our component (`ActionBar`) | A phone's primary action in a bar fixed to the bottom, over the safe area (section 8) |
| Relative time, truncated text | Our components on `Hint` | Time ago, or text cut with an ellipsis (on one line, or on two as a workflow graph node's title); the exact time or full value in a tooltip that opens on hover, focus or tap (T5, F4, F5). A truncated link is one tab stop, with an optional icon before its text |
| Table date | `DateText` on `Hint` | A date in a table or list item, the exact time in its tooltip (T5, section 13) |
| Form field | shadcn `Field` (`Field.Error`); `FieldErrors` in the workflow builder | Label above the control, a description of one line at most, and each error under it with the problem icon and the verify colour (T1, section 14) |
| Move to stage | `MoveToStage` on shadcn `DropdownMenu` | The non-drag way to move a candidate (section 17): a button that opens the stages, the current one marked. On a candidate's page, Save candidate follows the stages, beside Rejected, for a candidate whose permission for future roles holds; it opens a sheet (`SaveCandidateSheet`) with a note and Save candidate as its primary. On a detail page a last item, With a note, opens the move in a sheet with a note; choosing a rejected stage there adds an optional Reason select (`MoveNoteSheet`, the list in `#lib/rejection.ts`), which the move keeps. A saved candidate has no current stage: moving them brings them back to the job |
| Move confirmation | `MoveConfirmDialog` (`move-confirm-dialog.svelte`) on shadcn `Dialog` and `Checkbox`; the question is `confirmation` in `createMoves` (`#lib/moves.svelte.ts`), the sentences are `enteringLines` in `#lib/automation-sentence.ts` | Asked before a move into a stage whose automations run when someone enters it (an email, a task, an interview, a questionnaire), from every move control, because they all go through `createMoves`: the menu, Shift + Left or Right, a drop on the board and the selection bar. The title is "Move Joel Mensah to Technical exercise?" ("Move 4 candidates to Offer?" for a selection), then one line ("Moving them there will:", "Each of them gets:"), the job template's automations for that stage as plain sentences in the Automations map's words, never a second wording ("Send the \"Moved to the next step\" email.", "If Right to work is No, ask the team to review."), a Don't ask again checkbox, and Cancel (outline) and Move (the one primary, A1). A stage with nothing to do moves at once, with no dialog; automations that are off are not listed, and nothing a check drives or asks a check to do is listed, so the dialog never mentions verification (ADR-0004). Cancel, Escape and the close button do nothing, and a dropped card goes back to its column. After Move or Cancel the focus goes back to what asked: the card (on the board, the card in its new place after Move), or the button whose menu started the move while the move leaves that button on the page. Don't ask again is the person's, one setting for every stage, kept on the server for the organization so it follows them to another browser; Your account > Preferences turns it back on ("Confirm moves that send emails or create tasks", beside Single-key shortcuts). A job that cannot be read moves without the question, never blocked by it |
| Confirm dialog | shadcn `Dialog` (`ConfirmDialog`, `CloseJobDialog`, `LeaveDialog`) | Section 9: the question as its title, what happens in one line, the outline way back and the action, in `destructive` when it destroys (A2). A choice that goes with the action (also revoke a removed member's keys, also save a closing job's candidates who allowed it) sits under the line as a checkbox. A failure stays in the dialog as `FailureText`. An action that cannot be undone and takes a person's data with it (`EraseCandidateDialog`) sets `confirmText`: a field "Type <name> to confirm" keeps the action off until the typed text matches, ignoring case and the spaces at the ends. A name is not a code, so W8 holds |
| Publish check dialog | shadcn `Dialog` (`PublishCheckDialog`) | Opens when Publish is pressed on a draft that the API's check found something in (`GET /v1/jobs/{id}/publish-check`: the description still has a starting template's fill-in text, an on-site or hybrid job with no city or no salary a chosen board needs, a description shorter than a chosen board accepts). It warns and never blocks. The title counts the problems ("2 things to check before publishing"), then one sentence per problem with an outline Edit details link beside it, which opens Edit details with that field focused (`?focus=description`, `city` or `salary`). With several problems the footer has the outline Edit details (the first problem's field), and with one the row's link is the only one (W7). Publish anyway is the one primary (A1). With nothing to check Publish publishes at once, and a check that cannot be read never stands in the way. The Jobs list's Publish to every board asks the same way |
| Secret dialog | shadcn `Dialog` (`SecretDialog`) on `SecretField` | A secret the API shows once after an action on a page: a new webhook endpoint's signing secret, after Add endpoint's sheet closes and before the endpoint's page opens, and a rotated one. "Copy it now. You will not see it again.", the value in a read-only monospace field with Copy, and Done. A secret made in a sheet shows in that sheet instead (Sheet). The value lives only while the dialog is open; never in the query cache, the address or storage |
| Code block | `CodeBlock` | Code someone pastes elsewhere: the careers widget's embed code (Settings, Careers site, Careers page, and a job's Job postings tab for that job), an MCP client's config (Integrations, MCP server), the example request (Integrations, API keys) and each DNS record's Host and Value, under a caption saying which is which (Settings, Organization, Email domain). The code in monospace on the `secondary` fill, wrapping at spaces first rather than scrolling, then a large outline Copy that turns to Copied, with a status line for screen readers. Without a clipboard, Copy selects the code |
| Email preview | `EmailPreview` | An email as the candidate receives it: the subject, then Email or Plain text (Toggle), on the subject's line. Where the row that opened it already shows the subject (Messages), the toggle sits on the email's own line, beside who it went to ("To ...") or came from, so it takes no row of its own (the owner's walk-through, 2026-11-21). Where that line has no room for both, as with a long address on a phone, the toggle drops under it, right-aligned, rather than breaking the address mid-word. The HTML renders in a sandboxed frame with no scripts, in an inset, never in the page, because a tenant wrote the template. The frame takes the email's own height, so a short email leaves no gap under it. The composer's preview, a sent email opened in Messages (`SentEmail`), as it went, and a reply opened there (`ReceivedEmail`), under who it is from and above its files |
| Email composer | `EmailComposer`; `EmailComposerSheet` on shadcn `Sheet` | Email one candidate or a selection: in a sheet from a list's selection bar and the candidate's header (section 9, in place), and in its own panel under the emails on the Messages tab, titled Email and the candidate's name, so what was sent is the first thing the tab shows. To, then Start from: Blank email, the default, or one of the organization's templates, which fills Subject and Message. The rejection and follow-up questions emails are never offered, because only their actions send them (`docs/architecture/email.md`), and a blank email never becomes a template. The Edit / Preview switch (A8, no window) and Insert variable, a menu of the variables that puts one at the caret of the field last used: a chip in the message, `{{ name }}` in the subject. Changing a template's wording changes this email only, and one muted line under the message says so and that the template stays as it is. Preview is the first candidate's email, rendered by the API with their details ("Preview for" and their name in a selection); each candidate's own copy is rendered with theirs. Send is the sheet's one primary ("Send 3 emails" for a selection); inline on Messages it is outline, because the page's primary is Move to stage (A1), and empty fields say what they need when it is pressed. Each email lands in Activity and Messages as it went. No bulk email while a verification filter or view picked the rows |
| Add candidate sheet | `AddCandidateSheet` on shadcn `Sheet` | A recruiter adds a candidate by hand: Job (only the drafts and open jobs, and left out on a job's page, where the job is preset), First name, Last name, Email, Phone and Resume, then Cancel and Add candidate. Last name, Phone and Resume say "(optional)", the exception here (W6). With no draft or open job the select reads "No job to choose" and a line under it says why, and links to New job for someone with Edit jobs or says who can create one for anyone else (A3, E3). A job list that cannot load is `QueryError` under the select. No Source field: the API sets Recruiter (W6). No notice, telemetry or declarations: the Verification tab says which checks had nothing to look at (section 16). The resume goes straight to storage, as on the careers form. The page's primary on Candidates (in the action bar on a phone); outline on a job's page, where Publish is the primary, in More on a phone, and absent on a closed job; never without Add candidates (A3). A refusal (a closed job, someone who already applied, someone on a job the person does not see) is `FailureText` in the sheet; on success the page's status line names the candidate as a link to their page |
| Customize sheet | `CustomizePostingSheet`, `CustomizePostingForm` on shadcn `Sheet` | What one job board receives in place of the job's title and description. Each field starts as what the board gets now, which is the job's text until it is customized; "Use the job's title" or "Use the job's description" shows under the field only while it differs, and an empty or equal field follows the job. The Edit / Preview switch (A8, no window) shows the API's preview, with the board's failing checks as `FailureText`. When the job changed since the board was customized, a block above the fields shows each field's old and new job text. Cancel and one primary, Save and post; a failing check stays in the sheet and nothing is sent. Where it opens from, and where it is never offered, is in section 21 |
| Post it yourself sheet | `PostItYourselfSheet`, `PostingTextCopy` and `ManualPostingForm` on shadcn `Sheet` | Posting a job by hand on a board ScreenHire does not post to. The job's text first, one row per field with its value and an outline Copy (the description has Copy formatted and Copy as plain text; each says Copied for two seconds), then the board, with common names offered while typing (a `datalist`), its apply link in a read-only field with Copy link, the listing's address and the two dates. Cancel and one primary, Save posting. Edit opens the form alone with Save changes; Copy the updated text opens the job's text with a muted Changed badge on each field the job changed since, the apply link, and Mark as updated as the primary. The listing's address is a link and nothing more. Where it opens from is in section 21 |
| Live indicator | `LiveIndicator` | The live updates status as a pill in the top bar, beside Search, on every page: icon, colour and label (T1), from section 3.2 (Connected, Connecting, Offline; not "Live", which is a job posting's status). While live, its icon has a slow, soft pulse behind it, which stops for reduced motion. On a phone only the icon shows; screen readers hear the label |
| Failure text | `FailureText` with `ProblemIcon` | A failed action or form, in one line where it happened: the problem icon, the verify colour and the reason (T1, section 14). A section that cannot load uses `QueryError` |
| Verification chip and panel | `VerificationChip`, `SignalEvidence`; `VerificationPanel` is the candidate's Verification tab (a map of the six check groups and a panel per group that found something), with `VerificationExplainDialog` and `VerificationDismissDialog` | Section 16. The map's tiles name what was looked at, never what it means about the person. A finding opens to its evidence, can be marked explained with an optional note, and shows the explanation under it with Undo for its author. Dismissals, explanations, undos, notes and reads through API keys are entries in the candidate's Activity, not a panel of their own |
| Tabs | `RecordTabs` on shadcn `Tabs` (`line`) | A record's peer views in a fixed order, each with its count in the count badge. A candidate's section with nothing in it (Messages, Scorecards) shows no count, not a zero (W6). The pipeline page's tabs (Pipeline, Forms, Automations, Test) are this strip too, kept in the address as `?tab=`, so a job and its pipeline keep one frame (S8). The Verification area's tabs are this strip too, kept in the path (`/verification/checks`) so a link can name a tab, each left out of the strip for a role that cannot use it (A3). The bell's panel has two of these tabs in its header, Needs attention and Activity, with no `RecordTabs` around them: they are the panel's own views, not a record's. A count is a plain number, never a fraction (Job postings 7), always in the same grey count badge; a state that needs a look is the record's status beside its title, never a coloured tab, a dot or a lone icon (E2). The list scrolls sideways, never wraps (F5) |
| Welcome line | `JoinedNotice` | What accepting an invitation did, said once on the first page after it: "You joined Harbour Lane Studio as Recruiter.", plus "You see the jobs you are on." for a member on Only jobs they're on. A `role="status"` line in the `secondary` colours with a Dismiss icon button (A4), no status icon (I2). It repeats the workspace and role the sidebar shows, on purpose: it confirms, once, what accepting did. It is not a `Notice` (a problem to fix) and not a toast. The Jobs list shows it from the `?joined=1` the accept redirect adds, reads it once and takes it out of the address, so nothing is stored |
| Notice | `Notice` | A problem to fix: the verify status's soft colour, full width, the problem icon (section 14), what is wrong in words and a link to the fix, or the button that fixes it from there (E2) |
| Info button | `InfoButton` | An explanation one press away (W12): a quiet `info` icon beside what it explains, whose words open in a popover on a click, a tap or keyboard focus and close on Escape, a click elsewhere or moving focus on. It holds words only, never a control, so focus stays on the button. It takes the long sentence a setting used to carry under its label; a short description stays one line (W4) |
| Alert grid | `AlertGrid` | Notification choices as a table: a row per alert with its name and an Info button for what it says and to whom, then In the app as a fixed tick with screen-reader text (never a disabled checkbox, A3), Email only where this deployment can send it, and Slack only once Slack is connected (A15). Used once, on Settings > Notification defaults, never on a feature's own page |
| Expandable row | `ExpandableRow` | A list's row that opens in place to show more, such as an email on Messages: the whole row is the button, with `aria-expanded`, a hover background and `chevron-down` beside its words (A9), which turns up when it is open; what it shows follows the row, sliding open and shut in 180ms, ease-in-out, and at once with reduced motion, the chevron too (A17). Its words wrap rather than truncate, since a tooltip has no place inside a button; the one exception is a first line of what it opens to (an email's snippet), cut to the row's width because a click shows all of it. The rows share one surface and its dividers (S4) |
| Summary card | shadcn `Card` as it is (`SummaryCard`), on a summary band (`SummaryBand`, section 6) | An icon in a neutral 32px chip and `Card.Title` (16/24 semibold); the count as a badge in `Card.Action`, saying what it counts (2 signals); `Card.Description` (14/20 muted, two lines at most, the rest in a tooltip). A link when it opens what it counts |
| Bar chart | `BarChart` | A horizontal bar chart in HTML (section 3.5): each row's name, then its bars from one baseline, at most 24px thick, square at the baseline and rounded 4px at the end, with the value written at each tip in text colours, then an optional muted share (62%) after the first value and an optional muted note (84% moved on); the one row with the biggest drop has its note in semibold with a small down-right arrow, and its summary says "the biggest drop". A zero draws no bar but keeps the bar's line: its value starts just past the baseline, where a bar would begin, so the row is as tall as the others and never drops to a line of its own. Two series sit in pairs with a 2px gap and a legend above. The bars and note are one image that reads out the row's summary on hover, focus and tap (F4); the row's name beside it is a link when the row opens something (a funnel stage opens its candidates), so the link stays outside the image. The name sits beside the bars in a chart 28rem wide or more, and above them in a narrower one |
| Chart panel | `ChartPanel` on `Panel`, `Toggle` and the sorted table (L2) | A report's chart: the title, Chart and Table toggles on the header's right, then the chart or the same numbers as a table, whose first column is the row's name (a link when the row opens something). In a panel narrower than 24rem the toggles show their icons only, with the word as the tooltip and for screen readers, so the title keeps its room. No sentence sits under the title (W9): what the numbers count is for screen readers only. On a phone the table view is list items, as every table is (L2): the row's name, then each number under its own label, with the Sort chip above, so no number is hidden off the edge. A panel with no chart is its table alone, without the toggles. The table view is flush: it runs edge to edge with its first and last columns on the panel's padding (L4), rows are 40px, the name column takes two fifths of the width and the numeric columns share the rest, right-aligned under right-aligned headers so the digits line up and spread across a wide panel rather than packing at its edge, and nothing sits under the last row. A fact that has no column of its own is the tooltip of the cell it explains (Opened on Days open). A chart fills its panel, so two panels in a row share a bottom edge without an empty band. The table opens on the chart's own order unless a default sort says otherwise. While new numbers load it keeps the old ones, dimmed, rather than a skeleton. An empty state replaces both views |
| Date range filter | `DateRangeFilter` on shadcn `Popover`; `#lib/report-range.ts` | A filter menu's outline button for dates (Dates: Last 30 days), `primary` once it is not the default. It opens the presets as ticked rows (Last 30, 90 and 365 days), which apply at once, then a custom range of two date fields under a rule, which applies with Apply and says what is wrong in words. Dates are the person's own days (W8); the API gets the local midnights |
| Metric card | shadcn `Card` (`MetricCard`) | The value in the UI typeface (`metric`), a signed change against a named period (+7 vs last week), a 12-point line and a readout on hover, tap and focus; no description. The change is green when good and amber when it needs a look, decided per metric (fewer days to hire is good). It opens its report |
| Report stat | shadcn `Card` (`ReportStat`) | A headline number on Reports: a small icon and a muted one-line title, then the value in the UI typeface (`metric`) as the one thing to read, with the change from the previous period as a small grey pill on the value's own line (an arrow and the amount, never a status colour; its sentence is in the tooltip and is what screen readers hear in full, "10 more than the previous period", so "up 10" is never heard alone), and an optional muted share under that (62% of applications), or a footnote that needs markup, such as how recent the number is ("Last checked 2 hours ago", T7). In a row of cards every card has its footnote or none does (Reports' five each say when they last moved), so the row reads as one. A tile is two rows at 14px padding, so a report's numbers cost one short band of the screen; the pill wraps under the value only when the tile is too narrow for both. No icon chip, no sparkline and no link |
| Notification bell | `NotificationBell` on shadcn `Popover`, in `TopBar` after Search; the lines in `#lib/notification-lines.ts` | An outline icon button named Notifications (A4) with the grey count badge (T4, 20px minimum and edged, because it sits on the button's corner) at its top right: the setup steps still to do plus the alerts not yet read, "99+" past that, nothing when both are zero. Its tooltip reads "Setup and notifications" while setup steps remain, so a new Owner knows why to open it, and "Notifications" after. It also opens by itself, once per person, right after onboarding finishes (remembered in `preferences`, so never twice), with focus on the setup section, and opens there when a page asks with `setupPanel` (the Jobs empty state's button). It opens a 380px panel named Notifications with two tabs, Needs attention and Activity (shadcn `Tabs`, `line`, in the panel's header), and A opens it straight at Activity (section 15; its key cap is in the bell's tooltip). Needs attention comes first and is what the badge counts. Its setup section comes first while steps remain (Setup checklist), with Not needed on each step and Hide setup for everyone, both for the whole organization. Under it are the alerts: one line each in plain words, the line itself an underlined link to the page that fixes it (A5), its age in text, a bold line and a screen-reader "Unread" while unread, and a Dismiss icon button (A4). No icons lead the lines and no team news appears in it. "Mark all as read" is an outline button in the panel header (A6), shown only on Needs attention with something unread. Following a link marks that alert read; opening the panel marks nothing. With nothing to show: "Nothing needs attention." A person's alerts are their own: dismissing one hides it for that person only. Alerts come from the server and arrive live (realtime.md); a kind with no line registered shows a generic line and its link. **Activity** is the workspace's changes the person may see, newest first (notifications.md, "Activity"): a filter row (My jobs or Everything I can see as a toggle group, Kinds as a `MultiFilterMenu`), then one row each: the event's icon in a circle, one sentence naming who did what with the record as an underlined link to exactly where the change is ("Riley Harper moved **Taylor Brook** to Interview"; a key or an automation after it, muted: "by ATS sync, via an API key", "by an automation"), and a muted second line with the job and the time ago, which ticks every minute. Checks say only "Checks finished for **Taylor Brook**", never a level (section 16). The changes since the person last opened the tab are the tab's count in the grey count badge ("99+" past 99), never a dot and never on the bell (E2); inside, "Since you were last here: 14 changes" heads the list and a muted "Seen before" line marks where those end. Show more loads the next 50. Empty: "Nothing has happened on your jobs yet.", or "Nothing of these kinds yet." under a kind filter |
| Email domain all-good state | an outline `Button` and a line in the DNS records panel of `settings/sending-domain` | When the domain is Verified and every record is found, the DNS records panel is one line, "Every record is in place. ScreenHire checks again every day and tells the Owners if it stops." (the second sentence is left out in the sandbox, which is not re-checked on its own), with the records behind a Show records button (a chevron and `aria-expanded`, A9). The title's Verified badge is not repeated (W7). The instructions for adding records and the records themselves show without the button until the domain is verified, so a calm domain is a short page (T3, W10). Check records stays, and Last checked stays in the Domain panel |
| Overview stat | `OverviewStat` (a `Card` in an `a`) | One number on Jobs above the table, the linked sibling of Report stat, packed the same way: a small icon and muted title, the value in the UI typeface (`metric`) as the one thing to read, and nothing else (no change pill, no sparkline, no chart for one value). The whole tile is the link and its title is the label of the list it opens (W6). Four sit in a group: New this week, Not screened, Interviews this week, Open jobs ("3 of 5 on your plan" where the plan has a job limit and the person may manage billing, which is the only person the limit is shown to, and that tile opens Billing; with no limit, or for anyone else, the count alone, opening the open jobs). "On your plan" is a muted `unit` after the value and wraps to the line under it when the tile is too narrow, so the value never shrinks or cuts. A tile whose number cannot load is left out, never zero. In a tile narrower than 12rem the title may wrap to two lines rather than cut (F5), and below 9rem the icon goes so the title keeps its width; the values still share one baseline |
| Week chart | `WeekChart` | Applications and hires by week as paired columns from one baseline, Applications in `chart-2` and Hires in `chart-1`, with a legend above and a readout line of its own above the columns, so reading a week never moves them. One tab stop: pointing at or tapping a week, or the arrow, Home and End keys, reads that week out (politely, to screen readers). Past about a year the columns keep a minimum width and the chart scrolls sideways inside its card. Its panel has the table view |
| File row and file viewer | `FileRow`; `FileViewer` on shadcn `Dialog`; `FileFrame` | A page shows a file row: type icon, name, size, View and Download, or the scan status when a file is withheld. View opens the file in a full-window dialog with Close and Download, never a new tab. Untrusted files render isolated in a sandboxed frame; it draws PDFs, and other types keep Download only |
| Form cards | `FormCards` on `Toggle`-style buttons in a `role="group"` named Forms, in a Forms panel; `#lib/forms-timeline.ts` | The strip across the top of a hiring template's or job's Forms tab: one card per form, in the order the candidate meets them, read at a glance in two rows (the owner's walk-through, 2026-11-22). First a `file-text` icon, the name in semibold, which wraps rather than cuts, and its question count as a count badge ("6", "6 questions" for screen readers). Then when it is sent, as a chip with its own icon: `inbox` When they apply, `log-in` Entering Screening, `clock` 3 days after Offer, `hand` By hand only, and `zap` with the moment's words when an automation that does more sends it; an equality monitoring form adds "Counted only". The badge and the chip are words inside the card's button, never controls of their own. A form no automation sends comes last. The chosen card is `aria-pressed` and takes the `accent` fill, the `primary` border and a `primary` edge down its left side; choosing one shows it below. Cards are 13rem to 16rem wide and share their height. The strip can grow with the number of forms, so it scrolls sideways inside its panel and never wraps (S9, F5). The last item is New form, a dashed card of the same size that opens the Add a form sheet: the library's published forms not in use, each with its question count, a form whose question another form here asks disabled with which one, and Build a new form. A chosen library form opens with a panel of its name holding When it's sent (When they apply, When they enter a stage with its Stage, Some days after they enter a stage with its Days, or Only when sent by hand), which writes the automation behind it and says "We add the email automation for you.", or, when an automation that does more sends it, a line naming that automation with a link; Remove form is in the panel header's menu and says which automations go with it. Its questions read below, with Edit in the library and Use version N when the library has a newer one; the application form edits in the form builder |
| Locked question row | `AiUseRow` | A row in the Apply form that ScreenHire adds and the template cannot change: "AI use. Asked by ScreenHire. Policy: Allowed with disclosure." The policy is the organization's, or the job's own where it sets one, and Change policy is a link (A5) to where it is set: the Checks tab of Verification for the organization, with its Answers row open, the job's settings for a job. The link is hidden from a person who cannot change it (A3). It has a lock icon and no switch. No automation reads the answer |
| Edit / Preview switch | `EditPreviewLayout` on `EditPreviewSwitch` (a `Toggle` group and an icon button), one at a time at every width; `PreviewFrame`; `#lib/preview-window.svelte.ts` | A8: a pencil and an eye; the window icon (`app-window`, "Open in a new window") pops the preview out, and the window follows each edit over a same-origin `BroadcastChannel`. A candidate page previews in a phone, tablet or desktop frame at its real size. A read-only view of what an editor makes (a closed job's questionnaire) keeps the same switch, so content and preview never share the screen there either, and its first choice is Questions with a list icon (Wording for an email template), because nothing there can be edited (E3). The edit pane keeps the 688px form column (S6), unless the editor sets `wide` because its panels sit in rows (Branding): then the pane and the switch's row take the work area's width, and `actions` sit at the row's right end, which stays in view while the page scrolls, with what takes focus in the panes kept clear of it (WCAG 2.4.11). The preview pane is always the work area's full width |
| Count button | `CountButton`: outline `Button` and `Sheet` | A count in a header ("2 hiring templates") that opens a sheet listing each item as a link, with search past 10 (T4). With nothing to list it is plain text ("None"): a header fact is never blank, though the list's column is (L1). `link` draws it as underlined text with its own words, for a fact that says more than the count: a hiring template's Used by, "3 jobs, 1 on an older version". It is still a button, because it opens a sheet (A5) |
| Sort header | `SortHeader` in the TanStack table header; `SortMenu` on phones; `#lib/table-sort.svelte.ts` | L2: the header text and `chevrons-up-down`, which turns into `arrow-up` or `arrow-down` on the sorted column; `aria-sort` on that header; a second click reverses. In a right-aligned column the icon comes first, so the text ends at the values' right edge (`SortHeader` reads `text-right` on the header). Each table opens on its default (a job's candidates on Stage, those needing attention first: overdue, the longest first, then pipeline order; candidates across jobs on Applied, newest first), and each person's sort is remembered per table in the browser (`preferences`), so it does not follow them to another browser. The Sort chip offers the same choices on a phone, above the list items |
| Built in badge | `BuiltInBadge` on `Badge` muted variant | After the name of a template or role ScreenHire supplies; nothing marks your own (W6) |
| Reorder grip | `ReorderList` and `ReorderGrip` on svelte-dnd-action | A7: a drag handle whose click (or Enter) opens Move up, Move down, Move to top and Move to bottom (Move left, Move right, Move to start and Move to end for a list laid out in a row, `orientation="horizontal"`), leaving out moves that do not apply; Space picks the item up and the arrow keys move it. Each list has its own drag zone, so lists on one page never trade items |
| Settings menu | `SettingsMenu`; its pages are `settingsSections` in `#lib/settings.ts`, grouped by `settingsGroups`, and its search is `searchSettings` | Settings is one sidebar item that opens the first settings page the person's role may use (General for an Owner): `/settings` has no page of its own, and the layout answers someone whose role may use none. The owner found a page of three panels of rows hard to scan, with rows that did not read as links (2026-11-09), so the whole of Settings lives in this menu. Its pages come in three groups: Organization (General, Team, Roles, Tags, Notification defaults, Email domain, Data requests, and Billing for an Owner or anyone who can see AI usage), Careers site (Careers page, Branding) and Integrations (AI model, Slack, MCP server, Webhooks, API keys). A page's breadcrumb is Settings, its group and the page (Settings / Organization / Team): Settings links to `/settings`, which opens the first page, the group is plain muted text because a group has no page of its own, and a record under a page adds its name (Settings / Integrations / Webhooks / Order sync). Every settings page has the same whole menu: every page the person's role may use, indented under its group's name, with a search at the top that narrows it by the words kept beside each page, so the whole of Settings is in view wherever someone is in it. Below `lg` it is one row that scrolls sideways, without the group names or the search. Email domain carries a green check once it works and a red cross while its records need fixing (`SettingMark`, section 3.2), and nothing while it is not set up, because it is optional. Slack is there only where a Slack app is set up (`GET /v1/slack` says `available`): elsewhere the page and its entry are hidden (A3), and an address to the page goes to the first page the person may use. Email domain is hidden the same way where the installation has no email provider (`available` is false); it shows until the shell hears that, so the entry does not wait for a query. Billing's plan and payments are hidden the same way where no payment provider is set up (`GET /v1/organization/billing` says `available`), and only an Owner (Manage billing) sees them; its AI use section is for anyone who can see AI usage, so the page opens for them whatever the provider (`alsoFor`), showing only that section; the account menu links to it for an Owner where billing is set up. Billing is the one place to see everything the organization pays, already added up: the plan, This month so far (each charge on its line, the total and "Next invoice about $X on <date>"), the payment method with Change card and Cancel plan (both open the payment provider's page, which is never named; Cancel plan is an outline button with no dialog of its own, because that page asks before it ends the plan and is A2's confirmation; the local sandbox's stand-in acts at once), the invoices, the plans and the AI use section, in the Overview template's columns (section 7; the AI use section holds the month's AI charge and whose key runs the check as a fact; what the checks used, by day split between ScreenHire's key and the organization's own and by model and key; and the spending limits, an Owner's form and anyone else's facts), with no primary button. AI cost is one amount, what the organization pays for ScreenHire's key, never the provider's price or a margin; an own key's amount is apart, named "Charged by your provider (estimate)", and never added to it ([billing](../architecture/billing.md#the-billing-page)). General holds the organization's name, data region and when it was created, and, for whoever has the setup checklist and only while someone hid it or marked a step Not needed, a Setup checklist panel that says which and has Show setup again (an outline button), which brings the checklist back for the whole organization; Careers page its careers address, the apply form's questions per page and the sites that may embed it. The menu's name for screen readers is its group. Never indented entries. Email templates are on the Templates page (section 21), and their old Settings addresses redirect there. A surface under the header band: from `lg` a 200px column left of the page's content, in the centred settings frame (section 7), one of the two side columns S7 allows; below `lg` one row above the content that scrolls sideways and never wraps (F5). Each entry is a link with its name and no icon, 32px high from `lg` with a mouse (36px in the row below it, 44px on touch): at 36px an Owner's fifteen entries made the menu taller than a 1440x900 window and pushed Branding, which fits that window, into a scroll; the current page takes `accent` and `aria-current`, and the row scrolls to show it. The menu lists only the pages the person's role may use, each behind its permission. A record under a settings page (a webhook endpoint, a role) is a page with no menu: a detail page when it has the details rail, the form template when it does not. On a settings page the header band fills the window and its title lines up with the menu, on the settings frame's box |
| Settings frame | `SettingsFrame` (`settings-frame.svelte`), which `SettingsMenu` and `AccountMenu` render; `HeaderBand`'s `frame` | The Settings template's frame (section 7, S10): the menu snippet and the page's column, centred at `max-w-settings-frame` (74rem of content plus the page's gutters), from `lg` a `12.5rem` menu column, a 24px gap and the column, below `lg` the menu as a row above it. The column is `@container/column`, so Billing's columns and any component in it respond to the column's width, and a form's fields keep 688px inside full-width panels. `HeaderBand frame` puts the band's contents on the same box, so the title starts above the menu. A page never sets its own width inside the frame |
| Overview columns | `OverviewColumns` (`overview-columns.svelte`), snippets `main` and `side`, `within="page"` or `"settings"` | The Overview template's columns (section 7, S10): two stacks of panels, each `flex flex-col` with a 20px gap, in a grid that is one `minmax(0, 1fr)` column (main then side) until it is itself 69rem wide on a page (`within="page"`) or 55rem in the settings column (`within="settings"`), then `2fr 1fr`. It measures its own width (`@container/overview`), so a parent's padding never moves the threshold. A 1440px window with the full sidebar clears both. Each column stacks on its own, so panels never match heights across a row; a row of headline numbers stays above it, and anything under both columns follows it at the full width |
| Candidates list tabs | `CandidatesListTabs` on `RecordTabs`, inside each list's `Tabs.Root` | Current, Saved and All as the tabs under the Candidates title, as every list page has its collections (the List template, section 7). Current is the people in a hiring process now (on a job that is not closed, not saved, not at an end stage), Saved the people kept for future roles, All every application. Each tab carries its count, and the header band carries none. The table panel is named after the list (Current candidates, Saved candidates, All candidates). The tab is the address, `?list=saved` or `?list=all` (Current has none), so a link and a reload keep it, and `/saved` and `/talent-pool` redirect there. A tab starts the other list clean: the address drops the view and filters of the first (`candidateListHref`) |
| Setup checklist | `NotificationBell` setup section, on `StepMarker`; the steps from `#lib/setup-checklist.ts` | Setup lives in the notification bell, not on Jobs, because a team can use ScreenHire fully without a careers site or an email domain. The Jobs empty state points to it while steps remain ("3 steps left to set up your organization." and Open the setup checklist, which opens the bell at this section), and the bell opens at it once by itself after onboarding. It is for whoever can do the steps and see every job (Manage the team, Edit settings and all jobs: Owners and Admins). Four steps, each a square marker numbered by its place and a link to the page where it is done, in the order a team does them: Set your branding, Publish your first job, Invite your team, Send email from your own domain (last, because it is DNS work outside ScreenHire). Each ticks from the organization's own data, never a click: Send email from your own domain is not offered where the installation has no email provider (A3). Send email from your own domain says Optional and never counts toward the bell's badge or "steps left": a team with no domain of its own would otherwise keep a badge it could never clear (the owner's walk-through, 2026-11-21). Each open step has Not needed, a small outline button at the row's end named "Not needed: " and the step (the owner's words from the walk-through, kept although A6 asks a text button to start with a verb): the step leaves the list and the count for the whole organization, on every device, saved on the server (the owner's walk-through, 2026-11-21). A skipped step keeps its place, so the numbers after it do not change, and its settings page still does it; focus moves to the section, and the panel says "Not needed for everyone in your organization." with a link, Bring it back in Settings, General (A11). Steps that are done fold into one muted line ("2 steps done"), so only what is left takes room; a skipped step is not done. It shows nothing until every answer is in, goes by itself once every step left is done or skipped, and its icon button, "Hide setup for everyone", hides it for the organization; the panel then says "Setup is hidden for everyone." with a link, Show it again in Settings, General. Settings, General has Show setup again, which brings back the checklist with every skipped step. Neither has a confirm dialog or an Undo: neither removes data, the step's own settings page still does it, and Settings, General undoes both in one click (section 10). A failed save says so in one line in the section (T1) |
| Form builder | `FormBuilder` and `AnswerMenu` on `Panel`, `ReorderList` and shadcn `Switch`; `#lib/form-builder.ts`, `#lib/form-sections.ts` | One builder for application questions, follow-up questionnaires and scorecards, modelled on Google Forms. The questions are rows in one panel with their count (S4), and every row starts closed. Open or closed, a row has one header line: its grip, the question, then on the right the answer kind, the row menu (a `more` icon button holding Duplicate and Delete, L3) and a chevron, in that order and in the same place either way, so opening a row never moves them. A closed row reads as its words: the question, a Required badge only on a required question (W6, so an optional one carries no label), its answer kind's icon and name, the help line, and a choice question's options on one line after the mark the person will see (a circle for Choice, a square for Multi-choice): "4 choices: Less than 3, 3 to 5, ...", cut with an ellipsis at the row's width. The row is one button that opens it, and its chevron points down. There is no preview column beside a row; the editor's Edit / Preview switch shows the candidate's view (A8). Only one question is open at a time, with a `primary` bar down its left edge: its text, the answer kind button (the answer menu) where the closed row shows its kind, its name giving way to its icon in a narrow list as the closed row's does, and a small filled Done where the closed row's chevron stands, the two in one slot as wide as Done so opening a row moves nothing; the text, the answer kind, the row menu and Done share the standard 36px height on that one line, whatever the kind. Under it, the help line, the options with their own grip list ending in an outline Add option, then what the kind needs (a knockout, a number's range, a file's types) and the Required switch, with no second Done. It closes from Done, from a click on its header outside a field or control, and on Escape, and the focus goes back to its row. It opens and closes with a height animation of 180 ms, and none under reduced motion. On a wide edit pane the open row lays its fields out in columns (question beside answer kind, help text beside the Required switch) instead of one narrow stack. Duplicate puts the copy straight after the original and opens it. Deleting changes nothing until Save, so it does not confirm (section 10). A problem shows under its question whether the row is open or closed (W7). Add a question, under the list, opens the answer menu. Questions and options reorder with a grip (A7). The editor that mounts it has the Edit / Preview switch at its top (A8). Changing a question never changes answers already given. A questionnaire (never a scorecard) may have named sections, each a page for the candidate: an outline Add a section beside Add a question, and each section a row in the same list, on the `muted` fill, with its grip, an inline name field, its question count and a Delete section icon button. A section row starts its section, so moving a question past one, by drag or the grip's Move up and Move down, moves it into that section; the list stays one panel with one open question. Deleting a section gives its questions to the section before it and does not confirm (section 10). The preview shows the candidate's form a step at a time, as the careers page does: the details, each page, then the review with the AI-use question (a follow-up: its pages), each opening with `PageProgress`, with Back and Next. The last step's send button is off, with "Nothing is sent from a preview." beside it |
| Answer menu | `AnswerMenu` on shadcn `DropdownMenu` | Picks a question's answer kind, each group under its heading and each kind with its icon; the current kind is ticked, and Add a question opens the same menu with nothing ticked. General: Yes or no, Choice (pick one), Multi-choice (pick any), Short text, Long text. Application questions add More: Number, Link and File. A follow-up questionnaire offers the same without File, because its page takes no uploads. A scorecard offers General and Rating, each rating with a star: 1 to 5, 1 to 10, Yes or no |
| Rating | `Rating`; `#lib/rating-column.ts` for a job's table | The candidate's rating: one to five stars filled to the tenth, the number and how many scorecards it comes from (in words in the count's tooltip), as a link to the Scorecards tab in a candidate's header (A5), or, where scorecards are given, opening from a small outline button (A6) to each scorecard that counts with its own number and recommendation. In a table cell it is the number only, no stars (L1). It averages people's rating questions only, never verification results, AI signals or the stage; a reviewer with a scorecard to give reads "Rating after your scorecard" ("After yours" in a table cell, the rest in its tooltip) until they submit their own; no automation acts on it. Until a scorecard counts a table cell is blank, its words ("Not rated yet") for screen readers only (L1). A job's table has a Rating column, left out until a scorecard has rated someone or a reviewer owes theirs (an empty column on every row is noise, L1), highest first, the unrated last |
| Rating answer | `RatingAnswer` on shadcn `ToggleGroup` | A rating or yes-or-no question on one row of joined buttons, with Low and High under the two ends of a number scale, once for a run of scales (the first one), not under each. Each button is a radio in the question's group; the chosen one takes `accent` (A6) and an inset ring in `accent-foreground`, so a chosen button is not told from a hovered one by a faint fill alone, and choosing it again clears an optional answer. A number scale never wraps: its buttons share the row equally, up to the control size, so 1 to 10 on a phone has buttons narrower than 44 px (still above WCAG 2.5.8's 24 px) and High stays under 10 |
| Plan card | `PlanCard` (`plan-card.svelte`) on `Panel` | The first panel of Settings > Billing for an Owner, titled Plan: the plan is what customers pay for most, and one plain row did not show it (the owner's fifth walk-through, 2026-11-22). The plan's name in `panel-title` and its monthly price in `metric` with "a month" (and "plus tax where it applies" under it while Checkout adds tax), then when it renews or ends as a day in words ("Renews December 22", section 13). That day repeats the date of This month so far's next invoice on purpose, as the plan's own fact; the amount is said only there (W7). Then what every plan includes as one wrapping row of `check` ticks from the plan catalogue (`packages/plans/included.json`), the list the marketing pricing page shows. Under a hairline, the headroom: on a limited plan a small meter (a `secondary` track filled in `primary`, never a status colour, and `aria-hidden` because the words beside it say it) with "3 of 10 open jobs"; with no limit the count alone, "3 open jobs". A job count against a limit is not a score (G1 is about verification). A downgrade that left more open than the limit fills the bar and keeps the true count. Change plan, an outline button, brings the Plans panel into view and moves focus to it; it is left out when no plan card there has an action, such as after a failed payment or for a plan set to end (A3). During the trial the card is the trial card: "Trial: 9 days left" (calendar days in the person's zone, the bell's count), "Growth until December 5", the ticks, the meter and Choose a plan, with no price, because the trial costs nothing. Once a plan has ended it reads "No plan", the count and Choose a plan. The state stays the badge beside the page's title, never on the card (W7) |
| Plan comparison cards | `PlanComparison` (`plan-comparison.svelte`) in Billing's Plans panel | The plans side by side as cards, in the catalogue's order (the order of the marketing pricing page's rows), with the same list of what every plan includes. Each is a bordered card with no shadow, because it sits in a panel (S4, S5): its name (`heading`), its price in `metric` with "a month" ("Price not set" without one), then its ticks: its open job limit first, semibold, the one thing plans differ in, then what every plan includes, muted. The ticks repeat on every card and on the plan card above, as on a pricing page, so each card reads alone. The plan in force takes the `primary` edge and a muted "Your plan" pill; during the trial the trial's plan is marked "Your trial", because it still has to be chosen. The other cards carry their action as a full-width outline button at the foot: Change to Growth on a live subscription (the plan change dialog, which prices it first), or Choose Growth on the trial or after a plan ended (Checkout). Three across once the panel's body is 38rem wide (`@container`, `@min-[38rem]`), the least that keeps Change to Unlimited on one line in each card (F5), and one above the other below that, never two and one. The plans are a set the catalogue fixes, three today, so cards side by side are the exception S9 allows; a fifth plan would make the cards a list |
| Choice card | `ChoiceCard`: a native radio in a labelled card | A choice of a few options in a `radiogroup` with its label: each a bordered card with its name and one muted line, and the whole card is the target. The chosen card takes the `primary` edge and `accent` fill. A member's jobs (All jobs or Only jobs they're on), the role to copy in New role, and how a candidate picks an interview time (Pick from our times, Tell us when you're free, We'll suggest a time), which comes first in Schedule interview, Change how they pick and a stage's scheduling default |
| Weekly hours | `WeeklyHoursEditor` (`weekly-hours-editor.svelte`) | Hours in a week as a fieldset with its legend: a row per day, Monday first, each a box with the day's name and, once on, its From and To selects; a day that is off says No hours in muted words. One stretch a day. In a container narrower than 24rem (a sheet on a phone) the From and To of a day that is on go on the line under its name, so the times stay readable. An interview's hours or interview blocks, a stage's default and a person's own interview hours |
| Filter menu | `FilterMenu` (one choice), `MultiFilterMenu` (several) | An outline button that names the filter and its choice ("Stage: All", "Job: 2 chosen"), `primary` while set, opening each choice with its count. `MultiFilterMenu` shows a box on every choice, ticked or not, and with two or more chosen each one is a pill with its own remove button beside the button. Filters sit in one row under a list panel's header that scrolls rather than wraps (F5); the page applies them to the rows |
| Save view button | `SaveViewButton`; its dialog is `SaveViewDialog` on shadcn `Dialog`, `Field`, `Input` and `Switch` | An outline button with a plus icon, "Save view", at the end of a candidate table's filter row. It is always there, so people can see that views can be saved: disabled until the filters or sort differ from the view the table is on, with the reason in its tooltip (A3). A phone shows it only once it works. It is the row's only button of its kind (A1). It opens a small dialog: Name (required, 80 characters at most) and a Share with the team switch, off by default; the dialog's one primary button is Save view, and the new view is opened at once, so the button goes away. The dialog is in the page only while it is open |
| Rename view dialog | `RenameViewDialog` on shadcn `Dialog`, `Field` and `Input` | Opened from a view's menu in the sidebar: one Name field and a primary Rename view button. A name the person already uses for a view of that kind shows under the field (W7) |
| Team views sheet | `TeamViewsSheet` on shadcn `Sheet`, `Badge`, `Button` and `Empty`; opened by the sidebar's Browse team views | The views teammates shared, newest share first, in a right-hand sheet (section 9). Each row is a block: the name, which wraps, with a muted New pill for a view shared since the person last opened the sheet, then three muted lines (who shared it, its filters in one line, "Shows 3 candidates now"), then outline Preview and Add, or "In your sidebar" and an outline Remove. Every row has them, so none is a primary (A1). A row's buttons wait while its request runs and a failure shows on that row. With nothing shared the sheet says where to share one. The button that opens it is Browse team views, with the `user-round-search` icon (Candidates has `users`, and the rail shows both): the last item of the Views list in the full sidebar and the phone's sheet, and in the icon rail an icon button with a tooltip in the fixed group above Settings, outside the Views list that scrolls, so it is always in reach and past the 24 px target size (WCAG 2.5.8) at 700 px tall |
| Picker | shadcn `Popover` and `Command` | An outline button with a verb ("Add a person", "Copy from another job") that opens a searchable list; typing filters by name, a pick closes it. The list keeps a 16px gutter from the window's edge. Nothing left to pick is said in the list, never by disabling the button (A3) |
| Country picker | `CountryPicker` on bits-ui `Combobox`; the codes and names in `#lib/countries.ts` | Several countries for one field (a remote job's countries, W8): a search field labelled by the form that finds countries by name in the person's languages, ignoring case and accents, and each chosen country a fact pill with its own Remove button beside it (A5). The arrow keys move through the matches, Enter or a click adds one and the list stays open for the next; a ticked country in the list is already chosen, and picking it again takes it away. Focus goes to the next pill's Remove button after a removal, and a live region says what was added or removed. One country is a native select of the same names. The value is always the ISO 3166-1 alpha-2 codes |
| Colour field | `ColorField` on shadcn `Field` and `Input` | A colour for Branding: the native colour picker beside a hex field (16px on touch, F4), empty for its default (its placeholder says so: "Default" for the brand colour, "None" for the second colour), with one line saying where the colour goes. As someone types a whole colour, the API checks it (section 18); each pair it fails is a line under the field in words ("Links in #ffd400 are hard to read on the page: 1.4:1, and they need 4.5:1."), and the nearest passing shade is an outline button on that row (F6), "Use #8a6d00", which fills the field. A refused save's messages take the same place |
| Drop area | `DropArea` in `@screenhire/ui/questions` (`packages/ui/src/questions/DropArea.svelte`), shared by the dashboard and the careers apply form; `FileField` is the question that uses it and `FilesField` is a reply's several files | The one way to choose a file (A12). A dashed area with 12px padding (`rounded-lg` in both: `border-input` on `secondary` in the app, a 2px edge in the candidate tokens on the careers pages) with a real file input inside it, so the keyboard (Tab to the input, Enter or Space), screen readers and phones work. Empty, it says "Drop a file here or" and shows Choose file; with a file it shows the name and size with Replace file and Remove (outline buttons), and it keeps a polite live region for "Uploading...". It answers to its own width (F2): under 448px the words sit above full-width buttons, so nothing wraps. In the app tone, which is a tile in a row of tiles, the buttons stay in one row at any width (they wrap only when two of them cannot fit) and the gaps are 8px, which keeps a set tile short. Sizes are 1024-based (`describeSize`), as every upload limit is, so "10 MB" means the same in the limit and in the name. A refused area has a destructive edge beside the words. The whole area is the target: a click anywhere opens the picker, and while a file from outside the page is over it the edge turns solid in the ring colour on the accent fill (dragging text or a link does nothing). The first dropped file is taken when several are, because a field holds one; in `multiple` mode, which a reply's files use, every dropped or picked file is taken. There each file is a row above the words with its name, size and its own Remove (labelled "Remove" and named for the file), the words read "Drop files here or" and Choose files, and once there are files "Drop more files here or" and Add files. A pick adds to the rows and never replaces them. A file the field refuses (wrong type, too large, empty, already attached, over the file count or the total size) is named in its own line in the field's error ("report.exe: Upload a PDF or DOCX file."), one line per file, and the others are still taken. The named input holds exactly the rows, so the form posts what is shown, and without script it is the same named input with `multiple` in the dashed frame. A type or size the field does not take is refused in words ("Upload a PDF or DOCX file.", "This file is larger than 10 MB.") through the field's own error, never as a toast, and the refused file is not kept. Replace file and Choose file look like buttons but are not: the input is the one control, so assistive technology finds one picker, and Remove is a real button. On a careers page rendered without JavaScript it is only the named input in its dashed frame and posts with the form; once the page's script runs the area takes over, and a field that posts with its form gets a dropped file put into its input. Tokens only, no inline styles, and the hand cursor comes from the theme (A10) |
| Image field | `ImageField` on shadcn `Field` and the Drop area; `BrandImageSlot` in `#lib/brand-images.svelte.ts` for the upload | A brand image as a tile showing it where it is used, at its real proportions: a logo on a light checkerboard (the candidate surfaces, light in both themes), a favicon at 32px inside a small browser-tab mock, a sharing image as a small card, its picture beside the title and description a shared link shows (the Branding preview draws the full card). A set tile is about 200px tall, so the three sit in one row of the wide Branding editor and the page does not scroll at 1440 by 900 with all of them set. The tile is the Drop area, so the picture is part of the target: empty, it says "No logo yet" and "Drop a file here or Upload logo"; set, it gives the pixel size, names the file and its file size with Replace and Remove in one row (their nouns are short: "Replace icon", "Remove image"), and a click or a dropped file replaces the image (nothing changes for anyone until Save). A file that is not a PNG or JPEG, or is over the kind's limit (the API's `BrandImageRules`), is refused in the field's error. While the malware scan runs it shows the grey Scanning badge (T3) and the page's Save waits, saying why (A3); a file the scan blocked says so with `FailureText`. The upload goes straight to storage, as a candidate's does |
| Heading font picker | `HeadingFontSheet` (shadcn `Sheet`, `SearchField`, `ToggleGroup`) | More fonts, from Branding's Heading font panel: the Google Fonts catalogue in a sheet over the page (section 9, in place). A line says where the fonts come from and that ScreenHire hosts the one picked. Below it, Search fonts and one style filter (All, Sans, Serif, Display, Rounded, Slab, single choice, All by default, one row at every width, the sheet being full width on a phone), then the count ("1,503 fonts") as a status, then the list as radios, 40 at a time with "Show 40 more". Each name is drawn in its own font at `text-heading` bold, loaded through the API only as its row scrolls into view; until then, or if it fails, the name shows in the page font. The font candidates see now, the last save's, says "In use", whatever is chosen but unsaved. The footer has Cancel and one primary, "Use {family}", disabled until a font is chosen; it fills the panel's choice and closes, and nothing changes for candidates until the page's Save. While the full catalogue cannot be reached, a line says only ScreenHire's fonts and those already in use are listed. A font Save could not download says why under the panel (W7) |
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

**One name for one thing.** The bell in the top bar is Notifications, and so is its panel, whose tabs are Needs attention and Activity (the workspace's, as a candidate's Activity tab is the candidate's); the person's own
choices of what reaches them, by toast, email and Slack, are Your account > Notifications; the organization's starting choices for
the alerts (Verification suggested, AI usage) are Settings > Notification defaults, one alert grid, never inside the feature an
alert is about. In Settings the group
is Careers site and its page is Careers page, so a sentence about the page says "careers page" (the careers page address,
Careers page settings). After Publish the job page says where the job went, because the Publish button is gone. The Jobs
overview waits for the first job, and the empty state says what to do first and where the setup steps are.

**Voice.**

- **Recruiters:** precise and neutral. "Clear", "Review", "Verify" (one word each, with "Review suggested" and "Verification suggested" kept for screen readers), "3 reasons";
  never "suspicious", "fake", "fraudster" or "AI-generated candidate". Every automated observation says what was
  observed, not what it means about the person.
- **Candidates:** warm, plain, honest. Explain what we look at and what we never do, before collection; offer the
  assistive-tech toggle; welcome declared AI use where the employer allows it. "Our checks never reject anyone."
- **Marketing:** confident and specific; cite a source for every statistic; no invented numbers.

## 13. Dates and times

| Where | Show | Example |
|---|---|---|
| Text, headers, activity | Time ago, exact date and time on hover | 2 hours ago |
| Tables | The date, so the column lines up and sorts | Oct 20, 2026 |
| Older than 7 days | The date | Oct 3, 2026 |
| A day coming up, in a line of words (the trial's end) | The day and month in full, no year, in the locale's order | December 5 (en-GB: 5 December) |
| Days left (the trial) | Calendar days in the person's time zone, the same count in the bell and on Billing's plan card (`calendarDaysUntil`); "ends today" on the last day | Trial: 9 days left |
| Last updated (T6) | Time ago and who, the exact time on hover; Created until the first change | 2 hours ago by Avery Quinn; Created 2 hours ago by Avery Quinn |

Always the reader's locale. The dashboard uses the language, time zone and date format the person chose on Your account >
Profile, and the browser's for anything they left unset (US English by default), through one shared formatter on
`Intl.DateTimeFormat` and `Intl.RelativeTimeFormat` (`#lib/dates.ts`, reading `#lib/date-settings.svelte.ts`, so a date on
screen rewrites itself when they save). The date formats are As your language writes it, Day, month, year (21 Nov 2026),
Month, day, year (Nov 21, 2026) and Year, month, day (2026-11-21). The careers pages have no script, so the server
writes every date and time of day with one helper (`apps/careers/src/lib/locale.ts`) from the request's `Accept-Language`,
the first language `Intl` supports, else en-GB. A candidate's emails use the language recorded with the application (the
same tag, sent when it was submitted), else en-GB, through one .NET helper (`CandidateLanguage`). Times keep the zone they
state, and the zone's name and the words around a date ("to", "Saved until") stay English. The API never sends formatted dates (`.claude/rules/utc-timestamps.md`).

## 14. Icons

Lucide, imported per icon as `~icons/lucide/<name>` (unplugin-icons with `@iconify-json/lucide`, which keeps the whole
set in one file; `@lucide/svelte` ships three files per icon and is not used). 16px beside 14px text, 20px in touch bars
and the icon rail. Size 16 is the default from the icon customizer in `apps/dashboard/vite.config.ts`, and stroke 1.75
is set on `svg[data-icon]` in `app.css`, where the shapes inherit it, so a `fill-*` or `stroke-[...]` class on the icon
reaches them. Navigation and main buttons pair an icon with a label; decorative icons are `aria-hidden`. Status icons (section 3.2) appear only in status badges, and no other control or
decoration uses them, except a built-in view of a status in the sidebar, which shows its status's icon uncoloured (I2, section 21). The level icons (`eye` for Review, `id-card` for Verify,
through `LevelMark`) draw only inside a verification state (section 3.2), and as the sidebar icons of To review and To verify. The warning triangle marks only
the verification cards that fed a level (section 16). A problem in words (a failed action, a form error, a builder's
problem to fix) starts with `circle-alert` in the verify icon colour (`ProblemIcon`). It is not a status icon, so it
never stands in for a badge.

`LevelMark` (`#lib/components/level-mark.svelte`) is `aria-hidden`, because the state's word and its screen-reader text
carry the meaning, and the icon tells Review from Verify without colour (T1). The
AI mark is a Lucide `sparkles` in a tooltip, outside any badge.

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
A opens the bell at its Activity tab from anywhere. Hints appear as `Kbd` key caps in tooltips and menus, never as an
always-on bar. Single-key shortcuts (the G chords, J and K, X, V, A and `?`) can be turned off (WCAG 2.1.4), skip text
fields, menus and dialogs, and work only while their table or board has focus, except the G chords, A and `?`. The rich text editor's shortcuts all have a modifier (Ctrl or
Cmd B and I, with Alt 2 and 3 for the headings, with Shift 8 and 7 for the lists) and Link has none, because Ctrl K is
the palette. Typing "- ", "1. " or "## " starts a list or a heading only while single-key shortcuts are on: for speech
input those sequences act like single-key shortcuts.

## 16. Verification UI

Evidence, not verdicts (ADR-0004, CLAUDE.md non-negotiables 1 and 2):

- The Verification tab is organised by the six check groups the engine runs (`CheckGroup`): Network, Contact details,
  Documents, Answers, Duplicates and Form activity. The two assessments (authenticity and AI assistance) are not panels any
  more. The candidate's level shows once, as the mark in the page header (T2); the tab does not spell out Clear, Review
  and Verify again (W7).
- The tab opens with the suggested next step. When the level needs a look it leads, semibold, in a callout edged in the
  level's colour with an arrow, the summary a muted line under it; otherwise it is one quiet line, the summary then its
  next step. Dismiss sits right under it, an outline button, and after a dismissal one muted line says who set it aside,
  when and why. The level stays as assessed. A role that cannot review reads one line saying it can read the checks and
  should ask a reviewer (E3).
- AI help is information (a bare `sparkles` mark with a tooltip, in its own colour, never a warning). Its row is the AI
  badge ("No signs", "Some signs" or "Strong signs of AI help") and the job's policy as a pill ("AI allowed with
  disclosure"). For Some and Strong signs one muted line carries its summary and next step; for No signs they sit in the
  badge's tooltip and screen-reader text. It never changes, sorts, filters or counts toward the level, and it has no
  Dismiss.
- A map of the six groups follows, one tile per group, each a link to its panel with one state in icon, colour and words:
  "2 found" (counts toward the level, the review status at any level), "1 note" (AI help and context
  that never counts; the sparkles only when every note is AI help, otherwise the grey `info` mark), "Nothing
  found", "Did not run" (a candidate a recruiter added by hand came through no careers form, so Network and Form activity had
  nothing to look at; it goes by the application's source alone, so a careers applicant who turned telemetry off with
  the assistive-technology switch reads like any other) and "Off" (the organization's settings, with the note in
  a tooltip). "AI checks paused" is the Answers tile when the organization's monthly AI cap paused the AI check and nothing
  else in the group found anything: the rule-based checks ran, and the note says it is no result about the candidate. Group icons sit on the tiles only, never in a panel header, and a group has no description paragraph.
- Below the map is a panel for each group that has findings, titled with the group's name. Nothing else gets a panel:
  "Nothing found" is a tile state and appears nowhere as a line. A finding is its plain sentence, short ("The resume and
  the answer give different dates for Lumen Health. Ask the candidate."), opening (A9) to its evidence as a facts row,
  any quote or comparison, the counter-evidence in its own card and its code. Only the exception is marked: a finding
  that never counts carries the sparkles mark for AI help or the grey `info` mark for context (a
  VPN with nothing beside it), a mark alone with no words, and "Information only" is said once on screen, in the AI row's tooltip
  (neither a tile nor a finding repeats it); there is no "Counts toward the level" tag. Expand all and Collapse all
  show only where a group has more than one finding.
- One review action per finding: Mark explained, an outline button on the row of its code (F6), offered only on a
  finding that counts toward the level while the level needs a look, the result is not dismissed and the role reviews
  Verification. It opens a dialog: a reason from a list (Confirmed with the candidate, Expected for this role, A known
  false positive, Other) and a note, optional unless the reason is Other. An explained finding closes, shows a quiet
  Explained badge, and under it one muted line starts with who explained it, then when, why and the note, with an outline
  Undo for its author only. Information-only findings have no action. Explaining decides nothing: the level, the
  candidate's state, "To review", filters, automations and webhooks never read it. Once every finding that counts is
  explained, Dismiss reads Dismiss all and opens the dismiss dialog with the explained findings and their notes listed.
  A dismissed result keeps its explanations, read only.
- A clear result (no finding in any group) opens with a `circle-check` in the clear colour and "Nothing unusual found",
  semibold, with the next step as a muted line under it; never "Verified" or "Genuine". Then the AI row and the same map of
  six tiles a concern gets, each "Nothing found", "Off", "Did not run" or "AI checks paused", so a quiet result is never read
  as a clean one and it shows what was looked at (W10). There is no hidden "What was checked" list.
- "Checked 2 hours ago" sits in the Verification panel's header, caption size, with the ruleset version in the tooltip of
  "Checked". The panel's closing line, inside it and in caption size, is "No automated decision has been made. Every action
  is logged." (W10). The ruleset is no line of its own on the page. A code or an id never sits above the next step.
- There is no Review log panel and no Add review note button. Every review action is an entry in the candidate's
  Activity, under the Verification filter: a dismissal, an explanation, its undo, a note, evidence that a re-check added
  or took away, and each read through an API key (one line for a run of reads by one key, with the key and who made
  it). A finding's explanation and note also show under the finding they concern.
- No score, no percentage, no reject control. Facts shown elsewhere on the page (the candidate's AI declaration) are not
  repeated in the tab (W7). On a candidate, when the level needs a look, the tab's count is the signals behind it, in
  the grey every tab's count has (E2).
- A level shows as one word (Clear, Review, Verify) with the level mark; its full name (Verification suggested, Review
  suggested) is screen-reader text. AI assistance reads No signs, Some signs or Strong signs of AI help.

### The Verification area

The area at `/verification` is the Area template (section 7): three tabs under one header band, in this order, each at its own
path. Everyone who can see verification has the area and all three tabs. It holds only the checks (the owner's walk-through,
2026-11-22): what they used and cost and the monthly limits are Billing's AI use section, whose key runs the AI check is Settings >
Integrations > AI model, and the alerts are Your account > Notifications with the organization's defaults in Settings >
Notification defaults. The addresses of the tabs that merged or moved (Policy, AI use, Usage, AI model, Notifications) lead to the
page that holds their settings now, for the person's role, with their query string.

| Tab | Template | Who sees it |
|---|---|---|
| Overview | Overview (section 7) | Everyone. Four stats (applications checked, then Clear, Review, Verify) in their equal-width row; in the main column a chart of all six check groups with the applications in which each counted a signal and its table (a group that is off reads Off, its name a link to Checks); and, for who can see AI usage, the AI credits balance in the side column with a link to Billing's AI credits |
| Activity log | List, full width | Everyone sees one row per run of the checks (an application's pass: "Frontend Engineer (Svelte), 8 checks, 1 did not run") with its Outcome as a status and nothing else in the cell (Nothing found, Found, Did not run). The row opens (A9) to its checks: why one did not run, as a sentence; what one found, in the candidate tab's words; for the AI check the model, key, tokens, cost and provider request id; Open application; and the ruleset in caption size. The call facts, the key filter and the CSV (one line per check, every column, with its run and ruleset) are for who can see AI usage (View AI usage with All jobs). Thresholds, weights and rules never appear. Above the log, for who can see AI usage, one quiet line: "This month: 12 AI calls", linking to Billing's AI use section |
| Checks | Compact rows at full width | Every switch that decides what runs, one compact row per group so all six and Save are on a 1440x900 screen with nothing open (the owner's walk-through, 2026-11-22: the old sections were dense and each switch floated halfway across the page). A row starts with the group's switch, labelled by the name beside it, then a one-line summary, the chevron, and the month's count as a quiet chip at the row's end ("Found in 4 of 14", "Nothing found" at 0, none for a group that is off; a count, never a score). The row opens in place (A9, A17) to what the group looks at, one example of what it catches, what turning it off costs, and the settings it tunes: AI use while applying ("Allowed with disclosure (default)") and "Compare each application's sources" in Answers, trusted networks in Network, trusted email domains and the device check in Duplicates, whose text says the device check also affects Form activity. What an Off group means is in an Info button beside the panel's title, and Expand all or Collapse all is on the panel's right. A link opens one row by its key (`#answers`). Then one Save, which keeps rows that are closed. Everyone reads it as text, with On or Off where the switch is; Edit settings gets the controls |

- The Overview counts applications and nothing else: how many were checked this month, how many at each level, and in how
  many a group of checks counted a signal. There is no score, no percentage, no AI-assistance count (AI help never counts),
  no ranking of jobs or people and no trend that reads as a verdict. A single number is a stat, never a chart. A chart is a
  Chart panel with its table (section 3.5), one series in `chart-1`, or two for built-in against own key.
- A group that found something is a count of applications in the group's own words, never a status colour on a tab (E2).
- Read-only is plain text, never a disabled control (A3). A tab someone cannot use is not in the strip.
- The Signal Network has no control. It stays off until legal review, and a switch that cannot be turned on would be a
  disabled control. When it can be, it joins the Checks tab.
- The Activity log and the call table are ordered by the server (newest first), so their headers do not sort (L2); the
  newest-first order is the point of a log.

## 17. Accessibility and motion

- WCAG 2.2 AA. A 2px `primary` focus ring with a 2px offset on every interactive element, never removed.
- Targets at least 24px with a mouse (2.5.8) and 44px on touch. Text at least 4.5:1; large text and boundaries 3:1.
- Every drag has a non-drag alternative (Move to stage, and the grip's Move menu, A7). Every shortcut has a visible
  control.
- A canvas of nodes (the workflow graph) makes each node one tab stop with the focus ring and a label that says what it
  is: the node is itself a button where it can be pressed. The arrow keys, Home and End move between nodes, and
  whatever takes the focus scrolls into view. Text in a node is clamped, with the full value in a tooltip that takes
  no tab stop of its own.
- Motion 120 to 180ms for hover and popovers, 200 to 240ms for sheets; `prefers-reduced-motion` turns it off, and so does
  Reduce motion on Your account > Preferences, for this browser: it puts `.reduce-motion` on the page, which the
  `motion-reduce` and `motion-safe` variants read as well as the system setting (`app.css`).
- Careers pages move from page to page with cross-document view transitions and no script: a form step leaves in 160ms
  and the next slides in from 16px to the right in 220ms while the header holds still. The rule lives inside
  `prefers-reduced-motion: no-preference` and `scripting: enabled`, so reduced motion and JavaScript off get a plain
  page load.
- axe runs in component tests and Playwright; manual NVDA and VoiceOver passes per release; an external audit before
  general availability.

## 18. Employer branding

Settings, Careers site, Branding (ADR-0018, `docs/architecture/branding.md`) sets a brand colour, a second colour, a heading font,
a logo, a favicon and a sharing image, each optional and shown next to ScreenHire's default (principle 10). They apply
to the careers pages, the apply flow, the embeddable widget and the top of every email to a candidate.

| Part | Where | Check before it saves |
|---|---|---|
| Brand colour | The Apply button and every filled button, links, focus rings | White or ink on it at 4.5:1 ("Text on buttons"); it on the page background at 4.5:1 ("Links") |
| Second colour | The careers header band, the email's top band | White or ink on it at 4.5:1 ("Text on a header") |
| Heading font | Every careers title (`font-display`) | Bricolage Grotesque (ScreenHire's), one of five more bundled fonts (Inter, Montserrat, Nunito, Fraunces, Playfair Display), or any Google Fonts family under the OFL or Apache 2.0, which Save downloads once and ScreenHire hosts (ADR-0031) |
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
- The Heading font panel shows ScreenHire's (named "ScreenHire's", in Bricolage Grotesque) and the five other bundled
  fonts as radios, each name in its own font on one line, then the catalogue font in use or just picked. More fonts, an outline button in the panel's
  header, opens the Heading font picker (section 11). Each choice has 6px of padding above and below and 8px at the sides (10px all round on touch), so the editor still
  fits 1440 by 900 with a catalogue font as a seventh choice. A careers page and the picker load every font from
  ScreenHire, never from a font host.
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

- **Job:** the work area first. The verification bar becomes filter chips with counts, the stages a Stage filter,
  and Job postings (once Distribution: where the job is posted) its own tab, whose count says how many boards the job went to. A refused posting is a notice that names the board and links to
  the fix. Calm rows say nothing. The board's empty and closed stages shrink so every stage fits, and the board
  scrolls inside itself. Workflow opens its own page from the header. Selected rows get a bar with Move and Email;
  with a verification filter on, Email hides and Rejected leaves Move (ADR-0004).
  Built: the chips and the Stage filter sit on a row under the pipeline panel's header (S3), as words and counts with
  no status icon (I2), and a phone has Stage, Verification and Sort chips above the list items. The tab is in the
  address (`?tab=postings`; the old `?tab=distribution` still opens it), and the count is the boards the job is on or on its way to. Job postings is a small table, one row per
  board: Status, Board, Posted, Last updated and a row menu. The menu has View posting and Copy link where we know the
  live address, which today is only the careers page: a board that reads our feed never says where its listing landed.
  Then comes what a person can do there: Edit details and Customize (every board but the Careers page) and Retry on a
  posting that needs fixes, Post or Post again, and Take down last. A board with text of its own carries one Customized
  badge after its name (L1). A board that only closes with the job says why it has no Take down. The board's estimate is in its
  status tooltip. A posting that needs fixes is a notice above the tabs, and the row's menu holds its two fixes, each
  saying how far it reaches: Customize for that board and Edit details for every board. A customized board whose job
  changed since has the badge Job changed instead of Customized, with the reason in its tooltip. Every row has an
  Applications column: the applications that came through that board's link, blank for Google for Jobs and the public
  job-board API, whose candidates land on the careers page's own address and count there. An open job's panel header has
  Post it yourself (outline, A1), for a board ScreenHire does not post to; each posting saved there is a row under the
  automatic boards, Posted by you, its board's name a link to the listing, with Job changed since you posted while the job's
  copied fields differ from what was posted (the tooltip names them), and a menu of Edit, Open the listing, Copy the updated
  text, Mark as updated and, after a separator, Mark as taken down. Once the job is closed or the posting's end date has
  passed, the row is Still posted by you (review, `megaphone`) until it is marked taken down, and the close dialog names each
  one still up. The tab's count includes them. On a draft or closed job the automatic boards have no
  menus: its header publishes or closes it, and while it is a draft the band's line under the title says, quietly, what the
  check before publishing found ("2 things to check before publishing", plain muted text, since Publish opens the Publish check dialog; nothing when
  there is nothing, and nothing for someone who may not edit jobs). A posting whose checks
  found issues is "did not take this job", never "refused": only a board's own answer is a refusal. On the board, the
  closed stages share one Closed column (section 5), whose rows take a dropped card, and a calm card shows no level.
  In the table one Verification column holds the state (a quiet Clear for calm, the level mark for Review and Verify, a muted
  Pending or Dismissed) with the AI mark beside it for Some and Strong signs. It sorts Verify, Review, Dismissed, Clear, then
  Pending. Overdue leaves the Stage cell for a tooltip, rating cells show the number only, and the panel title is Candidates,
  with no count.
  Hiring team: an outline button in the header with the team's count badge (T4), in More on a phone, opening a sheet
  of the people on the job, each with their role, "Only jobs they're on" for someone who sees only their own (W6), a
  remove button, and Add a person, Add a team and Copy from another job (Pickers). Add a team puts a named team's people
  in the list, its hint the number of people; anyone can then be taken off before Save, and the job keeps the people, never
  the team. Changes land on Save. Without Edit jobs the sheet lists the people and says why (E3). A hiring template has
  no team: who works on a job is chosen on the job (the owner's walk-through, 2026-11-21).
  New job asks Who's hiring? as a fieldset after the template: a Team select (No team, then each team with how many
  people), which starts on the team the last job from the same template or department used, with a hint naming its
  people and that it was used on the last job like this one, until the person picks (no hint for No team); then the people
  added one by one, each with a remove button, and Add a person, which leaves out the chosen team's people. A list that
  fails to load shows its error and Try again in the fieldset. With nobody but the creator in the organization the
  fieldset is not shown, and the job starts with them alone.
- **Jobs, Edit details and Workflow (page audit):** the Jobs list opens on Open, the jobs a team is hiring for (All for a team with nothing open yet, decided once as the list arrives so the filter never changes under someone; `?status=` holds a choice, `all` included); after the title come Candidates, the people in play on the job by the Current list's own rule (a closed job, or one with nobody, is blank, L1; a phone's item says "3 candidates"), and Last applied, the date the latest application arrived (blank for a job nobody applied to; a phone's item says "Applied" and the date), so a recruiter sees which job needs them and which has gone quiet without opening each; Published is left to the job's header, and the row
  menu, for someone who may edit jobs (A3), opens Edit details (a link, A5) before Publish to every board or Close and remove everywhere; with a filter on,
  its empty state is the title and Clear filters, with no sentence. A job's candidate table ends each row, and the board each card, with one
  menu holding Move to (L3). On a job's header, Edit details, Publish and Close and remove everywhere are left out for someone who may not edit jobs (A3), Opened is left out while Last updated says the same minute (W7), and on a phone the More
  menu's item is Edit hiring team. The Jobs search matches the title and location, the columns a person sees (W6). Job postings has the Sort chip on a phone (L2). Edit details is headed with the job's
  name and its status, titles its panel Edit details as the crumb and the button do, and on an open job says before
  Save how far it reaches: the careers page changes now and boards that already have the job keep what they were sent until someone updates them (E3). Back on the job, a notice above the tabs says the boards do not have the change yet, with Update boards, which sends the new version to every board the job is on and checks refused ones again. It shows only to someone who may edit jobs. A
  job made from a template has the fact Template ("Retail assistant v5"), and while the template has a newer published
  version, a second fact, Update available ("Retail assistant v6, See what changes"), whose link opens the job's
  pipeline page with the differences. The job's header button is Edit pipeline. The pipeline page's header is "<job
  title> pipeline" with the job's status first and the check on the pipeline second (S2, T2), and what saving did sits
  in the band's line under the title, as on the job page, so it never pushes the actions to a second row (S8); its
  primary action is Save pipeline, and `?review=update` opens the template update's differences, with Apply update. Its stages are the Stage cards, each with one menu for Edit stage, Add an automation, Choose scorecard and Remove stage, and their sheet; a stage with no time limit says nothing about one; what happens at a stage is its
  Stage chips, counts that open lists of names, never sentences. Where the workflow reads only (a closed job's), its Pipeline tab is the Stage flow, not these rows. Try it (section 11) walks one made-up candidate through the stages one at a time, each stage's emails and tasks
  drawn as the real things, and ends on Looks good, publish or Change something.
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
  header links here on every tab, as a Candidate profile button beside Email (in More on a phone).
- **Candidate:** the same seven tabs in the same order for everyone: Overview, Application (the answers, the forms sent
  to them and the candidate's other applications), Files, Messages (every email with the candidate, both
  ways, and the reply box under them), Scorecards, Verification, Activity. The header's actions start with Candidate
  profile, so the profile is one click from every tab (the owner's walk-through, 2026-11-21). Activity lists everything newest first, saying who and
  how long ago, and filters by kind, and holds the note box and the manual automations. Only a note's author edits it; the
  author, or a role with Delete anyone's notes (Owners and Admins), deletes it, after a confirm. The header holds who they
  are and where they stand, in this order: the name and the verification state (Clear, Review, Verify, Pending or
  Dismissed, then Saved, Rejected, Withdrawn or Hired once they left), with the actions on the right (Email and Move to
  stage, the only filled button, or Message and Invite to another job for a saved candidate); one meta line (section 11, Fact
  lines); the rating as a link to Scorecards, once someone scored them, and the tags (remove on hover, focus and tap, Add tag in
  place); then the stage rail (section 11). On a phone the rail shows the current stage with "2 of 6". The Application tab's
  Answers panel lists the job's questions, then the ones every application asks (AI use,
  adjustments to the process, future roles), each under the question as the apply form words it; only what the candidate
  declared is a row (W6). A question is muted body text and its answer medium weight, so the two never read as one; in a
  panel 48rem wide or more the question has a column up to 30rem wide, so most questions fit on one line, and in a
  narrower one the answer sits under it. The Files tab is the files panel, each file opening in the viewer (section 9), and its
  count is in the tab. Overview is four panels, and no tab's content repeats there (W7, S3): Needs you and Scorecards on the
  left, Interview and Latest message on the right. Needs you is one row per step: a round to-do tick (title only, no pills
  and no due date, which a task does not have) or the level mark with one sentence ("Confirm who they are on a short video call
  before interviewing."), and its link on the right. Nothing in it speaks of rejecting. A review or a
  requested scorecard has no tick; each one's link is Fill in, with the sheet open on the requested scorecard, or for a review on
  the scorecard for the stage the candidate is in (`?fill=stage`), which is where My tasks takes a review too. Scorecards is the
  rating and one line per person: name, scorecard, the recommendation in words and the date. Until a
  reviewer gives theirs, the Scorecards tab lists who gave the others and when, never what they said, and says "Submit yours to see the others." with a Fill in your scorecard button. Interview is
  there once one is offered. Latest message is the sender, "replied yesterday" and the first line of the body that says something, not a lone greeting ("Hi Taylor,").
  Built: Messages lists every email to the candidate, about any of their applications, as a history to scan, with the email
  composer in its own panel under them for people who can send. Each email is one compact row (Expandable row): the
  direction's icon (send or the reply arrow), the subject, who ("Sent by you", "Sent by an automation", "From" the
  address), the first line of the text that says something (as in Latest message, not a lone greeting), cut to the row,
  and how long ago. A candidate's reply sits under the email it
  answers, on a rule from that email's icon, oldest first; a reply whose email is not listed is a row of its own. Rows are
  newest first by their latest message, so a reply that came today brings its email to the top. A click opens a row in
  place to the email as it went, with Email and Plain text on its To or From line; Expand all and Collapse all in the
  panel header open or close every row, once there are two emails, each shown only while it would change something (A3). One sent before ScreenHire kept a copy of each
  email says only its subject was kept. A reply opens to the sender, the message and its files; a file shows once its
  malware scan allows it. A
  flagged address puts a status beside the name, a notice at the top of the tab and Allow email again, and takes the
  composer and the Email button away ([email](../architecture/email.md)).
- **Application tab forms:** after the Answers panel, a Forms panel: each form sent to the candidate is an Expandable row with its name and "Answered 2 days ago" or "Sent 3 days ago, waiting" ("its link has expired" past its link), opening to the answers, with Expand all or Collapse all when there are two or more (A9). An equality monitoring form's row opens to one line, that its answers count only toward totals in Reports. Send a form, an outline button in the panel's header for someone who may email the candidate while the application is open, opens a sheet of the job's forms, each with Send, Send again while it waits, or nothing once answered. Reports ends, where the deployment offers equality monitoring forms and for someone who may edit settings, with a panel per equality form: a table per question, "Fewer than 5" where a count is hidden. Those tables are fixed grids (L2): the answers keep the form's order, with No answer last, so they have no sort headers.
- **Templates:** one page with a tab per kind (Hiring, Scorecards, Forms, Emails). Each row says where it is used, and each
  editor has the Edit / Preview switch. A job keeps the copy of a template it started with. A built-in the team edited
  offers Reset to original in its menu.
  Built: the tab is in the address (`?tab=scorecards`), each with the count of templates that are not archived. The
  table panel's header is its title (Hiring templates) and the search, with no sentence about the tab (W9). The
  filters are All, In use (something uses it now), Drafts (hiring templates with a draft version, on the Hiring tab
  only) and Archived, each with its count. Stale templates are found by
  sorting Last used: a Not used in 6 months filter counted 0 beside rows that said Never, so it went (the owner's
  walk-through, 2026-11-21). Columns: Name with Built in, then for hiring Status (a quiet badge: Published, Draft,
  Published, draft open, or Archived; never a version number), then for hiring What it is for, for scorecards
  Questions, for emails the Subject with each variable as a muted word; Used by (jobs that are not closed, hiring
  templates, automations), blank while nothing uses it, and for hiring the jobs left on an older version ("3 jobs, 1 on
  an older version"); Last used, or Last sent; Last updated. Version and Draft columns gave way to Status (W7, the
  owner's walk-through, 2026-11-21). On a phone each list item carries the same Status badge and Used by, and ends with
  the same row menu. The row menu holds Duplicate, which makes "<name> (copy)" as the team's own and opens it, Archive or Restore for the team's own, and Reset to original
  for an edited built-in email, which confirms. The built-in rejection and follow-up questions emails have no
  Duplicate: their variables only their own actions fill. Archive does not confirm: Restore on the same screen undoes
  it (section 10). An archived template leaves the pickers and reads only until restored; jobs keep their copy, and
  automations keep sending an archived email. New template is a menu of the kinds the person's role may make; a hiring template starts from the Start from page (section 11, Start from card). Each
  editor's header has Used by as a count button, then its facts and Last updated with History. The hiring template's
  header facts are Status (the shown version's: Published, or Draft, unpublished changes, with its icon and colour),
  Live version (the version new jobs get, or Not published yet), Used by as a link-styled count button ("3 jobs, 1 on
  an older version") and Last updated; its Versions button and History open one sheet (section 11, Versions sheet), and
  its primary action says Publish. Under the title, a draft says what publishing does: "Publishing makes version 2 the
  version new jobs get. Your 1 job stays on version 1 until you update it." (only the first sentence for a template no
  job uses; "Your 3 jobs stay on the versions they have" when they differ). While a job is on an older version than the
  live one, the same line says "1 job uses an older version." with Review updates, a sheet listing each such job with
  See what changes (its pipeline page, which opens the differences) and Update, for someone who may edit jobs, which
  asks with the differences first because it replaces that job's own changes too. Email templates and scorecards show
  no version, so they have neither fact; a library form has the hiring template's Status, Live version, Used by (the
  hiring templates that copy it) and Last updated facts, Save draft then Publish, and after a publish that leaves templates
  on an older version a dialog that lists them, all ticked, says what updating does ("A template without a draft is
  published again with the new questions ... Jobs stay on their version until you update them.") and reports each one.
  The hiring template's Edit / Preview switch is in its Forms tab, the part
  someone else sees. The Forms tab is Form cards above
  one wide editor for the chosen form (`EditPreviewLayout` with `wide`), with the locked AI-use row last in the Apply
  form; the AI-use policy is set once for the organization in Verification and a job may override it in its own
  settings, never in the workflow. A built-in hiring template or
  scorecard reads only, says why and offers Copy (E3), with no History. Built-in emails stay editable in place. A
  publish or save says how far it reaches in the notice beside it ("Published version 2. Jobs created from now on
  start from it."). In the hiring template's Pipeline tab each stage's Edit stage, Add an automation, Choose scorecard and Remove
  stage are its card's More menu (L2); a hiring template that reads only (built in, archived, a published version, or one you may not edit) has the Stage flow there instead. An empty Emails tab offers New template as a
  link to the new-email page. A pop-out preview window with no editor behind it (a reload, an editor that closed) says
  so in one line instead of loading for ever.
- **Settings:** one sidebar item that opens the first settings page; every page has the whole menu, every setting in three groups, Organization, Careers site and Integrations, with search, and a breadcrumb of Settings, the group and the page (section 11). Email domain is under Organization, Hiring setup is Careers site, and Verification is its own sidebar item with its own tabs (section 16). Careers branding becomes Branding, and email
  templates move to Templates.
- **Team and Roles:** Team lists the members full width, each role by its name alone: a custom role has no Custom label
  (W6). Its title's row has Last updated and History (S8) for every change to the team: invitations sent and revoked,
  joins, role and job access changes, and removals. Invite opens a sheet with the email field, then the role as one line per role (a radio, its name and a one-line summary, cut to the width with the whole text in a tooltip, F5; built-in or custom), starting at Viewer, the least access, with a link to Compare roles, then one switch, Only some jobs, off at the start. No two-line cards. Roles is its own page on the list template: every role, the built-in ones marked Built in, with what
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
  team changes them (W6). For an Owner or Admin the choice is a line saying they see all jobs. Invite asks it as
  the one switch, Only some jobs, with a line under it saying what it means. On, they see only the jobs whose hiring
  team they are on. An invitation carries no job ids, so they are added to each team after they join. Someone who sees
  only their own jobs gets no switch, only a line saying so (A3). Team's second tab, Teams (`?tab=teams`), is the named
  teams on the list template: Name, a button that opens the team, People, their names cut to the cell with the whole in
  a tooltip (blank for nobody, L1; a phone's item says "2 people: ..."), and Last updated (T6), with a search past ten
  teams (S9); the row menu holds Edit team and Delete team, which confirms that jobs
  the team was added to keep their people. With none, the empty state says what a team is for, with New team. New team and
  Edit team are one sheet (`TeamSheet`, not in the style guide because it reads the organization's people): Name, then
  People as the hiring team sheet lists them with Add a person, then Cancel and Add team (or Save), and its description
  says changing a team later changes no job.
- **Tags (Settings, Organization):** the organization's tags on the list template, most used first: Tag, Candidates (a
  number) and Published templates, the hiring templates whose newest published version names the tag, as plain text
  ("Engineering (version 2)"), with no row link because a tag has no page. Past ten tags the panel has a search by
  name (S9). A phone has list items with the candidates as a fact pill. The row's More menu holds Rename, Merge into another tag (only with another tag to merge into) and
  Delete tag (only for a tag some candidate has), each opening its dialog once the menu closes. Rename is one field;
  a name another tag already has, in any case, is refused under the field with the way to combine them, Merge. Merge is
  a native select of the other tags. Delete is confirmed (section 10): "It comes off 4 candidates. Automations that add
  it keep adding it." A rename or a merge also changes the automations of every open job and template draft and the
  saved views that filter on the tag. Screen readers hear what was done; a published template that still names the old
  tag is a notice above the panel (E2), "Engineering still names "Sr" in its published version. Publish a new version to
  use the new name.", with Open Engineering when there is one, until the next change. Candidates change a moment later and the
  counts follow live. Edit settings and all jobs, as the API asks ([shared tags](../architecture/tags.md)).
- **Saved candidates:** the Saved tab on the Candidates page, under its title (G then S goes to `/candidates?list=saved`;
  `/saved` and `/talent-pool` redirect there), on the list template, in a panel titled Saved candidates, never counting its list, because the Saved tab has the count (W7), with Add candidate in the header as on the other two tabs: the people the team saved, with search, Saved from and Tag filters, and columns Candidate, Rating, Note,
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
  decision 11): a requested scorecard opens the candidate's Scorecards tab with the sheet open on it ("Asked by Avery Quinn on Nov 5, 2026."), a review ("Give your scorecard") opens it
  with the sheet on the scorecard for the candidate's current stage, and every other task the candidate's Overview, where Needs you ticks a to-do or a rejection review. A review and a requested
  scorecard are done when the scorecard is submitted, and the sheet then offers Open next task, a link to the person's next open task.
- **Reports:** a sidebar item after My tasks (G R) for every member, since every role sees jobs and candidates; job
  access narrows the numbers to the jobs a person sees. The Overview template (section 7): Dates, Job and the Compare
  periods switch in the header band (on by default), and Export CSV as the band's action (not offered while the report is empty, and its tooltip says it holds every panel for these dates and jobs). Then a row of
  five headline cards (Applications, Reached interview with its share, Offers, Hires, Median time to hire), each with its
  change from the previous period as an arrow and amount, its sentence in a tooltip; Open jobs as a table of Applications, In interview, Days open and Hires, because what is open now comes first (when a job opened is the tooltip on Days open, and a column of the export);
  then the Overview template's two columns (section 7): Applications and hires by week, Open jobs and Time to hire in the main
  column, and the Hiring funnel and Sources in the side column, so neither stretches across a large monitor, each row of the funnel with its count, its share of applicants and the share that moved on, the stage name
  linking to the candidates behind it and the biggest drop marked in its own row (no callout under the funnel); and Time to
  hire (median days in each stage, then by job) and Sources (applications, hires and the hire rate; the careers widget
  is Your website and a job board's apply link is that board). The overall median shows only in the headline
  (W7), and no panel has a sentence under its title (W9). No verification data: no levels, signals or counts by level, and
  no row for a verification stage (ADR-0004, `docs/architecture/reports.md`). With nothing in the dates the page shows one
  empty card, not a panel with an empty header; an organization with no jobs yet is told what fills Reports and linked to
  Jobs, instead of being offered a longer range that cannot help.
- **Views:** the sidebar group is headed Views, so Saved means saved candidates only. Each shows a short name
  (To verify), so nothing is cut off, and no count (T4 asks for one on a control that counts something to do, and a view
  only filters). Each built-in view has its own icon, the same in the full sidebar and the icon rail, so the rail tells them
  apart (the owner's walk-through, 2026-11-21): To review the Review mark (`eye`), To verify the Verify mark (`id-card`),
  Overdue the Overdue badge's clock (`clock-alert`), New today `calendar-plus`, Not screened `inbox` and Interviewing
  `messages-square`. They are drawn uncoloured, as navigation, not as a status (I2). A view people save shows list-filter
  (users when a teammate shared it and the person added it) in the full sidebar; in the rail it is the first letter of its
  name in a small rounded square, with its name in the tooltip. A hairline divider in the rail sets the views apart from the
  sections above them.
  Sharing is an offer: a shared view is in nobody else's sidebar until they add it from Browse team views, an item in the
  Views heading's menu (in the rail, an icon above Settings, outside the list that scrolls). The Team views sheet lists each shared view with who shared it, its filters in
  one muted line, "Shows 3 candidates now" (the one place a view carries a count: it is the sheet's reading, not a sidebar
  badge, so T4 and I2 still hold for the rows), a muted New pill for a view shared since the person last opened it and not
  in their sidebar when it opened, and outline Preview and Add (or "In your sidebar" and an outline Remove): every row has them, so
  none is a primary (A1). A row's buttons wait while its request runs, and a failure shows on the row that failed. With
  nothing shared the sheet says where to share one (the view's menu in the sidebar). The name wraps rather than truncating. A teammate sharing a view is no toast and no bell line. The short name is also the candidates page's title for the view. A Save view
  outline button ends the filter row of the Candidates table and of a job's table once the filters or sort differ from the
  view the table is on; its dialog asks for a name and Share with the team, off by default (`docs/architecture/saved-views.md`).
- **API keys and webhooks:** each page says what it is for, how to call it and where the reference is. The API reference
  is served in production. API keys has Last updated and History on its title's row: each key created and revoked, with
  its name, access and who. A webhook endpoint's page has Last updated and History (its address by host, its events, its
  switch and each rotation of its secret) beside Secret rotated (T6).
- **Account pages (Your account):** what belongs to the person, whatever organization they work in, is under `/account`,
  which the person's menu links to as Your account and which opens Profile. It is Settings' twin for the person (the owner's
  walk-through, 2026-11-21): the same layout, with the Your account menu (`AccountMenu`, the Settings menu's 200px column from
  `lg` and one sideways row below it, five entries, no groups and no search) left of each page, no permission, and a breadcrumb
  that starts with Account. Its pages, in order:
  - **Profile**, on the form template: Photo (the Drop area, A12, cropped to a square in the browser, with Remove photo), then
    Name, Email (a value, not an input, E3, that wraps rather than being cut off, saying sign-in uses it), Time zone, Language (how dates and numbers are written; the interface is
    English) and Date format, and one Save.
  - **Notifications**, on the form template: a Toasts and jobs panel (Jobs as a choice toggle, My jobs or Everything I can see;
    Pause toasts; Close toasts automatically), then What you hear about, a table of a row per kind (its name and one muted
    line) and the columns In the app (a switch), Email (Instantly, Daily digest or Off) and Slack (a switch, a column shown only
    once the person's Slack account is linked; before that one line links to Calendar and accounts). A cell with nothing to choose
    is a muted dash with words for a screen reader, never a disabled control (A3). Teammates' moves and notes has Choose kinds,
    an outline button beside its switch, which opens a row per kind of toast inside it. When the panel is narrower than the
    table needs (672px, read from the panel's own width, so beside the account menu on a laptop as well as on a phone), each
    kind is a card with the three choices stacked, and Choose kinds opens the same switches inside the card. The Toasts and jobs panel keeps the
    form column; the table fills the width (S6). One Save in the table panel's footer.
  - **Calendar and accounts**, always shown (the address stays `/account/connected`): Interview hours (Set my interview hours,
    then the Weekly hours editor, Time between interviews, Most interviews in a day and Time zone, with one Save hours in outline, because the Calendar panel's Connect is the page's primary), then
    ScreenHire calendar (one line saying the link shows times and jobs, never a candidate's name; Make my link, then the
    private link read only with Copy link, Reset link behind a dialog that says the old one stops working, and Turn off), then
    a Calendar panel and a Slack panel where there is one to connect. Connecting comes back here and says how it went in that panel.
  - **Sign-in and security**: How you sign in (the email link, the providers the person has used, and how many passkeys,
    with Manage passkeys, an outline link, in the panel's footer), and Sessions, whose one outline button Sign out everywhere confirms in a dialog (A2). There is
    no list of sessions, because ScreenHire keeps none.
  - **Preferences**: On this device (Theme as a choice toggle of Light, Dark and System; Reduce motion) and How you work
    (Single-key shortcuts; Confirm moves that send emails or create tasks). Each switch applies at once and is announced.
  Passkeys (`/account/passkeys`) is a page under Sign-in and security, whose entry stays current there, on the List template: a table of Name, Added and
  Last used (newest use first; a passkey never used says Never used, a value and not a blank, L1), a row
  menu with Remove passkey, and Add a passkey in the title's row, in the bar on a phone. Adding names the passkey in a
  dialog (pre-filled with the device), then the browser asks. The limit is said in the panel's footer only when two or
  fewer are left; at the limit Add is off and the sentence beside it says why. A browser without WebAuthn gets a notice
  in place of Add. The API refuses to remove the last way to sign in, and the confirm dialog shows its reason.
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
| Real people | The three levels, each with one plain line, beside what ScreenHire never does |
| Never guessing | The team's Activity feed (`ActivityFeed`) beside the candidate's own page, in a stacked layout |
| Automate anything | One automation written out as When, If, Then, then the line on Try it |
| Integrations | An assistant asking before it emails a candidate (`AssistantAsks`), the picture first |
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
| `LevelChip` | A level as the dashboard's `VerificationChip` draws it: one word (Clear, Review, Verify), its icon, and the long name (No concerns, Review suggested, Verification suggested) for screen readers. Clear is quiet; Review and Verify are filled. `aiHelp` is Some signs of AI help, shown apart from the levels as information |
| `ActivityFeed`, `AssistantAsks` | Product moments in markup, in the product's own words: the bell's Activity tab, and an AI assistant asking before it emails a candidate (the MCP server's confirmation). The page captions them as fictional |
| `SourcedNumber` | A number with its claim and source link |
| `PlanRows` | The plans as rows, read from the plan catalogue (`packages/plans`): name and open-job limit, then the price. A plan with no price shows no amount, and one with no limit shows none. On the pricing page, `includes` adds what every plan includes under the rows as a line of ticks, the catalogue's `included`, the same list the dashboard's Billing page shows |
| `QuestionsList` | Questions as a list with rules between them |
| `ClosingBand` | The dark band that ends a page. Without an `href` its call to action goes where the header's Start free trial goes |
| `PageIntro` | The opening of a compare, law, blog or 404 page: the `h1` at `text-section`, a lead and a slot for dates or a notice. White, or `tone="dark"` for a page that sells, with the header's `headerTone="dark"` |
| `CtaLink` | The call to action (22.9) |

A claim about a competitor is `Claim`: the words, the numbers of the sources it rests on (linked to `SourceList`'s anchors) and the day the vendor's pages were read. A compare page shows no claim without them. Index pages take a different shape each: compare a heading beside a ruled list, laws a ruled table, blog a date column and a larger first post.

A status on a marketing page is `LevelChip` (or `StatusBadge` for a state), never a chip built from `bg-status-*` (T1).

### 22.11 Motion

At most one deliberate moment on a page, and none when the person asks for reduced motion (`motion-safe:`). No fade-up on
every section, no looping animation, and a transition lists its properties.
