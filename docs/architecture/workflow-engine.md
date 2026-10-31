---
type: Design
title: "Workflow Engine: Per-Job Hiring Workflows from User-Defined Templates"
description: "Hiring templates and per-job workflow definitions: stages, scorecards, follow-up questionnaires, automations with triggers, conditions and actions, the guardrail that integrity never rejects, the execution model, the builder, webhook subscriptions with signed deliveries, and what the v1 engine runs."
tags: [workflow, automations, webhooks, scorecards, questionnaires]
sources:
  - resource: src/ScreenHire.Domain/Hiring/HiringWorkflow.cs
  - resource: src/ScreenHire.Domain/Applications/ApplicationNote.cs
  - resource: src/ScreenHire.Domain/Hiring/HiringTemplate.cs
  - resource: src/ScreenHire.Domain/Hiring/DefinitionValidator.cs
  - resource: src/ScreenHire.Domain/Hiring/FactSchema.cs
  - resource: src/ScreenHire.Domain/Hiring/SystemTemplates.cs
  - resource: src/ScreenHire.Domain/Automations/ApplicationTask.cs
  - resource: src/ScreenHire.Domain/Automations/AutomationEvaluator.cs
  - resource: src/ScreenHire.Domain/Automations/AutomationFacts.cs
  - resource: src/ScreenHire.Domain/Automations/AutomationRun.cs
  - resource: src/ScreenHire.Domain/Applications/ApplicationScorecard.cs
  - resource: src/ScreenHire.Domain/Applications/ScorecardAnswers.cs
  - resource: src/ScreenHire.Domain/Applications/RatingRule.cs
  - resource: src/ScreenHire.Domain/Applications/ReviewerGate.cs
  - resource: src/ScreenHire.Domain/Hiring/BuiltInScorecards.cs
  - resource: src/ScreenHire.Domain/Hiring/ScorecardTemplate.cs
  - resource: src/ScreenHire.Domain/Applications/QuestionnaireRequest.cs
  - resource: src/ScreenHire.Domain/Webhooks/WebhookDelivery.cs
  - resource: src/ScreenHire.Domain/Webhooks/WebhookEndpoint.cs
  - resource: src/ScreenHire.Application/Automations/ApplicationTaskCompleted.cs
  - resource: src/ScreenHire.Application/Automations/ApplicationTaskReopened.cs
  - resource: src/ScreenHire.Application/Automations/ApplicationTaskCreated.cs
  - resource: apps/api/Applications/ApplicationChanges.cs
  - resource: src/ScreenHire.Application/Automations/AutomationEngine.cs
  - resource: src/ScreenHire.Application/Automations/AutomationFailures.cs
  - resource: src/ScreenHire.Application/Automations/AutomationMessages.cs
  - resource: src/ScreenHire.Application/Automations/ExecuteAutomationActionHandler.cs
  - resource: src/ScreenHire.Application/Automations/RunAutomationsHandler.cs
  - resource: src/ScreenHire.Application/Automations/Webhooks.cs
  - resource: src/ScreenHire.Application/Applications/QuestionnaireLinks.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/DataProtectionRegistration.cs
  - resource: src/ScreenHire.Application/Webhooks/WebhookDeliveryHandlers.cs
  - resource: src/ScreenHire.Application/Webhooks/WebhookEvents.cs
  - resource: src/ScreenHire.Application/Webhooks/WebhookRetention.cs
  - resource: apps/api/Endpoints/TemplateEndpoints.cs
  - resource: apps/api/Endpoints/TemplateUsageEndpoints.cs
  - resource: apps/api/Hiring/TemplateUsage.cs
  - resource: apps/dashboard/src/routes/(app)/templates/+page.svelte
  - resource: apps/dashboard/src/lib/components/template-list.svelte
  - resource: apps/dashboard/src/routes/(app)/templates/scorecards/[templateId]/+page.svelte
  - resource: src/ScreenHire.Domain/Hiring/TemplateUses.cs
  - resource: apps/api/Endpoints/WorkflowEndpoints.cs
  - resource: apps/api/Endpoints/AutomationEndpoints.cs
  - resource: apps/api/Endpoints/ScorecardEndpoints.cs
  - resource: apps/api/Endpoints/ScorecardTemplateEndpoints.cs
  - resource: apps/api/Hiring/RatingReads.cs
  - resource: apps/api/Hiring/ScorecardReferences.cs
  - resource: apps/api/Hiring/TemplateReferences.cs
  - resource: apps/api/Endpoints/WebhookEndpoints.cs
  - resource: apps/api/Endpoints/PublicQuestionnaireEndpoints.cs
  - resource: apps/dashboard/src/lib/workflow.ts
  - resource: apps/dashboard/src/lib/workflow-graph.ts
  - resource: apps/dashboard/src/lib/components/workflow/workflow-graph.svelte
  - resource: apps/dashboard/src/lib/components/workflow/step-node.svelte
  - resource: apps/dashboard/src/lib/components/workflow/graph-keys.svelte
  - resource: apps/dashboard/src/lib/components/workflow/graph-controls.svelte
  - resource: apps/dashboard/src/lib/components/workflow/workflow-editor.svelte
  - resource: apps/dashboard/src/lib/components/workflow/workflow-summary.svelte
  - resource: apps/dashboard/src/lib/components/workflow/automation-form.svelte
  - resource: apps/dashboard/src/lib/components/workflow/pipeline-view.svelte
  - resource: apps/dashboard/src/lib/components/workflow/questionnaire-builder.svelte
  - resource: apps/dashboard/src/lib/components/workflow/question-settings.svelte
  - resource: apps/dashboard/src/lib/components/form-builder.svelte
  - resource: apps/dashboard/src/lib/components/answer-menu.svelte
  - resource: apps/dashboard/src/lib/components/scorecard-builder.svelte
  - resource: apps/dashboard/src/lib/components/scorecard-sheet.svelte
  - resource: apps/dashboard/src/lib/components/rating.svelte
  - resource: apps/dashboard/src/lib/components/rating-answer.svelte
  - resource: apps/dashboard/src/lib/components/workflow/scorecard-picker.svelte
  - resource: apps/dashboard/src/lib/scorecards.ts
  - resource: apps/dashboard/src/lib/rating-column.ts
  - resource: apps/dashboard/src/routes/(preview)/preview/form/+page.svelte
  - resource: apps/dashboard/src/lib/form-builder.ts
  - resource: apps/dashboard/src/lib/sla.ts
---
# Workflow Engine: Per-Job Hiring Workflows from User-Defined Templates

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
- **Archived templates.** An organization archives its own hiring template or library scorecard
  (`POST .../archive`, `.../unarchive`): it leaves the pickers, no new job starts from it (`POST /v1/jobs` answers 400),
  and it reads only until restored (409 on an edit). Jobs keep the copy they took, and stages that name an archived
  scorecard keep it. System templates and built-in scorecards are never archived.
