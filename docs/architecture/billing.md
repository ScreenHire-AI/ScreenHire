---
type: Design
title: "Billing, plans and the free trial"
description: "How an organization pays and what its plan allows: the Off, Sandbox and Stripe modes, the plan catalogue shared with the pricing page, the trial that starts at sign-up and the reminders before it ends, the four entitlement states and the one thing a plan refuses (opening a draft job, as a 402), Checkout, the portal and the priced plan change, the signed Stripe webhook and its idempotency, AI use on ScreenHire's key reported to a Stripe meter at the provider's price plus a margin and shown as one amount, the month added up, the invoices and the payment method, the local sandbox, the three tables, the Owner's Billing page, tests and what is not built."
tags: [billing, stripe, plans, trial, entitlement]
sources:
  - resource: packages/plans/plans.json
  - resource: packages/plans/src/index.ts
  - resource: packages/plans/ai.json
  - resource: src/ScreenHire.Domain/Billing/AiCharge.cs
  - resource: src/ScreenHire.Domain/Billing/BillingAccount.cs
  - resource: src/ScreenHire.Domain/Billing/Entitlement.cs
  - resource: src/ScreenHire.Domain/Billing/TrialReminders.cs
  - resource: src/ScreenHire.Application/Billing/BillingOptions.cs
  - resource: src/ScreenHire.Application/Billing/PlanCatalogue.cs
  - resource: src/ScreenHire.Application/Billing/IBillingProvider.cs
  - resource: src/ScreenHire.Application/Billing/BillingAccess.cs
  - resource: src/ScreenHire.Application/Billing/BillingService.cs
  - resource: src/ScreenHire.Application/Billing/TrialReminderHandlers.cs
  - resource: src/ScreenHire.Application/Billing/AiUsageReporting.cs
  - resource: src/ScreenHire.Application/Billing/StripeBillingProvider.cs
  - resource: src/ScreenHire.Application/Billing/StripeSignature.cs
  - resource: src/ScreenHire.Application/Billing/SandboxBillingProvider.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/BillingConfiguration.cs
  - resource: apps/api/Endpoints/BillingEndpoints.cs
  - resource: apps/api/Endpoints/BillingWebhookEndpoints.cs
  - resource: apps/api/Contracts/BillingContracts.cs
  - resource: apps/api/Endpoints/MetaEndpoints.cs
  - resource: apps/api/Contracts/MetaContracts.cs
  - resource: apps/dashboard/src/lib/components/app-sidebar.svelte
  - resource: apps/dashboard/src/routes/(app)/+layout.svelte
  - resource: apps/dashboard/src/lib/billing.ts
  - resource: apps/dashboard/src/lib/components/plan-problem-notice.svelte
  - resource: apps/dashboard/src/lib/components/change-plan-dialog.svelte
  - resource: apps/dashboard/src/routes/(app)/settings/billing/+page.svelte
  - resource: apps/dashboard/src/lib/components/billing-invoices.svelte
  - resource: apps/dashboard/src/lib/query.ts
  - resource: apps/marketing/src/lib/pricing.ts
---
# Billing, plans and the free trial

Every organization starts on a free trial. After it, an Owner picks a plan on Settings > Billing and pays through Stripe.
A plan allows a number of jobs open at once, and the one thing it ever refuses is opening one more (ADR-0029). Billing
goes through `IBillingProvider`, with a local sandbox in place of Stripe, so no test and no `pnpm dev` run needs a Stripe
account or the internet. Candidates never meet any of it.

## Configuration

`ScreenHire:Billing` (`BillingOptions`), forwarded by the AppHost to the API. Its `Trial:*`, `Plans:*`, `Ai:*`, `Sandbox:*` and `Stripe:*` keys,
`ApiBaseUrl` and `Timeout` go to the worker too, which sends the trial's reminders and reports AI use to Stripe's meter, and the worker gets
both Stripe parameters as the API does. The mode (`BillingOptions.Mode`) is worked
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
| `Stripe:Tax:Enabled` | off | Checkout calculates tax with Stripe Tax and collects the billing address it needs. Off until the owner has registered in Stripe Tax, because Stripe collects tax only where a registration exists (see Tax below) |
| `Stripe:Tax:CollectTaxIds` | on, with tax on | Checkout also asks a business for its tax id. Does nothing while `Stripe:Tax:Enabled` is off. Unlike `Stripe:Enabled`, both `Stripe:Tax:*` settings reach the API |
| `Sandbox:Enabled` | off | The local fake. On in `pnpm dev` (`appsettings.Development.json`) and in the test stacks |
| `Trial:Days` | `packages/plans/trial.json` (14) | The length of a new organization's trial, 0 or more. The marketing site reads the file to say how long the trial is, so a host that overrides this changes the file too |
| `Trial:Plan` | `packages/plans/trial.json` (`growth`) | The plan whose limit applies during the trial. With billing on, a key the catalogue does not have stops the host at start (`BillingTrialGate`), as a negative `Trial:Days` does, because neither would show up any other way |
| `Trial:RemindDaysBefore` | `[7, 1]` | The days before `TrialEndsAt` the Owners are emailed. The day it ends is always reminded and is not a setting; a day below 1 stops the host at start. Configure it as a list (`ScreenHire__Billing__Trial__RemindDaysBefore__0=3`), which replaces the default |
| `Plans:<key>:*` | the catalogue | Overrides `Name`, `Order`, `MonthlyPrice`, `Currency`, `ActiveJobs`, `Unlimited` and `StripePriceId` for one plan. A field left out keeps the catalogue's value |
| `Ai:Margin` | `packages/plans/ai.json` (0.20) | The share added to the provider's price when AI use on ScreenHire's key is billed (0.20 is 20% on top). 0 to 10; anything else stops the host at start (`BillingAiGate`). The marketing site reads the same file to say "plus 20%", so a host that overrides this changes the file too. An organization's own key costs it nothing extra. The app never shows it: every figure for AI use already includes it (`BillingOptions.AiMarginInForce`, which is 0 while billing is off) |
| `Ai:StripePriceId` | none | The metered Stripe price on the meter. Checkout adds it to a new subscription, and Stripe mode reports no usage without it (a warning in the log). The sandbox needs none. Every subscribable plan must be in USD while it is set (`BillingAiGate`), because the ledger prices in US dollars and a Stripe subscription has one currency |
| `Ai:MeterEventName` | `screenhire_ai_usage` | The `event_name` of the Stripe meter the owner creates |
| `Ai:TrialAllowance` | 5.00 | US dollars of AI use on ScreenHire's key the free trial includes. 0 includes none |
| `Ai:ReportBatchSize` | 200 | The most calls one run reports for one organization (1 to 1000); a full batch asks for the next |
| `ApiBaseUrl`, `Timeout` | Stripe's API, 15 s | For a test that points the real client at a stand-in |

