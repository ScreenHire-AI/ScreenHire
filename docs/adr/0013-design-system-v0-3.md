---
type: Decision
title: "ADR-0013: Design system v0.3, the work area first"
description: "Design system v0.3, which amends v0.2: pages that fill the window, status icons instead of shapes, panels with a soft shadow, tabs and a details rail on detail pages, links that go somewhere and buttons that act, tables that sort, counts instead of lists, every fact in one home, and a candidate rating averaged from people's scorecards."
tags: [adr, design-system]
---
# ADR-0013: Design system v0.3, the work area first

- **Status:** Accepted (2026-10-25)
- **Deciders:** Owner
- **Amends:** ADR-0010
- **Spec:** `docs/design/design-system.md` (the one source of truth)

## Context

The MVP dashboard was built to v0.2 (ADR-0010). Using it on real screens showed:

- wide screens left half the window empty, because content stopped at 1280px;
- the job page was crowded: a side column, a scrolling strip of stage tabs and a stacked authenticity bar all beside
  the table;
- the candidate page showed everything at once, and its sections moved from one candidate to the next;
- the status shapes (circle, diamond, square) were hard to tell apart and meant nothing on their own;
- templates, email and roles had no clear default, so a new team had to set things up before it could start.

The owner reviewed a canvas of proposed screens ("ScreenHire design v0.3 review") over several rounds. Each round's
feedback became a rule on the canvas, and the owner approved the result. This ADR records those decisions;
`docs/design/design-system.md` holds the detail.

## Decision

1. **Works out of the box, customizable when needed.** Every area ships a default that needs no setup: templates,
   email, branding, roles. Customizing is opt-in, reversible and shown next to the default.
2. **The work area first.** Pages fill the window; only text keeps a measure (72 characters for long paragraphs,
   688px for forms). No side column beside a table or a board. Detail pages hold their content in tabs in a fixed
   order, with a 340px details rail that never moves.
3. **Status icons replace shapes.** Each status has its own Lucide icon, its colour and its label. Waiting states
   (Sent, Queued, Scanning) turn grey because nobody needs to act. AI assistance gets an icon too, in its information
   colour.
4. **Panels lift off the page** with a soft shadow, and a summary sits on a warm grey band as shadcn Cards.
5. **Links go somewhere, buttons do something.** A link never opens a sheet and a button never navigates. A button
   with a text label always has a border or a fill.
6. **Fewest words.** A control's label is one to three words, an icon button keeps its label in a tooltip, and no
   sentence repeats the screen. Notes about a mock stay on the canvas.
7. **Headers:** a title, its status badges, then a facts row. Anything people edit says who updated it last, with a
   History sheet of every change.
8. **Tables:** two text styles and one line per cell; every column sorts, with a sensible default per table.
9. **Design for the count:** a list that can grow shows a count, and in a header the count opens a sheet listing the
   items.
10. **Every fact has one home:** mark the exception, never the default (Built in, never Custom); a state goes in the
    column it describes; actions go in the menu; detail goes in a tooltip.
11. **Editors and previews never share the screen.** An Edit / Preview switch, with a pop-out window for a second
    screen.
12. **One form builder** for application questions, follow-up questionnaires and scorecards, modelled on Google Forms.
13. **A candidate rating** from scorecards: rating questions average into one to five stars.

## What v0.3 reverses from v0.2

- **Averaging scorecards.** v0.2 said scorecard ratings are labels and nothing totals or averages them. v0.3 averages
  rating questions into the candidate's stars, because the owner wants one way to compare candidates on the job's
  own criteria. It stays clear of ADR-0004. The average is of people's judgements about the work, never integrity
  results, AI signals or the stage. A reviewer sees it only after submitting their own scorecard, so it cannot anchor
  them. No automation acts on it. The integrity non-negotiables are unchanged: no integrity score, no reject control.
- **Shadows.** v0.2 kept shadows to raised and overlay layers. v0.3 gives panels and cards a soft surface shadow,
  because white on warm grey reads as a layer and a hairline alone did not.
- **Width.** The 1280px page cap, the 1536px wide page and the 320px side column go.
- **Inline files.** A page no longer embeds a resume preview: it shows a file row, and the file opens full window.
- **Labels.** "Verification recommended" becomes "Verification suggested", mirroring "Review suggested". An infected
  upload is an "Unsafe file". AI assistance reads No signs, Some signs or Strong signs of AI help.
- **Facts as pills.** Header facts are a labelled facts row, and table facts are plain text in their own column.

## Options considered and turned down

- **Labels on rating scales** (Poor to Excellent, Met or Not met). Plain number scales with Low and High under their
  ends say the same in fewer words.
- **A "Counts toward the rating" switch** on each question. A Rating group in the answer menu makes the choice when the
  question is written.
- **Marking custom things "Custom".** Marking only the built-ins says the same with one badge fewer.
- **A second line under a row's name** for drafts or edits. It doubled row height and made people ask what it meant.
- **Links that open sheets,** such as History. Something that acts is a button.

## Consequences

- `docs/design/design-system.md` v0.3 holds the detail. Its checklist ids stay: v0.2 checks were amended in place and
  new ones added (S7, A6 to A8, W6, T6, E2, E3, L1, L2). Sections 21 and 22 list the components and screens the build
  adds; a component moves into section 11 when the style guide shows it, because the style guide's test reads
  section 11.
- The `design-review` agent enforces v0.3 from its next run. Until the dashboard is rebuilt, a change to an existing
  screen can draw findings for the 1280px cap, status shapes or a side column; fix them in the screen you touch, or
  name the build task that will.
- The build adds the panel shadow token and the status icons to `packages/ui` and `#lib/integrity.ts`, replaces
  `max-w-page`, `page-wide` and `basis-side`, and moves the integrity panel to a candidate tab.
- The scorecard model gains answer kinds and rating questions; the apply form asks for first and last name, so
  emails can say `firstName`.
