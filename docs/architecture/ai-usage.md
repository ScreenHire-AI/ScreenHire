---
type: Design
title: "AI usage, budgets and the cap"
description: "What ScreenHire records about every model call and what hangs on it: the provider presets for an own key, the append-only usage ledger with the provider's request id, tokens and cost, how a cost is found without a price list in code, the monthly budget, its 50, 80 and 100 percent alerts, the spike rule, the Owner's hard cap that pauses only the AI-based checks, resuming them, retention, who may see usage, the CSV, and what is not recorded."
tags: [verification, llm, usage, billing, notifications, tenancy]
sources:
  - resource: src/ScreenHire.Domain/Verification/AiUsageCall.cs
  - resource: src/ScreenHire.Domain/Verification/AiUsageRules.cs
  - resource: src/ScreenHire.Domain/Verification/VerificationSettings.cs
  - resource: src/ScreenHire.Infrastructure/Verification/Llm/AiUsageRecorder.cs
  - resource: src/ScreenHire.Infrastructure/Verification/Llm/AiUsageQueries.cs
  - resource: src/ScreenHire.Infrastructure/Verification/VerificationOptions.cs
  - resource: src/ScreenHire.Application/Verification/Models/ModelPresets.cs
  - resource: src/ScreenHire.Application/Verification/Models/OrganizationModels.cs
  - resource: src/ScreenHire.Application/Verification/Models/AiUsageSupport.cs
  - resource: src/ScreenHire.Application/Verification/Models/VerificationAlerts.cs
  - resource: src/ScreenHire.Application/Verification/AiUsageSweeps.cs
  - resource: src/ScreenHire.Application/Verification/AiUsageCsv.cs
  - resource: src/ScreenHire.Application/Verification/Content/ContentMessages.cs
  - resource: src/ScreenHire.Application/Verification/Content/RecheckApplicationConsistency.cs
  - resource: apps/api/Endpoints/AiUsageEndpoints.cs
  - resource: apps/api/Contracts/AiUsageContracts.cs
  - resource: apps/dashboard/src/lib/components/ai-usage-panel.svelte
  - resource: apps/dashboard/src/lib/components/ai-usage-limits-form.svelte
---

# AI usage, budgets and the cap

The consistency check ([verification engine](verification-engine.md) section 10.10) is the only feature that calls a language
model today. People are wary of token use, and a provider's own dashboard shows only the provider's side, so ScreenHire records
every call itself and shows it: the same numbers an organization can put next to its provider's bill, one to one, with the
provider's own id for each response. The built-in key is billed at the provider's price plus a margin
([billing](billing.md), a later task bills it). An organization's own key costs it nothing extra.

## Providers

An organization's own model is one of six presets (`ModelPresets`). A preset fills in the address and the way the key is sent;
the person enters the key and the model. The model list is asked of the provider's own models endpoint when the person presses
Load models, and no model name is kept in the code.

| Preset | Address | Models list |
|---|---|---|
| Anthropic | the SDK's own | yes |
| OpenAI | `https://api.openai.com/v1` | yes |
| OpenRouter | `https://openrouter.ai/api/v1` | yes |
| Azure OpenAI | the organization's resource (the v1 path is added to a bare resource address) | no: its list is base models, not the deployments a key calls, so the model field is the deployment name |
| Google Gemini | `https://generativelanguage.googleapis.com/v1beta/openai` | yes, with Google's `models/` prefix dropped |
| Other | any https OpenAI-compatible address the person types | yes |

A fixed address is the provider's public API address and is refused in a request, so a preset can never be pointed somewhere
else. A typed address goes through the same public-only handler as webhooks (`PublicOnlyHandler`), so it can never reach into
ScreenHire's own network. Keys are stored as before: protected with Data Protection per organization, write-only, never in a
response, log or History line. The deployment's model (`ScreenHire:Verification:Llm`) stays the default, and under the fake model
every preset answers with the fake, so no test needs a key or the internet.

## The ledger

