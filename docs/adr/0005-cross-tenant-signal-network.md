---
type: Decision
title: "ADR-0005: Cross-employer Signal Network, pseudonymised, opt-in, legal-gated"
description: "The cross-employer Signal Network, accepted with a gate: keyed hashes of hard identifiers only, counts shown at three or more organisations, 90-day retention, opt in to contribute before querying, step-up verification only, and a feature flag that stays off until legal review."
tags: [adr, integrity, signal-network, legal]
---
# ADR-0005: Cross-employer Signal Network, pseudonymised, opt-in, legal-gated

- **Status:** Accepted with gate (2026-10-18)
- **Deciders:** Owner
- **Design:** `docs/architecture/integrity-engine.md` §6

## Context
Fraud rings and auto-apply farms hit many employers at once; per-employer detection misses them. The owner wants
cross-tenant detection **if it can be de-identified / safely deconstructed**. Research: cross-employer use makes
ScreenHire a GDPR **controller** for that processing, and network outputs might be argued to be FCRA consumer reports.

## Decision
- Build the network in the MVP architecture, **behind a feature flag**.
- Share only **keyed HMAC-SHA256** of normalised hard identifiers (email, phone, device-fingerprint hash, resume file
  hash) and **SimHash buckets** of answer text — never names, content, outcomes or which employers.
- Queries return **counts only** ("seen in N applications across M organisations in 30 days"), shown only when
  **M ≥ 3**.
- **90-day rolling retention**; KMS-held key, rotated.
- Tenants must **opt in to contribute** in order to **query**.
- Results may only trigger **step-up verification** or reviewer attention — never a decision input.

## Legal reality (not "de-identified" in the GDPR sense)
Hashed identifiers remain personal data for ScreenHire because we hold the key. ScreenHire acts as controller for the
network: own legitimate-interest assessment (fraud prevention, Recital 47), Art. 14 notice, DPIA. US FCRA exposure to be
assessed by counsel.

## Gate
**Production enablement requires legal review + DPIA sign-off.** Until then the flag stays off in all regions.

## Consequences
- Strong differentiator against application farms, at a cost of controller obligations and legal spend before launch.
- The data model and pipelines are built once; enabling is a configuration + legal milestone, not a re-architecture.
