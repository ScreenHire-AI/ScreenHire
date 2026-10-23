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

What the dashboard builds (`apps/dashboard`, routes `/templates`, `/templates/{id}` and `/jobs/{id}/workflow`): one
editor, `#lib/components/workflow/workflow-editor.svelte`, over a template draft or a job's own copy, with four tabs on
the page's header band. A workflow that cannot be edited there (a ScreenHire template, a published version, a closed
job's) opens the same tabs in `workflow-summary.svelte`, which reads it as content, with no form controls. The
validation result is a status badge beside the page title.

- **Pipeline:** the stages in order (reordered with buttons, never only by dragging) and, per stage, its automations
  in a Sheet drawer. Automations whose trigger has no stage (an arriving application, the integrity checks, a manual
  run) sit in a "Whole workflow" lane. The drawer's forms offer only the triggers and actions v1 runs, and the facts
  and operators `FactSchema` allows.
- **Graph:** @xyflow/svelte, rebuilt from the workflow on every change by `toGraph` (`#lib/workflow-graph.ts`): stages
  along the top, one row per automation from trigger to conditions to actions, edges to the stages it fires on and
  moves into. Nodes are not draggable or focusable; each automation node's Edit button opens its form.
- **Questionnaire:** the question list beside a live preview rendered by the shared components in
  `packages/ui/src/questions`, the ones the careers apply page renders. Marking a yes/no question as a knockout shows
  what a knockout may be (an objective eligibility check, never AI use or an accommodation).
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
- `POST /v1/applications/{id}/move`, `GET /v1/applications/{id}/timeline`
- Webhooks: `/v1/webhooks` (below).

### Webhook subscriptions

Integrators subscribe to events instead of polling. An organization's owners and admins manage its endpoints; every
other role gets 403, and another organization's endpoint or delivery is a 404 (tenant filter and row-level security on
`webhook_endpoints` and `webhook_deliveries`).

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
- **Validation** is `DefinitionValidator`: structure (ids, stage and question references, per-kind fields) plus the
  guardrails above as an allowlist. An automation that uses an `integrity.*` or `verification.*` fact, or an
  `IntegrityAssessed` or `VerificationCompleted` trigger, may only request verification, assign a reviewer, add a tag
  or move to a screening, assessment, verification or interview stage. The same holds at one remove, across the whole
  workflow: an automation that reads a tag such an automation adds, or fires on a stage it moves applications into,
  counts as signal-driven too, so a signal cannot be laundered through a tag or a stage. Once any signal-driven
  automation moves applications, every `StageExited` automation counts as well: the stage a move leaves is wherever the
  application is when the action runs, which no definition can know. Only an automation whose
  every condition is a knockout question equal to its disqualifying answer, with `allowAutoReject` set, may `Reject`,
  and it must give the candidate a reason. Accommodation status and the AI-use declaration are not facts. Publishing a
  template version validates it again, so a draft saved under older rules cannot go live.
- **What the validator cannot check:** a question's meaning. A knockout must be an objective eligibility check (right
  to work, a required licence). A knockout on AI use or on needing an accommodation would reject candidates for
  exactly what ADR-0004 and the accommodation guardrail protect; the template builder must say so, and review of
  system templates checks it.
- **Templates** have numbered versions: at most one draft, then published versions that never change. The system
  templates "Software engineer" and "Hourly role" ship in the `AddHiringModel` migration with no organization, and a
  later version of one ships as its own migration (`AddSoftwareEngineerTemplateVersion2`); an organization copies one
  (`copyFromVersionId`) to customise it.
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
timeline entry it appends. `QuestionnaireCompleted` and `ReviewerDecision` are valid in definitions and run through
`RunAutomations`, but nothing raises them yet.

**Facts snapshot.** `AutomationFacts.FromTimeline` rebuilds the facts from the append-only timeline as it stood at the
trigger event (tags from `TagAdded` entries, hours in stage from the last stage entry), so a late or replayed evaluation
sees what the first one saw. The snapshot and every rule's result are stored on the run. Integrity levels come from
the latest `IntegrityAssessed` entry at or before the trigger. Answers and the application source are absent until
the features that store them exist. The facts schema
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
done. Email and webhooks happen before that commit; a crash in between can repeat one once.