`ai_usage_calls` has one row for each call a model answers or fails to: when it started, which check (`consistency.extraction`,
`consistency.comparison`, or `model-test` for the settings page's Test connection), the application, the provider and model,
whether the built-in key or the organization's own paid, the provider's request id, input and output tokens, the cost when it
can be known, how long it took, and how it ended (`Ok`, `Error`, `TimedOut`). A failed call stores the exception's type name
and nothing else, because a provider's text can echo the request. No row holds anything that was sent or received.

- **Recorded once.** `UsageRecordingChatClient` wraps the client of one check and collects the rows in memory. The handler
  saves them in the same save that completes the check, or before it rethrows a failure, so a call that cost money is not lost
  to a crash after it. A redelivered message finds a finished check and calls nothing, so it records nothing again. The SDK's own
  retries are one row.
- **The request id** is the provider's id for the response (Anthropic `msg_...`, OpenAI `chatcmpl-...`, OpenRouter `gen-...`), which
  finds the call in the provider's logs. The HTTP request-id header is not exposed by the chat abstraction.
- **Whose key** comes from the organization's settings, not from the client that answered. Under the fake model an organization
  on its own key still records own-key calls.
- **Append-only, and organization-level.** The application role has no UPDATE on the table. `application_id` is a plain id with
  no foreign key and no job scope: the record outlives an erased candidate, and Owners and Admins see every job's calls. Row-level
  security isolates organizations as for every tenant table.

### Cost

A call's cost, in US dollars, is found in this order (`AiUsageCost`):

1. The cost the provider reported (OpenRouter sends `usage.cost`; a small HTTP handler reads it into the call in progress).
2. Tokens times a price per million tokens: for the built-in key from configuration, `ScreenHire:Verification:Llm:Prices:<modelId>:Input`
   and `:Output`; for an own key from the two prices the organization entered. There is no price table in code, because it would
   go stale. A side that used no tokens needs no price.
3. Otherwise the cost is unknown (`CostBasis` `Unknown`, a null cost), never a made-up zero. A money budget or cap counts an unknown
   cost as nothing, and the usage page says how many calls there are.

The margin is `ScreenHire:Billing:Ai:Margin` (default 0.20, 0 to 10), read by the billing of this use.

## Budget, alerts and the cap

An Owner sets a monthly budget and an optional cap, in tokens or in dollars (one unit for both), with
`PUT /v1/organization/ai-usage/limits`. That route needs Manage billing, an Owner-only permission, so an Admin who may edit the
rest of Verification cannot change spend controls. The month is the UTC calendar month and its use is the sum of the ledger.

- **Budget.** Warns, stops nothing. A notification in the bell, and an email or Slack message when the organization chose them
  (the existing "budget alert" boxes), go once each at 50%, 80% and 100%. The alert key is `yyyy-MM:budget-N`, claimed with a unique
  row (`ai_usage_alerts`), so two checks that pass a share together tell nobody twice.
- **Cap.** At the cap the AI-based checks pause until next month or until the cap is raised. The consistency check is recorded as
  `Paused`, the rule-based checks keep running, and the candidate's Verification tab lists the AI checks as paused in words that say
  it is no result about the candidate (`RunNotes.AiChecksPaused`; never a signal, never a level). The cap is read once before
  each check, so checks that start together can overshoot it by a few checks' worth. Reaching it raises a `ai-usage-cap` notification.
- **Defaults.** With no budget or cap of its own, ScreenHire's key falls back to `Llm:DefaultMonthlyTokenBudget` and
  `Llm:DefaultMonthlyTokenCap` (2,000,000 tokens each; a cap of 0 turns the deployment's off), so the key stops where it always did.
  An own key has neither unless the Owner sets one.
- **Resuming.** Raising or removing the cap, and a daily run at 05:12 UTC, ask again for the paused checks of an organization whose
  cap now allows it (200 a run). The asked-again check runs, and the pipeline reads it again, so the tab stops saying the AI
  checks were paused.
- **Spike.** A daily run at 06:07 UTC judges the previous completed UTC day, in tokens, from both keys: it is a spike when it used at
  least `ScreenHire:AiUsage:Spike:MinimumTokens` (100,000) and at least `Factor` (3) times the average of the `LookbackDays` (14) days
  before it, and at least `MinimumActiveDays` (3) of those days had any use, so a new organization never spikes. The alert key is
  `yyyy-MM-dd:spike`.

| Notification kind | Key | Link | Audience |
|---|---|---|---|
| `ai-usage-budget` | `yyyy-MM:budget-N` | `/verification` | who holds View AI usage |
| `ai-usage-cap` | `yyyy-MM:cap` | `/verification` | who holds View AI usage |
| `ai-usage-spike` | `yyyy-MM-dd:spike` | `/verification` | who holds View AI usage |

## Who sees it

`ViewAiUsage` (Owners and Admins; an Owner may also put it in a custom role, as with Edit settings) reads usage, the call list and
the CSV. The settings read, which every member has, leaves out the limits, what was used and the own key's prices for anyone
without it. Usage is never about a candidate and never shows a score.

| Route | Permission |
|---|---|
| `GET /v1/organization/ai-usage/summary` | View AI usage: totals, by day (in the requester's time zone), by model and by key, and the month against the limits |
| `GET /v1/organization/ai-usage/calls` | View AI usage: the calls, newest first, paged by the id of the last one (`before`), filtered by dates, key, provider, model, outcome, check and application |
| `GET /v1/organization/ai-usage/calls/export` | View AI usage: the same calls as a CSV (RFC 4180, a byte order mark, formula-looking cells neutralised), at most 50,000 rows, said in the file when cut |
| `PUT /v1/organization/ai-usage/limits` | Manage billing: the unit, the budget and the cap |

## Retention

A daily run at 04:42 UTC deletes the organization's ledger rows and alert claims older than its record retention period
(`RetentionSettings`), but never newer than the start of the previous calendar month, so the last full month stays readable for
billing. With no retention set, nothing is deleted.

## Settings

`ScreenHire:Verification:Llm:Prices`, `:DefaultMonthlyTokenBudget` and `:DefaultMonthlyTokenCap` go to the worker, `ScreenHire:AiUsage:*`
to the API and the worker, and `ScreenHire:Billing:Ai:Margin` to the API; the header of `apps/apphost/AppHost.cs` lists them. The
worker's Development settings price the fake model so the local demo and the tests show costs. Prices for the deployed models are
the owner's to set.

## Not recorded

- Calls made before the ledger existed. Their tokens stay on `consistency_checks`.
- A provider's own retries inside one SDK call, and a request the SDK never sent.
- Listing a provider's models: it uses no tokens.
- Streaming calls: none exist.

## Tests

`AiUsageRulesTests` (cost, thresholds, cap, spike, retention floor, CSV), `ModelPresetsTests` (every preset against replayed
answers, the recorder), `AiUsageTests` (Aspire: the ledger, summary, list and CSV, permissions, validation, the cap pausing only
the AI check and resuming it, the cap notification, the spike, retention, row-level security) and `VerificationOrganizationControlsTests`
(the budget alerts and the own key).
