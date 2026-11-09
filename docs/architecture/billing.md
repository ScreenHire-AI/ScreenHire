---
type: Design
title: "Billing, plans and the free trial"
description: "How an organization pays and what its plan allows: the Off, Sandbox and Stripe modes, the plan catalogue shared with the pricing page, the trial that starts at sign-up, the four entitlement states and the one thing a plan refuses (opening a draft job, as a 402), Checkout, the portal and the priced plan change, the signed Stripe webhook and its idempotency, the local sandbox, the two tables, the Owner's Billing page, tests and what is not built."
tags: [billing, stripe, plans, trial, entitlement]
sources:
  - resource: packages/plans/plans.json
  - resource: packages/plans/src/index.ts
  - resource: src/ScreenHire.Domain/Billing/BillingAccount.cs
  - resource: src/ScreenHire.Domain/Billing/Entitlement.cs
  - resource: src/ScreenHire.Application/Billing/BillingOptions.cs
  - resource: src/ScreenHire.Application/Billing/PlanCatalogue.cs
  - resource: src/ScreenHire.Application/Billing/IBillingProvider.cs
  - resource: src/ScreenHire.Application/Billing/BillingAccess.cs
  - resource: src/ScreenHire.Application/Billing/BillingService.cs
  - resource: src/ScreenHire.Application/Billing/StripeBillingProvider.cs
  - resource: src/ScreenHire.Application/Billing/StripeSignature.cs
  - resource: src/ScreenHire.Application/Billing/SandboxBillingProvider.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/BillingConfiguration.cs
  - resource: apps/api/Endpoints/BillingEndpoints.cs
  - resource: apps/api/Endpoints/BillingWebhookEndpoints.cs
  - resource: apps/api/Contracts/BillingContracts.cs
  - resource: apps/dashboard/src/lib/billing.ts
  - resource: apps/dashboard/src/lib/components/plan-problem-notice.svelte
  - resource: apps/dashboard/src/lib/components/change-plan-dialog.svelte
  - resource: apps/dashboard/src/routes/(app)/settings/billing/+page.svelte
  - resource: apps/dashboard/src/lib/query.ts
  - resource: apps/marketing/src/lib/pricing.ts
---
# Billing, plans and the free trial

Every organization starts on a free trial. After it, an Owner picks a plan on Settings > Billing and pays through Stripe.
A plan allows a number of jobs open at once, and the one thing it ever refuses is opening one more (ADR-0029). Billing
goes through `IBillingProvider`, with a local sandbox in place of Stripe, so no test and no `pnpm dev` run needs a Stripe
account or the internet. Candidates never meet any of it.

## Configuration

`ScreenHire:Billing` (`BillingOptions`), forwarded by the AppHost to the API. The mode (`BillingOptions.Mode`) is worked
out the same way everywhere:

