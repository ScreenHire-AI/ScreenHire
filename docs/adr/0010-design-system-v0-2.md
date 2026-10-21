# ADR-0010: Design system v0.2, simple and approachable

- **Status:** Accepted (2026-10-20)
- **Deciders:** Owner
- **Amends:** ADR-0009
- **Spec:** `docs/design/design-system.md` (the one source of truth)

## Context

The MVP dashboard followed ADR-0009's brief, "dense, keyboard-first". In review the owner found it stuffy:

- a 13.5px root shrank every Tailwind size, leaving body text near 12px and labels at 10.5px in monospace capitals;
- sections floated on the page background, so titles and columns misaligned;
- read-only data was drawn as disabled forms, files did not look like files, and a shortcut bar sat under every
  table.

The positioning in `docs/README.md` asks for a simple, approachable tracker in the style of GoHire or BambooHR. The
owner reviewed a canvas of today's screens against proposed ones and approved the decisions below.

## Decision

1. **Direction:** simple, approachable, fast. This replaces ADR-0009's "dense, keyboard-first" brief. Its palette,
   fonts, component libraries, single token source and employer branding stand.
2. **Keep it simple** is the first principle: the fewest words and elements, and features that do not exist yet are
   hidden rather than shown disabled.
3. **Type:** the browser's 16px root, 14px body, a 12px floor, sentence case, monospace only for codes and keys.
4. **Layout:** content at most 1280px wide; detail pages are a main column and a 320px side column; page templates for
   list, detail, read-only, form and builder screens.
5. **Layers:** page, surface, inset, raised and overlay, after Material 3's elevation, IBM Carbon's layering and
   Atlassian's surfaces. Every section is a panel with its title inside.
6. **Status first,** facts as edged pills, no dot separators.
7. **Controls never wrap.**
8. **Time:** time ago in text, dates in tables, the browser's locale (US English by default).
9. **Counts and steps** never share a shape.
10. **Fluid:** phone, tablet, laptop and desktop size classes on Tailwind's breakpoints, with shadcn-svelte's
    Sidebar as a full sidebar, an icon rail or a sheet.

The integrity non-negotiables are unchanged: every result keeps its suggested next step, calm results show it as one
quiet line, and there is no score and no reject control.

## Consequences

- `docs/design/design-system.md` v0.2 holds the detail and a review checklist. CLAUDE.md, the rules, the page skills
  and the `design-review` agent point to it; a design change is made there once.
- The dashboard needs a foundation change (the root size, type tokens, moving every hard-coded size onto them) before
  its pages move to the new templates.
- Tests that assert the disabled "Request verification" control or the boxed next step change with the pages that
  drop them.