The two keys are secrets: they come from the parameters (set with `dotnet user-secrets set Parameters:stripe-secret-key
"<key>"` in `apps/apphost`, or in the Key Vault in production, see the [deploy runbook](../operations/deploy.md)), never
from the settings. `BillingAccess` is always registered in the API and every test host, so an endpoint that takes it always
resolves; with billing off, nothing reads or writes a billing row. The worker takes no part in Checkout, the portal or the publish check. It reads
the trial's settings and the plan catalogue (`AddScreenHireTrialReminders`), to send the trial's reminders, and holds both providers
(`AddScreenHireAiUsageBilling`) to report AI use to the meter.

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

No card is taken for the trial, so nothing is charged until an Owner chooses a plan. The Billing page says so under Plans while
the organization is Trialing: the trial needs no card, open jobs stay open when it ends, and a new job needs a plan.

Two other places say it, so nobody finds out late:

- **Onboarding** says under its button, "Your free trial lasts 14 days and needs no card." There is no organization yet to ask,
  so the days come from the anonymous `GET /v1/meta` as `trialDays` (`BillingService.TrialDays`: `Trial:Days`, or 0 while billing
  is off). The line is left out at 0, and the dashboard never writes the number itself.
- **The sidebar's footer**, for an Owner only, while the trial runs: one muted line above the person's block, "Trial ends December 5"
  (the browser locale orders the day and month, so en-GB reads 5 December), linking to Billing. It reads the same `GET /v1/organization/billing` the
  shell already asks for, and only an Owner can ask. The API sends `trialEndsAt` only while the state is Trialing, so the line
  is gone once a plan is paid, the trial ends or billing is off, and `BillingChanged` makes it go at once. It is not in the icon rail.

## Trial reminders

A trial used to end in silence: the first an Owner heard of it was a refused publish. Now each Owner is told, by email and in the
bell, while no subscription has started. Nothing here refuses or changes anything; `Entitlement` is untouched.

**Scheduling.** Creating the billing account (onboarding, or the first time an organization older than billing asks) publishes
`TrialStarted` through the outbox, in the same transaction. The worker's handler reads the account. If no subscription has started
and there is no `billing_events` row `trial-reminders-scheduled:<TrialEndsAt in seconds>` for the organization yet, it schedules these
Wolverine messages and inserts that row in the same save. The handler never writes the account: that row has an optimistic concurrency
token, and a write in the first moments after sign-up collided with a checkout or a webhook saving it (the sandbox route ignores
such a collision, so the plan was never applied). `TrialReminders.Plan` (domain, pure) decides what to schedule, looked at from now:

| Step | When | What it does |
|---|---|---|
| `RemindTrialEnding` | `TrialEndsAt` minus each day in `Trial:RemindDaysBefore` (7 and 1), and `TrialEndsAt` itself | One `EmailTrialEnding` per Owner |
| `ShowTrialNotice` | 7 days before `TrialEndsAt`, or at once when fewer than 7 days are left | Raises the `trial-ending` bell notification |
| `ShowTrialNotice` (ended) | `TrialEndsAt`, or at once when it has passed | Clears `trial-ending` and raises `trial-ended` |

A moment already past is skipped. A trial first seen 5 days before its end gets the 1 day and end day emails and the bell notice
now; one that ended without a plan gets only the ended notice. Nothing is polled: the messages wait in Wolverine's schedule until
they are due.

**A reminder decides from the account, not the clock.** Each message carries the `TrialEndsAt` it was scheduled for. When it
fires, `TrialReminders.Applies` is true only if the account's status is still `None` and its `TrialEndsAt` is that same instant.
A reminder that fires after a subscription started, or for a trial that was changed, does nothing. The email handler checks
again, because a plan may start between picking the Owners and sending.

