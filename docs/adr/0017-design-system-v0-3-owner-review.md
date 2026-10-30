---
type: Decision
title: "ADR-0017: Design system v0.3 changes from the owner's review of the build"
description: "What the owner's review of the running v0.3 dashboard changed: a record's facts sit in its header instead of a details rail, a tab's state is always a count, a field's actions share the row of its message, checkbox edges meet 3:1, and a candidate's integrity level is their status beside the name."
tags: [adr, design-system, ui]
---
# ADR-0017: Design system v0.3 changes from the owner's review of the build

- **Status:** Accepted (2026-10-26)
- **Deciders:** Owner
- **Amends:** ADR-0013 (design system v0.3), decision 2
- **Detail:** `docs/design/design-system.md`, S7, E2, F6, sections 7, 8, 11, 16 and 22

## Context

The owner went through the running v0.3 dashboard at full width, on a 1920px screen. Pages fill the window (ADR-0013
decision 2), so the candidate's details rail sat at the window's far edge, out of the line of reading. Other details
read as noise or hid an action: a bare reason count after an integrity chip, a dot on the Integrity tab, a faint
checkbox and a Save button pushed to its own row by a validation message, and reasons that expanded with no sign they
could.

## Decision

1. **A record's facts sit in its header band, not a rail.** The candidate's contact goes on the line under the name
   and Stage, Job, Applied, Source and Rating go in the facts row, as a job's facts do. Overview becomes a grid of
   panels: next steps and activity on the left, files, tags and answers on the right, one column when narrow. The
   details rail, its layout and its width token go. A webhook endpoint's events become a panel above its deliveries.
2. **A tab's state is a count, in the same grey on every tab.** A state that needs a look is the record's status beside
   its title. A coloured count, a dot or a lone icon on a tab read as noise next to the other tabs.
3. **A candidate's integrity level is their status,** a badge beside the name on every tab when it needs a look. It
   stays a badge, never a link (A5); Next steps links to the reasons.
4. **A field's message sits under it, and the field's own actions share that row** (F6).
5. **A checkbox's edge meets 3:1 against white** (WCAG 1.4.11, already in section 17), and a task's tick is round with
   a 2px edge, so it reads as something to tick.
6. **No bare reason count after an integrity chip.** Screen readers still hear it; the reasons live on the Integrity
   tab (W6).
7. **Anything that opens in place shows a chevron** at the end of its row, turning up when open (A9). The integrity
   reasons expanded with no sign that they could.
8. **Say it once per screen** (W7). The candidate's integrity level showed in the header and again in the Integrity
   panel, and the signals' count on the tab and again as a heading's count; the header and the tab keep them.
9. **Each integrity assessment has one skeleton** (section 16): name and level in the header, a facts row, "Why" and
   "Suggested next step" as labelled lines, reasons grouped under the check that found them, then Dismiss. The
   summary cards and the separate reasons list said the same thing twice and did not connect.
10. **Never ask people to type a code** (W8): the job form's currency is picked by name and symbol, the closing date
    is the person's own day, labelled plainly, and the countries field's two-letter codes give way to a country
    picker. A hiring template's draft has its own column, not a badge beside the version.
11. **A task is ticked off where its work is done.** My tasks links each row to the candidate, and Next steps there
    has the checkbox. A task that is a piece of work (a requested scorecard) will complete when that work is
    submitted. Ticking a list of to-dos away from the work let people mark work done without doing it.
12. **The MCP server has its own Settings page**, named as people know it, with the values any MCP client needs (URL,
    transport, header) and one generic config, never one client's setup steps; the key stays on API keys.
13. **Less text: a label only where the value cannot explain itself** (W9, W10). A record's header is a meta line of
    icons and values; the integrity panel leads with its next step in a callout, says a calm result in one line and
    puts when it was assessed and under which ruleset last. Small labels over every value made the candidate page
    hard to scan. The next step stays on calm results too, because every result carries one (CLAUDE.md
    non-negotiable 1).
14. **Candidates filter by several jobs and stages at once**, in a filter row under the list's header, each choice a
    removable pill. Stages match by name across jobs.
15. **Moving between sibling pages never shifts the frame** (S8). Settings headers are one row of one height: Updated,
    who and History sit on the title's row, and a page's sentence goes in its panel. The Live indicator moves to the
    top bar, where every page shows it, with a soft pulse while connected.
16. **The feature people see is Verification, not Integrity.** "Integrity" was too broad a name for what the checks
    do. The candidate's tab, the Settings page (`/settings/verification`, the old address redirects), the Candidates
    filter, the workflow trigger and facts, the role permission and the rejection guardrail's message all say
    Verification; `?tab=integrity` still opens the tab. Code, API routes, webhook events (`integrity.assessed`),
    configuration keys and tables keep "integrity" until a rename task changes them before launch, while no
    integration depends on them yet. The authenticity level "Verification suggested" keeps its name: under the
    Verification tab it reads as what the tab suggests.
17. **An editor and its preview share the screen where both fit** (A8 amended). Scorecard templates, email templates
    and questionnaires show the result beside the editor from about a 1,600px window, so a wide screen is not half
    empty and an edit shows at once; narrower, the Edit / Preview switch stays. An empty table cell is blank, its
    word for screen readers only (L1).

## Consequences

- The job, candidate and Templates pages' tabs are 44px tall, as the canvas drew them.
- `DetailLayout`, `DetailsRail`, `DetailsRailSection` and `--container-rail` are gone, with the catalogue's Details
  rail row.
