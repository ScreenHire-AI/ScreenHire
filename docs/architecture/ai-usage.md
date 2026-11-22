---
type: Design
title: "AI usage, credits and the pause"
description: "What ScreenHire records about every model call and what hangs on it: the provider presets for an own key, the append-only usage ledger with the provider's request id, tokens and cost, how a cost is found without a price list in code, the one amount a customer sees for it, the prepaid AI credits each call on ScreenHire's key draws, the pause of only the AI-based checks at zero and resuming them, the spike rule, retention, who may see usage, the CSV, and what is not recorded."
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
  - resource: apps/dashboard/src/routes/(app)/settings/billing/+page.svelte
  - resource: apps/dashboard/src/lib/components/ai-usage-figures.svelte
  - resource: apps/dashboard/src/routes/(app)/verification/activity/+page.svelte
  - resource: apps/dashboard/src/lib/components/verification-area/activity-rows.svelte
  - resource: apps/dashboard/src/lib/components/ai-credits-panel.svelte
  - resource: apps/dashboard/src/lib/ai-usage.ts
---

# AI usage, credits and the pause

The consistency check ([verification engine](verification-engine.md) section 10.10) is the only feature that calls a language
model today. People are wary of token use, and a provider's own dashboard shows only the provider's side, so ScreenHire records
every call itself and shows it: the same numbers an organization can put next to its provider's bill, one to one, with the
provider's own id for each response. The built-in key is paid for in advance as AI credits, at the provider's price plus a margin
([billing](billing.md#ai-credits), ADR-0032): each call draws its amount from the balance in the save that records it. An organization's own
key costs it nothing extra and never touches the credits. Every amount shown for AI use is the one the customer pays (see "The amount shown" below).

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
3. Otherwise the cost is unknown (`CostBasis` `Unknown`, a null cost), never a made-up zero. Such a call draws no credits, and Billing's
   AI use section says how many calls there are.

The margin is `ScreenHire:Billing:Ai:Margin` (default 0.20 from `packages/plans/ai.json`, 0 to 10). A built-in call is charged its cost times
(1 + margin) in whole micro-dollars, once: its debit in `credit_entries` is keyed on the call ([billing](billing.md#ai-credits)). A call whose
cost is unknown is never charged.

### The amount shown

A customer sees one amount for AI use, the amount they pay, and never our margin (the owner's walk-through, 2026-11-21). The ledger keeps
the provider's price; what the app shows is `AiCharge.Billed`:

| Key | Amount | Label |
|---|---|---|
| ScreenHire's | the cost times (1 + margin), rounded to a micro-dollar: the amount drawn from the credits | Cost |
| The organization's own | the provider's price, which that provider charges; ScreenHire never bills it | Charged by your provider (estimate) |

The margin is `BillingOptions.AiMarginInForce`: `Ai:Margin` whenever a payment provider is set up, 0 with billing off, because nobody is billed then. The
summary (totals, by day, by model, by key), the call list, its CSV's `cost_usd`, the Activity log and its CSV, the credit debits and
the Billing page all use it (`AiUsageQueries.BilledAmount` where Postgres sums it). In Billing's AI use section the Cost tile is ScreenHire's key, and
an own key's amount is its own line under it ("$0.12 more charged by your provider (estimate)"), its own column in the by-day table and
"estimate" after its cost in the by-model table: no screen adds the two into one figure. The API's `totals.cost` and each day's `cost` do
sum both keys, and `byKeySource` and the per-key day fields keep them apart. Money shows with two decimals, and an amount
above zero that would round to $0.00 reads "Under $0.01".

## Credits and the pause

The monthly budget and cap are gone (ADR-0032). What the checks may spend on ScreenHire's key is the organization's prepaid AI credits,
which an Owner buys and can top up by auto-reload on Billing ([billing](billing.md#ai-credits)).

- **The debit.** Every call on ScreenHire's key with a known cost draws its amount from the balance, in the same save as its ledger row
  (both the save that completes the check and the one before a failure is rethrown). An own key's calls never do.
- **The pause.** Before each check on ScreenHire's key, a balance at or below zero records the consistency check as `Paused` with
  `RunNotes.AiCreditsPaused`; the rule-based checks keep running, and the candidate's Verification tab lists the AI checks as paused in
  words that say it is no result about the candidate (never a signal, never a level). A check stored as paused by the old cap keeps its note
  (`RunNotes.AiChecksPaused`), which still reads as paused. The balance is read once before each check, so checks that start together can
  take it a little below zero. With billing off there is no balance and nothing pauses.
- **Resuming.** A grant of credits, and a daily run at 05:12 UTC, ask again for the paused checks of an organization that has credits again
  (or no longer draws on them: an own key, or billing off), `ResumeBatchSize` (200) at a time: a full batch sends the next one, after the
  last check it asked again. The asked-again check runs, and the pipeline reads it again, so the tab stops saying the AI checks were paused.
- **Alerts.** The low-balance and empty alerts and a refused auto-reload go to the Owners ([billing](billing.md#ai-credits)).
- **Spike.** A daily run at 06:07 UTC judges the previous completed UTC day, in tokens, from both keys: it is a spike when it used at
  least `ScreenHire:AiUsage:Spike:MinimumTokens` (100,000) and at least `Factor` (3) times the average of the `LookbackDays` (14) days
  before it, and at least `MinimumActiveDays` (3) of those days had any use, so a new organization never spikes. The alert key is
  `yyyy-MM-dd:spike`. It goes to who holds View AI usage, in the bell, and by email and Slack by each person's choice in Your account >
  Notifications (starting from the AI usage row of Settings > Notification defaults), claimed with a unique row (`ai_usage_alerts`).

| Notification kind | Key | Link | Audience |
|---|---|---|---|
| `ai-usage-spike` | `yyyy-MM-dd:spike` | `/settings/billing#ai-use` | who holds View AI usage |
| `ai-credits-low`, `ai-credits-empty`, `ai-credits-reload-failed` | `credits` | `/settings/billing#ai-credits` | who holds Manage billing ([billing](billing.md#ai-credits)) |

## Who sees it

`ViewAiUsage` (Owners and Admins; an Owner may also put it in a custom role, as with Edit settings) reads usage, the call list and
the CSV. It needs All jobs, as API keys and webhooks do (`RolePermissions.NeedsAllJobs`): the ledger lists every job's calls, so a
member on Only jobs they're on is refused, and the settings read leaves usage out for them too. The settings read, which every member has, leaves out the own key's prices for anyone without Edit settings. Usage is never about a candidate and never shows a score.

| Route | Permission |
|---|---|
| `GET /v1/organization/ai-usage/summary` | View AI usage: totals, by day (in the requester's time zone, each day split between ScreenHire's key and the own key in tokens and cost), by model and by key, and when the newest call happened (`lastCallAt`, whatever the range, null before the first) |
| `GET /v1/organization/ai-usage/calls` | View AI usage: the calls, newest first, paged by the id of the last one (`before`), filtered by dates, key, provider, model, outcome, check and application |
| `GET /v1/organization/ai-usage/calls/export` | View AI usage: the same calls as a CSV (RFC 4180, a byte order mark, formula-looking cells neutralised), at most 50,000 rows, said in the file when cut |

The call list is newest first by design (it pages by a server cursor), and the by-model table is ordered by cost, so neither has
sortable headers.

In the dashboard this is Billing's AI use section (`/settings/billing#ai-use`, [billing](billing.md#the-billing-page)), where the
money lives, following the owner's walk-through of 2026-11-22: Verification keeps only the checks. Billing opens for whoever can see AI
usage, so an Admin gets this section without the Owner's plan, and an Owner where no payment provider is set up gets it alone. On
its own it shows, in order (beside an Owner's plan, from about a 1440px window, the credits and auto-reload move to the side column
and the purchases and deductions go under both columns, [billing](billing.md#the-billing-page)):

1. **AI credits.** The balance, paused at zero on ScreenHire's key, and for an Owner the amounts to buy and auto-reload
   ([billing](billing.md#the-billing-page)).
2. **AI use.** The Owner's use this month ("41 AI calls, $3.74" and its notes), whose key runs the AI check as a fact
   ("ScreenHire's key. The checks use your AI credits."), and links to every call in the Activity log and, for Edit settings,
   to Settings > Integrations > AI model ("Use your own key" or "Change the key").
3. **What the checks used**, at the column's width: calls, tokens and the amount for AI use for a range (ScreenHire's key as Cost, an own
   key apart as its provider's estimate), tokens by day for ScreenHire's key against the own key with its table, and use by model and
   key. The Model calls tile ends with "Last call 5 minutes ago" (`lastCallAt`, the newest call whatever the range, left out before
   the first; design system T7).
4. **Purchases** (an Owner) and **Deductions**: every credit added, and every deduction traceable to the check that used it.

A link to `#ai-use` or `#ai-credits` scrolls it into view once billing's queries have settled, because the Owner's panels above it
grow as they load. The Verification Overview shows the balance, paused at zero, with a link to `#ai-credits`. The Activity log has one quiet line above the log for who can see usage, "This month: 12 AI calls", linking here.

The calls themselves, with the provider's request id on each, are in the Activity log beside the rule-based check runs (section
10.12 of [verification-engine.md](verification-engine.md)), where the CSV is `GET /v1/verification/activity/export`;
`GET .../ai-usage/calls` and its export stay for API use. Whose key runs the AI check is Settings > Integrations > AI model
(verification-engine.md section 10.11). The AI usage alert is each person's AI use and billing row in Your account > Notifications,
starting from the organization's choice in Settings > Organization > Notification defaults ([notifications](notifications.md)).

The old addresses redirect by role (`movedTabs` in `#lib/verification-area.ts`): `/verification/ai-use` and `/verification/usage` go
to this section for who can see usage, else to the AI model for Edit settings, else to the Overview; `/verification/ai-model` goes to
the AI model for Edit settings, else as `/verification/ai-use`.

## Retention

A daily run at 04:42 UTC deletes the organization's ledger rows and alert claims older than its record retention period
(`RetentionSettings`), but never newer than the start of the previous calendar month, so the last full month stays readable for
billing. With no retention set, nothing is deleted.

## Settings

`ScreenHire:Verification:Llm:*` (the prices) goes to the worker and the API, `ScreenHire:AiUsage:*` to the API and the worker, and
`ScreenHire:Billing:Ai:*` and `Credits:*` to the API and the worker; the header of `apps/apphost/AppHost.cs` lists them. The worker's and the
API's Development settings price the fake model, so the local demo and the tests show costs and draw credits. In a deployment the API and the worker both get the provider and the model ids (`verification-llm-*` parameters,
`AzurePublish.cs`), and both load the Key Vault into configuration at start, so the owner prices a deployed model once, as vault
secrets `ScreenHire--Verification--Llm--Prices--<modelId>--Input` and `--Output`. Until then a call has no known cost and draws no credits.

## Not recorded

- Calls made before the ledger existed. Their tokens stay on `consistency_checks`.
- A provider's own retries inside one SDK call, and a request the SDK never sent.
- Listing a provider's models: it uses no tokens.
- Streaming calls: none exist.

## Tests

`AiUsageRulesTests` (cost, the amount shown on each key, the spike, retention floor, CSV, a paused note from the old cap and from the credits both read as paused), `ModelPresetsTests` (every preset against replayed
answers, the recorder), `AiUsageTests` (Aspire: the ledger, summary, list and CSV in billed amounts and the Billing page's same figure, an own key's price beside ScreenHire's billed amount, permissions, the AI check paused at zero credits with the rule-based checks running and resumed by a grant, the spike, retention, row-level security),
`BillingCreditsTests` (the debits, the pause and the alerts, [billing](billing.md#tests)) and `VerificationOrganizationControlsTests` (the own key).
