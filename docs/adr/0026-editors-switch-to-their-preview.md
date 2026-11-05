---
type: Decision
title: "ADR-0026: Editors switch to their preview"
description: "An editor and its preview no longer share the screen: every editor uses the Edit / Preview switch at every width, so the editor has the room and the preview is one press away."
tags: [adr, design, dashboard]
---
# ADR-0026: Editors switch to their preview

- **Status:** Accepted (2026-11-05)
- **Deciders:** Owner
- **Amends:** ADR-0017 decision 17 (an editor and its preview share the screen where both fit)
- **Detail:** `docs/design/design-system.md` A8

## Context

ADR-0017 put the result beside the editor on a wide screen, so a wide screen was not half empty and an edit showed at
once. In review the owner found the two compete for attention and take room from the editing, on questionnaires and on
Branding alike. Someone opening an editor came to change something.

## Decision

Every editor shows one thing at a time, at every width, with the Edit / Preview switch at the top. The window icon beside
it opens the preview in a second window for someone who wants both, and that window follows the edits.

## Consequences

An edit no longer shows at once unless the person opens the preview or its window. The layout has one shape, so
`EditPreviewLayout` needs no container query, and its preview takes the full width.