**Idempotency.** `RemindTrialEnding` and `EmailTrialEnding` are `[Deduplicated]` on a key built from the organization, the
trial's end (in seconds) and the day, and for the email also the Owner. A redelivered message sends nothing twice, and a changed
trial is not swallowed by the old trial's 24 hour claim. Raising a notification is idempotent on its own (see
[notifications](notifications.md)), and `ShowTrialNotice` needs no key because it raises or clears a row that is already there.

**The email** goes to the members who hold `ManageBilling` (the Owners, ADR-0014) through `TransactionalEmail`, so Mailpit locally.
It is ScreenHire's own wording: the organization, when the trial ends (in days, tomorrow or today, with the date in UTC), the
trial's plan by name, that nothing is deleted when it ends, that open jobs stay open, that opening a new job needs a plan, and a
link to Settings > Billing. A host with no email sender or no dashboard address sends nothing and logs why.

**The bell.** One `trial-ending` notification per Owner, key `trial`, with `endsAt` and the plan's name in its payload and a link
to `/settings/billing`. The dashboard works out "ends in N days", "ends tomorrow" or "ends today" from `endsAt` when it draws the
line, in calendar days in the browser's time zone, so the one row stays right as the days pass instead of piling up one a day and
agrees with the date the trial ends on. The line is the fact alone; the link goes to the Billing page, which shows when the trial ends and, once it has ended, that open
jobs stay open and a new job needs a plan. When the trial ends, the ending notice is cleared and `trial-ended` raised, so it
comes back unread even for an Owner who had read or dismissed the first. Only Owners are addressed, so a member without Manage
billing has no row and sees nothing. Both kinds are exempt from the retention purge (`NotificationKinds.KeptUntilCleared`), because
a trial that ended with no plan is still true a month later. A plan starting takes them away: `BillingEventApplier`, which every
checkout, plan change and Stripe event goes through, clears both kinds when the saved status is no longer `None`. The Owners
are picked when each step fires: an Owner who joins after the 7 day notice gets the emails still to come, and in the bell nothing
until the trial ends and the ended notice is raised for every Owner.

**Catch-up.** An organization whose trial began before this shipped has no scheduled reminders. The worker sends
`CatchUpTrialReminders` once when it starts (`TrialReminderCatchUp`, a one-shot hosted service registered in the worker's
`Program.cs` only, not in the shared messaging setup, so the tests' private handler hosts cannot schedule a trial's reminders into
a store nobody reads). It fans out one `TrialStarted` per organization, as the other fan-outs do, and the same handler does the
work. The `billing_events` row makes a restart schedule nothing twice, and it is
unique per organization, so two workers starting together cannot both schedule: one insert fails and its retry finds the row. The repo's other backfills are SQL inside a migration,
which cannot write Wolverine's schedule (the migrator creates that storage after the migrations), so this one is a message.
Changing `Trial:RemindDaysBefore` later does not move reminders already scheduled.

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

All seven routes need Manage billing (`Permission.ManageBilling`, Owner-only, ADR-0014). A cookie session only: an API key
gets nothing. With billing off each is a 404 except the first, which says `available: false`.

| Route | What it does |
|---|---|
| `GET /v1/organization/billing` | The overview (`BillingOverview`): the state, the current plan, `trialEndsAt` while on the trial, `nextPaymentAt` and `nextPaymentAmount` while a subscription renews (no amount once it is set to cancel), `cancelsAtPeriodEnd`, `openJobs`, `activeJobLimit`, `canManage` (the portal can open), every plan with its price, limit and `canSubscribe`, this month's AI use (`aiUsage`) and this month added up (`thisMonth`, see The Billing page) |
| `POST /v1/organization/billing/checkout` | Body `{ plan }`. Returns `{ url }` for the provider's checkout page. 400 for a plan that is unknown or cannot be subscribed to, 409 when the organization already has a subscription (Active or PastDue), 502 when Stripe cannot be reached |
| `POST /v1/organization/billing/plan/preview` | Body `{ plan }`. Returns what switching the live subscription to that plan costs: `amountDueNow`, `nextPaymentAmount`, `nextPaymentAt`, `currency` and `prorationDate`. Changes nothing. 400 for a plan that is unknown, cannot be subscribed to or is the current one, 409 without an Active subscription (a trial or an ended organization chooses through Checkout, a payment that failed goes to Manage billing) or when the subscription is set to end (it is renewed with Manage billing first), 502 as above |
| `POST /v1/organization/billing/plan` | Body `{ plan, prorationDate }`, the date the preview returned. Switches the plan and answers 204. The same refusals as the preview, plus 400 on `prorationDate` when it is more than 15 minutes old or in the future, and 502 when the payment provider refused the change (a declined card) or did not answer: the detail says to check Manage billing before trying again, because a timeout cannot tell whether Stripe made the change |
| `POST /v1/organization/billing/portal` | Returns `{ url }` for the portal where the owner changes the card or cancels (and switches plan there too, unpriced by us). `?intent=ChangeCard` or `Cancel` names the control that opened it: Stripe opens the same portal for both, and the sandbox cancels only for `Cancel` (and with no intent). 409 before there is a subscription, 502 as above |
| `GET /v1/organization/billing/invoices` | The customer's invoices, newest first (`IBillingProvider.ListInvoicesAsync`): date, amount (the total, tax included), currency, status (`Paid`, `Due` or `Void`) and the PDF's address. Empty before there is a customer, 502 as above |
| `GET /v1/organization/billing/payment-method` | The payment method the plan is charged to (`IBillingProvider.GetPaymentMethodAsync`): its type, and for a card its network and last four digits. `method` is null before there is a customer or when none is on file, 502 as above |

