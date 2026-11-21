---
type: Design
title: "AI usage, budgets and the cap"
description: "What ScreenHire records about every model call and what hangs on it: the provider presets for an own key, the append-only usage ledger with the provider's request id, tokens and cost, how a cost is found without a price list in code, the one amount a customer sees for it, the monthly budget in billed dollars, its 50, 80 and 100 percent alerts, the spike rule, the Owner's hard cap that pauses only the AI-based checks, resuming them, retention, who may see usage, the CSV, and what is not recorded."
tags: [verification, llm, usage, billing, notifications, tenancy]
sources:
  - resource: src/ScreenHire.Domain/Verification/AiUsageCall.cs
  - resource: src/ScreenHire.Domain/Verification/AiUsageRules.cs
  - resource: src/ScreenHire.Domain/Verification/VerificationSettings.cs
  - resource: src/ScreenHire.Infrastructure/Verification/Llm/AiUsageRecorder.cs
  - resource: src/ScreenHire.Infrastructure/Verification/Llm/AiUsageQueries.cs
  - resource: src/ScreenHire.Infrastructure/Verification/Llm/AiUsageLimitsInForce.cs
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
  - resource: apps/dashboard/src/routes/(app)/verification/usage/+page.svelte
  - resource: apps/dashboard/src/lib/components/verification-area/activity-rows.svelte
  - resource: apps/dashboard/src/lib/components/verification-area/limits-summary.svelte
  - resource: apps/dashboard/src/lib/components/ai-usage-limits-form.svelte
  - resource: apps/dashboard/src/lib/ai-usage.ts
---

# AI usage, budgets and the cap

