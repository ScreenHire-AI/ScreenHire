---
type: Decision
title: "ADR-0032: Prepaid AI credits on our own ledger"
description: "AI use on ScreenHire's key is paid for in advance as credits, which replace the metered charge and the monthly budget and cap: our own append-only ledger, not Stripe's billing credits, because those apply only when a subscription invoice finalizes; credits granted only from Stripe's signed confirmation, once per purchase; Checkout in payment mode to buy and an invoice paid off-session to auto-reload; the AI check pausing at zero as the cap did, never marking a candidate."
tags: [adr, billing, stripe, ai, credits]
---
# ADR-0032: Prepaid AI credits on our own ledger

- **Status:** Accepted (2026-11-22)
- **Deciders:** Owner (the fifth walk-through, note 12), the planning agent for the ledger
- **Amends:** ADR-0029 decision 12 (AI use metered on top of the plan)
- **Builds on:** ADR-0004 (evidence, not verdicts), ADR-0014 (Manage billing is an Owner's)
- **Detail:** `docs/architecture/billing.md` ("AI credits"), `docs/architecture/ai-usage.md` ("Credits and the pause")

## Context

Until now ScreenHire billed AI use on its own key after the fact. The worker reported every call to a Stripe meter every 15
minutes, the metered price on the subscription turned it into money on the next invoice, and an Owner could set a monthly
budget (alerts) and a cap (a pause). The owner's fifth walk-through asked for what Anthropic's console does: an organization
loads credits first, may turn on auto-reload, and has paid before it uses anything. Only a payment that is confirmed as completed may
grant credits.

The platform-first rule (`.claude/rules/cli-features-over-custom-layers.md`) asks whether Stripe already has this feature. It
has two candidates: billing credits (credit grants) and the customer balance. Both were read on 2026-11-22.

## Decisions

1. **The ledger is ours.** `credit_entries` is append-only: the application role can neither update nor delete a row. It holds
   one row per grant (the starting credit, a purchase, an auto-reload) and per debit (an AI call, an expiry). The balance is
   its sum. Every row carries a key unique within its organization, so each effect happens once: one grant per purchase, one
   debit per call and one expiry per grant.
2. **Stripe takes the money and nothing else.** A purchase is Stripe Checkout in payment mode for one of the fixed amounts
   (`ScreenHire:Billing:Credits:Amounts`, default 10, 25, 50 and 100 US dollars). It saves the card for later off-session use
   and creates an invoice. An auto-reload is a Stripe invoice that we create, finalize and pay off-session with that card. It is
   an invoice and not a bare PaymentIntent so that Stripe Tax applies to it under `Stripe:Tax:Enabled`, as it does at Checkout,
   and so that it appears with the other invoices.
3. **Credits are granted only from Stripe's signed confirmation, once per purchase.** That confirmation is
   `checkout.session.completed` with `payment_status=paid`, `checkout.session.async_payment_succeeded`, or `invoice.paid` for a
   reload. The webhook publishes `GrantAiCredits` through the outbox, and the worker's handler is `[Deduplicated]` on the purchase.
   The grant's ledger key is our purchase id, which both Stripe events of one Checkout carry in metadata. The browser's return to
   the page grants nothing. The sandbox has no webhook, so its fake Checkout publishes the same message to the same handler.
4. **Every call on ScreenHire's key draws the balance down** at the billed price: the provider's price plus `Ai:Margin`, in
   micro-dollars, in the same save that records the call. An organization's own key never touches the balance.
5. **At zero the AI check pauses**, as the cap paused it. The other checks keep running. The paused check records that it did
   not run, in words that say it is no result about the candidate. A grant asks again for the paused checks. A balance read before a
   check can be overshot by checks that run at the same moment; the next grant repays the shortfall first.
6. **Auto-reload is off by default and never retries a refused charge.** "When the balance falls below $X, add $Y", up to a
   monthly maximum. A declined or authentication-required charge marks the purchase failed, turns auto-reload off, voids the
   invoice and tells the Owners in the bell and by email. At most one reload is in flight per organization (a partial unique index).
7. **The monthly budget and cap are gone.** The balance, a low-balance alert (default $5) and auto-reload's monthly maximum
   replace them. A dollar cap on ScreenHire's key became the monthly maximum. A budget, a limit stored in tokens and a limit on
   an own key had no equivalent and were dropped. So were the deployment's default token budget and cap: a balance cannot be
   overspent by more than a few checks.
8. **Credits expire** after `Credits:ExpiryMonths` (12 by default; null never). Debits use the credits that expire first, and
   a daily run writes off what is left of each expired grant. The trial's AI allowance becomes the starting credit
   (`Ai:TrialAllowance`, $5), granted with the billing account.
9. **No candidate is ever marked by a billing state** (ADR-0004). A paused check is neither a signal nor a level, and a
   balance never appears on a candidate's page.

## Consequences

- The metered price, the meter, `ReportAiUsage` and `ai_usage_charges` are removed. AI use is no longer on the plan's invoice,
  and credit purchases are invoices of their own.
- A refund is a person's action in Stripe and does not change the balance. There is no self-serve refund.
- The Stripe webhook endpoint subscribes to two more events (`checkout.session.async_payment_succeeded` and `invoice.paid`).
- Credits are in US dollars, whatever currency a plan is in, because the ledger prices a call in US dollars.
- With billing off (no provider), there is no balance and nothing pauses the AI check. That is a self-hosted deployment, where
  nobody is billed.

## Alternatives considered

- **Stripe billing credits (credit grants).** Stripe's own prepaid credits for usage-based billing. Rejected for three reasons.
  First, Stripe applies a grant to a subscription invoice's metered lines only when the invoice finalizes
  (https://docs.stripe.com/billing/subscriptions/usage-based/billing-credits), so the app could not pause the AI check at zero
  while the month runs. The credit balance summary documents no live figure to read instead. Second, a grant needs a customer
  and a subscription with a metered price, and a trial organization has neither; the starting credit is for exactly those
  organizations. Third, there is no auto-recharge: Stripe's implementation guide funds each grant with a manual invoice, and
  credit-balance alerts are early access. The reference is also written against API versions far past our pinned `2024-06-20`
  (ADR-0029).
- **Stripe's customer balance (cash balance).** It is applied to invoices too, with the same timing problem.
- **A cached balance column on `billing_accounts`.** One row to read, but every AI call would write the row whose concurrency
  token the webhook races on (`docs/architecture/billing.md`, "Races"). The sum of an organization's ledger is small and indexed.
- **A bare PaymentIntent for auto-reload.** One call instead of four, but Stripe Tax does not apply to it, and the charge
  would not appear among the invoices.
