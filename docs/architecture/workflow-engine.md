# Workflow Engine — Per-Job Hiring Workflows from User-Defined Templates

> Status: **Draft v0.1 (2026-10-18)**. Requirement: "customized workflow creation for each job with templates that a
> user defines". GoHire's gap (from `docs/research/competitors.md`): Pro-only stage actions, **no event triggers, no
> knockout filters, no public API**. We beat that with an event-driven engine exposed fully through the API.

## 1. Concepts

```
Organization
 ├─ HiringTemplate (versioned)          ← user-defined, reusable
 │    ├─ WorkflowDefinition             stages + automations
 │    ├─ Questionnaire                  screening questions (+ knockouts, honeypots)
 │    ├─ IntegrityPolicy                AI-use policy, step-up rules, telemetry jurisdiction defaults
 │    ├─ PostingDefaults                target boards, duration, sponsorship defaults
 │    └─ JobContentDefaults             description blocks, benefits, EEO/legal boilerplate
 └─ Job
      ├─ JobWorkflow  (instantiated from a template version, then customisable per job)
      ├─ Applications ──▶ ApplicationState (current stage, history, assessments)
      └─ Postings (per board; see job-distribution.md)
```

- **HiringTemplate** bundles everything a recruiter wants to reuse for a type of role ("Hourly warehouse — US",
  "Senior engineer — EU remote"). Templates are **versioned and immutable once published**; edits create a new version.
- **Job instantiation** copies the template version into a `JobWorkflow` with a `sourceTemplateVersionId`. Per-job edits
  fork the copy. When a template gets a new version, jobs show "update available" and the recruiter can diff and apply —
  live jobs are never mutated silently.
- **System templates** ship with the product (SMB quick-start), customers clone and edit them.

## 2. WorkflowDefinition

### Stages
Ordered list. Each stage has `id`, `name`, `kind` (`applied`, `screening`, `assessment`, `verification`, `interview`,
`offer`, `hired`, `rejected`, `withdrawn`, `custom`), `color`, optional SLA (e.g., "respond within 3 days"), and
visibility rules (which roles see candidates here). `hired`/`rejected`/`withdrawn` are terminal.

### Automations (trigger → conditions → actions)

| Part | Examples |
|---|---|
| **Triggers** | `ApplicationSubmitted`, `StageEntered(stage)`, `StageExited(stage)`, `QuestionnaireCompleted`, `IntegrityAssessed`, `VerificationCompleted`, `ReviewerDecision`, `TimeElapsed(since: stageEntered, duration)`, `CandidateReplied`, `InterviewScheduled`, `Manual` |
| **Conditions** | Expression over application facts: answers (`answers.work_authorization == "yes"`), tags, source board, location, integrity levels (`integrity.authenticity >= ReviewSuggested`), reviewer ratings, time in stage. Combinators `all/any/not`. |
| **Actions** | `SendEmail(template)`, `SendQuestionnaire(q)`, `RequestVerification(provider)`, `MoveToStage(stage)`, `AddTag`, `AssignReviewer`, `NotifyTeam`, `ScheduleInterview(link)`, `CreateTask`, `CallWebhook(url)`, `QueueForRejectionReview(reason)`, `Reject(reason, emailTemplate)` |

### Guardrails enforced by the engine (product-legal policy, see ADR-0004)
1. **Integrity-derived conditions can never drive `Reject`.** Any automation whose condition references `integrity.*`
   (or any signal) may only use routing/step-up actions (`RequestVerification`, `AssignReviewer`, `AddTag`,
   `MoveToStage` into a non-terminal review stage). The validator rejects such definitions at save time.
2. **Knockouts** (objective, candidate-declared eligibility like work authorisation, required licence) may use `Reject`,
   but the default action is `QueueForRejectionReview`; auto-reject requires an explicit opt-in per rule, shows the
   candidate a reason, and is logged for record-keeping.
3. **Every automated action is audit-logged** with the definition version, trigger event, evaluated facts and result.
4. Accommodation: if the candidate requested accommodation, automations referencing timing/telemetry facts are skipped.

## 3. Execution model

```
Domain event (e.g., StageEntered) ── transactional outbox (Postgres) ──▶ Worker
   Worker: load JobWorkflow version → match automations by trigger
         → evaluate conditions against an immutable facts snapshot
         → enqueue each action as a durable job keyed (applicationId, automationId, eventId)  ← idempotent
         → actions emit new domain events (may trigger further automations; depth limit 10, cycle detection)
TimeElapsed triggers → scheduled durable messages, cancelled when the stage is exited
```

- **Determinism**: conditions evaluate a facts snapshot captured at event time, so replays give the same result.
- **Versioning**: running applications stay on the workflow version they started under unless the recruiter migrates
  them (stage mapping UI).
- **Failure handling**: retries with backoff; poisoned actions land in a per-org "automation issues" inbox, never
  silently dropped.
- **Expression language**: a small, safe JSON-based condition AST (not arbitrary code). The UI builds it; the API
  accepts it; the server validates it against a typed facts schema.

## 4. Builder UX (dashboard)

- **Pipeline view** (default, SMB-friendly): horizontal stages, each with an "automations" drawer — "When a candidate
  enters *Screening* → send *Questionnaire A*; if *work authorisation = No* → queue for rejection review".
- **Graph view** (power users): node-graph editor showing triggers → conditions → actions across stages. Same
  underlying model; the graph is a projection, not a separate format.
- **Questionnaire builder** with live preview rendering the **same question components** the candidate sees on the
  Astro apply page (shared component package — a factor in the dashboard framework decision).
- **Simulation**: "test this workflow" runs a sample application through the definition and shows which automations
  would fire — essential trust feature, cheap because evaluation is deterministic.

## 5. API surface (public, same API the dashboard uses)

- `GET/POST /v1/templates`, `POST /v1/templates/{id}/versions`, `POST /v1/templates/{id}/versions/{v}/publish`
- `POST /v1/jobs` (from template version), `PATCH /v1/jobs/{id}/workflow`, `POST /v1/jobs/{id}/workflow/apply-template-update`
- `POST /v1/workflows/validate`, `POST /v1/workflows/simulate`
- `POST /v1/applications/{id}/move`, `GET /v1/applications/{id}/timeline`
- Webhooks: `application.submitted`, `application.stage_changed`, `integrity.assessed`, `automation.failed`, …
  (signed, retried, replayable).

## 6. Data model sketch (Postgres)

`hiring_templates(id, org_id, name, current_version_id)`
`hiring_template_versions(id, template_id, version, status, definition_jsonb, published_at, published_by)`
`jobs(id, org_id, template_version_id, workflow_jsonb, workflow_version, …)`
`applications(id, org_id, job_id, candidate_id, stage_id, workflow_version, accommodation, …)`
`application_events(id, application_id, type, payload_jsonb, occurred_at)` — append-only timeline
`automation_runs(id, application_id, automation_id, event_id, facts_jsonb, result, error, created_at)`

Definitions are stored as validated JSONB with a JSON Schema published alongside the OpenAPI spec, so the dashboard,
API clients and the engine share one contract.

## Open questions

- Approval steps (mid-market): model as a stage kind or a cross-cutting action? (Leaning: `RequestApproval` action +
  `ApprovalCompleted` trigger.)
- Agency mode: templates scoped per client company.
- Interview scheduling: build vs integrate (Cal.com self-host / Nylas / Cronofy).