The consistency check ([verification engine](verification-engine.md) section 10.10) is the only feature that calls a language
model today. People are wary of token use, and a provider's own dashboard shows only the provider's side, so ScreenHire records
every call itself and shows it: the same numbers an organization can put next to its provider's bill, one to one, with the
provider's own id for each response. The built-in key is billed at the provider's price plus a margin
([billing](billing.md#ai-usage): the worker reports each call to the payment provider's meter every 15 minutes). An organization's own
key costs it nothing extra. Every amount shown for AI use is the one the customer pays (see "The amount shown" below).

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
   cost as nothing, and the Usage tab says how many calls there are.

The margin is `ScreenHire:Billing:Ai:Margin` (default 0.20 from `packages/plans/ai.json`, 0 to 10). A built-in call is charged its cost times
(1 + margin) in whole micro-dollars, once, and `ai_usage_charges` records which ledger calls are settled, because this table is
append-only and its rows commit late ([billing](billing.md#ai-usage)). A call whose cost is unknown is never charged.

### The amount shown

A customer sees one amount for AI use, the amount they pay, and never our margin (the owner's walk-through, 2026-11-21). The ledger keeps
the provider's price; what the app shows is `AiCharge.Billed`:

| Key | Amount | Label |
|---|---|---|
| ScreenHire's | the cost times (1 + margin), rounded to a micro-dollar: the figure the meter is sent | Cost |
| The organization's own | the provider's price, which that provider charges; ScreenHire never bills it | Charged by your provider (estimate) |

The margin is `BillingOptions.AiMarginInForce`: `Ai:Margin` while AI use is billed (the sandbox, or Stripe with `Ai:StripePriceId` set), 0 otherwise, because nobody is billed for it then. The
summary (totals, by day, by model, by key), the call list, its CSV's `cost_usd`, the Activity log and its CSV, the budget and the cap, and
the Billing page all use it (`AiUsageQueries.BilledAmount` where Postgres sums it). On the Usage tab the Cost tile is ScreenHire's key, and
an own key's amount is its own line under it ("$0.12 more charged by your provider (estimate)"), its own column in the by-day table and
"estimate" after its cost in the by-model table: the two are never added into one figure. Money shows with two decimals, and an amount
above zero that would round to $0.00 reads "Under $0.01".

## Budget, alerts and the cap

An Owner sets a monthly budget and an optional cap, in US dollars to the cent, with `PUT /v1/organization/ai-usage/limits`. Both count
billed dollars, the amount above (owner decision, 2026-11-21), so a cap of $50 stops at $50 billed. A limit needs a price for the key in
use: an own key's two prices, or the deployment's price for its model; with none the route refuses one, on the field it is about, and the
form says why instead of showing its fields. Removing both always works, and the form offers Remove limits (after a confirmation)
to an Owner who set one before the key lost its price. That route needs Manage billing, an Owner-only permission, so an Admin who may edit the
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
- **A limit stored in tokens.** Limits used to be set in tokens or dollars, and the deployment's default is still tokens. No stored
  row is changed (no destructive migration). `AiUsageLimitsInForce`, which the API and the worker both use, converts a token limit once
  to dollars at the key's price and the usual mix of 80% input and 20% output tokens (`AiUsageMix.BilledPerMillion`: ScreenHire's key at
  `LlmOptions.ConfiguredPrice` times (1 + margin), an own key at its own two prices), to the cent, and then counts it against the dollars
  billed, like any other. At 1.00 in and 4.00 out per million tokens and a 20% margin that is 1.92 dollars a million, so the 2,000,000-token
  default is $3.84. "Used of amount", the share (`percent`), the badge, the alerts at 50%, 80% and 100% and the pause therefore all count
  the same billed dollars. Where the key has no price, the limit stays in tokens and counts tokens, and the limits panel says once "In tokens
  until the AI model has a price". The form opens with an Owner's own limit in dollars (one stored in tokens shown converted), so saving it
  moves both limits to dollars; the default leaves the fields empty and names its figure ("$3.84 a month"). The API reads `Llm:Prices`, the
  model ids and the defaults, which the AppHost forwards to it as to the worker, and which a deployment gives both (below).
- **Resuming.** Raising or removing the cap, and a daily run at 05:12 UTC, ask again for the paused checks of an organization whose
  cap now allows it, `ResumeBatchSize` (200) at a time: a full batch sends the next one, after the last check it asked again. The asked-again check runs, and the pipeline reads it again, so the tab stops saying the AI
  checks were paused.
- **Spike.** A daily run at 06:07 UTC judges the previous completed UTC day, in tokens, from both keys: it is a spike when it used at
  least `ScreenHire:AiUsage:Spike:MinimumTokens` (100,000) and at least `Factor` (3) times the average of the `LookbackDays` (14) days
  before it, and at least `MinimumActiveDays` (3) of those days had any use, so a new organization never spikes. The alert key is
  `yyyy-MM-dd:spike`.

| Notification kind | Key | Link | Audience |
|---|---|---|---|
| `ai-usage-budget` | `yyyy-MM:budget-N` | `/verification/usage` | who holds View AI usage |
| `ai-usage-cap` | `yyyy-MM:cap` | `/verification/usage` | who holds View AI usage |
| `ai-usage-spike` | `yyyy-MM-dd:spike` | `/verification/usage` | who holds View AI usage |

## Who sees it

`ViewAiUsage` (Owners and Admins; an Owner may also put it in a custom role, as with Edit settings) reads usage, the call list and
the CSV. It needs All jobs, as API keys and webhooks do (`RolePermissions.NeedsAllJobs`): the ledger lists every job's calls, so a
member on Only jobs they're on is refused, and the settings read leaves usage out for them too. The settings read, which every member has, leaves out the limits, what was used and the own key's prices for anyone
without it. Usage is never about a candidate and never shows a score.

| Route | Permission |
|---|---|
| `GET /v1/organization/ai-usage/summary` | View AI usage: totals, by day (in the requester's time zone, each day split between ScreenHire's key and the own key in tokens and cost), by model and by key, the month against the limits, and when the newest call happened (`lastCallAt`, whatever the range, null before the first) |
| `GET /v1/organization/ai-usage/calls` | View AI usage: the calls, newest first, paged by the id of the last one (`before`), filtered by dates, key, provider, model, outcome, check and application |
| `GET /v1/organization/ai-usage/calls/export` | View AI usage: the same calls as a CSV (RFC 4180, a byte order mark, formula-looking cells neutralised), at most 50,000 rows, said in the file when cut |
| `PUT /v1/organization/ai-usage/limits` | Manage billing: the budget and the cap, in dollars |

The call list is newest first by design (it pages by a server cursor), and the by-model table is ordered by cost, so neither has
sortable headers.

In the dashboard this is the Verification area ([verification-engine.md](verification-engine.md) section 10.11). The Usage tab shows the
summary: calls, tokens and the amount for AI use for a range (ScreenHire's key as Cost, an own key apart as its provider's estimate), tokens by day for ScreenHire's key against the own key with its table, the month against
the budget and cap, and use by model and key. The Model calls tile ends with "Last call 5 minutes ago" (`lastCallAt`, the newest call
whatever the range, left out before the first; design system T7). The calls themselves, with the provider's request id on each, are in the Activity log
beside the rule-based check runs (section 10.12), where the CSV is `GET /v1/verification/activity/export`; `GET .../ai-usage/calls`
and its export stay for API use. The Notifications tab holds the budget and cap (an Owner's form, an Admin's read-only facts) and the
two alerts.

## Retention

A daily run at 04:42 UTC deletes the organization's ledger rows and alert claims older than its record retention period
(`RetentionSettings`), but never newer than the start of the previous calendar month, so the last full month stays readable for
billing. With no retention set, nothing is deleted.

## Settings

`ScreenHire:Verification:Llm:*` (the prices, the default budget and cap) goes to the worker and the API, `ScreenHire:AiUsage:*`
to the API and the worker, and `ScreenHire:Billing:Ai:*` to the API and the worker; the header of `apps/apphost/AppHost.cs` lists them. The
worker's and the API's Development settings price the fake model, so the local demo and the tests show costs and the default in
dollars. In a deployment the API and the worker both get the provider and the model ids (`verification-llm-*` parameters,
`AzurePublish.cs`), and both load the Key Vault into configuration at start, so the owner prices a deployed model once, as vault
secrets `ScreenHire--Verification--Llm--Prices--<modelId>--Input` and `--Output`. Until then the default shows in tokens.

## Not recorded

- Calls made before the ledger existed. Their tokens stay on `consistency_checks`.
- A provider's own retries inside one SDK call, and a request the SDK never sent.
- Listing a provider's models: it uses no tokens.
- Streaming calls: none exist.

## Tests

`AiUsageRulesTests` (cost, the amount shown on each key, a token limit converted at the key's billed price or left in tokens, thresholds, cap, spike, retention floor, CSV), `ModelPresetsTests` (every preset against replayed
answers, the recorder), `AiUsageTests` (Aspire: the ledger, summary, list and CSV in billed amounts and the Billing page's same figure, an own key's price beside ScreenHire's billed amount, limits in billed dollars with a token limit converted or left in tokens, permissions, validation in dollars, the cap pausing only
the AI check and resuming it, the cap notification, the spike, retention, row-level security) and `VerificationOrganizationControlsTests`
(the budget alerts and the own key).
