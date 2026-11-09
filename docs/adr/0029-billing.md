---
type: Decision
title: "ADR-0029: Billing, plans and the free trial"
description: "How an organization pays and what a plan allows: plans are configuration over a shared packages/plans catalogue, Stripe sits behind IBillingProvider with a local sandbox and is called over plain HTTP with a pinned API version, billing is off and limits nothing until configured, a trial starts at sign-up, only opening a draft job is ever refused (402), the open-job count is organization-wide under a per-organization lock, a signed webhook names its organization in metadata and applies once, and only an Owner sees any of it."
tags: [adr, billing, stripe, plans, trial]
---
# ADR-0029: Billing, plans and the free trial

- **Status:** Accepted (2026-11-07)
- **Deciders:** Owner
- **Builds on:** ADR-0008 (licence policy and the package list), ADR-0011 (an API key reaches only the hiring API),
  ADR-0014 (permissions), ADR-0021 (the Owner-only grant, which Manage billing follows)
- **Detail:** `docs/architecture/billing.md`
- **Research:** `docs/research/billing-providers.md` (vendor fees and tax duties read on 2026-11-08)

## Context

ScreenHire is sold to small hiring teams on a monthly plan priced by how many jobs are open at once. Until now nothing
charged anyone and nothing limited anyone. The owner has not chosen prices or limits, has no Stripe account yet, and wants
the whole flow to run locally and in tests without one (`.claude/rules/unattended-runs.md`). The marketing site's pricing
page already shows three plans with placeholders for the numbers.

The rules that already hold stay put. Every tenant table has row-level security (CLAUDE.md non-negotiable 6). A route
that changes data names a permission, never a role (ADR-0014). Candidates only ever meet the careers site, so nothing
about payment may reach it. A payment problem must not lock a team out of candidates they are already talking to.

## Decisions

1. **Plans are configuration, and `packages/plans/plans.json` holds the defaults.** Each plan has a key, a name, an
   order, a monthly price, a currency, an active job limit, an "unlimited" flag and a Stripe price id. The file ships
   with all three plans (Starter, Growth, Unlimited) and no numbers, because the numbers are the owner's call.
   (Update 2026-11-07: the owner chose "every feature on every plan, priced by open jobs", and the file now ships working
   numbers: Starter $79 with 3 open jobs, Growth $149 with 10, Unlimited $249. The owner confirms them before launch.)
   The API embeds the file, `ScreenHire:Billing:Plans:<key>:*` overrides any field, and the marketing pricing page reads
   the same file through `@screenhire/plans`, so the page and the product cannot disagree. A plan with no limit set limits
   nothing: a job count nobody chose is never enforced.
2. **Payment goes through `IBillingProvider`, with a sandbox and Stripe over plain HTTP.** The interface starts a
   subscription (Checkout), sends the owner to the provider's own page to change or cancel it (the portal) and turns a
   signed webhook into a common event. `SandboxBillingProvider` is the local fake. `StripeBillingProvider` calls Stripe
   with `HttpClient`, not Stripe.net, and pins the API version of its calls to `2024-06-20` in code. A webhook is not
   covered by that header: Stripe renders it at the webhook endpoint's own version, or the account's default, so the
   endpoint is created at the same version (`docs/operations/deploy.md`) and a new Stripe release changes nothing until
   we move both. It uses one long-lived client rather than a factory client, because the standard resilience handler
   would retry a Checkout call and could create a second session.
3. **With no provider configured, billing is hidden and limits nothing.** The mode is Stripe when both its keys are set,
   Sandbox when the sandbox is enabled, otherwise Off. Off means no account row, no trial, no 402 and a billing route
   that answers `available: false` or 404, as Slack does. The sandbox is refused outside Development when the host
   starts, because its checkout charges nobody.
4. **A trial starts at sign-up.** Creating an organization also creates its billing account, with a trial that ends
   `Trial:Days` later (14 by default) and applies the limit of `Trial:Plan` (Growth by default). The end date is stored,
   so changing the setting never moves a trial already running. An organization older than billing gets its account the
   first time anything asks for it, which starts its trial then.
