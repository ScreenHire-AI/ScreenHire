---
type: Decision
title: "ADR-0019: Integrity is renamed Verification in code, the API, events, configuration and tables"
description: "Before launch, every name that said integrity says verification: API routes and schemas, the verification.assessed webhook event, the realtime event, ScreenHire:Verification configuration, the verification_* tables by a migration that renames and never drops, namespaces, the dashboard, docs and agents; what keeps its name and why."
tags: [adr, verification, api, naming]
---
# ADR-0019: Integrity is renamed Verification in code, the API, events, configuration and tables

- **Status:** Accepted (2026-10-31)
- **Deciders:** Owner, through the rename task that ADR-0017 decision 16 called for
- **Amends:** ADR-0017 decision 16; the terminology of ADR-0004 and ADR-0011
- **Detail:** `docs/architecture/verification-engine.md`

## Context

ADR-0017 decision 16 renamed the feature people see to Verification, because "integrity" was too broad a name for
what the checks do. It left code, API routes, the webhook event, configuration keys and tables saying "integrity"
until a rename before launch. Nothing outside depends on those names yet: there is no deployment and no integrator.
After launch every one of them would be a breaking change, so this is the cheapest moment to rename them all.

## Decision

1. **Every name that said integrity says verification.**
   - API routes: `/v1/applications/{id}/verification[/...]` and `/v1/organization/verification-settings[/history]`,
     the OpenAPI tag `Verification`, and every schema, operation id and property (`ApplicationVerification`,
     `ApplicationSummary.verification`, `HiringWorkflow.verificationPolicy`).
   - Workflow names: the trigger `VerificationAssessed`, the facts `verification.authenticity` and
     `verification.aiAssistance`, and the permission `ReviewVerification`.
   - The webhook event `verification.assessed` and the realtime event `VerificationAssessed`.
   - Configuration under `ScreenHire:Verification:*`, and the AppHost parameter `verification-device-hash-key`.
   - Tables `verification_analyzer_runs`, `verification_signals`, `verification_assessments`,
     `verification_review_actions` and `verification_settings`, with their keys and indexes.
   - The C# namespaces `ScreenHire.*.Verification`, the dashboard's files and components, the MCP server's text, the
     marketing site, CLAUDE.md, the living docs, the `verification-guardrails` agent and the golden files' folder.
2. **No aliases.** The old routes, event name and configuration keys stop working at once. Nothing calls them, and an
   alias is a layer to maintain.
3. **The migration renames and never drops.** `RenameIntegrityToVerification` renames the tables, keys and indexes.
   Row-level security policies and grants belong to the table and follow it. It rewrites the names stored as data in
   place: definition JSON keys and values, the trigger and facts of past automation runs, application events, custom
   roles' permissions, webhook subscriptions and change-history subject kinds. Its `Down` reverses all of it.
4. **People's words are not rewritten.** Tags, automation names and ids, notes and change-history lines already
   written keep their text. Automation ids feed idempotency keys, so renaming them could run an automation twice.
   ScreenHire's own Software engineer template gets a version 5 whose automation and tag say verification, because a
   content change to a system template is always a new version.
5. **The guardrail still treats a leftover `integrity.` fact as a signal.** Every stored definition is rewritten, but
   non-negotiable 2 is too important to rest on that alone.

What keeps its name, and why:

- **The level "Verification suggested"** (`VerificationRecommended`): under the Verification tab it reads as what the
  tab suggests (ADR-0017 decision 16).
- **Phase-2 identity verification**, which already said verification: the `VerificationCompleted` trigger, the
  `RequestVerification` action, the `verification.result` fact and the `request-verification` route.
- **Records:** merged migrations (their names, SQL and comments), accepted ADRs' words, `docs/research/`, the docs
  log, dated test-run notes, and webhook delivery history. A delivery's body is the exact signed bytes that were sent,
  and a resend copies them, so its event type stays `integrity.assessed` too.
- **Generic words:** npm's `integrity` hashes, "data integrity", and other products' names.
- **The dashboard's old addresses:** `/settings/integrity` redirects and `?tab=integrity` opens the tab.

## Consequences

- A local stack started with persisted data before the rename generates a new device-hash key, so device hashes
  stored earlier stop matching new ones. `/preview fresh` gives a clean demo.
- A Wolverine message in flight in a persisted local volume during the upgrade goes to the dead-letter table, because
  envelopes name their message type.
- A webhook event id hashes its event type, so an assessment raised again after the rename gets a new id.
- Values set under `ScreenHire:Integrity:*` in local user secrets are ignored until they are set again under
  `ScreenHire:Verification:*`.
- The merged migration `AddSoftwareEngineerTemplateVersion2` still cites the old doc path in a comment, so the docs
  citation check exempts that one file.
