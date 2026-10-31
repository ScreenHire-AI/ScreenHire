---
type: Guide
title: "ScreenHire documentation"
description: "Where to start reading by audience (trying the demo, building a feature, changing UI, reviewing, planning), what ScreenHire is, who it is for and why it exists, and a pointer to index.md, the map of every concept."
tags: [docs, start-here, product]
---
# ScreenHire documentation

ScreenHire is a hiring platform (ATS) for SMBs first — growing to mid-market, enterprise and agencies — in the US and
UK/EU. It sets itself apart by **detecting AI-generated, fraudulent and bot applicants with explainable, lawful
evidence**, by **per-job workflows built from user-defined templates**, by a **public API**, and by **in-house
one-click job posting and teardown**.

> Positioning (from research): *a verification layer from first click to first day that recruiters and candidates can
> both understand, at GoHire prices.*

**Why it exists:** the owner's team hires on gohire.io (mainly .NET developer roles), and fake / AI-generated
candidates get through its screening. **Positioning (owner, 2026-09-23):** a simple, intuitive, affordable tracker for
small hiring teams in the style of GoHire or BambooHR — not an enterprise fraud platform like Sardine or Ashby.
Detection runs in the background (custom models possible later). A public job board alongside the tracker is a
possible later addition. **Domain:** `screenhire.io` planned (not yet bought; `.com` is taken) — the base domain is
configuration everywhere (ADR-0003).

## Finding a doc

[index.md](index.md) is the map. Each folder's `index.md` lists its docs with one line on what only that doc holds, so
read the lines and open the one doc that answers the question. [log.md](log.md) records changes to the layout of these
docs. The layout follows the Open Knowledge Format ([ADR-0012](adr/0012-docs-open-knowledge-format.md)).

## Start here

| You want to | Start with |
|---|---|
| Run the product and give the five-minute demo | [Run ScreenHire locally](operations/local-demo.md) |
| Know what is built, what is faked and what the owner still has to do | [MVP status](status.md) |
| Know what comes next | [Roadmap](roadmap.md) |
| Change a subsystem (verification, workflows, distribution, sign-in, messaging, email, the apply flow) | [Architecture](architecture/index.md), then the ADRs it names |
| Build or review a screen | [Design system](design/design-system.md), section 2 is the review checklist |
| Know why a technology or rule was chosen | [Decisions](adr/index.md) and the [research](research/index.md) behind them |
| Write or run tests, or move a task through the board | [Process](process/index.md) |