Stripe Checkout runs in subscription mode for the plan's `StripePriceId`, quantity 1. It carries the
organization id and the plan key as `metadata` on the session and on the subscription, and the organization id as
`client_reference_id`. A second Checkout reuses the Stripe customer already stored. The browser comes back to
`<app url>/settings/billing?checkout=success` or `?checkout=cancelled`, built from `DomainOptions` as every dashboard
link is (ADR-0003). The portal's return address is the Billing page. A Stripe error becomes a 502 with no key or
customer in the message.

**Invoices and the card in Stripe.** `ListInvoicesAsync` reads `GET /v1/invoices?customer=...&limit=24`, newest first. A draft is left out,
`paid` is Paid, `open` and `uncollectible` are Due (an uncollectible invoice is still owed; Stripe has only stopped retrying it) and `void`
is Void. The amount is `total` in the invoice's currency, and the link is Stripe's own `invoice_pdf` when it is https. `GetPaymentMethodAsync`
reads the subscription with `expand[]=default_payment_method&expand[]=customer.invoice_settings.default_payment_method` in one call, because
Stripe charges the subscription's own default first and the customer's when it has none
(https://docs.stripe.com/api/subscriptions/update#update_subscription-default_payment_method). A customer with no subscription is read the same way
through `customers/{id}`. A card gives its `brand` and `last4`, and any other method only its `type`.

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
follows points to Change card without promising nothing was charged, because a timeout reads the same way.

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

## Tax

Prices in `plans.json` are tax-exclusive. With `Stripe:Tax:Enabled` on, `StripeBillingProvider` adds to the Checkout form
(names checked against Stripe's documentation on 2026-11-08):

| Field | When | Why |
|---|---|---|
| `automatic_tax[enabled]=true` | tax on | Stripe calculates the tax for the customer's location |
| `billing_address_collection=required` | tax on | The location comes from the billing address, so Checkout always asks for it |
| `customer_update[address]=auto` | tax on, and the organization already has a Stripe customer | Stripe refuses to use an address it was not told to save onto an existing customer |
| `tax_id_collection[enabled]=true` | tax on and `CollectTaxIds` | Checkout asks a business for its tax id. A tax id Stripe can check lets Stripe Tax apply the reverse charge or a zero rate |
| `customer_update[name]=auto` | as above, with an existing customer | Saves the business name that goes with the tax id |

A first Checkout passes `customer_email`, so Checkout creates the customer and keeps the address, name and tax id with no
`customer_update`. With tax off none of these fields is sent, and `CollectTaxIds` alone sends nothing.

The portal takes no tax parameter. What a customer may change there (billing address, which is on by default, and tax id,
which is off by default) is part of the portal configuration in the Stripe Dashboard, so it is an owner action below.

The sandbox's Checkout records the same request: its state carries `TaxRequested` and `TaxIdsRequested`, and the page adds
`tax=requested` and `taxId=requested` to the address it sends the browser back to, so a test can see what a real Checkout
would have asked. The Billing page takes both out of its address with `checkout`, so a reload does not keep them. The overview's `taxAtCheckout` is the setting, and the Billing page says "plus tax where it applies"
beside each price while it is true. The marketing pricing page says the same in its questions, unconditionally, because the
site is static and the owner turns tax on before launch.

### Owner actions for tax

Billing code cannot do these, and tax stays off until they are done:

1. Add each Stripe Tax registration (Dashboard, Tax, Registrations), after confirming with an accountant where one is
   needed ([status](../status.md#owner-actions), [billing providers](../research/billing-providers.md)). Stripe collects
   tax only in a jurisdiction with an active registration.
2. Complete Stripe Tax's settings (head office address, default tax code for software as a service) and give each Stripe
   price `tax_behavior=exclusive` when creating it. A price's tax behavior cannot be changed afterwards, and a price with
   none takes the Tax setting's default, so set it on the price. `exclusive` matches `plans.json`, whose prices exclude tax.
3. In the customer portal configuration, turn on Billing address and Tax ID (Customer information), so a customer can
   change both after Checkout.
4. Set `ScreenHire:Billing:Stripe:Tax:Enabled=true` where the API runs, and `CollectTaxIds=false` if the business does not
   want tax ids.

## AI usage

The consistency check calls a language model ([AI usage](ai-usage.md)). Its calls on an organization's own key cost ScreenHire
nothing and are never billed. Its calls on ScreenHire's key cost ScreenHire the provider's price, and the organization pays that
price plus a margin (`Ai:Margin`, 20% from `packages/plans/ai.json`). The plan includes the checks; the use of the built-in key is
the one metered thing on top of it.

**The amount.** `AiCharge.Micros` prices one call: its cost in US dollars times (1 + margin), rounded half away from zero to a
micro-dollar, a whole number. The unit is the micro-dollar because Stripe's meter takes positive integers and a call often costs a
fraction of a cent. The Billing page sums the same function over the month, in SQL (Postgres rounds numeric half away from zero too),
so its AI line and the meter agree to the micro-dollar for calls past the trial (the trial's allowance is its own line). A call with no known cost (`Unknown`, never a made-up zero) charges nothing.

**One amount for AI use.** A customer sees one amount for AI use, the amount they pay, and never the provider's price or the margin
(the owner's walk-through, 2026-11-21). `AiCharge.Billed` is that amount for one call: on ScreenHire's key `AiCharge.Micros` in dollars, the
figure the meter is sent; on the organization's own key the provider's price, which that provider charges and ScreenHire never bills, so
it is labelled "Charged by your provider (estimate)". The Usage tab, its by-day and by-model tables, the call list, the Activity log, both
CSVs, the budget and the cap and the Billing page all show it (`AiUsageQueries.BilledAmount` in SQL). The margin is
`BillingOptions.AiMarginInForce`: `Ai:Margin` while billing is on, and 0 while it is off, because nobody is billed then. The ledger keeps
the provider's price, so a margin changed later reprices what is shown but never what was reported.

**Reporting.** Every 15 minutes (`ReportAiUsage`, a recurring Wolverine schedule in the worker) one message for each organization
(`ReportOrganizationAiUsage`, `[Deduplicated]` on the organization and the 15-minute slot) settles that organization's pending calls:
built-in key, a cost, started within the last 34 days and not yet in `ai_usage_charges`,
oldest first, at most `Ai:ReportBatchSize`. A full batch sends the next one, keyed by the last call's id so the cron slot's own key
does not swallow it. The 34 days are a choice, not Stripe's 35-day limit (the event carries the report's time, not the call's): an older
call that was never settled is never billed, so a first invoice reaches back about one period at most, and the query stays bounded.

- **Only a paying organization is reported.** The account must be Active or PastDue. During the trial there is no Stripe customer,
  and after the subscription ended there is nothing to bill on, so those calls wait (up to the 34 days) and are settled when a plan
  starts. In Stripe mode the account also needs its customer and `Ai:StripePriceId`; without either, nothing is sent and the run
  warns in the log. The sandbox needs neither.
- **One meter event per call**, sent one after another because Stripe answers 409 when two events for one customer and meter arrive
  at once. The event is `POST /v1/billing/meter_events` with the meter's `event_name`, `identifier` = the ledger call's id,
  `payload[stripe_customer_id]`, `payload[value]` = the charge in micro-dollars, and `timestamp` = the moment of the report, so a call
  backfilled from the trial lands in the first paid period (Stripe ignores usage from before a subscription started). The metered
  price turns a micro-dollar into money at 0.0001 cents a unit.
- **Never twice.** The ledger is append-only and its rows commit late (a row is saved when its whole check finishes, with the call's
  start as its time and an id made when the call ended), so no time or id watermark is safe. `ai_usage_charges` has one row per
  settled call, with the ledger call's id as its primary key. Three layers hold: the message's dedupe key, that primary key, and
  Stripe's own `identifier`, which it remembers for 24 hours (a repeat is a 400 `duplicate_meter_event` and counts as taken).
- **A batch Stripe stops part-way.** `IBillingProvider.ReportAiUsageAsync` returns the ids Stripe took and the failure, and does not
  throw. The handler saves the rows for what Stripe took and returns, so a retry cannot send those events again after Stripe's 24
  hours; the rest waits for the next slot. Throwing would roll that save back with Wolverine's transaction. A refusal logs the status and
  Stripe's error code and never the key or the customer.
- **Not priced, not billed.** A call's cost is fixed when its row is written. A built-in call recorded with no price for its model
  (`Verification:Llm:Prices:<model>` unset) has no cost, is never reported, and a price added later does not reprice it. The page counts
  these calls and says they are not charged, and every run of a paying organization logs how many there are from the last 34 days
  (a warning), so a missing price shows up in the Aspire dashboard.
- **USD only.** The ledger prices in US dollars and all items of one Stripe subscription share a currency. `BillingAiGate` refuses to
  start a host that has `Ai:StripePriceId` set beside a subscribable plan in another currency. The brief said "in the plan's currency";
  converting would need an exchange rate the ledger does not have.

**The free trial.** Included up to `Ai:TrialAllowance` (US dollars, 5.00 by default), because a trial has no Stripe customer to meter
against. A call that started before `TrialEndsAt` is a trial call (no new column: an Owner who subscribes early still keeps the allowance
for the trial window). Once the account is paying, `AiChargePlanner` (pure, in the domain) walks the pending trial calls in time order and
settles each as `Waived` while the waived total, counting earlier runs, stays within the allowance; the first call that would pass it,
and every call after it, is reported in full, so use above the allowance is on the first paid invoice. A call that cost nothing is
`Waived` with no micro-dollars, because the meter takes none. A call made after the trial with no plan is not a trial call and is
billed in full when a plan starts. The Billing page says all of this in plain words.

**On the subscription.** Checkout adds `line_items[1][price]=<Ai:StripePriceId>` (a metered price takes no quantity). The plan change
and the webhook read the item that carries a catalogue price, whichever order Stripe lists the items in (`PlanKeyOf`, `PeriodEnd`), and
the plan change's preview ignores the metered line: it is usage so far, not the plan, and its earlier period would pull the next payment's
date back. A subscription made before the metered price was set has no such item; the owner adds it in Stripe. Until then Stripe still
takes that customer's meter events, so the calls are recorded as `Reported`, but no invoice charges them.

**The sandbox** takes every event (`SandboxBillingProvider.ReportAiUsageAsync`) and keeps no meter of its own. The `ai_usage_charges` row,
with provider `Sandbox`, is the record that the usage was reported, so the local demo and the tests show it. Brightwater is seeded Active on
Unlimited with a trial that ended the day before, so the worker's next 15-minute report sends its demo calls to the sandbox meter in full.

**Budgets and caps count billed dollars** (owner decision, 2026-11-21): the same amount the Usage tab and this page show, so a cap of $50
stops at $50 billed ([AI usage](ai-usage.md#budget-alerts-and-the-cap)).

### Owner actions for AI usage

Billing code cannot do these. Until they are done Stripe mode reports no usage (the log says so each run), and the sandbox is unaffected:

1. In Stripe, create a meter: `event_name` = `Ai:MeterEventName`, aggregation `sum`, customer by `stripe_customer_id`, value in `value`.
2. Create a product (for example "AI checks") with a metered price on that meter: currency USD, `unit_amount_decimal=0.0001` (cents) per
   unit, and `tax_behavior=exclusive` as for the plans. A unit is a micro-dollar. Set the price id as `ScreenHire:Billing:Ai:StripePriceId` where the API and the worker run.
3. Give the worker the same Stripe secrets as the API (`ScreenHire--Billing--SecretKey` and `WebhookSecret` in the vault, which both hosts
   read; locally the same two parameters).
4. Set `Verification:Llm:Prices:<model>` for the deployed model. A call recorded without a price is never billed.
5. Add the metered item to any subscription made before the price was set. Check the first real meter event in Stripe's test mode
   ([status](../status.md#owner-actions)): no test here reaches Stripe, and the calls are pinned to API version `2024-06-20`.
6. Decide the public pricing page's wording. It says "plus 20%" (`apps/marketing/src/lib/pricing.ts`, from `packages/plans/ai.json`) while
   the app shows one amount and never the margin.
7. Check the Billing page's invoices and card against a test-mode customer: the list, the PDF link and the card on file.

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
  locally because it shows the ended state. Opened by Change card (the state carries the intent), it changes nothing and goes back.

Once there is a customer, the sandbox lists three sample invoices, one a month at the plan's price, the newest at the start of the current
period and Due while a payment has failed, and its card is the test Visa ending in 4242. A sample invoice's PDF is
`GET /v1/billing/sandbox/invoices/{id}`, mapped only in sandbox mode, for an Owner: a one-page PDF written by hand
(`SandboxBillingProvider.SampleInvoicePdf`, no PDF package), and a 404 for an id the sandbox never made.

The routes are anonymous because the browser arrives by a plain navigation. What each does is in the `state`, a Data
Protection value issued by the API under the purpose `ScreenHire.Billing.SandboxState.v1` that expires after 10 minutes,
so the address cannot be edited to name another organization or plan. The sandbox has no webhook. With the sandbox on, the
demo's Brightwater is seeded with an Active subscription on Unlimited, so the demo is never held to a limit.

## Data

Three tenant-owned tables, each with the `tenant_isolation` row-level security policy (migrations `AddBilling` and `AddAiUsageCharges`):

- `billing_accounts`, one per organization (unique on `organization_id`): `trial_ends_at`, `plan_key`, `status`
  (`None`, `Active`, `PastDue`, `Ended`), the Stripe customer and subscription ids, `current_period_end`,
  `cancels_at_period_end`, `last_event_at`, `created_at` and `updated_at`. It is the last state the provider told us, which
  is all the publish check reads. The provider owns the truth.
- `billing_events`, one row per provider event applied (unique on `organization_id` and `event_id`), with no payload.
- `ai_usage_charges`, one row per ledger call that billing has settled (see AI usage): the ledger call's id as the primary key,
  `state` (`Reported` or `Waived`), `micros`, the `margin` it was priced with, `provider` (`Stripe` or `Sandbox`) and `settled_at`.
  Insert-only for the application role, with the same tenant isolation policy as the other two.

## The Billing page

Settings > Billing (`/settings/billing`) is the one place an Owner sees everything the organization pays, already added up. It is in the
Settings menu's Organization group for an Owner, and in the account menu for the same people, and only where a payment provider is set up
(`available`): elsewhere the entries and the page are hidden, as Slack's are (design system A3). It names no payment provider. Its panels,
in order:

- **Plan.** The state as a status badge beside the title (Free trial, Active, Payment failed or Ended), the current plan, the trial's end
  or, for a subscription set to end, when it ends, and the open jobs against the limit (`3 of 5`, or `3` with none). The next payment is
  not repeated here: This month so far says it (W7).
- **This month so far** (`thisMonth` on the overview, `BillingEndpoints.ThisMonth`). Each charge on its own line: the plan at its monthly
  price while subscribed ("Growth, free trial" at $0.00 on the trial, no line once ended), AI use on ScreenHire's key with its call count,
  which links to the Verification area's Usage tab (no line where Stripe has no metered price, because none is billed), "Included in your
  trial" taken off for what the allowance covers (`AiUsageBillingQueries.TrialCreditThisMonthAsync`: calls already settled as waived and the
  trial calls still waiting, planned by `AiChargePlanner` as the report will settle them), and the total. Then "Next invoice about $X on
  <date>" while a subscription runs: the next plan payment (none once it is set to end) plus this month's AI use, "about" because the
  calendar month and the billing period differ, and "plus tax where it applies" while `taxAtCheckout`. On the trial it says nothing is
  charged until a plan is chosen.
- **Payment method**, once there is a customer (`canManage`): "Visa ending in 4242", with Change card and Cancel plan, which both open the
  provider's portal with their intent (no Cancel plan for a plan already set to end). They replace the old "To change your card or cancel,
  use Manage billing" line and the header's Manage billing button, so the words are the controls (A14) and the page has no primary button.
- **Invoices**, once there is a customer (`BillingInvoices`, `GET .../invoices`): date, amount, status (Paid, Due, Void) and a PDF link
  opening in a new tab, newest first, sortable (L2).
- **Plans.** A plan has a Choose button while Trialing or Ended (`mayChoosePlan`), which goes to Checkout. With an Active subscription that
  is not set to end, every other plan has a Switch button, which opens `ChangePlanDialog`: it asks the preview route when it opens and shows
  "$X now, then $Y a month from DATE" ("Nothing now" for a cheaper plan, with the line that the unused time is credited), and Confirm change
  sends the plan and the preview's `prorationDate`. The dialog's Confirm is the one primary there (A1). The dialog takes focus itself when it
  opens, so the date's tooltip does not open over the text. A failed preview keeps Confirm off, and a refused change stays in the dialog,
  which prices it again. After it, the Plans panel says the plan is changing until the page shows it.
- **AI usage** (the overview's `aiUsage`): "This month: 59 AI calls, $0.05", the one amount, never the provider's price or the margin;
  then one sentence for where the organization stands: that billing for AI use is not set up where Stripe has no metered price (checked
  first, so a trial is never promised an invoice line that will not come), what the trial includes and that use above it is added to the
  first invoice, "AI use is billed once you choose a plan" after the trial, or what the trial covered once a plan is paying. Calls on the
  organization's own key and calls with no known price are each counted in a sentence of their own and are not charged. The footer says
  bringing your own key removes the charge, with a link to Verification to set it and one to the Activity log for the call-by-call list.

Money always has two decimals ($79.00, $0.05). The page refetches when `BillingChanged` arrives (which invalidates the invoices and the card
too, by path) and when the browser returns from Checkout.

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
| `StripeBillingProviderTests` (unit) | the invoices (newest first, drafts left out, each status, a zero-decimal amount, only an https PDF link), the payment method (the subscription's own, then the customer's, a method that is not a card, none), a refused read never naming the key, the meter events (fields, identifier, a duplicate counted as taken, a refusal stopping the batch, no customer sending nothing), the metered item on Checkout only when its price is set, a preview and an event ignoring the metered line and item, and the Checkout and portal requests against a stub handler (fields, metadata, key, pinned version, customer reuse), the tax fields with tax off, on, on for an existing customer and on with tax ids off, the plan change's lookup, preview and update requests (the plan's item among others, the date, `always_invoice`, the idempotency key) and a preview read into amounts (prorations as due now, a downgrade at 0, a zero-decimal and a three-decimal currency, a missing next payment), refusals and network failures without the key, each event type read into the common event, a period end from the subscription or its first item, and a plan from the price, ahead of the metadata |
| `SandboxBillingProviderTests` (unit) | the sample invoices (monthly at the plan's price, newest first, Due while past due, none before a customer), the test card, a well-formed sample PDF and none for an id it never made, the portal's intent in its state, the sandbox's links and state: what it names, whether tax and a tax id were asked for, its lifetime, a tampered, foreign or oversize state not reading, and no webhook; and its day-based proration (mid-period, first and last day, a downgrade, no known period end, a period end that has passed) |
| `BillingTests` (integration) | the Owner's overview, the trial at sign-up and for an older organization, Checkout and the portal through the sandbox (asking for no tax on the shared stack), who may use them, and billing off; the invoices, their PDF and the card through the sandbox and who may read them; the portal opened to change the card leaving the plan and opened to cancel ending it; a paying month added up |
| `BillingThisMonthTests` (unit) | This month so far in each state: the plan and AI use added up, the trial's credit taken off and never more than the use, a plan set to end invoicing only its AI use, an ended plan's missing line, and AI use that is not billed left out |
| `BillingTaxTests` (integration) | on a stack of its own with `Stripe:Tax:Enabled`, the overview's `taxAtCheckout` and the sandbox Checkout asking for tax and a tax id |
| `BillingPlanChangeTests` (integration) | the preview and the change through the sandbox: an upgrade's amounts bounded by the shipped prices, a downgrade owing nothing, a confirmation switching the plan and no other organization's, a stale or future `prorationDate` refused, a trial, an ended organization or a subscription set to end a 409, an unknown or current plan a 400 on `plan`, and Admins and Recruiters a 403 |
| `BillingPlanLimitTests` (integration) | the 402 at the limit and when ended, open jobs left open, republish and close not counted, the whole organization counted for a member on "only jobs they're on", and no limit when billing is off |
| `BillingPublishCheckTests` (integration) | the job being published is not counted, so a double submit is not refused for taking its own place, and a different draft still is |
| `BillingWebhookTests` (integration) | the route is a 404 outside Stripe mode. What a delivery does once read, through `BillingService` against the stack's database: a replay applied once, an older event ignored, and an event for another organization not applied |
| `StripeWebhookRouteTests` (integration) | on a stack of its own in Stripe mode, the route end to end: a signed event applied to the organization it names, a wrong or missing signature a 401, a redelivery changing nothing, an unknown organization a 200, and another organization untouched |
| `BillingTests` (end-to-end) | an owner sees the trial and the plans and chooses one through Checkout, then the month's total, the next invoice, the card and the sandbox's invoices, changes the card without losing the plan and reaches Billing from their own menu; a publish past the limit shows the notice and its link to Billing; and a recruiter has no Billing entry in Settings or their menu |
| `billing-page.test.ts`, `billing.test.ts`, `app-sidebar.test.ts`, `jobs-page.test.ts`, `distribution-panel.test.ts`, `job-page.test.ts`, `query.test.ts`, `settings.test.ts` (Vitest) | the page in each state, this month added up, the payment method's two controls and their intents, the invoices, the AI usage panel's one amount, money in two decimals, the account menu's Billing entry, the plan change dialog (the cost before confirming, a downgrade's nothing now, the change sent with the preview's date, a failed preview and a refused change), the notice and its link for an owner and anyone else at each place a draft opens, the 402 read as a plan problem, the settings entry hidden without a provider |
| `TrialRemindersTests` (unit) | the reminder days (7 and 1 by default, a configured list replacing them, a day below 1 refused), the steps planned for a fresh trial, one under way, one that ended and one shorter than the notice window, a reminder doing nothing after a subscription or for a changed trial, the id of the record that a trial's reminders are scheduled, and the email's words |
| `TrialReminderTests` (integration) | on the running worker: signing up schedules the five steps, the worker leaves the account alone and an immediate checkout is applied, each email reaches every Owner once and a redelivery adds none, an Admin gets nothing, one `trial-ending` notification becomes one `trial-ended` one, the retention purge keeps it, and choosing a plan clears it and makes the reminders still waiting do nothing |
| `AiChargeTests` (unit) | a call's charge in micro-dollars (cost plus the margin, half away from zero, nothing for a cost that is unknown or zero), the sum of rounded charges, and `AiChargePlanner`: trial calls waived in time order within the allowance, the first to pass it and every later one reported, earlier waivers counted, a call after the trial reported, a free call waived, the same decisions in any order |
| `BillingAiUsageTests` (integration) | in process against the stack with a fixed clock: only built-in calls with a known cost reported, once, at cost plus the margin; a free call waived and never sent; the allowance; no report before a plan or after it ended, and the wait ending when a plan starts; batches and the follow-up key; a batch Stripe stops part-way keeping what it took and the next run sending only the rest; the Stripe event's fields; Stripe without a price or customer sending nothing; billing off; row-level security and no UPDATE on `ai_usage_charges`; the fan-out and the 15-minute schedule; the overview's month, with no provider's price or margin in it; the page's sum equal to what was reported; the trial credit agreeing with what the report waives |
| `RolePermissionsTests` (unit) | Manage billing is an Owner-only permission and its routes join the route table |

The test stacks pin nothing: they sell the plans in `plans.json`, and a test that needs a price or a limit reads it through
`ShippedPlans` (`tests/ScreenHire.Testing`), so a changed working number needs no test edit.

## Not built

- Tax filing and registration. Checkout calculates tax and collects tax ids (see Tax), but registering with Stripe Tax and filing returns are the owner's ([billing providers](../research/billing-providers.md) says what Stripe Tax does and does not do). The overview and the Billing page show prices without tax, with the words "plus tax where it applies", and never the tax Stripe charged.
- A list of invoices or receipts; Stripe's portal has them.
- Amounts read from Stripe except in a plan change's preview. The rest of the page shows the catalogue's price and Stripe
  charges its own.
- Completing a payment that needs the Owner (3-D Secure) inside a plan change. It fails whole and sends the Owner to Change card.
- Coupons and tax in a plan change's preview. Its amounts are the invoice lines' amounts, before discounts and tax, and only
  the lines Stripe returns with the preview (its first page) are read.
- Seat-based pricing. A plan counts open jobs only.
- AI use in a currency other than US dollars, a refund or credit for a call that was reported and later disputed, and the margin on the
  Verification dollar budget and cap (they count the provider's cost). A meter event Stripe took and our save lost for more than 24 hours
  would be reported twice; the next run follows the first by 15 minutes, so only a long outage can do it.
- Usage in the Billing page by day or by model: Verification has the call-by-call detail.
- A grace period, or an Ended state that blocks more than opening a draft.