| Action | What v1 does |
|---|---|
| `SendEmail` | Renders `application-received` or `stage-change` to the candidate. Skipped when email is not configured. The rejection email is not available here: it says a candidate is out, so only `Reject` sends it. `stage-change` says "moved to the next step", so it is refused at save on entering a rejected or withdrawn stage and skipped when the application is in one by the time it sends. |
| `MoveToStage` | Moves the application and links the timeline entry to the run, which fires the next stage's triggers. Never moves an application out of a terminal stage. |
| `AddTag` | Adds a tag (a `TagAdded` timeline entry). Conditions read tags as `application.tags`. |
| `AssignReviewer` | Creates a `Review` task, for the named member or the whole team. The builder offers the team's Owners, Admins and Recruiters (a Viewer could not complete the task) or "Anyone on the team". |
| `NotifyTeam` | Raises `TeamNotified` to the organization's dashboards, which show the action's note in the shell's notification tray. |
| `CreateTask` | Creates a `Task` with the note. |
| `QueueForRejectionReview` | Creates a `RejectionReview` task. It never moves the application. |
| `Reject` | Only for an opted-in knockout automation. Sends `application-rejected` with the reason, saying the decision was automatic and that a person reviews it on request, then moves to the rejected stage. It re-validates the workflow first. Without email, with the opt-in gone, with a workflow that no longer validates or that changed version since the conditions were checked, it queues a rejection review instead: the candidate must be told why. |
| `CallWebhook` | Posts ids only, with the action key as `Idempotency-Key`, to public addresses only (no loopback, private or link-local targets, checked at connect time; loopback only where `ScreenHire:Webhooks:AllowLoopback` is on, in the local stack and the tests). Signed: `ScreenHire-Signature: t={unix seconds},v1={hex}`, HMAC-SHA256 of `{t}.{body}` under the organization's key: hex HMAC-SHA256 of `webhook-signing:{organizationId}` under `ScreenHire:Webhooks:SigningKey` (`WebhookSignature.OrganizationKey`; the AppHost generates the platform key locally). A receiver is given its organization's key only, so it cannot sign for another organization. Never through a proxy, which would resolve the target past the address check. Without a key the action fails as an issue instead of sending something unverifiable. No HTTP retries of its own: Wolverine retries the action. |
| `RequestVerification`, `SendQuestionnaire`, `ScheduleInterview` | Refused at save. Verification is phase 2; the other two need features that do not exist. Triggers `VerificationCompleted`, `CandidateReplied` and `InterviewScheduled` are refused for the same reason. |

**Loops and depth.** A run records the chain of automations whose actions led to it. An automation already in its own
chain is a loop, and a chain of `AutomationEvaluator.MaxDepth` (10) stops there; either way the run is `Blocked` and
nothing runs. The engine also re-validates the job's workflow before running anything, so a definition saved under
older rules is blocked rather than run. A tag counts as signal-tainted by the evaluator's own comparison, so the validator and the
worker cannot disagree about which rules read it.

**Accommodation.** For an application with an accommodation, an automation with a `TimeElapsed` trigger or a condition
on `application.hoursInStage` (or, later, a `telemetry.*` fact) is skipped: recorded as `Skipped` ("Not evaluated for
this application"), with no actions. Its stored snapshot leaves the timing facts out.

**Issues and events.** `GET /v1/automation-issues` lists blocked and failed runs, newest first. An action that fails
after every retry goes to the dead-letter table, and the error policy's last step (`AutomationFailures`) publishes
`AutomationActionFailed`, which marks the action and its run failed. `AutomationRunFinished` (completed, failed or
blocked) and `TeamNotified` reach the organization's dashboards; quiet outcomes raise nothing, so a skip for an
accommodation never reaches anyone's browser. `GET /v1/applications/{id}/automation-runs` and `.../tasks` show the rest.

**Tasks, notes and tags by hand.** The tasks automations make are the dashboard's My tasks: `GET /v1/tasks` lists open
tasks for the signed-in person and the whole team (no assignee), oldest first; `scope=Everyone` adds teammates' and
`status=Completed` lists the most recent 200 done ones. `POST /v1/tasks/{id}/complete` records who finished it and when,
once, and raises `ApplicationTaskCompleted`; it never changes the application, so a rejection review is still decided
by a person moving the candidate. Anyone whose role changes hiring data may complete any of the organization's tasks;
the team shares the work. People add notes (`POST /v1/applications/{id}/notes`, a `NoteAdded` timeline entry no
automation reads) and add or remove tags (`POST` and `DELETE /v1/applications/{id}/tags`, `TagAdded` and `TagRemoved`
entries). The facts snapshot replays additions and removals in timeline order, ignoring case, so a removed tag is gone
from every later evaluation. No automation action removes a tag. A Manual automation runs from the application's
Automations panel, which sends a new `Idempotency-Key` with each click.

**Validate and simulate.** `POST /v1/workflows/validate` returns the validator's errors with their paths, 200 either
way. `POST /v1/workflows/simulate` refuses an invalid workflow, then runs a sample facts snapshot through the same
evaluator for one trigger and lists each triggered automation's outcome, rule results and actions.

**Decisions that differ from the text above.**

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

## Open questions

- Approval steps (mid-market): model as a stage kind or a cross-cutting action? (Leaning: `RequestApproval` action +
  `ApprovalCompleted` trigger.)
- Agency mode: templates scoped per client company.
- Interview scheduling: build vs integrate (Cal.com self-host / Nylas / Cronofy).