| Mode | When | Provider |
|---|---|---|
| Stripe | `SecretKey` and `WebhookSecret` are both set | `StripeBillingProvider`, Stripe's API |
| Sandbox | `Sandbox:Enabled` is true, and Stripe is not | `SandboxBillingProvider`, refused outside Development (`BillingSandboxGate` fails the host's start) |
| Off | neither | none: no account, no trial, no limit. `GET /v1/organization/billing` says `available: false`, every other billing route is a 404, and the dashboard hides the page |

| Setting | Default | What it does |
|---|---|---|
| `Stripe:Enabled` | off | AppHost only. When true the AppHost asks for the two parameters below; it never reaches the API |
| `stripe-secret-key` parameter | none | Becomes `SecretKey`. |
| `stripe-webhook-secret` parameter | none | Becomes `WebhookSecret`, the signing secret of the Stripe webhook endpoint |
| `Sandbox:Enabled` | off | The local fake. On in `pnpm dev` (`appsettings.Development.json`) and in the test stacks |
| `Trial:Days` | `packages/plans/trial.json` (14) | The length of a new organization's trial, 0 or more. The marketing site reads the file to say how long the trial is, so a host that overrides this changes the file too |
| `Trial:Plan` | `packages/plans/trial.json` (`growth`) | The plan whose limit applies during the trial. With billing on, a key the catalogue does not have stops the host at start (`BillingTrialGate`), as a negative `Trial:Days` does, because neither would show up any other way |
| `Plans:<key>:*` | the catalogue | Overrides `Name`, `Order`, `MonthlyPrice`, `Currency`, `ActiveJobs`, `Unlimited` and `StripePriceId` for one plan. A field left out keeps the catalogue's value |
| `ApiBaseUrl`, `Timeout` | Stripe's API, 15 s | For a test that points the real client at a stand-in |

The two keys are secrets: they come from the parameters (set with `dotnet user-secrets set Parameters:stripe-secret-key
"<key>"` in `apps/apphost`, or in the Key Vault in production, see the [deploy runbook](../operations/deploy.md)), never
from the settings. `BillingAccess` is always registered in the API and every test host, so an endpoint that takes it always
resolves; with billing off, nothing reads or writes a billing row. The worker has no part in billing.

## Plans and the shared catalogue

`packages/plans/plans.json` holds three plans: `starter`, `growth` and `unlimited`, each with a name, an order, a monthly
price, a currency, an active job limit, an `unlimited` flag and a `stripePriceId`. The shipped file has working numbers
(2026-11-07, "every feature on every plan, priced by open jobs"): Starter $79 a month with 3 open jobs, Growth $149 with 10
(the trial's plan) and Unlimited $249 with no limit, all in USD, and no Stripe price ids. They are working values the owner
confirms before launch, and the owner creates the Stripe prices (see [status](../status.md#owner-actions)). Dev, the test
stacks and the dashboard's Billing page all run on these numbers, and the tests read them rather than repeat them. Two readers
share it:

- The API embeds it (`PlanCatalogue`) and merges any `Plans:<key>:*` override over it at read time.
- The marketing site reads it through `@screenhire/plans` (`catalogPlans`), and `apps/marketing/src/lib/pricing.ts` builds the pricing page from it, so the page shows the same numbers; a plan with no price shows its name and limit and no amount.
  The page's words (summary, highlight) stay in the site.

A plan's limit is `ActiveJobs`, or none when `Unlimited` is true. A plan with neither limits nothing: a job count nobody
chose is never enforced. A plan can be subscribed to in Stripe mode only when it has a `StripePriceId` (`canSubscribe`);
in the sandbox every plan can.

## The trial

Completing onboarding creates the organization and its `billing_accounts` row in one transaction, with `TrialEndsAt` set
to now plus `Trial:Days`. The date is stored, so changing the setting does not move a trial already running. An
organization that existed before billing was turned on has no row; `BillingService.AccountAsync` creates one the first time
the Billing page, a checkout or a publish asks (a unique index on the organization makes two requests starting it at once
safe). The publish route asks before its transaction, so a refused publish does not roll the trial's start back and a
trial can never restart. With billing off the trial does not exist.

## What a plan allows

`Entitlement` (domain, pure) works out the state from the stored subscription status and the clock:

| State | When | Effect on publishing |
|---|---|---|
| Trialing | No subscription yet and now is before `TrialEndsAt` | The `Trial:Plan` limit applies |
| Active | The provider says active or in its own trial | The subscription's plan limit applies. A subscription set to cancel stays Active until the period ends |
| PastDue | A payment failed and the provider is retrying | The subscription's plan limit still applies; nothing else changes |
| Ended | The subscription was cancelled, unpaid or never completed, or the trial ran out with no subscription | Opening a draft is refused |

**Only opening a draft is refused.** `PublishJob` checks a job that is in Draft: the check is `BillingService.CheckPublishAsync`
inside the publish transaction. A job already open, a republish of changed content and a close are never counted or
refused. Everything stays readable and editable in every state, and open jobs stay open when a plan lapses. The decision:

1. Ended: refused as `subscription-ended`, in the words of a free trial when no subscription ever started (`TrialEnded`).
2. A plan limit of N and N or more jobs already open: refused as `plan-limit`.
3. Otherwise allowed. No limit (unlimited, or not set) is always allowed.

The open jobs are counted by `OpenJobCounter` in a scope of its own that acts for the organization, because the caller's
scope may be narrower (a member on "only jobs they're on", ADR-0015) and a limit counts every job. When the plan has a
limit, `pg_advisory_xact_lock` on the organization is taken first and lasts until the publish commits, so two jobs opening
at once cannot both slip under the limit.

The refusal is a 402 problem details response (`BillingProblems`), and `PublishJob` declares it:

| `type` | `title` | Meaning |
|---|---|---|
| `urn:screenhire:problem:plan-limit` | Plan limit reached | The plan allows N open jobs and N are open. Close one or ask an owner to upgrade |
| `urn:screenhire:problem:subscription-ended` | Subscription ended, or Free trial ended | Nothing new can be published; ask an owner to choose a plan |

The `detail` is plain language and says that open jobs stay open. It names no price and no Stripe object. The dashboard
adds the link to Billing only for an Owner.

## Checkout and the portal

All five routes need Manage billing (`Permission.ManageBilling`, Owner-only, ADR-0014). A cookie session only: an API key
gets nothing. With billing off each is a 404 except the first, which says `available: false`.

| Route | What it does |
|---|---|
| `GET /v1/organization/billing` | The overview (`BillingOverview`): the state, the current plan, `trialEndsAt` while on the trial, `nextPaymentAt` and `nextPaymentAmount` while a subscription renews (no amount once it is set to cancel), `cancelsAtPeriodEnd`, `openJobs`, `activeJobLimit`, `canManage` (the portal can open) and every plan with its price, limit and `canSubscribe` |
| `POST /v1/organization/billing/checkout` | Body `{ plan }`. Returns `{ url }` for the provider's checkout page. 400 for a plan that is unknown or cannot be subscribed to, 409 when the organization already has a subscription (Active or PastDue), 502 when Stripe cannot be reached |
| `POST /v1/organization/billing/plan/preview` | Body `{ plan }`. Returns what switching the live subscription to that plan costs: `amountDueNow`, `nextPaymentAmount`, `nextPaymentAt`, `currency` and `prorationDate`. Changes nothing. 400 for a plan that is unknown, cannot be subscribed to or is the current one, 409 without an Active subscription (a trial or an ended organization chooses through Checkout, a payment that failed goes to Manage billing) or when the subscription is set to end (it is renewed with Manage billing first), 502 as above |
| `POST /v1/organization/billing/plan` | Body `{ plan, prorationDate }`, the date the preview returned. Switches the plan and answers 204. The same refusals as the preview, plus 400 on `prorationDate` when it is more than 15 minutes old or in the future, and 502 when the payment provider refused the change (a declined card) or did not answer: the detail says to check Manage billing before trying again, because a timeout cannot tell whether Stripe made the change |
| `POST /v1/organization/billing/portal` | Returns `{ url }` for the portal where the owner changes the card or cancels (and switches plan there too, unpriced by us). 409 before there is a subscription, 502 as above |

Stripe Checkout runs in subscription mode for the plan's `StripePriceId`, quantity 1. It carries the
organization id and the plan key as `metadata` on the session and on the subscription, and the organization id as
`client_reference_id`. A second Checkout reuses the Stripe customer already stored. The browser comes back to
`<app url>/settings/billing?checkout=success` or `?checkout=cancelled`, built from `DomainOptions` as every dashboard
link is (ADR-0003). The portal's return address is the Billing page. A Stripe error becomes a 502 with no key or
customer in the message.

`StripeBillingProvider` calls Stripe with `HttpClient`, pins `Stripe-Version: 2024-06-20`, and uses one long-lived client
instead of a factory client: the standard resilience handler would retry a Checkout call and could make a second session.
The person clicking again is the retry. There is no Stripe SDK (ADR-0029).

The header pins our calls, not Stripe's. A webhook arrives at the version its endpoint was created with, or the account's
default if it was created with none, so the endpoint is created at `2024-06-20` ([deploy runbook](../operations/deploy.md)).
Moving the pin means moving both. A subscription's period end is read from the subscription and then from its first item,
where API version 2025-03-31 moved it, so a mismatch does not lose the next payment date.

### Pricing a plan change

An Owner sees the cost before switching. Both routes check the same things in the same order, so a change the preview showed is
never refused for a new reason (`BillingEndpoints.CheckPlanChangeAsync`): billing on, a plan that is on offer and not the
current one, then an Active subscription that is not set to end and whose current plan the catalogue knows. A subscription set to end has no
next payment to price, and a switch would not renew it. The preview takes "now" in whole seconds as the
`prorationDate`, and the change sends that date back.

Why the change goes through the API and not the portal: the page can then show a price that is the price charged. The
portal prorates at its own moment and by its own configuration, so a number shown here could differ from the one the Owner
confirms there, and a one-item deep link would stop working the day a subscription gets a second item. The cost is that a
payment which needs the Owner (a declined card, a 3-D Secure step) cannot be completed on our page. The change uses
`payment_behavior=error_if_incomplete`, so a declined payment fails whole and leaves the subscription as it was. The 502 that
follows points to Manage billing without promising nothing was charged, because a timeout reads the same way.

`StripeBillingProvider.PreviewPlanChangeAsync` reads the subscription (`GET /v1/subscriptions/{id}`) for the item that carries
one of the catalogue's prices, so an item that is not a plan is left alone. It then posts `invoices/create_preview`
(https://docs.stripe.com/api/invoices/create_preview) with that item's id, the new price and `subscription_details[proration_date]`.
The preview is an upcoming invoice: its lines marked `proration` whose period starts at the proration date (Stripe's advice for
isolating the prorations being previewed) are the credit for unused time and the charge for the rest of the period. Any other
proration line is an earlier pending item, which an `always_invoice` update leaves out in classic billing mode, the mode Checkout
gives a subscription at the pinned version (flexible billing mode would invoice it too). The line
that is not a proration is the first full period at the new price. The prorations sum to the amount
due now, floored at 0, because a downgrade's credit stays on the Stripe customer and is not paid out. The full-period line is
the next regular charge, and its period start is its date. Amounts are whole minor units, so a zero-decimal currency (yen) is
not divided by 100 and the five three-decimal ones (dinar) are divided by 1000. `ChangePlanAsync` updates the subscription with the same item, `proration_behavior=always_invoice`, the same
`proration_date` and an `Idempotency-Key` made from the subscription, price and date, so a second click on Confirm is one
change. Stripe keeps the answer under that key, a declined card's included, so the dialog prices the change again after any
refusal: the new date is a new key, and the retry is a new attempt. The new plan reaches the account through the `customer.subscription.updated` webhook, as every other change does.
The `create_preview` endpoint is Stripe's replacement for the upcoming-invoice endpoint it removed in API version 2025-03-31, and
its reference page exists at the pinned `2024-06-20`, where `customer` is optional when `subscription` is given. No test here
reaches Stripe, so the first real call, in test mode, is an owner check ([status](../status.md#owner-actions)).

The sandbox prorates by the day instead (`SandboxBillingProvider.PreviewPlanChangeAsync`): the price difference times the whole
days left in the month that ends at the account's period end, over the days in it, rounded to the cent, and 0 for a downgrade.
The sandbox never renews, so a period end in the past rolls on by whole months to the next one still to come.
It has no webhook, so after `ChangePlanAsync` (which calls nothing) the route records the new plan itself, as its checkout page does.

## Webhooks

`POST /v1/webhooks/stripe` is anonymous, not in the generated client and not in the API's description. Its checks, in order:

1. Not Stripe mode: 404.
2. A rate limit of 600 posts a minute per client address (`billing-webhook`), so a flood of unsigned posts cannot use up the
   permits Stripe's own deliveries need, and a body limit of 1 MB.
3. The `Stripe-Signature` header (`StripeSignature`): `t=timestamp,v1=signature`, each `v1` the lowercase hex HMAC-SHA256
   of `timestamp.body` keyed with the webhook secret, compared in constant time, with the timestamp within 5 minutes of now.
   Anything else is a 401 and nothing is read.
4. A signed event this app does not act on, or one that cannot be read, is a 200 so Stripe stops retrying it.

Four event types are acted on:

| Event | Applies |
|---|---|
| `checkout.session.completed` | The customer and subscription ids and the plan; the status becomes Active when the payment is `paid` or `no_payment_required` |
| `customer.subscription.created`, `customer.subscription.updated` | The status, the period end (from the subscription, then its first item) and whether it cancels at the period end. `active` and `trialing` become Active, `past_due` PastDue, `canceled`, `unpaid` and `incomplete_expired` Ended; any other status says nothing and changes nothing |
| `customer.subscription.deleted` | Ended, and not cancelling any more |

The organization comes from the event's `metadata.organization_id` (then `client_reference_id`), which Checkout set and the
signature protects. `BillingEventApplier` sets the tenant with `SetPublicOrganization`, as every anonymous route that acts
for one organization does, and does nothing if that organization does not exist. The plan key is looked up from the first
item's price id among the catalogue's `StripePriceId`s, because a plan switched in the portal changes the price and keeps the
metadata. A price the catalogue does not list names no plan, so the stored plan stands. `metadata.plan` is read only for an
event with no items, such as the Checkout session.

**Idempotent.** `BillingService.ApplyAsync` stores the event id in `billing_events` (a unique index on the organization and
the event id) in the same save as the change, so a redelivery, even two at once, applies once. The account also keeps
`LastEventAt`, the provider's `created` time of the newest event applied, and `BillingAccount.Apply` ignores an event older
than that, so events that arrive out of order cannot undo a newer one. A fact an event does not carry keeps its stored value.
A change publishes `BillingChanged` (an `ITenantEvent`, carrying only the organization) through the outbox so an open
Billing page refetches.

**Races.** Stripe sends the events of one Checkout within the same second and the webhook handles them in parallel, so
`billing_accounts` has an optimistic concurrency token (Postgres `xmin`, the `Version` property). The delivery that loses the
race gets a 409 and Stripe sends it again. An event that ends a subscription other than the one the account has now is
recorded and ignored, so a subscription the owner replaced cannot end the one that pays.

## The sandbox

`SandboxBillingProvider` stands in for Stripe in Development and the test stacks. Its "checkout" and "portal" are two routes
of the API itself, mapped only in sandbox mode so the API's description never lists them:

- `GET /v1/billing/sandbox/checkout?state=...` "pays" for the plan in the state at once (an Active subscription on that
  plan, ending a month later, with a made-up customer and subscription id) and sends the browser back to the Billing page.
- `GET /v1/billing/sandbox/portal?state=...` cancels the subscription at once (Ended), which is the one change worth trying
  locally because it shows the ended state.

The routes are anonymous because the browser arrives by a plain navigation. What each does is in the `state`, a Data
Protection value issued by the API under the purpose `ScreenHire.Billing.SandboxState.v1` that expires after 10 minutes,
so the address cannot be edited to name another organization or plan. The sandbox has no webhook. With the sandbox on, the
demo's Brightwater is seeded with an Active subscription on Unlimited, so the demo is never held to a limit.

## Data

Two tenant-owned tables, each with the `tenant_isolation` row-level security policy (migration `AddBilling`):

- `billing_accounts`, one per organization (unique on `organization_id`): `trial_ends_at`, `plan_key`, `status`
  (`None`, `Active`, `PastDue`, `Ended`), the Stripe customer and subscription ids, `current_period_end`,
  `cancels_at_period_end`, `last_event_at`, `created_at` and `updated_at`. It is the last state the provider told us, which
  is all the publish check reads. The provider owns the truth.
- `billing_events`, one row per provider event applied (unique on `organization_id` and `event_id`), with no payload.

## The Billing page

Settings > Billing (`/settings/billing`) is in the Organization area for an Owner, and only where a payment provider is set
up (`available`): elsewhere the entry and the page are hidden, as Slack's are (design system A3). It shows the state as a
status badge (Free trial, Active, Payment failed or Ended), the current plan, the trial's end or the next payment (and that
the subscription will cancel), the open jobs against the limit (`3 of 5`, or `3` with none), and the plans. A plan has a Choose button while Trialing or Ended
(`mayChoosePlan`), which goes to Checkout. With an Active subscription that is not set to end, every other plan has a Switch button, which opens
`ChangePlanDialog`: it asks the preview route when it opens and shows "$X now, then $Y a month from DATE" in plain numbers
("Nothing now" for a cheaper plan, with the line that the unused time is credited), and Confirm change sends the plan and the
preview's `prorationDate`. The dialog is where the Owner decides, so its Confirm is the one primary there (A1); Manage billing
stays the page's. The dialog takes focus itself when it opens, so the date's tooltip does not open over the text. A failed preview keeps Confirm
off, and a refused change stays in the dialog, which prices it again. After it, the Plans panel says
the plan is changing until the page shows it. Manage billing opens the portal for the card and for cancelling. The page
refetches when `BillingChanged` arrives and when the browser returns from Checkout.

A refused publish reaches a person at the three places a draft is opened (the jobs list, a job's page and its distribution
panel) as `PlanProblemNotice` (a 402 is told from other failures by `planProblem` in `apps/dashboard/src/lib/query.ts`), a full-width notice above the table or panel in place of a plain failure line: the API's own
sentence, and for an Owner an Upgrade plan link to this page. Everyone else reads the sentence, which already says to ask an
owner.

## Tests

| Test | Proves |
|---|---|
| `BillingOptionsTests` (unit) | the mode from the keys and the sandbox switch, Stripe winning when both are set, the 14-day Growth trial defaults, the sandbox refused outside Development and allowed in it, a negative trial or an unknown trial plan refused while billing is on |
| `PlanCatalogueTests` (unit) | the shipped file has exactly Starter, Growth and Unlimited with a price on each, a limit that grows with the price (none on Unlimited) and no Stripe price, an override changes only the fields it sets, a blank Stripe price keeps the catalogue's, an unlimited plan has no limit, lookup by key, the trial plan |
| `EntitlementTests` (unit) | the trial's last moment, a subscription deciding the state, the plan in force, the publish decision for each state and limit, a trial that ran out told apart from a subscription that ended, an event applying only what it carries and an older one changing nothing |
| `BillingProblemsTests` (unit) | the 402 problems: type, title and a detail that names the numbers, the singular, that open jobs stay open, and a trial that ran out never mentioning a subscription |
| `StripeSignatureTests` (unit) | a good signature, another secret, a changed body or timestamp, the five-minute window both ways, a malformed header without throwing, several `v1` values |
| `StripeBillingProviderTests` (unit) | the Checkout and portal requests against a stub handler (fields, metadata, key, pinned version, customer reuse), the plan change's lookup, preview and update requests (the plan's item among others, the date, `always_invoice`, the idempotency key) and a preview read into amounts (prorations as due now, a downgrade at 0, a zero-decimal and a three-decimal currency, a missing next payment), refusals and network failures without the key, each event type read into the common event, a period end from the subscription or its first item, and a plan from the price, ahead of the metadata |
| `SandboxBillingProviderTests` (unit) | the sandbox's links and state: what it names, its lifetime, a tampered, foreign or oversize state not reading, and no webhook; and its day-based proration (mid-period, first and last day, a downgrade, no known period end, a period end that has passed) |
| `BillingTests` (integration) | the Owner's overview, the trial at sign-up and for an older organization, Checkout and the portal through the sandbox, who may use them, and billing off |
| `BillingPlanChangeTests` (integration) | the preview and the change through the sandbox: an upgrade's amounts bounded by the shipped prices, a downgrade owing nothing, a confirmation switching the plan and no other organization's, a stale or future `prorationDate` refused, a trial, an ended organization or a subscription set to end a 409, an unknown or current plan a 400 on `plan`, and Admins and Recruiters a 403 |
| `BillingPlanLimitTests` (integration) | the 402 at the limit and when ended, open jobs left open, republish and close not counted, the whole organization counted for a member on "only jobs they're on", and no limit when billing is off |
| `BillingPublishCheckTests` (integration) | the job being published is not counted, so a double submit is not refused for taking its own place, and a different draft still is |
| `BillingWebhookTests` (integration) | the route is a 404 outside Stripe mode. What a delivery does once read, through `BillingService` against the stack's database: a replay applied once, an older event ignored, and an event for another organization not applied |
| `StripeWebhookRouteTests` (integration) | on a stack of its own in Stripe mode, the route end to end: a signed event applied to the organization it names, a wrong or missing signature a 401, a redelivery changing nothing, an unknown organization a 200, and another organization untouched |
| `BillingTests` (end-to-end) | an owner sees the trial and the plans and chooses one through Checkout, a publish past the limit shows the notice and its link to Billing, and a recruiter has no Billing entry |
| `billing-page.test.ts`, `jobs-page.test.ts`, `distribution-panel.test.ts`, `job-page.test.ts`, `query.test.ts`, `settings.test.ts` (Vitest) | the page in each state, the plan change dialog (the cost before confirming, a downgrade's nothing now, the change sent with the preview's date, a failed preview and a refused change), the notice and its link for an owner and anyone else at each place a draft opens, the 402 read as a plan problem, the settings entry hidden without a provider |
| `RolePermissionsTests` (unit) | Manage billing is an Owner-only permission and its routes join the route table |

The test stacks pin nothing: they sell the plans in `plans.json`, and a test that needs a price or a limit reads it through
`ShippedPlans` (`tests/ScreenHire.Testing`), so a changed working number needs no test edit.

## Not built

- Reminders before the trial ends, by email or in the app.
- Tax: Stripe Tax and the collection of tax ids are not set up (an owner action).
- A list of invoices or receipts; Stripe's portal has them.
- Amounts read from Stripe except in a plan change's preview. The rest of the page shows the catalogue's price and Stripe
  charges its own.
- Completing a payment that needs the Owner (3-D Secure) inside a plan change. It fails whole and sends the Owner to Manage billing.
- Coupons and tax in a plan change's preview. Its amounts are the invoice lines' amounts, before discounts and tax, and only
  the lines Stripe returns with the preview (its first page) are read.
- Seat-based pricing. A plan counts open jobs only.
- A grace period, or an Ended state that blocks more than opening a draft.
