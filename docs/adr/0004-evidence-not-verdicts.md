---
type: Decision
title: "ADR-0004: Integrity output is evidence, not verdicts"
description: "The integrity non-negotiables: two categorical assessments with reason codes and counter-evidence, no numeric fraud score, never auto-reject on integrity, two signal categories before a level rises, no emotion or biometric inference, and notice before collection."
tags: [adr, integrity, legal]
---
# ADR-0004: Integrity output is evidence, not verdicts

- **Status:** Accepted (2026-10-18)
- **Deciders:** Owner
- **Research:** `docs/research/legal.md`, `docs/research/competitors.md` · **Design:** `docs/architecture/verification-engine.md`

## Context
The owner wants every signal from first visit to submitted questionnaire used to flag whether an applicant is human,
while staying privacy-safe and lawful in the US + UK/EU. Research shows:
- Opaque scores and automated rejection trigger GDPR Art. 22 / UK Arts. 22A–D, NYC LL144, Colorado SB 26-189, CPPA
  ADMT rules and the EU AI Act high-risk regime (from 2 Dec 2027).
- AI-text detectors are unreliable and biased against non-native writers.
- The EU AI Act bans emotion recognition in recruitment (since 2 Feb 2025), and its biometric definition reaches typing
  patterns.
- Telemetry false-positives hit people using assistive technology (ADA / FEHA exposure).

## Decision
1. Two separate assessments — **Authenticity** and **AI Assistance** — each a categorical level with **reason codes,
   evidence and counter-evidence**. No numeric "fraud %" or "% AI-written".
2. **Never auto-reject on integrity signals.** The workflow engine enforces this at definition-validation time
   (integrity-derived conditions may only route, tag, assign or request verification).
3. A level above the lowest needs **≥ 2 independent signal categories**; **telemetry alone never raises a level**.
4. **No emotion / intent / honesty / personality inference** anywhere. **No biometric templates.** Biometrics only in the
   optional, consented ID step, stored as pass/fail.
5. **Notice before collection.** Browsing collects only server/security logs; fingerprinting and telemetry begin at
   application start, after notice, with an accommodation toggle that disables telemetry without penalty.
6. Levels come from a **versioned, documented ruleset**; every assessment records ruleset, analyzer, prompt and model
   versions for audit and bias testing.

## Consequences
- Compliance becomes a marketing asset ("explainable, lawful fraud detection") rather than a sales blocker.
- Some detection power is traded for defensibility; step-up verification recovers it for high-risk cases.
- Requires a candidate-facing "why we asked you to verify" page and an appeal/human-review flow.