5. **Four entitlement states, and only opening a draft is ever refused.** The states are Trialing, Active, PastDue
   and Ended, worked out from the stored subscription status and the trial's end by one pure function in the domain.
   Opening a job from Draft (publish) is refused when the account has Ended, or when the plan's limit is already used.
   A job that is open stays open, republishing and closing are never counted, and every page and report stays readable
   and editable. A payment that fails (PastDue) changes nothing at all until the provider gives up. A team that stops
   paying loses the ability to add work, not the work.
6. **The refusal is a 402 with a problem type the dashboard recognises.** `urn:screenhire:problem:plan-limit` says the
   plan allows N open jobs and N are open. `urn:screenhire:problem:subscription-ended` says the subscription has
   ended, or the free trial when no subscription ever started. Both carry a plain-language `detail`, and the dashboard adds the upgrade link only for an Owner. 402 is the
   status that means "payment needed", it is not a 409 (the job is fine, the plan is not) or a 403 (the person may
   publish, the organization may not), and the publish route's OpenAPI description lists it.
7. **The open-job count runs in its own organization-wide scope, under a per-organization advisory lock.** A member
   on "only jobs they're on" sees only those jobs, and a plan limit counts every job. So the count opens a scope of its own
   that acts for the organization, as a worker handler does. Inside the publish transaction a `pg_advisory_xact_lock`
   keyed on the organization serialises the check, so two jobs opened at the same moment cannot both slip under the
   limit. The lock is taken only when the plan has a limit.
8. **A webhook names its organization in signed metadata and is applied once.** Checkout puts the organization id and the
   plan key in the session's and the subscription's metadata (and the organization in `client_reference_id`). The webhook
   route is anonymous and checks the `Stripe-Signature` header first (HMAC-SHA256, a five-minute tolerance, constant-time
   comparison); only then does it trust the metadata, and it sets the tenant with `SetPublicOrganization`, as every
   anonymous route that acts for one organization does. Each provider event id is stored in `billing_events` under a
   unique index, so a redelivery, even two at once, changes nothing, and the account keeps the provider's timestamp of the
   newest event applied so a late older event cannot undo a newer one. A signed delivery gets a 200, even for an event
   type or organization this app does not know, so Stripe stops retrying it. The exception is a delivery that loses a race
   for its account, which gets a 409 so Stripe sends it again.
9. **Only an Owner manages billing.** A new permission, Manage billing, is Owner-only (ADR-0014), so a custom role cannot
   have it. Every billing route needs it, and an API key gets nothing here (ADR-0011). The page, its settings entry and
   its 402 upgrade link are hidden from everyone else, who read "Ask an owner to upgrade the plan".
10. **The price shown comes from configuration; the charge comes from the Stripe price.** The dashboard and the pricing
    page read `monthlyPrice` from the catalogue. Checkout charges the Stripe price in `StripePriceId`. We do not read
    amounts from Stripe on every page load: that makes a page depend on a third party and spreads the owner's pricing
    decision across two systems. The cost is that the owner keeps the two in step when a price changes, and a plan with
    no `StripePriceId` cannot be subscribed to in Stripe mode (the sandbox ignores the field).
11. **Candidates never see any of it.** No careers page, email, notice or realtime event for a candidate mentions plans,
    limits or payment. A job that a lapsed plan cannot open is simply not published, and the one that is open keeps
    taking applications.

## Consequences

- A paying customer needs, from the owner, a Stripe account, one product with a price per plan, the three
  `StripePriceId` values, the two secrets and a webhook endpoint (`docs/operations/deploy.md`). Until then
  every flow runs on the sandbox and none of it needs the internet.
- The shipped catalogue has no prices and no limits, so a production API with Stripe keys but no `Plans:*` settings
  shows plans without prices and limits nothing. That is deliberate: it fails open, never closed.
