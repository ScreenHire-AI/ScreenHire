---
type: Decision
title: "ADR-0016: Design system v0.3 amendments from the full review"
description: "What the first whole-dashboard design review settled in ADR-0013: a page's action that opens another page is a button rendered as a link, a record whose row holds a secret has no change history, a form keeps its actions at its end on a phone, and a table wider than its panel beside the full sidebar scrolls inside it."
tags: [adr, design-system, ui]
---
# ADR-0016: Design system v0.3 amendments from the full review

- **Status:** Accepted (2026-10-25)
- **Deciders:** Owner
- **Amends:** ADR-0013 (design system v0.3), decisions 5 and 7
- **Detail:** `docs/design/design-system.md`, A5, T6 and section 8

## Context

The dashboard was built screen by screen to v0.3. The first `design-review` run over the whole dashboard found rules in
ADR-0013 that the rest of the system contradicts, or that one kind of record cannot meet. Each finding needed a
decision the system did not record, and the review's brief says to follow the closest rule and write it down.

## Decision

1. **A page's action that opens another page is a button.** ADR-0013 says a button never navigates, yet the system
   asks for "Edit as the primary action" (section 7), Back on a phone's detail page (section 8), a full page to create
   (section 9) and Cancel and Save at the end of a form. Those actions start the work, so they keep their border or
   fill, and they render as links (`<a>`) so the browser can open them in a new tab. A menu item that opens a page is
   a link inside the menu. Everything else stays as ADR-0013 says: a link never opens a sheet, a button never navigates
   in place of acting, and names in tables, records and text are underlined links.
2. **A record whose row holds a secret has no change history.** Change history reads only the entities on its
   allow-list, and nothing that holds a secret is on it (`docs/architecture/change-history.md`). A webhook endpoint's
   row holds its signing secret, so its header shows when it was added and when its secret was last rotated instead
   of Last updated with History. Splitting the secret into its own row is the way to give it a history later.
3. **A form keeps its actions at its end on a phone.** Section 8 puts a phone's primary action in a bar at the bottom;
   section 7 puts a form's actions at its end. On a form page the second wins: Cancel and the primary follow the
   fields they act on.
4. **A table wider than its panel beside the full sidebar scrolls inside it.** At 1024px the full sidebar leaves about
   776px, less than a candidate table with its integrity columns needs. Such a table scrolls inside its panel with its
   name column stuck, as on a tablet, and the page never scrolls sideways (F1). The same room lets a header's facts
   wrap onto a second line.

## Consequences

- `design-review` holds these screens to the amended rules from its next run, because it reads section 2 each time.
- A webhook endpoint's history is a roadmap item, not a review finding.