- **Where a template is used.** `GET /v1/template-usage` (`apps/api/Hiring/TemplateUsage.cs`) gives the Templates
  page each template's users and when it was last used: the jobs that are not closed for a hiring template, the hiring
  templates whose current versions (the newest published and the draft) name a scorecard, and the automations that
  send an email, a Reject action counting for the rejection email and SendQuestionnaire for the follow-up questions
  email. "Not used in 6 months" is `TemplateUses.NotUsedRecently`: nothing used it in 180 days, and the team has had it
  longer than that (a built-in from the organization's creation).

## 2. WorkflowDefinition

### Stages
Ordered list. Each stage has `id`, `name`, `kind` (`applied`, `screening`, `assessment`, `verification`, `interview`,
`offer`, `hired`, `rejected`, `withdrawn`, `custom`), `color`, optional SLA (e.g., "respond within 3 days"), and
visibility rules (which roles see candidates here). `hired`/`rejected`/`withdrawn` are terminal. The SLA (`slaDays`, the
builder's "time limit") measures the hiring team's delay, never the candidate: section 8 says what acts on it.

### Automations (trigger → conditions → actions)

| Part | Examples |
|---|---|
| **Triggers** | `ApplicationSubmitted`, `StageEntered(stage)`, `StageExited(stage)`, `QuestionnaireCompleted(questionnaire)`, `IntegrityAssessed`, `VerificationCompleted`, `ReviewerDecision`, `TimeElapsed(since: stageEntered, duration)`, `SlaBreached(stage?)`, `CandidateReplied`, `InterviewScheduled`, `Manual` |
| **Conditions** | Expression over application facts: answers (`answers.work_authorization == "yes"`), tags, source board, location, integrity levels (`integrity.authenticity >= ReviewSuggested`), a reviewer's recommendation, time in stage. Combinators `all/any/not`. |
| **Actions** | `SendEmail(template)`, `SendQuestionnaire(questionnaire)`, `RequestVerification(provider)`, `MoveToStage(stage)`, `AddTag`, `AssignReviewer`, `NotifyTeam`, `ScheduleInterview(link)`, `CreateTask`, `CallWebhook(url)`, `QueueForRejectionReview(reason)`, `Reject(reason, emailTemplate)` |

### Guardrails enforced by the engine (product-legal policy, see ADR-0004)
1. **Integrity-derived conditions can never drive `Reject`.** Any automation whose condition references `integrity.*`
   (or any signal) may only use routing/step-up actions (`RequestVerification`, `AssignReviewer`, `AddTag`,
   `MoveToStage` into a non-terminal review stage). The validator rejects such definitions at save time.
2. **Knockouts** (objective, candidate-declared eligibility like work authorisation, required licence) may use `Reject`,
   but the default action is `QueueForRejectionReview`; auto-reject requires an explicit opt-in per rule, shows the
   candidate a reason, and is logged for record-keeping.
3. **Every automated action is audit-logged** with the definition version, trigger event, evaluated facts and result.
4. Accommodation: if the candidate requested accommodation, automations referencing timing/telemetry facts are skipped.
   `SlaBreached` is the exception, and the reason is section 8.
5. **Keep in mind is a person's action only** ([talent pool](talent-pool.md)): no automation action keeps a candidate in
   mind, so no condition, integrity or otherwise, can take someone out of a job that way. An application kept in mind
   has ended like one in a Hired, Rejected or Withdrawn stage: actions skip it and its stage's timers do nothing.

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
- **Questionnaire builder** with a preview rendering the **same question components** the candidate sees on the
  Astro apply page (shared component package, a factor in the dashboard framework decision).
- **Simulation**: "test this workflow" runs a sample application through the definition and shows which automations
  would fire — essential trust feature, cheap because evaluation is deterministic.

What the dashboard builds (`apps/dashboard`, routes `/templates`, `/templates/{id}` and `/jobs/{id}/workflow`). The
Templates page has a tab each for hiring templates, library scorecards (`/templates/scorecards/{id}`, the form builder
with its preview beside it, or behind an Edit / Preview switch where both do not fit) and email templates
([email.md](email.md)), each listed by `template-list.svelte` with where it is used, from `GET /v1/template-usage`. The
workflow builder is one editor, `#lib/components/workflow/workflow-editor.svelte`, over a template draft or a job's own
copy, with four tabs on the page's header band. A workflow that cannot be edited there (a ScreenHire template, a
published version, a closed job's) opens the same tabs in `workflow-summary.svelte`, which reads it as content, with no
form controls. The validation result is a status badge beside the page title.

- **Pipeline:** the stages in order (reordered by dragging a grip, or from the menu the grip opens, `ReorderList`), and an automation's actions the same way and, per stage, its automations
  in a Sheet drawer and its scorecard in another (`scorecard-picker.svelte`): a library scorecard, built-in or the
  team's own, picked from a list and shown as it asks. Questions are written on the library scorecard itself, never in
  the workflow: on the Templates page's Scorecards tab (`/templates/scorecards/{id}`), in the form builder
  (`scorecard-builder.svelte`), or through `/v1/scorecard-templates`. An archived scorecard leaves the picker unless the
  stage already names it. A stage that ends the application offers no
  scorecard. Automations whose trigger
  has no stage (an arriving application, the integrity checks, a manual run, any stage's time limit) sit in a "Whole
  workflow" panel under the stages. "A stage's time limit passes" offers the stages that have one, and only the four reminder actions. The drawer's forms offer only the triggers and actions v1 runs, and the facts
  and operators `FactSchema` allows.
- **Graph:** @xyflow/svelte, rebuilt from the workflow on every change by `toGraph` (`#lib/workflow-graph.ts`): a
  "Whole workflow" lane and the stages along the top, one row per automation from trigger to conditions to actions,
  edges from the lane or stage it fires on and to the stage it moves into. It stays a projection: no node position is
  stored, because a stored layout would change the definition and every save of a job's workflow is a new version. The
  layout is automatic, each row as tall as its tallest node can be, so nodes never overlap. It opens at full size from
  the top left, where reading order starts: fitted to the pane, a node's buttons would fall under the 24px target size
  (WCAG 2.5.8), so fitting is the zoom controls' choice. The first column starts right of the zoom toolbar, and a
  node that takes the focus under the toolbar is panned out from under it. The graph edits the same
  draft as the Pipeline view, through the same form:
  - a lane or stage node's "Add automation" adds one there (`addAutomation`) and opens its form at the name;
  - every automation node's Edit opens that automation's form, the drawer's own `automation-form.svelte`, at the
    node's part (its trigger, its conditions or that action), so every trigger, condition and action the form offers,
    the questionnaire and time-limit ones included, is built from the graph;
  - a trigger node's Remove takes the automation out (`removeAutomation`);
  - a "move to a stage" action points at its stage through the form's Stage field, or by drawing a link from the
    action to a stage (`connect`), which is a pointer shortcut only.

  Every node is a tab stop with the focus ring, Tab reaches the buttons in it, and the arrow keys, Home and End move
  between nodes in reading order (`neighbour`); whatever takes the focus is panned into view. A validation message
  shows on the node its path names, an automation's own messages on its trigger node, as it does on the form control.
  Closing the form returns the focus to the graph. On a phone the graph is for reading, with no buttons, and says to
  edit in Pipeline. A version that cannot be edited draws the same graph with no buttons.
- **Questionnaire:** the apply form and each follow-up questionnaire, chosen with toggles, in the form builder
  (`#lib/components/form-builder.svelte`, design system section 11): a row per question that opens to edit, with its
  answer kind from the answer menu, Duplicate, Delete, Required and a grip to reorder. The kinds a question takes
  (`applyFormKinds`, `followUpKinds` in `#lib/form-builder.ts`) are every `QuestionType`; a follow-up offers all but
  File, because its page takes no uploads. What a kind needs beyond its text and options (a knockout, a number's range,
  the longest answer, a file's types) is `question-settings.svelte`. A duplicate takes an id unique across the workflow,
  so `answers.{id}` still names one question. The preview beside the builder, or behind the Edit / Preview switch where
  both do not fit, shows the candidate's form at phone, tablet or desktop width, rendered by the shared components in
  `packages/ui/src/questions` that the careers pages render, and its window icon opens it at `/preview/form`, a window
  that follows each edit. The apply form's preview starts with the candidate's details and the resume; a follow-up's is
  its questions alone, as its page shows them. Marking a yes/no question as a knockout shows what a knockout may be (an
  objective eligibility check, never AI use or an accommodation). The automation form offers "Send a questionnaire" and
  the "questionnaire answered" trigger's picker only when the workflow has a follow-up to name. A version that cannot be
  edited reads its questions as content, with the same switch.
- **Simulate:** a sample application's answers, tags, source, hours in stage and integrity levels through
  `POST /v1/workflows/simulate`, unsaved changes included.

Every change is checked with `POST /v1/workflows/validate` after a short pause, and each message shows on the control
its path names, so the guardrail refusal of an integrity-driven rejection appears on that action. A draft that does
not validate cannot be saved, so the editor holds unsaved work in the page and asks before leaving it. The editor lists
the open problems in a "Problems to fix" panel above every tab; a read-only view lists a stored version's in a
"Problems" panel. Saving with no changes sends nothing, because each save of a job's workflow is a new version.

## 5. API surface (public, same API the dashboard uses)

- `GET/POST /v1/templates`, `POST /v1/templates/{id}/versions`, `POST /v1/templates/{id}/versions/{v}/publish`
- `POST /v1/jobs` (from template version), `PUT /v1/jobs/{id}/workflow`, `POST /v1/jobs/{id}/workflow/apply-template-update`
- `POST /v1/workflows/validate`, `POST /v1/workflows/simulate`
- `POST /v1/applications/{id}/move`, `GET /v1/applications/{id}/timeline`, `POST /v1/applications/{id}/notes`,
  `POST` and `DELETE /v1/applications/{id}/tags`, `POST /v1/applications/{id}/tasks` (Create tasks; a Write key too)
- `GET /v1/tasks`, `POST /v1/tasks/{id}/complete` (signed-in people only: "mine" needs a person)
- `GET` and `POST /v1/applications/{id}/scorecards` (an API key reads them; only a signed-in person submits one)
- `GET` and `POST /v1/scorecard-templates`, `GET` and `PUT /v1/scorecard-templates/{id}`,
  `GET /v1/scorecard-templates/{id}/history`, `POST /v1/scorecard-templates/{id}/archive` and `/unarchive`: the
  scorecard library (section 8)
- `POST /v1/templates/{id}/archive` and `/unarchive`; `GET /v1/template-usage` (signed-in people only), where each
  hiring, scorecard and email template is used
- Webhooks: `/v1/webhooks` (below).

### Webhook subscriptions

Integrators subscribe to events instead of polling. An organization's owners and admins manage its endpoints; every
other role gets 403, and another organization's endpoint or delivery is a 404 (tenant filter and row-level security on
`webhook_endpoints` and `webhook_deliveries`). In the dashboard they do it in Settings > Webhooks: an endpoint's page
shows its delivery log, refreshed while a delivery is pending, with replay; the signing secret appears once, after
creating or rotating.

| Route | Does |
|---|---|
| `GET /v1/webhooks/event-types` | The event names below |
| `GET/POST /v1/webhooks` | List endpoints; create one (URL, events, optional description). The 201 carries the signing secret, the only time it is shown |
| `GET/PUT/DELETE /v1/webhooks/{id}` | Read, replace (URL, events, description, and `enabled`, which keeps its value when left out) or delete an endpoint with its delivery log |
| `POST /v1/webhooks/{id}/rotate-secret` | A new secret, shown once. The old one stops signing at once, retries of earlier events included |
| `GET /v1/webhooks/{id}/deliveries?limit=` | The delivery log, newest first (default 50, at most 200): status, attempts, last status code, our description of the last error, the exact body |
| `POST /v1/webhooks/{id}/deliveries/{deliveryId}/replay` | Sends a finished delivery's event again as a new delivery (`replayOfId` set, same event id and body). 409 while any delivery of that event to the endpoint still has attempts, or while the endpoint is off |

At most 20 endpoints per organization; a 21st is a 409. A URL is absolute `https`, with no user info or fragment, and an
IP literal must be public. `ScreenHire:Webhooks:AllowLoopback` (the local stack and the tests only) also accepts plain
`http` to loopback, so a receiver on the same machine works. The worker checks the address again on the resolved IP at
connect time (`WebhookAddresses`), so DNS cannot point a public name at a private address.

**Events.** Each comes from a domain event that already exists; that event's worker handler cascades a
`FanOutWebhook` message (`WebhookEvents`), which writes one delivery row per enabled endpoint that subscribes and sends
its first attempt.

| Event | From | `data` |
|---|---|---|
| `application.submitted` | `ApplicationSubmitted` | `applicationId`, `jobId`, `stageId` |
| `application.moved` | `ApplicationMoved` | `applicationId`, `jobId`, `fromStageId`, `toStageId` |
| `integrity.assessed` | `IntegrityAssessed` | `applicationId`, `jobId`, `authenticity`, `aiAssistance` (levels), `rulesetVersion` |
| `automation.failed` | `AutomationRunFinished` with status `Failed` or `Blocked` | `applicationId`, `jobId`, `automationId`, `runId`, `status` |
| `job.published` | `JobPublished` | `jobId`, `version`, `jobVersionId` |
| `job.closed` | `JobClosed` | `jobId` |

The body is `{"id", "type", "occurredAt", "organizationId", "data"}`: ids and facts, never names, emails, resume text,
answers or integrity evidence. An integrator reads anything else through the API with its own credentials.
`integrity.assessed` carries the categorical levels only (ADR-0004), the same output the dashboard shows. It is a
notification for routing a review. ScreenHire never acts on it, and the rule that integrity signals never drive
rejection binds ScreenHire's own code. An integrator that rejects automatically on this event goes against the
product's guidance, and that decision is the customer's responsibility under GDPR Art. 22, NYC LL144 and the other
rules in docs/research/legal.md. The integrator docs and terms must say so.

The event `id` comes from the domain event's own key (a version 8 UUID from SHA-256 of the type and that key), so a
redelivered domain event gives the same id and the unique index on (endpoint, event) for first deliveries adds
nothing twice.

**Signing.** Each endpoint's secret is `whsec_` and the hex HMAC-SHA256 of `webhook-endpoint:{endpointId}:{version}`
under the platform key `ScreenHire:Webhooks:SigningKey` (`WebhookSignature.EndpointSecret`). The database keeps only
the version, and rotating bumps it. The API derives the secret to show it once; the worker derives it to sign. Both
hold the platform key, and no response or log carries it. Every delivery carries:

- `ScreenHire-Signature: t={unix seconds},v1={hex}`, the hex HMAC-SHA256 of `{t}.{raw body}` keyed with the secret.
  `t` is the attempt's time, so a receiver can refuse old requests while retries still pass.
- `Idempotency-Key` and `ScreenHire-Event-Id`: the event id, the same on every attempt and replay.
- `ScreenHire-Event-Type`, `ScreenHire-Delivery-Id`, and `User-Agent: ScreenHire-Webhooks/1`.

**Delivery and retries** (`DeliverWebhookHandler`). Each attempt is a `DeliverWebhook` message keyed by delivery and
attempt (`[Deduplicated]`), and the row's attempt count makes a stale or duplicate message a no-op. A 2xx is success.
Anything else (a status, a timeout after `ScreenHire:Webhooks:Timeout`, a refused address) is recorded on the row and
the next attempt is scheduled durably after `RetryDelays`, counted from the receiver's answer: 1 min, 5 min, 30 min, 2
h, 6 h, 12 h by default, seven attempts over about 21 hours. The handler schedules its own retries rather than throwing
into the error policy, which would roll back the attempt it should record. Only the receiver's status code is kept,
never its body. An endpoint deleted meanwhile takes its deliveries with it, and later attempts find nothing to send.

**Automatic disabling.** A delivery that fails every attempt adds one to the endpoint's `consecutiveFailures`, in one
`UPDATE` so parallel deliveries never lose a count; any success resets it. At `DisableAfterFailedDeliveries` (5) the
same statement turns the endpoint off and records `disabledAt` and `disabledReason`, which the API returns. A disabled
endpoint gets no new deliveries, and its pending attempts end as `Skipped`. Turning it back on (`PUT` with
`enabled: true`) clears the reason and the count; skipped and failed deliveries can then be replayed.

**Retention.** `PurgeExpiredWebhookDeliveries` runs daily at 03:37 UTC and fans out one
`PurgeOrganizationWebhookDeliveries` per organization, which deletes deliveries that finished more than
`DeliveryRetention` (30 days) ago. Pending deliveries stay until they finish.

`CallWebhook`, the automation action, is separate and unchanged: it posts to one URL with the organization's derived
key (section 8).

## 6. Data model sketch (Postgres)

`hiring_templates(id, org_id, name, current_version_id)`
`hiring_template_versions(id, template_id, version, status, definition_jsonb, published_at, published_by)`
`jobs(id, org_id, template_version_id, workflow_jsonb, workflow_version, …)`
`applications(id, org_id, job_id, candidate_id, stage_id, workflow_version, accommodation, …)`
`application_events(id, application_id, type, payload_jsonb, occurred_at)` — append-only timeline
`automation_runs`, `automation_action_runs` and `application_tasks` as built in v1: section 8 and the
`AddWorkflowEngine` migration.

Definitions are stored as validated JSONB with a JSON Schema published alongside the OpenAPI spec, so the dashboard,
API clients and the engine share one contract.

## 7. What v1 implements

The data model and API from the jobs and templates task, and the engine that runs automations (section 8).

- **Definitions** are the records in `src/ScreenHire.Domain/Hiring/HiringWorkflow.cs`. Each element is one flat record
  with a kind enum, so OpenAPI, Orval and the JSON Schema stay simple: no polymorphism, and conditions are one level
  (`match: All | Any`, rules with `negate`) rather than a tree. The API serves their JSON Schemas, generated by .NET's
  `JsonSchemaExporter`, at `/v1/schemas/hiring-template-definition.json`, `/v1/schemas/hiring-workflow.json` and
  `/v1/schemas/questionnaire.json`, and the OpenAPI components link to them with `externalDocs`.
- **Follow-up questionnaires** (`followUpQuestionnaires`, optional, so stored definitions still read): up to 10 named
  questionnaires beside the apply questionnaire, each with at least one question. A question id is unique across the
  apply questionnaire and every follow-up, and a follow-up cannot use the apply form's own ids (`resume`,
  `linkedin_export`), so `answers.{id}` names one question. No file questions: the follow-up page works without
  JavaScript and takes no uploads. The knockout rules apply to follow-up questions as to apply questions.
- **Validation** is `DefinitionValidator`: structure (ids, stage, question and questionnaire references, per-kind
  fields) plus the guardrails above as an allowlist. A stage may have a scorecard (section 8), never on a hired, rejected
  or withdrawn stage: a hiring template's stage names a library scorecard (`scorecardTemplateId`, a built-in's id or a
  uuid) and holds no copy, and a job's stage holds its copy, 1 to 20 questions with unique ids. A condition on the
  candidate's rating (`application.rating`, `rating`, `scorecard.*` and the like) is refused outright, whatever the
  trigger or action: no automation acts on the rating (ADR-0013). An automation that uses an `integrity.*` or `verification.*` fact, or an
  `IntegrityAssessed` or `VerificationCompleted` trigger, may only request verification, assign a reviewer, add a tag
  or move to a screening, assessment, verification or interview stage. The same holds at one remove, across the whole
  workflow: an automation that reads a tag such an automation adds, or fires on a stage it moves applications into,
  counts as signal-driven too, so a signal cannot be laundered through a tag or a stage. Once any signal-driven
  automation moves applications, every `StageExited` automation counts as well: the stage a move leaves is wherever the
  application is when the action runs, which no definition can know. Only an automation whose
  every condition is a knockout question equal to its disqualifying answer, with `allowAutoReject` set, may `Reject`,
  and it must give the candidate a reason. Accommodation status and the AI-use declaration are not facts. Publishing a
  template version validates it again, so a draft saved under older rules cannot go live.
- **What the validator cannot check:** a question's meaning, a scorecard question's included. It refuses scorecard
  questions, guidance and options that name personality, emotion, honesty and the like (whole words, so "data
  integrity" passes), but that is a backstop: a scorecard asks about the job, and the builder's note and review of
  the built-ins hold that line.
  A knockout question must be an objective eligibility check (right
  to work, a required licence). A knockout on AI use or on needing an accommodation would reject candidates for
  exactly what ADR-0004 and the accommodation guardrail protect; the template builder must say so, and review of
  system templates checks it.
- **Templates** have numbered versions: at most one draft, then published versions that never change. The system
  templates "Software engineer" and "Hourly role" ship in the `AddHiringModel` migration with no organization, and a
  later version of one ships as its own migration (`AddSoftwareEngineerTemplateVersion2`); an organization copies one
  (`copyFromVersionId`) to customise it, and the copy remembers its source (`copied_from_template_id`), so its history
  starts "created the template from Software engineer" ([change-history.md](change-history.md)). `AddScorecards` ships "Software engineer" version 3 and "Hourly role"
  version 2, the earlier versions with default scorecards on their review stages; the default workflow (a job made
  without a template) has scorecards on Screening and Interview. `AddScorecardTemplates` rewrote every version in
  place to name the built-in scorecards instead of holding v0.2 criteria: a change of storage, not of what they ask. `AddFollowUpQuestionnaires` ships "Software engineer"
  version 4 and "Hourly role" version 3, whose right-to-work automation fires on `ApplicationSubmitted`: it read an
  apply-form answer on a `QuestionnaireCompleted` trigger that nothing raised.
- **Jobs** copy a published template version's workflow and keep `sourceTemplateVersionId`. `JobSummary` shows
  `templateUpdateAvailable`, and `GET /v1/jobs/{id}/workflow/template-diff` compares the job's workflow with the
  template's newest published version, matching list elements by id.
- **Tailoring a job:** `PUT /v1/jobs/{id}/workflow` replaces the job's copy with an edited one, and
  `POST /v1/jobs/{id}/workflow/apply-template-update` replaces it with a published version of its source template
  (which replaces the job's own edits too; the diff lists them). Both validate like a template save, refuse a closed
  job (409), and take `expectedWorkflowVersion`, so a stale editor gets 409 rather than overwriting someone's change.
  Each change adds one to the job's `workflowVersion` and raises `JobWorkflowChanged` through the outbox, so open
  dashboards refetch the job; an editor with unsaved changes keeps them and says a newer version exists. Neither
  republishes the job: the careers form reads the workflow live. There is no stage mapping yet. A change that drops a
  stage still holding applications is refused (400 on `workflow.stages`, naming the stage) until someone moves those
  candidates. The check reads the applications before the save and takes no lock, so an application that arrives in
  or moves to that stage in between is not caught.
- **Applications** start in the first stage. `POST /v1/applications/{id}/move` is the only way they move, always by a
  person, and every change appends to `application_events`. Recruiters add applications by hand with
  `POST /v1/jobs/{jobId}/applications`; the careers apply flow is its own task.
- **Events** through the outbox: `JobPublished` (with the job version), `JobClosed`, `ApplicationSubmitted`,
  `ApplicationMoved` (both carrying the timeline event id, the engine's idempotency key) and
  `HiringTemplateVersionPublished`.

## 8. The engine (v1)

Code: `src/ScreenHire.Domain/Automations` (facts, evaluator, run records) and `src/ScreenHire.Application/Automations`
(Wolverine messages and handlers). It follows section 3; what follows is what v1 actually does and where it stops.

**Triggers.** Submitting an application fires `ApplicationSubmitted` and `StageEntered` for the first stage; a move fires
`StageExited` and `StageEntered`. Both evaluate inline in the existing `ApplicationSubmitted` and `ApplicationMoved`
worker handlers, which write and send nothing when the job has no matching automation. `TimeElapsed` is a scheduled
`RunAutomations` message, due `afterHours` after the stage entry it counts from; when it fires it does nothing if the
application has left that stage entry, which is how leaving the stage cancels it. `Manual` is
`POST /v1/applications/{id}/automations/{automationId}/run`; an optional `Idempotency-Key` header makes a retried request reuse its run. The integrity
engine publishes `RunAutomations(IntegrityAssessed)` whenever an assessment changes, keyed on the `IntegrityAssessed`
timeline entry it appends. Submitting a scorecard publishes `RunAutomations(ReviewerDecision)` keyed on its
`ScorecardSubmitted` timeline entry (below). `SlaBreached` is a scheduled `RunAutomations` message like `TimeElapsed`
(below). Answering a follow-up questionnaire publishes
`RunAutomations(QuestionnaireCompleted, questionnaire, entry)` keyed on its `QuestionnaireCompleted` timeline entry
(below). A `QuestionnaireCompleted` automation names the follow-up it fires on, as a stage trigger names its stage;
one that names none fires on any follow-up, which keeps definitions saved before follow-ups existed valid. The apply
questionnaire never fires it: `ApplicationSubmitted` covers that.

**Stage time limits (SLAs).** A stage's `slaDays` is how long a candidate should wait there for the hiring team. It
measures the team's delay, not the candidate's, so a breach never counts against a candidate. A hired, rejected or
withdrawn stage ends the application, so a limit there is ignored (`WorkflowStage.SlaDueAt`) and the builder offers
none.

- An application is overdue from `slaDueAt`: its stage entry plus the stage's `slaDays` in the job's current workflow,
  sent on `ApplicationSummary` and `ApplicationDetail` and null without a limit. The dashboard compares it with its own
  minute clock, so the candidates list, the job's table and board, and the application's page show "Overdue by 2 days"
  while the page is open (design-system.md section 3.2). The sidebar's Overdue view is the list's filter.
- `SlaBreached`, optionally for one stage, fires once per stage entry. Entering a stage with a limit schedules one
  `RunAutomations(SlaBreached)` per enabled automation for that stage or for any stage, due when the limit passes and
  keyed on the entry's timeline id. When it fires it does nothing if the application has left that stage entry, as a
  `TimeElapsed` timer does, or if the stage's limit has since been removed or lengthened past the due time. The run is
  keyed on the entry and the message is `[Deduplicated]`, so a redelivery runs nothing twice. Its facts are read at
  the due time.
- The validator allows it only `NotifyTeam`, `AssignReviewer`, `CreateTask` and `AddTag`, and refuses anything else
  with the reason: nothing it does may count against the candidate. It refuses timing facts in its conditions. An
  automation that reads a tag it adds, or fires on a stage such an automation moves into, may do the same four things
  or move to a screening, assessment, verification or interview stage, the limits a signal has: no email and no custom
  stage, since either could be a rejection under another name. It needs a stage whose limit counts, or with no stage, at least one such stage.
- It is not skipped for an application with an accommodation. Guardrail 4 skips timing automations so that time
  never counts against a candidate who asked for more of it; a breach reminds the team and nothing it does can harm
  the candidate, so skipping it would leave the candidate who needs the most care waiting with nobody told.

**Facts snapshot.** `AutomationFacts.FromTimeline` rebuilds the facts from the append-only timeline as it stood at the
trigger event (tags from `TagAdded` entries, hours in stage from the last stage entry), so a late or replayed evaluation
sees what the first one saw. The snapshot and every rule's result are stored on the run. Integrity levels come from
the latest `IntegrityAssessed` entry at or before the trigger. `reviewer.recommendation` is the recommendation of the
scorecard whose entry fired a ReviewerDecision trigger, and absent on every other trigger. `answers.{id}` holds the
apply form's answers on every trigger, and a follow-up questionnaire's once its `QuestionnaireCompleted` entry is among
the entries the snapshot reads, so follow-up answers count only for triggers after they arrive. `application.source` is
`CareersSite` or `Recruiter`. The facts schema
(`FactSchema`) gives every fact a type and the operators that type allows; integrity levels take equality only. The integrity engine's level names are `NoConcerns`, `ReviewSuggested` and
`VerificationRecommended` for `integrity.authenticity`, and `NotIndicated`, `SomeIndicators` and `StrongIndicators` for
`integrity.aiAssistance`. The validator does not check them yet: version 1 of the "Software engineer" system template
compares `integrity.aiAssistance` with `ReviewSuggested`, and validating names would block jobs copied from it.
Version 2 corrects the condition (integrity-engine.md §10.9).

**Runs and actions.** Every triggered automation gets an `automation_runs` row, unique on
`(application, automation, event)`, whether it matched or not: that row is the audit record (guardrail 3) and the
reason a replayed event runs nothing twice. A matched run copies its actions into `automation_action_runs` and runs them
in order, one `ExecuteAutomationAction` message each, keyed `application:automation:event:index` and
`[Deduplicated]`. An action's database effect (a move, a tag, a task) commits with its status, so a redelivery finds it
done. An email is queued in that transaction (`SendAutomationEmail`, [email.md](email.md)), so it leaves only once
the action commits: an action retried after a conflict with another automation's action on the same application sends
one email. A webhook happens before the commit; a crash in between can repeat one once.

| Action | What v1 does |
|---|---|
| `SendEmail` | Renders one of the organization's email templates to the candidate, in its current wording: a built-in (`application-received`, `stage-change`) or one of its own, named by id ([email.md](email.md)). Queues the email with the action's commit; the worker sends it and adds an `EmailSent` timeline entry linked to the run. Skipped when email is not configured; fails when the template was deleted. The rejection email is not available here: it says a candidate is out, so only `Reject` sends it. `stage-change` says "moved to the next step", so it is refused at save on entering a rejected or withdrawn stage and skipped when the application is in one by the time it sends. |
| `MoveToStage` | Moves the application and links the timeline entry to the run, which fires the next stage's triggers. Never moves an application out of a terminal stage. |
| `AddTag` | Adds a tag (a `TagAdded` timeline entry). Conditions read tags as `application.tags`. |
| `AssignReviewer` | Creates a `Review` task, for the named member or the whole team. The builder offers the team's Owners, Admins and Recruiters (a Viewer could not complete the task) or "Anyone on the team". Saving a workflow checks only that a member is named; the run checks they still belong to the organization, and fails the action if not. |
| `NotifyTeam` | Raises `TeamNotified` to the organization's dashboards, which show the action's note in the shell's notification tray. |
| `CreateTask` | Creates a `Task` with the note. |
| `QueueForRejectionReview` | Creates a `RejectionReview` task. It never moves the application. |
| `Reject` | Only for an opted-in knockout automation. Sends `application-rejected`, in the organization's wording, with the reason (the wording must use it). ScreenHire adds the sentence that says the decision was automatic and that a person reviews it on request, which no wording can remove. It then moves to the rejected stage. It re-validates the workflow first. Without email, with the opt-in gone, with a workflow that no longer validates or that changed version since the conditions were checked, it queues a rejection review instead: the candidate must be told why. |
| `CallWebhook` | Posts ids only, with the action key as `Idempotency-Key`, to public addresses only (no loopback, private or link-local targets, checked at connect time; loopback only where `ScreenHire:Webhooks:AllowLoopback` is on, in the local stack and the tests). Signed: `ScreenHire-Signature: t={unix seconds},v1={hex}`, HMAC-SHA256 of `{t}.{body}` under the organization's key: hex HMAC-SHA256 of `webhook-signing:{organizationId}` under `ScreenHire:Webhooks:SigningKey` (`WebhookSignature.OrganizationKey`; the AppHost generates the platform key locally). A receiver is given its organization's key only, so it cannot sign for another organization. Never through a proxy, which would resolve the target past the address check. Without a key the action fails as an issue instead of sending something unverifiable. No HTTP retries of its own: Wolverine retries the action. |
| `SendQuestionnaire` | Emails the candidate a link to one of the workflow's follow-up questionnaires on the careers site, through the built-in `questionnaire-request` email, whose wording must use `{{ questionnaireLink }}` ([email.md](email.md)). One request per application and questionnaire (`questionnaire_requests`, unique): the first send creates it, a later one sends a fresh link to the same request while it is unanswered, and once it is answered nothing is sent. A `QuestionnaireSent` timeline entry links each send to the run. Skipped without email, as `SendEmail` is, and for an application that has ended; fails when the questionnaire left the workflow or the careers site address (`ScreenHire:Domains`) is not set up, and in either case before the request changes. Not open to signal-driven automations. |
| `RequestVerification`, `ScheduleInterview` | Refused at save. Verification is phase 2; scheduling does not exist. Triggers `VerificationCompleted`, `CandidateReplied` and `InterviewScheduled` are refused for the same reason. |

**Loops and depth.** A run records the chain of automations whose actions led to it. An automation already in its own
chain is a loop, and a chain of `AutomationEvaluator.MaxDepth` (10) stops there; either way the run is `Blocked` and
nothing runs. The engine also re-validates the job's workflow before running anything, so a definition saved under
older rules is blocked rather than run. A tag counts as signal-tainted by the evaluator's own comparison, so the validator and the
worker cannot disagree about which rules read it.

**Accommodation.** For an application with an accommodation, an automation with a `TimeElapsed` trigger or a condition
on `application.hoursInStage` (or, later, a `telemetry.*` fact) is skipped: recorded as `Skipped` ("Not evaluated for
this application"), with no actions. Its stored snapshot leaves the timing facts out. An `SlaBreached` automation runs
as for anyone (above), and the validator keeps timing facts out of its conditions so the skip can never catch it.

**Issues and events.** `GET /v1/automation-issues` lists blocked and failed runs, newest first. An action that fails
after every retry goes to the dead-letter table, and the error policy's last step (`AutomationFailures`) publishes
`AutomationActionFailed`, which marks the action and its run failed. `AutomationRunFinished` (completed, failed or
blocked) and `TeamNotified` reach the organization's dashboards; quiet outcomes raise nothing, so a skip for an
accommodation never reaches anyone's browser. `GET /v1/applications/{id}/automation-runs` and `.../tasks` show the rest.

**Tasks, notes and tags by hand.** The tasks automations make are the dashboard's My tasks: `GET /v1/tasks` lists open
tasks assigned to the signed-in person, oldest first; `scope=Team` lists the ones assigned to nobody in particular,
`scope=Everyone` every task the person can see, and `status=Completed` the most recent 200 done ones. `POST
/v1/tasks/{id}/complete` records who finished it and when, once, and raises `ApplicationTaskCompleted`; `POST
/v1/tasks/{id}/reopen`, the dashboard's Undo, opens a done task again and raises `ApplicationTaskReopened`. Neither
changes the application, so a rejection review is still decided by a person moving the candidate. Anyone whose role
changes hiring data may complete or reopen any of the organization's tasks; the team shares the work. A person with Create tasks, or an API key with the Write scope, gives the team a task by hand
(`POST /v1/applications/{id}/tasks`, `ApplicationChanges.CreateTaskAsync`): a `Task` with a note of up to 1,000
characters, for one member who can see the candidate (as a mention must) or the whole team, in the application's
current stage. It records a `TaskCreated` timeline entry with the task's note and whoever created it, a person or the key,
and raises `ApplicationTaskCreated`; like every task, it never changes the application. People add notes (`POST /v1/applications/{id}/notes`, a `NoteAdded` timeline entry no
automation reads), and edit and delete them (`PUT` and `DELETE /v1/applications/{id}/notes/{noteId}`; only the author
edits, and the author or a role with Delete anyone's notes deletes, which the API checks against the role in this organization). A
note's text lives in `application_notes`, never in the append-only timeline: its `NoteAdded`, `NoteEdited` and
`NoteDeleted` entries name it by `noteId`, so deleting a note erases its text, earlier edits included, and leaves only who
deleted it and when. A mention is `@[userId]` in the text and a row in `application_note_mentions`; each person a note
newly mentions, other than its author, gets `TeammateMentioned`, which is a card in their open dashboards and, through
the worker, one email that says who and where and never quotes the note. People also add or remove tags (`POST` and `DELETE /v1/applications/{id}/tags`, `TagAdded` and `TagRemoved`
entries). The facts snapshot replays additions and removals in timeline order, ignoring case, so a removed tag is gone
from every later evaluation. No automation action removes a tag. A Manual automation runs from the application's
Automations panel, which sends a new `Idempotency-Key` with each click.

**Scorecards and the reviewer decision.** A scorecard is a library object (ADR-0013): ScreenHire's built-ins
(`BuiltInScorecards`, in code and read only: a team copies one to change it) and an organization's own
(`ScorecardTemplate`, table `scorecard_templates`, tracked in change history). A hiring template's stage names one in
`scorecardTemplateId`; a job's stage holds its own copy (`Scorecard`: the name and the questions), which the API makes
when the job is created from a template, takes a template update, or a workflow edit picks another scorecard. So every
candidate for one job answers the same questions, and editing the library changes the jobs created afterwards, never
a job that has its copy. A template stage that names a scorecard the library does not have is refused at save and at
publish.

A question (`ScorecardQuestion`) has an answer kind: Yes or no, Choice, Multi-choice, Short text, Long text, or one of
the Rating kinds, 1 to 5 (`Rating5`), 1 to 10 (`Rating10`) and Yes or no (`RatingYesNo`). It may be required. Every
scorecard then asks for a required overall `Recommendation` (strong no, no, yes, strong yes) and a note.

- `POST /v1/applications/{id}/scorecards` stores one reviewer's scorecard for a stage that has one and that the
  application has been in. Each answer takes its kind's shape: "yes" or "no", an option id, option ids, text (500 or
  10,000 characters), or a number on the scale; a required question must be answered. It keeps each question as asked
  (label, kind, options), so a later workflow change cannot rewrite it. In one transaction with the
  `application_scorecards` row it appends a `ScorecardSubmitted` timeline entry (the stage and the recommendation),
  publishes `RunAutomations(ReviewerDecision, stage, entry)` and raises the `ScorecardSubmitted` realtime event (ids
  only).
- One scorecard per reviewer and stage, on a unique index. The same submission again, a retried or doubled request,
  returns the stored one with 200 and raises nothing. A different one is 409: a scorecard is final, and the role may
  not update or delete the rows. Deleting the application is the only way one goes, by cascade.
- The trigger fires once per scorecard: the run is keyed on the timeline entry, and `RunAutomations` is
  `[Deduplicated]`.
- A ReviewerDecision automation with a stage fires on that stage's scorecards; without one it fires on any stage's.
  Only its conditions may test `reviewer.recommendation` (EqualTo, In or Exists, on the recommendation names).
- Only a person submits a scorecard. An API key gets 403, and a Viewer gets 403 as on every write. Both may read
  scorecards.
- Integrity output never feeds a scorecard. The sheet shows only the questions, and no automation action writes one. A
  reviewer's "strong no" can queue a rejection review but never reject: only an opted-in knockout rejects.

**The candidate's rating** (ADR-0013 decision 13, `RatingRule`) is one to five stars from people's scorecards:

- Each answered rating question maps onto 1 to 5. A 1 to 5 answer counts as it is. A 1 to 10 answer is spread so that
  1 counts as 1 and 10 as 5 (`1 + (n - 1) * 4 / 9`). A rating Yes counts as 5 and No as 1. Halving a 1 to 10 answer
  would put a 1 at 0.5, off the scale, so the spread keeps both ends on it.
- Each scorecard counts once, as the mean of its answered rating questions; the rating is the mean of those, to one
  decimal, halves away from zero, with the number of scorecards that counted. This differs from a flat mean of every
  answer: the rating opens to each scorecard with its own number (4.3, 3.7 and 4.3 make 4.1), which only a mean of
  means matches.
- A scorecard with no answered rating question counts for nothing, and so does a v0.2 scorecard: its labels (Not
  demonstrated to Strongly demonstrated) were never numbers. Until one counts, the candidate is "Not rated yet". v0.2
  scorecards still read as they were given.
- It averages people's judgements about the work only: never integrity results, AI signals or the stage, which is a
  decision already made. It is worked out on every read (`RatingReads`) and never stored.
- No automation reads it: `DefinitionValidator` refuses a condition on it with the reason.
- A reviewer sees it only after giving their own (`ReviewerGate`). A reviewer is a member with an open Review task
  (`ApplicationTaskKind.Review`, made by AssignReviewer) assigned to them on the application that their scorecards have
  not answered. A review in a stage with a scorecard is answered only by their scorecard for that stage (the task's
  `stageId`), so scoring another stage does not show it. A review in a stage without a scorecard, or one made before
  tasks kept their stage, is answered by any scorecard of theirs submitted since it was assigned. Until then the API leaves the rating (`AfterYourScorecard`, no number),
  the other scorecards, the others' recommendations in the timeline and automation runs out of what they read. A
  Review task for the whole team holds nothing back, and neither does an API key. Ticking the task done also shows it.
- The rating is on `ApplicationSummary` and `ApplicationDetail`, so the job's table, the candidates list and the
  candidate page show it, as the reader may see it. Each `ScorecardEntry` has its own `rating`.

**Follow-up questionnaires.** The link is the careers board's `/questionnaires/{token}`. Its token names the
organization and the request, protected with ASP.NET Core Data Protection under its own purpose
(`ScreenHire.Questionnaire.Link.v1`, `QuestionnaireLinks`) and time-limited. The worker issues it and the API reads it,
so the two share one key ring in Postgres (`data_protection_keys`, `AddScreenHireDataProtection`) under one application
name; the worker builds the careers URL from the same `ScreenHire:Domains` settings as the API.

- The request's `expiresAt` (14 days after the latest send) decides whether a link has expired, against the API's
  clock, so the page can say "expired" rather than "not found", and a second send revives every link to the request.
  The token's own Data Protection limit is that expiry plus 30 days; after it the token reads as forged.
- `GET /v1/public/organizations/{slug}/questionnaires/{token}` returns the organization, the job title, the
  questionnaire's name and a status: `Open` (with the questions, knockout settings left out), `Answered`, `Expired`, or
  `Closed` once the questionnaire left the workflow or the job or application ended. A forged token, or one for another
  organization than the slug's, is 404.
- `POST` to the same path takes the answers, checked with `ApplicationForm.Validate` against the follow-up's questions.
  In one transaction it stores them with a copy of the questions on the request, appends the `QuestionnaireCompleted`
  entry, publishes `RunAutomations` keyed on that entry (`[Deduplicated]`) and raises `QuestionnaireAnswered` (ids
  only) to open dashboards. An answered link is 409, and an expired or closed one is 410. Two writes racing meet the
  row's concurrency check: a second answer is 409, and an answer that lost to a resend is 503 with nothing saved, so the
  careers page keeps the answers and the candidate sends again. Both routes are anonymous and rate limited with the `apply` policy.
- The careers page collects the answers and nothing else: no device check, no form telemetry, no file (CLAUDE.md
  non-negotiable 4). [apply-flow.md](apply-flow.md) §8 has the page.
- The candidate page lists each follow-up in its own panel, with the answers once given, read against the copied
  questions; Activity shows each send and the completion.

**Validate and simulate.** `POST /v1/workflows/validate` returns the validator's errors with their paths, 200 either
way. `POST /v1/workflows/simulate` refuses an invalid workflow, then runs a sample facts snapshot through the same
evaluator for one trigger and lists each triggered automation's outcome, rule results and actions.

**Decisions that differ from the text above.**

- A ReviewerDecision automation counts as signal-driven like a stage trigger: on a stage a signal-driven automation
  moves applications into, or with no stage once any signal-driven automation moves applications. A person decides in
  between, but the candidate reached that reviewer because of the signal, so the guardrail stays conservative. Such
  an automation may route, never queue a rejection.
- v0.3 reverses v0.2's "nothing averages scorecards" (ADR-0013): rating questions average into the candidate's
  rating, under the limits above. Rating scales are plain numbers with Low and High under their ends, not labels, and
  a question counts toward the rating because its kind is in the Rating group, not through a switch.
- Signal-driven automations may not use `NotifyTeam` even though "notify" reads as routing. ADR-0004 lists route, tag,
  assign and request verification, and the validator keeps to that list.
- A signal may move an application into a screening, assessment, verification or interview stage only, not any
  non-terminal stage: a custom stage named "Not progressing" would be a rejection under another name.
- Conditions stay one level (section 7) rather than a recursive tree, because a recursive schema makes the OpenAPI
  document and the generated client recursive.
- A `TimeElapsed` run is keyed on its stage entry but reads the timeline as it stood at its due time, so a tag added
  while it waited counts. Whether it fires at all is decided when it is handled: if the application has left the stage
  entry by then, even after the due time, it does nothing.
- "Cancelled on stage exit" means the timer is dropped when it fires, not removed when the stage is left: its scheduled
  envelope stays in Wolverine's tables until it falls due (up to 8760 hours), then finds the application has left that
  stage entry and does nothing. Removing scheduled envelopes by hand would couple the engine to Wolverine's tables.
- A manual run is keyed on the client's `Idempotency-Key` when it sends one (a version 8 UUID from SHA-256 of the
  application, the automation and the key), so a retried POST runs nothing twice. Without the header each request is
  its own run: one click, one intent.
- A job's applications do not keep the workflow version they arrived under: the engine runs the job's current
  workflow for all of them (section 3 says otherwise). An application keeps its `workflowVersion` as a record, and a
  `Reject` whose run started before a workflow change falls back to a rejection review, as above.
- Transactional email in the worker needs `ScreenHire:Email:From` and the AppHost's Mailpit connection. Without
  either, no sender is registered and email actions are skipped. The production provider is still to be chosen.
- Overdue is computed on screen from `slaDueAt` and the dashboard's clock, with no realtime event when an
  application falls overdue. Overdue changes with time, not with an event, so an event would only mark the moment of
  crossing while the clock already redraws every minute. An event would also need a timer for every stage entry in
  every job, automations or not. `JobWorkflowChanged` refetches the application lists, so a changed limit shows at
  once.
- A breach timer is fixed when the stage is entered and is never moved: removing or lengthening a stage's limit drops a
  waiting timer when it fires, and shortening one does not bring it forward. Applications already in a stage when a
  limit or an `SlaBreached` automation is added get no timer, though the screens show them overdue. Moving a timer
  would mean rewriting Wolverine's scheduled envelopes, and `[Deduplicated]` would drop a rescheduled message with
  the same key anyway.
- An `SlaBreached` automation counts as signal-driven on a stage a signal moves applications into, and with no stage
  once any signal-driven automation moves applications, like `ReviewerDecision`. There it may only assign a reviewer
  or add a tag: `NotifyTeam` and `CreateTask` are outside guardrail 1's list.
- "At one remove" from a breach reaches as far as it does for a signal (`DrivenAutomations`, one walk for both): a
  negated tag rule counts once a breach adds any tag, and once a breach-tag reader moves applications, that stage's
  triggers, every `StageExited` automation and every stage-less `ReviewerDecision` count too. Narrower would be
  friendlier, but the stage a move leaves is unknowable at save time, and one rule for both keeps the validator and the
  guardrail it shadows in step. Only the breach itself is promised to run for a candidate with an accommodation: a
  chained automation with a timing condition or a `TimeElapsed` trigger is skipped as guardrail 4 says, which drops a
  reminder and never harms anyone.
- The candidates page offers no bulk email on the Overdue view, as on an integrity view: a "not moving forward"
  email to every overdue candidate would count the team's delay against them. One candidate at a time is still a
  person's decision.
- The validator checks only that an `emailTemplate` is a built-in id or a Guid, because it also runs in the worker,
  which cannot know the organization's templates. The API checks that an organization template exists when a person
  saves a workflow or validates it in the builder (`EmailTemplateReferences`).
- A `QuestionnaireCompleted` trigger's questionnaire is optional, not required as a stage trigger's stage is: the
  system templates' versions before `AddFollowUpQuestionnaires` carry a bare one, and a required id would make the
  engine block every automation of each job copied from them.
- Follow-up answers are not integrity-analysed: the integrity engine reads the apply form only. A follow-up asks what
  the hiring team needs after a person or an automation chose to ask, and nothing in it is a signal.
- Follow-up questionnaires take no file questions, so their page needs no upload island and works without JavaScript.
- `SendQuestionnaire` stays off the list of actions a signal-driven automation may use: asking a candidate for more
  because a signal fired would act on the signal as a finding about them (ADR-0004 lists route, tag, assign and
  request verification).
- The Data Protection key ring moved to Postgres for the API too, so its sign-in cookies, antiforgery tokens and
  telemetry sessions survive a restart. The keys are stored as Data Protection writes them; production should protect
  them at rest (status.md, owner actions).

## Open questions

- Approval steps (mid-market): model as a stage kind or a cross-cutting action? (Leaning: `RequestApproval` action +
  `ApprovalCompleted` trigger.)
- Agency mode: templates scoped per client company.
- Interview scheduling: build vs integrate (Cal.com self-host / Nylas / Cronofy).
