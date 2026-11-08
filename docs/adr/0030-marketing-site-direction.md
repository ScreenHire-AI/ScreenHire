---
type: Decision
title: "ADR-0030: Marketing site direction"
description: "How the marketing site looks and what it sells: five selling points in the owner's order told as pain, promise and proof, dark and white bands with the real product as the one bold element, a ban on generated-page decoration, the rules written in the design system and enforced by its review agent, and no third-party design skill installed."
tags: [adr, design, marketing]
---
# ADR-0030: Marketing site direction

- **Status:** Accepted (2026-11-08)
- **Deciders:** Owner
- **Amends:** ADR-0010 (the design system's one source of truth gains a marketing section)
- **Detail:** `docs/design/design-system.md` section 22, `docs/design/marketing-site.md`

## Context

The marketing site was built to the dashboard's panel-and-kicker look and never mentioned most of what ScreenHire does.
The owner wants it to pop, stay clean and clear, and never look like another AI SaaS page. The look comes from the
owner's "B + C, second pass" board. Anthropic's frontend-design skill (anthropics/skills, Apache-2.0) and Impeccable's
anti-pattern rules (pbakaus/impeccable, Apache-2.0) describe what makes a page look generated, and Vercel's Web Interface
Guidelines (vercel-labs/web-interface-guidelines, MIT) list checks a page should pass.

## Decision

1. The site sells five things in this order: real people, never guessing, automate anything, integrations, built your
   way. Every page, section and headline serves one, told as the buyer's pain, our promise, then the proof.
2. A page is bands: a dark hero band, a white page, a warm alternate band, a dark pricing band and a dark close. The real
   product, shown big and sharp, is the one bold element. Everything around it stays quiet.
3. Generated-page decoration is banned (gradient washes, an icon tile or a small label above every heading, all-caps,
   middle-dot meta strings, 01 / 02 markers, a fade-up on every section, cards inside cards, a bright accent on near-black).
4. The rules live in the design system, which stays the one source of truth. The `design-review` agent enforces them
   through checklist ids M1 to M12, and `public-style.test.ts` fails on what a search can prove.
5. The site says that checks happen, what kind of thing they look at in plain words, and that a person decides. It never
   shows reason codes, rule names, thresholds or how a check is computed, never a score or a percentage of AI, and never
   says ScreenHire rejects anyone. Every claim names a feature that is built, and every number has a source the page links.
6. No third-party design skill is installed: no Impeccable plugin or hook, and no frontend-design skill in `.claude`.
   Their rules are written into the design system in our own words, with credit.

## Consequences

Existing pages keep their old look until the page tasks restyle them. The new bans apply to every marketing file that
is not on the legacy list in `public-style.test.ts`, and a file leaves the list when it stops offending. The messaging
doc is where page tasks take their copy, so a claim changes in one place.