- A trial that ends without a subscription is Ended: new jobs stop, everything else works.
- Two more tenant tables (`billing_accounts`, `billing_events`) have row-level security. The webhook and the sandbox
  pages write them with the organization from the event, never from a user.
- The Stripe client is about 200 lines we maintain. A Stripe API change that breaks it is caught at the pinned version,
  not at an upgrade we did not choose.
- A cancelled subscription keeps working until Stripe says the period ended; the account follows Stripe's events,
  not the calendar.
- Invoices and proration are Stripe's, and we show none of them (`docs/architecture/billing.md`, Not built). Tax on our
  own charges is our duty as merchant of record, with Stripe Tax as the tool (see the alternatives below).
- Nothing about verification changes. A level is evidence for a person to review and never a reason to reject
  (ADR-0004), and a plan limit is not a verification signal.

## Alternatives considered

- **The Stripe.net SDK.** It is MIT and passes ADR-0008. Three calls and one signature check do not earn a package that
  ships a Stripe API version of its own choosing and a large surface of types we would not read. Plain `HttpClient` with a
  pinned `Stripe-Version` header keeps the version a decision. Stripe.net remains an option if the integration grows
  past Checkout, the portal and four events.
- **Reading prices and limits from Stripe.** One source of truth for the price, but the pricing page is static, the
  dashboard would wait on a third party, and limits are not a Stripe concept (they would live in product metadata,
  which is configuration with a worse editor). The catalogue stays ours and Stripe holds the charge.
- **A Stripe-managed trial.** Stripe can run a trial on a subscription, but that asks for a card before anyone has seen
  the product and needs a Stripe customer for every sign-up. The trial is a date on our own row; Stripe is involved only
  once someone chooses to pay.
- **A lookup table from Stripe customer id to organization.** The webhook could find its organization from the customer
  id. The metadata Checkout carries is signed by the same webhook secret and needs no table to keep in step, and an event
  that arrives before our row exists (a Checkout that completes first) still names its organization.
- **Another billing platform or a merchant of record (2026-11-09 review).** The owner asked whether Stripe is the right
  platform. `docs/research/billing-providers.md` read each vendor's own pages on 2026-11-08. At the working prices a month
  costs 3.7 to 4.0% of the price on Stripe Payments plus Billing (2.9% + 30 cents and 0.7%), 5.2 to 5.6% on Paddle
  (5% + 50 cents, merchant of record) and 7.2 to 7.5% on Stripe Managed Payments (Stripe's fees plus 3.5%, Stripe as
  merchant of record). Lemon Squeezy lists Paddle's fee and is being folded into Managed Payments. Azure Marketplace takes
  3% and moves US sales tax to Microsoft, but only for customers who buy through Microsoft, so it is a second channel and
  not a replacement. Stripe stays, because it is the cheapest way to charge a card, the integration exists, and the
  merchant-of-record products add 1.5 to 3.5 points for a duty that Stripe Tax covers from $90 a month.
- **Stripe Tax is two products.** Tax Basic (0.5% per transaction where
  registered) calculates and collects tax. It does not register the seller or file returns. Tax Complete (from $90 a
  month) adds registration help and filing, and Stripe files US returns through TaxJar from the seller's own US bank
  account. The tax work therefore stays with ScreenHire's owner: register where the owner's accountant says, add each
  registration to Stripe, and choose how to file. The task that builds Stripe Tax and tax ids at checkout turns on
  automatic tax and tax id collection, and the registrations decide where it collects.
- **Stripe Managed Payments as the fallback.** It was out of preview on 2026-04-29 per Stripe's own recap, works with the
  Checkout and portal calls the provider already makes, and takes registration, filing and remittance in 80+ countries off
  the seller for 3.5% more. Below about $2,600 of monthly revenue that is cheaper than the cheapest Tax Complete plan, and
  above it Tax Complete is cheaper. It also changes what customers see (receipts and card statements come from Link) and
  Stripe's eligibility review applies. If tax administration becomes a burden, switching is a Checkout setting, not a new
  vendor, and `IBillingProvider` does not change. Nothing here builds that switch.
