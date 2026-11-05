---
type: Decision
title: "ADR-0025: The pipeline tracker is a bar of stages"
description: "The pipeline tracker in a candidate's header is a bordered bar of stages with the current stage as a solid block, in place of a row of dots, so a person's stage is the first thing read."
tags: [adr, design, candidate, dashboard]
---
# ADR-0025: The pipeline tracker is a bar of stages

- **Status:** Accepted (2026-11-05)
- **Deciders:** Owner
- **Amends:** ADR-0023 decision 3 (the tracker's dots), and the exception it made to square steps
- **Detail:** `docs/design/design-system.md` T4 and section 11

## Context

ADR-0023 drew the tracker as a thin row of small dots with their names. In review the owner found it floated on the
page and had to be scanned to find the candidate's stage, which is the first thing a person opens the page to learn.

## Decision

The tracker is a bordered bar with one block per stage, the width of the header. The current stage is a solid block in
the brand colour, and the stage a candidate left from is solid and dark. Passed stages are tinted, with a check and the
date. Later stages are plain. Everything else ADR-0023 decided about the tracker stands: what it reads, the words in each
stage, and showing one stage in a narrow container.

## Consequences

The tracker is the strongest thing in the header after the name, so it stays the only solid colour block there. It no
longer needs the exception to square steps, because it is a bar and not a set of markers.
