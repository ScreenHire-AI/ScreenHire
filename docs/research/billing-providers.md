---
type: Research
title: "Billing Providers and Sales Tax for a US SaaS Seller"
description: "Stripe Payments, Billing, Tax and Managed Payments, Paddle, Lemon Squeezy and Azure Marketplace compared on fees and tax duties as read on each vendor's own page on 2026-11-08, a state-by-state table of whether SaaS sold to businesses is taxed and each state's economic nexus threshold, how a seller's own presence creates nexus, EU and UK reverse charge, what Stripe Tax does and does not do, the monthly cost of each plan under each option, and the recommendation behind ADR-0029."
tags: [research, billing, stripe, tax, sales-tax, vat, merchant-of-record]
---
# Billing Providers and Sales Tax for a US SaaS Seller

> Read on **2026-11-08**. This is research, not tax or legal advice. It describes how the rules and the vendors work in
> general and says nothing about any particular seller. A seller applies it to its own facts with an accountant.

ScreenHire is the merchant of record for its own subscriptions (ADR-0029): it charges customers through Stripe, so
sales tax and VAT on those charges are its duty. The working answer going in was to stay on Stripe, turn on Stripe Tax,
and keep Stripe Managed Payments (Stripe as merchant of record) as the fallback. This page checks the figures behind that
answer at their sources.

Method. Each vendor page was opened with a page fetch and each figure below was read in the text that came back. The
fetch returns an extract of the page, so a figure the extract did not state is marked Not verified, whatever search
snippets say, as `pain-points.md` does. Tax statements come from official sources (state revenue departments, HMRC, the
European Commission, Stripe's and Microsoft's own documentation for what their products do). Status values: Verified,
Not verified, or a qualified form ("Verified as extracted", "Verified for X, Not verified for Y") that says which part
was read.

## 1. The answer in short

- Stripe stays. Payments plus Billing cost about 3.7 to 4.0% of a month's price at the working prices. Nothing else
  compared here is cheaper for a seller that handles its own tax, except Azure Marketplace's 3% store fee, which is a
  second sales channel and not a replacement for card billing.
- "Turn on Stripe Tax" is two products. Tax Basic (0.5% per transaction where registered) calculates and collects tax. It
  does not register the seller or file returns. Filing is Tax Complete, from $90 a month on a one-year contract, and
  Stripe files US returns through TaxJar, which pulls the tax from the seller's own US bank account.
- Managed Payments is out of preview and is the fallback. It adds 3.5% on top of Stripe's fees, which makes a month
  cost about 7.2 to 7.5% of the price, and it takes registration, filing and remittance off the seller. Below about
  $2,600 of monthly revenue it is cheaper than the cheapest Tax Complete plan (section 5).
- The numbers do not overturn the working answer. Paddle's 5% plus 50 cents is cheaper than Managed Payments but costs a
  migration to a second vendor, and Lemon Squeezy is being folded into Managed Payments.
- One fact changes the work for UK customers: a proposed rule from 2027-04-01 would make an overseas seller charge UK VAT
  to a UK business that has no VAT number (section 4.3).

## 2. The vendors

Every row was read on 2026-11-08.

### 2.1 Stripe

| Item | Figure as the page states it | Source | Status |
|---|---|---|---|
| Payments, US card | 2.9% + 30 cents per successful transaction for domestic cards | https://stripe.com/pricing | Verified |
| Billing, pay as you go | 0.7% of Billing volume, one-off invoices excluded, no recurring fee | https://stripe.com/billing/pricing | Verified |
| Billing, monthly plan | From $620 a month on an annual contract; 0.67% on volume above the covered amount. The page pairs larger fees with larger covered volumes, and the pairing was read from the layout | https://stripe.com/billing/pricing | Verified for $620 and 0.67%, the rest Not verified |
| Tax Basic, no-code (Billing, Checkout, Invoicing, Payment Links) | 0.5% per transaction where you are registered to collect tax | https://stripe.com/tax/pricing | Verified |
| Tax Basic, API integration | 50 cents per transaction where registered, with 10 calculation calls included and 5 cents for each call above 10 | https://stripe.com/tax/pricing | Verified |
| Tax Complete | "Starting at $90 per month" on a one-year contract. $90 covers 2 registrations a year, 200 transactions a month and 4 filings a year; $430 covers 4, 1,000 and 12; $1,000 covers 6, 2,500 and 20; $1,500 covers 10, 5,000 and 32. No per-transaction fee is listed. Registrations and filings outside the US, or above the plan's limits, can cost more | https://stripe.com/tax/pricing | Verified as extracted |
| Managed Payments | 3.5% per successful transaction on top of Payments fees. No setup fee or monthly minimum. Billing and other products are billed separately | https://stripe.com/managed-payments and https://stripe.com/pricing | Verified |

What Stripe Tax does and does not do, from Stripe's documentation.

- It calculates tax on 700+ product tax categories, collects it, monitors the seller's sales against each location's
  registration threshold and sends alerts (https://stripe.com/tax, https://docs.stripe.com/tax). Stripe's own
  documentation says the business must identify where it has obligations, register itself, and add each registration to
  Stripe under Locations in the Dashboard, or through the Tax Registrations API, before Stripe collects tax there
  (https://docs.stripe.com/tax/registering). Verified.
- Registration is optional help. In the US, "Register for me" with Tax Complete has Stripe register the seller in
  states other than its own. Outside the US a partner, Taxually, does it. The documentation says "Register for me"
  "doesn't prepare returns or remit tax" (https://docs.stripe.com/tax/filing). Verified.
- Filing and remitting is separate and needs Tax Complete for the TaxJar route. Stripe files US sales tax through
  TaxJar in all 46 US locations that have a state-level sales and use tax, and TaxJar uses the seller's US bank account to
  remit the tax collected, so the account must hold enough to cover it. Stripe also files Canada's simplified GST/HST
  and the EU non-Union OSS through Ireland. Filing in other countries is a private preview
  (https://docs.stripe.com/tax/file-with-stripe). Verified.
- Only transactions with automatic tax on are filed. Sales made before Stripe Tax was enabled, and manual invoices
  without it, are not (same page). Verified.
- Stripe does not decide where a seller must register. Its registering page tells its own agents to route that question to
  the seller's tax advisor. Verified.
- A tax id collected at Checkout is checked for format by Stripe at once and against the government databases for EU and
  GB VAT numbers afterward. When the id has the right format Stripe Tax applies the reverse charge or the zero rate
  "regardless of the government verification result", and the seller is responsible for the id being valid
  (https://docs.stripe.com/tax/checkout/tax-ids). Verified.

Managed Payments, from Stripe's documentation.

- Stripe, through Link, is the merchant of record. It collects tax, registers, files and remits in "more than 80
  countries", and it handles fraud, disputes and transaction-level support
  (https://docs.stripe.com/payments/managed-payments and its tax-compliance page). The US is on the list of countries it
  covers for cross-border sales and is a supported business location (eligibility page). Verified.
- Product fit. SaaS is a supported category and the eligibility page lists a business-use SaaS tax code
  (`txcd_10103001`). Stripe "determines access to Managed Payments based on an eligibility review that considers factors
  such as business type and geography", so access is not automatic. The page links Stripe's list of restricted
  businesses, which was not read. Verified for the quoted lines, Not verified for how an applicant-screening product
  would fare in the review.
- Integration limits. It works with Checkout and Payment Links only, direct integrations only, no Connect, no
  subscriptions created outside Checkout, no custom domain on the checkout page, and no third-party tax integrations
  (Stripe Tax is the only compatible tax product, and its calculation fee is waived on Managed Payments transactions in
  countries Stripe does not cover). Verified. ScreenHire's `StripeBillingProvider` creates subscriptions only through
  Checkout, so the Checkout side should need little more than a setting, which this task does not build. It also opens
  Stripe's customer portal for plan changes and cancelling, and pins every call to API version `2024-06-20`. Whether the
  customer portal works under Managed Payments, and which API version Managed Payments needs, were not on the pages
  read. Not verified.
- What customers see. Receipts, invoices and renewal emails come from Link, the card statement reads
  `LINK.COM* <descriptor>`, and customers can manage orders on link.com. Verified
  (https://docs.stripe.com/payments/managed-payments/how-it-works).
- Out of preview. Stripe's Sessions 2026 recap, dated 2026-04-29, says "All digital businesses can now use Managed
  Payments" (https://stripe.com/blog/everything-we-announced-at-sessions-2026), and the documentation pages carry no
  preview label on 2026-11-08. Stripe's own post on X of the same day is reported by a search result as saying "generally
  available", but that page was not opened. Lemon Squeezy's post of 2026-01-28 still described an invite and a waitlist,
  so that wording is out of date. Verified for the recap, Not verified for the "generally available" wording.
- Business customers. The pages read do not say how Managed Payments treats a business customer's VAT number or
  reverse charge. Not verified.

### 2.2 Merchant-of-record competitors and the marketplace

| Item | Figure as the page states it | Source | Status |
|---|---|---|---|
| Paddle | "5% + 50¢" per checkout transaction, pay as you go, no monthly fees. Includes the merchant-of-record role, tax collection, filing and payment of returns, fraud protection, chargebacks and buyer support. Products under $10 get bespoke pricing. No minimum fee or payout schedule is stated | https://www.paddle.com/pricing | Verified |
| Paddle, approval of a screening product | The page does not say | https://www.paddle.com/pricing | Not verified |
| Lemon Squeezy, fee | "5% + 50¢" per transaction, with "small additional fees" outside the US | https://www.lemonsqueezy.com/pricing | Verified |
| Lemon Squeezy, status | Its pricing page carries a "2026 Update: Lemon Squeezy + Stripe Managed Payments" banner and the footer line "Sold through Link, LLC f/k/a Lemon Squeezy LLC". Its post of 2026-01-28 says "Our goal is to provide Lemon Squeezy users an easy way to migrate to Stripe Managed Payments" | https://www.lemonsqueezy.com/blog/2026-update | Verified |
| Lemon Squeezy, owned by Stripe | Its post of 2024-07-26 says "@stripe has acquired @lmsqueezy". That is the company's own account; no deal terms were read | https://www.lemonsqueezy.com/blog/stripe-acquires-lemon-squeezy | Verified |
| Azure Marketplace, transactable SaaS | 3% standard store service fee; Microsoft pays out 97%. Microsoft acts as the publisher's agent and bills the customer; the publisher stays seller and licensor | https://learn.microsoft.com/en-us/partner-center/marketplace-offers/marketplace-commercial-transaction-capabilities-and-considerations | Verified |
| Azure Marketplace, US sales tax | "Microsoft Corporation, as a marketplace facilitator, is responsible for calculating, collecting, and remitting sales tax" on sales to US customers, in every jurisdiction that has a sales tax except US territories. Other countries are Microsoft-managed or publisher-managed, and the list is in Partner Center | https://learn.microsoft.com/en-us/partner-center/marketplace-offers/tax-information-for-commercial-marketplace | Verified |

Azure Marketplace, who can buy and what it takes.

- Customers pay Microsoft, by a card, a monthly invoice or an Enterprise Agreement. The page says Azure free credits and
  prepaid monetary commitments "can't be used to pay for publisher software license fees". Whether an eligible offer can
  count toward a customer's Azure commitment was not on the pages read. Verified for the quoted line, Not verified for
  the commitment question.
- A transactable SaaS offer must be multitenant, let buyers sign in with Microsoft Entra ID single sign-on, integrate
  with the SaaS Fulfillment APIs, run a landing page and a webhook 24 hours a day, and have a tax profile and payout
  profile validated in Partner Center
  (https://learn.microsoft.com/en-us/partner-center/marketplace-offers/plan-saas-offer). Verified.
- It is a way to be found and paid by customers who already buy through Microsoft, and it moves US sales tax to
  Microsoft for those sales. It does not replace card billing for everyone else, so it sits beside Stripe, not in its
  place. The same applies to its fit with ADR-0007's choice of Azure for hosting, which is a separate question.

## 3. US sales tax on SaaS sold to businesses

Sales tax belongs to the buyer and the seller collects it when two things are true: the state taxes the product, and the
seller has nexus there. Both vary by state, and the product question is where SaaS sold to businesses is awkward, because
states classify it differently (as prewritten software, as a service, as an information service, as not taxed at all).

### 3.1 Nexus

Nexus is the connection that makes a seller collect a state's tax. Two kinds matter.

- Economic nexus. After South Dakota v. Wayfair (2018) states can require an out-of-state seller to collect once its sales
  into the state pass a threshold. Most thresholds are $100,000 of sales, some add or once added a 200-transaction test,
  and a few are higher. The table in section 3.2 gives each state's own figure.
- Physical presence. A seller's own presence creates nexus in a state with no sales threshold met. Washington's
  Department of Revenue lists, among activities that "may create physical presence nexus": having an employee working in
  the state, owning or leasing property there, holding inventory there, soliciting sales through employees or
  representatives, and providing services there through employees or other representatives
  (https://dor.wa.gov/education/industry-guides/out-state-businesses/physical-presence-nexus, read 2026-11-08, Verified).
  Stripe's registering page names the same triggers as building offices, employee locations, inventory locations,
  products sold and customer locations (https://docs.stripe.com/tax/registering, Verified).

A seller registered in one state and run from another.

- Both states are places the seller may have nexus. The state where the entity is registered and the state where the owner
  or an employee works, or where it has an office or property, each can create a duty to register, and a seller can owe
  a registration in the second state even though the entity is registered in the first. The seller's accountant confirms
  which states apply. Washington's list above is the cited example of the second kind, and no source read here says
  that registering an entity in a state creates sales tax nexus there by itself, so that part is a question for the
  accountant and not a finding.
- A state that does not tax SaaS sold to businesses (several do not, see the table) gives the seller nothing to collect
  there for that product, though it may still want a registration, a business license or an income tax filing. Those are
  outside this page.
- Some states look at what is sold, not where the seller sits. New York's page on whether to register says the duty
  "is based on what types of items you sell, or what kind of services you provide, and not on your location or number of
  hours you work" (https://www.tax.ny.gov/pubs_and_bulls/tg_bulletins/st/do_i_need_to_register_for_sales_tax.htm, seen in
  a search result and not opened, Not verified).
- Texas separates a "remote seller", whose only in-state activity is remote solicitation, from one with a location or
  salespeople there (https://comptroller.texas.gov/economy/fiscal-notes/archive/2019/sep/taxlaws.php, seen in a search
  result and not opened, Not verified).

### 3.2 State by state

Every row was tried on 2026-11-08, and the two links in a row are the page tried for taxability and the page tried for
the threshold. Most are the state's own revenue or tax department site; a few are not (Ohio's Revised Code, the Alaska
Remote Seller Sales Tax Commission), and a cell says (403), (404), (not read) or "seen in a search result only" where
the page was not read in full. 51 jurisdictions are listed (50 states and the District of Columbia). Of these, 9 are
taxable, 7 not taxable, 18 conditional, 2 have no state sales tax and 15 are Not verified. A row says "Not verified"
where the state's page refused the fetch or did not say. Alaska, Delaware and New Hampshire are counted as Not verified
because their pages were not read in full, though none of them has a state-level sales tax either; with Montana and
Oregon that makes the 5 that Stripe's 46 filing locations leave out.

Read the table with these limits.

- Many states answer through a letter ruling, advisory or bulletin that binds only the taxpayer who asked or is years old.
  A cell says so where that is the only source. A state's answer can also turn on facts: whether the customer downloads
  code, where the server sits, whether the software is customised or sold with a service.
- Rows marked Conditional where the source does not name SaaS are a reading of rule text, not a statement by the state that
  SaaS is taxable.
- California and Colorado change on 2027-01-01, after the read date. Several thresholds still list a 200-transaction test
  and several states have dropped it, so a threshold is the state's current page, not a rule across states.
- Local taxes (Alaska's, Colorado's home rule cities, Chicago's lease tax) are outside the table.
- A state with no sales tax can still have a gross receipts or business tax on the seller (Delaware, Oregon).
- States for which the page could not be read say Not verified and no taxability is assumed. The accountant fills those.

| Jurisdiction | SaaS to businesses | Economic nexus threshold | Taxability source | Threshold source |
|---|---|---|---|---|
| Alabama | Conditional: software licences are taxed whatever the transmission, SaaS not named | $250,000 in sales | https://www.revenue.alabama.gov/wp-content/uploads/2021/12/810-6-1-.37-.pdf | https://www.revenue.alabama.gov/sales-use/simplified-sellers-use-tax-ssut/ |
| Alaska | Not verified (no state sales tax; local taxes vary) | No state sales tax. Local taxes through ARSSTC member jurisdictions: $100,000 of statewide gross sales | none read | https://arsstc.org/faqs-for-sellers/ |
| Arizona | Conditional: taxed as a rental of personal property when the customer controls the software (a taxpayer ruling) | $100,000 | https://azdor.gov/sites/default/files/2023-03/RULINGS_TPT_2017_lr17-001.pdf | https://azdor.gov/business/transaction-privilege-tax/retail-sales-subject-tpt/out-state-sellers/economic-threshold |
| Arkansas | Not verified (a 2018 matrix taxes prewritten software on a tangible medium and does not address SaaS) | $100,000 or 200 taxable transactions | https://www.dfa.arkansas.gov/wp-content/uploads/2018TaxabilityMatrix.pdf | https://www.dfa.arkansas.gov/office/taxes/excise-tax-administration/sales-use-tax/remote-sellers/ |
| California | Conditional: not taxable until 2027, taxable from 2027-01-01 (SB 122) | $500,000 | https://cdtfa.ca.gov/formspubs/l1036.pdf | https://cdtfa.ca.gov/industry/wayfair/general-information.htm |
| Colorado | Conditional: not taxable through 2026, generally taxable from 2027-01-01 unless a negotiated licence | $100,000 | https://tax.colorado.gov/sales-use-tax-topics-computer-software | https://tax.colorado.gov/sales-tax-guide |
| Connecticut | Conditional: taxed as computer and data processing services (canned software accessed electronically) | $100,000 and 200 retail sales, both required | https://portal.ct.gov/DRS/Sales-Tax/Services-Subject-to-Sales-and-Use-Taxes | https://portal.ct.gov/drs/businesses/new-business-resource-center/registering-with-drs |
| Delaware | Not verified (no sales tax; a gross receipts tax on sellers, SaaS not addressed) | No state sales tax | https://revenue.delaware.gov/business-tax-forms/doing-business-in-delaware/step-4-gross-receipts-taxes/ (seen in a search result only) | none |
| District of Columbia | Conditional: software taxed as data processing services, SaaS not named | More than $100,000 or more than 200 separate retail sales | https://otr.cfo.dc.gov/page/digital-goods-sales-taxability-chart | https://otr.cfo.dc.gov/page/sales-and-use-tax-faqs |
| Florida | Conditional: a technical assistance advisory says not taxable on its own facts (customised, no tangible medium) | $100,000 of taxable remote sales in the prior year | https://floridarevenue.com/TaxLaw/Documents/16A-014.pdf | https://floridarevenue.com/taxes/taxesfees/Pages/sales_tax.aspx |
| Georgia | Not taxable (electronically delivered software is not tangible property; a letter ruling to one taxpayer) | $100,000 of gross revenue or 200 separate retail sales, current or prior year | https://dor.georgia.gov/document/sut-lr/lr-sut-2016-09/download | https://dor.georgia.gov/document/document-sut-pb/sut-2019-02/download |
| Hawaii | Conditional: the general excise tax applies and prewritten software is taxable property, SaaS not named | Not verified | https://files.hawaii.gov/tax/legal/tir/tir21-06.pdf | https://tax.hawaii.gov/get/ (not read) |
| Idaho | Conditional: remotely accessed software is listed as nontaxable for "some" cases, the wording is vague | $100,000 of sales, current or previous year | https://tax.idaho.gov/taxes/sales-use/guides-for-certain-groups/retailers/collecting/ | https://tax.idaho.gov/taxes/sales-use/guides-for-certain-groups/online-sellers/online-guide/ |
| Illinois | Not taxable (a letter ruling: the SaaS provider is a serviceman and cloud software is not taxed; Chicago's lease tax is separate) | $100,000 of gross receipts (the 200-transaction test ended 2026-01-01) | https://tax.illinois.gov/content/dam/soi/en/web/taxarchive/research/legal/letter-rulings/sales-tax/2024/ST24-0044-GIL.pdf | https://tax.illinois.gov/research/publications/bulletins/fy-2026-12.html |
| Indiana | Not taxable when accessed remotely and nothing is downloaded | $100,000 of gross revenue (the 200-transaction test ended 2024-01-01) | https://www.in.gov/dor/files/sib08.pdf | https://www.in.gov/dor/i-am-a/business-corp/business-faq/remote-seller-faqs/ |
| Iowa | Conditional: taxable, but exempt when used exclusively by a for-profit commercial enterprise | $100,000 of gross revenue from Iowa sales | https://revenue.iowa.gov/taxes/tax-guidance/sales-use-excise-tax/taxation-specified-digital-products-software-and-related-services | https://revenue.iowa.gov/taxes/tax-guidance/sales-use-excise-tax/remote-sellers-marketplace-facilitators |
| Kansas | Conditional: remote access is not taxed, taxable if installed on the purchaser's Kansas server (a 2010 guide) | $100,000 of cumulative gross receipts, current or preceding year | https://www.ksrevenue.gov/pdf/edu71r.pdf | https://www.ksrevenue.gov/taxnotices/notice21-17.pdf |
| Kentucky | Taxable (prewritten software access services, downloaded or remote) | $100,000 of gross receipts; the 200-sale test was removed from 2026-08-01 (a 2018 page gives the $100,000) | https://revenue.ky.gov/News/Publications/Sales%20Tax%20Newsletters/Sales%20Tax%20Facts%20Winter%202025-2026.pdf | https://revenue.ky.gov/Business/Sales-Use-Tax/Documents/KY%20SSUTA%20Recertification%20-%207-29-26.pdf |
| Louisiana | Taxable (prewritten computer software access services, from 2025-01-01) | $100,000 of sales or 200 transactions a year, per an undated page, so the current status of the 200 test is unconfirmed | https://revenue.louisiana.gov/tax-education-and-faqs/faqs/sales-tax-reform/what-are-prewritten-computer-software-access-services/ | https://parishe-file.revenue.louisiana.gov/ |
| Maine | Not verified (the state's pages refused the fetch) | Not verified | https://www.maine.gov/revenue/sites/maine.gov.revenue/files/inline-files/125c326-2025-188%20(NEW)_accessibility%20update.pdf (403) | https://www.maine.gov/revenue/taxes/sales-use-service-provider-tax/guidance-documents/remote-sellers (403) |
| Maryland | Conditional: a taxable digital product unless bought solely for business use in an enterprise computer system | More than $100,000 or 200 separate transactions | https://www.marylandcomptroller.gov/legal-library/btt-29-dec-07-2022.html | https://interactive.marylandtaxes.gov/Business/bFile/Help/SalesAndUse_Help_202.aspx |
| Massachusetts | Not verified (the state's pages refused the fetch; a search summary of them points to taxable, so confirm) | Not verified | https://www.mass.gov/letter-ruling/letter-ruling-12-8-cloud-computing (403) | https://www.mass.gov/guides/sales-tax-for-remote-sellers (403) |
| Michigan | Not verified (the state's pages refused the fetch; a search summary points to not taxable when no code is delivered, so confirm) | Not verified | https://www.michigan.gov/treasury/reference/tech/sales-and-use-tax-treatment-of-software-products (403) | https://www.michigan.gov/taxes/business-taxes/sales-use-tax/information/remote-seller-faq (403) |
| Minnesota | Not taxable (online-hosted software subscriptions) | More than $100,000 or 200 retail sales | https://www.revenue.state.mn.us/guide/computer-software-and-digital-products | https://www.revenue.state.mn.us/sales-tax-faqs-remote-sellers |
| Mississippi | Conditional: exempt if accessed remotely and hosted on a server outside Mississippi | More than $250,000 in any 12 months | https://www.dor.ms.gov/2023-legislation | https://www.dor.ms.gov/business/business-tax-frequently-asked-questions |
| Missouri | Not taxable (a ruling to one taxpayer) | More than $100,000 of gross receipts | https://dor.mo.gov/rulings/show/8250 | https://dor.mo.gov/faq/taxation/business/remote-seller-and-marketplace-facilitator.html |
| Montana | No state sales tax | No state sales tax | https://revenue.mt.gov/taxes/general-sales-tax | https://revenue.mt.gov/taxes/general-sales-tax |
| Nebraska | Not verified (its FAQ is silent on SaaS) | More than $100,000 of gross sales or 200 transactions | https://revenue.nebraska.gov/about/frequently-asked-questions/nebraska-sales-and-use-tax-faqs | https://revenue.nebraska.gov/about/frequently-asked-questions/remote-seller-and-marketplace-facilitator-faqs |
| Nevada | Not verified (its FAQ says electronically delivered software is not taxable and is silent on SaaS) | More than $100,000 of retail sales or 200 separate transactions | https://tax.nv.gov/faqs/sales-tax-faqs/ | https://tax.nv.gov/faqs/marketplace-facilitator-seller-faqs/remote-sellers-wayfair-decision/ |
| New Hampshire | Not verified (no general sales tax expected, page refused the fetch) | Not verified | https://www.revenue.nh.gov/faq/does-new-hampshire-have-sales-tax (403) | same page |
| New Jersey | Conditional: not taxable, except SaaS that is an "information service" (2013 bulletin) | $100,000 in sales or 200 transactions | https://www.nj.gov/treasury/taxation/pdf/pubs/tb/tb72.pdf | https://www.nj.gov/treasury/taxation/remotesellers.shtml |
| New Mexico | Conditional: a gross receipts tax; only a hearing decision treats software access as a taxable licence, no SaaS rule found | $100,000 of taxable gross receipts in the prior calendar year | https://www.tax.newmexico.gov/all-nm-taxes/2020/05/12/pst-services-inc/ | https://www.tax.newmexico.gov/businesses/determining-nexus/ |
| New York | Taxable | More than $500,000 and more than 100 sales, both required | https://www.tax.ny.gov/pubs_and_bulls/tg_bulletins/st/computer_software.htm | https://www.tax.ny.gov/pubs_and_bulls/publications/sales/nexus.htm |
| North Carolina | Not taxable (a letter ruling to one taxpayer; some digital property is taxed) | $100,000 of gross sales | https://www.ncdor.gov/documents/files/suplr-2021-0005-remote-access-computer-software/open | https://ncdor.gov/remote-sales |
| North Dakota | Not verified (its software guideline taxes off-the-shelf software however transferred and does not mention SaaS) | $100,000 of taxable sales | https://www.tax.nd.gov/sites/www/files/documents/guidelines/business/sales-use/gl-21825-1.pdf | https://www.tax.nd.gov/remoteseller |
| Ohio | Not verified (the tax department's pages returned 403 or 404; Revised Code 5739.01 makes automatic data processing and electronic information services taxable when used in business, and does not name SaaS) | Not verified | https://codes.ohio.gov/ohio-revised-code/section-5739.01 | https://tax.ohio.gov/business/ohio-business-taxes/sales-and-use/out-of-state-sellers (404) |
| Oklahoma | Not taxable (a letter ruling: electronically delivered prewritten software is exempt) | $100,000 of taxable sales, preceding or current year | https://oklahoma.gov/content/dam/ok/en/tax/documents/resources/rules-and-policies/letter-rulings/2026/LR-26-012.pdf | https://oklahoma.gov/tax/helpcenter/businesses.html |
| Oregon | No state sales tax (a separate Corporate Activity Tax exists) | No state sales tax | https://www.oregon.gov/dor/programs/businesses/pages/sales-tax.aspx | same page |
| Pennsylvania | Taxable (canned software accessed by a user in the state) | More than $100,000 of gross sales in a calendar year | https://www.pa.gov/agencies/revenue/resources/tax-types-and-information/sales-use-and-hotel-occupancy-tax/canned-computer-software-digital-goods | https://www.pa.gov/agencies/revenue/resources/tax-types-and-information/sales-use-and-hotel-occupancy-tax/online-retailers |
| Rhode Island | Not verified (the state's pages refused the fetch) | Not verified | https://tax.ri.gov/guidance/declaratory-rulings/ruling-request-no-2023-02 (403) | https://tax.ri.gov/tax-sections/sales-excise-taxes/remote-sellers (403) |
| South Carolina | Taxable (as a communications service, no business-use carve-out found; a private letter ruling) | $100,000 of gross revenue, current or prior year | https://dor.sc.gov/sales-use-tax-online-software-subscription-service-and-training | https://dor.sc.gov/sites/dor/files/Documents/Policy%20Manuals/Chapter%2013%20-%20Nexus.pdf |
| South Dakota | Not verified (SaaS not named; "products transferred electronically" are taxable) | $100,000 of gross sales, previous or current calendar year | https://dor.sd.gov/media/argitevb/products-transferred-electronically.pdf | https://dor.sd.gov/businesses/taxes/sales-use-tax/ |
| Tennessee | Not verified (the state's pages refused the fetch) | Not verified | https://revenue.support.tn.gov/hc/en-us/articles/360057266092-RAS-1-Remotely-Accessed-Software-Overview (403) | https://revenue.support.tn.gov/hc/en-us/articles/360058087691-MS-5-When-Remote-Sellers-Must-Register-and-Collect-Sales-Tax (403) |
| Texas | Taxable as data processing, with 20% of the charge exempt so 80% is taxed | $500,000 of Texas revenue in the preceding 12 calendar months | https://comptroller.texas.gov/taxes/publications/96-259.php | https://comptroller.texas.gov/taxes/sales/remote-sellers.php |
| Utah | Taxable (remotely accessed prewritten software used in Utah; custom software is not) | More than $100,000 of gross revenue, previous or current year (the 200-transaction test ended 2025-07-01) | https://tax.utah.gov/forms-pubs/pub-64/ (Rev. 05/12) | https://tax.utah.gov/business/sales-tax/other-sales-tax/out-of-state-remote-sellers/ |
| Vermont | Taxable (prewritten software accessed remotely, since 2024-07-01) | $100,000 or 200 transactions, preceding 12 months | https://tax.vermont.gov/business-and-corp/sales-and-use-tax/prewritten-computer-software | https://tax.vermont.gov/business-and-corp/sales-and-use-tax/wayfair/faqs |
| Virginia | Conditional: not taxable when delivered electronically, taxable when sold with tangible property (rulings, SaaS treated as electronic delivery) | $100,000 or 200 retail transactions, prior or current year (page footer dated 2019) | https://www.tax.virginia.gov/laws-rules-decisions/rulings-tax-commissioner/25-98 | https://www.tax.virginia.gov/remote-sellers-marketplace-facilitators-economic-nexus |
| Washington | Taxable (digital automated services and remote access software; the business-purpose exemption does not apply to them) | $100,000 of combined gross receipts into Washington, current or prior year | https://dor.wa.gov/forms-publications/publications-subject/tax-topics/digital-products-including-digital-goods | https://dor.wa.gov/taxes-rates/retail-sales-tax/marketplace-fairness-leveling-playing-field/remote-sellers |
| West Virginia | Not verified (no SaaS page found; a data processing exemption exists and its reach is unclear) | $100,000 or 200 transactions (the page wording is inconsistent) | https://tax.wv.gov/Documents/TSD/tsd300.pdf | https://tax.wv.gov/Documents/TSD/tsd317.pdf |
| Wisconsin | Conditional: not taxable if the provider runs it and nothing is downloaded to the customer's equipment | $100,000 of gross sales, previous or current year (the 200-transaction test ended 2021-02-20) | https://www.revenue.wi.gov/Pages/FAQS/pcs-computerc.aspx | https://www.revenue.wi.gov/Pages/FAQS/ise-remote-sellers.aspx |
| Wyoming | Conditional: not taxable when no tangible property or enumerated service is embedded; prewritten software itself is taxable | $100,000 of gross sales, current or preceding calendar year | the Excise Tax Division's "Computer Sales and Services" file, linked from https://excise-tax-div.wyo.gov/general-administrative/educational-materials | the Division's "Remote Sellers" file, same page |

## 4. Outside the US

### 4.1 Reverse charge in short

When a seller supplies a service to a business in another country, many countries move the VAT duty to the customer. The
seller charges no VAT and the customer accounts for it on its own return ("reverse charge"). The seller keeps a record
that the customer is a business, usually its VAT number.

### 4.2 EU

- A business that sells services to businesses in another EU country "don't usually need to charge" VAT, and the customer
  "will pay VAT on the services received" under the reverse charge
  (https://europa.eu/youreurope/business/taxation/vat/cross-border-vat/index_en.htm, read 2026-11-08). That page is
  written for businesses based in the EU. Verified for the quoted lines.
- For a seller outside the EU, the VAT Directive is explicit. Article 196: "VAT shall be payable by any taxable person to
  whom the services referred to in Article 56 are supplied or by any person identified for VAT purposes in the Member State
  in which the tax is due to whom the services referred to in Articles 44, 47, 50, 53, 54 and 55 are supplied, if the
  services are supplied by a taxable person not established in that Member State". Article 194 lets a Member State make
  the customer liable for other supplies by a supplier not established there
  (https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:32006L0112, read 2026-11-08, Verified for Articles 194
  and 196). Which article covers a given business customer's service was not read, so the Article 44 rule that a business
  customer's Member State is the place of supply is Not verified from the Directive itself.
- A customer with no VAT number is treated as a consumer for electronic services, which "are always taxed in the
  customer's country" (the europa.eu page above, Verified). A seller outside the EU can register once for the non-Union
  One Stop Shop in a Member State of its choice and file quarterly there. That scheme is described by Commission pages
  seen in search results (https://vat-one-stop-shop.ec.europa.eu/one-stop-shop_en), Not verified, and Stripe's
  documentation confirms the registration type exists, and that Stripe files the non-Union scheme through Ireland
  (https://docs.stripe.com/tax/registering and https://docs.stripe.com/tax/file-with-stripe, Verified).

### 4.3 United Kingdom

- HMRC's VAT Notice 741A (last updated 2022-09-29): the general rule for services supplied to a business is that the
  supply is made "where the customer belongs", and the reverse charge applies to "almost all B2B supplies of services
  except exempt supplies". A supplier "may presume that your customer is in business if they provide you with their VAT
  number", and otherwise may accept other evidence (https://www.gov.uk/guidance/vat-place-of-supply-of-services-notice-741a,
  read 2026-11-08, Verified). The notice's rules for supplies to consumers have exceptions for electronically supplied
  services that were not read. Not verified.
- A proposal changes this for suppliers outside the UK. GOV.UK's page "Reform of the business to business reverse charge
  for supplies of services from overseas", published 2026-10-28, says the reverse charge would apply only when the
  purchasing business is registered for UK VAT. Otherwise "the supplier will be required to charge VAT to the customer and
  account for it to HMRC". The page says the measure would take effect from 2027-04-01 and is tied to Budget 2026. It is a
  proposal, not law, as of the date read
  (https://www.gov.uk/government/publications/reverse-charge-on-supplies-received-from-abroad/reform-of-the-business-to-business-reverse-charge-for-supplies-of-services-from-overseas,
  Verified as a proposal). ScreenHire's customers are small teams, many of which will have no VAT number, so this would
  put UK VAT collection, and so a UK VAT registration, on the seller.

## 5. What each option costs for one month

Assumptions. The charge is the plan's list price in USD (`packages/plans/plans.json`: Starter $79, Growth $149, Unlimited
$249), paid by a US card, with tax left out of the base because tax is collected from the customer and handed on. The
percentage fees are applied to the price, though in practice they also apply to the tax a customer pays, which adds a
little. Each fee is the page's own figure from section 2.

| Option | Starter ($79) | Growth ($149) | Unlimited ($249) | What it covers |
|---|---|---|---|---|
| A. Stripe Payments + Billing (2.9% + 30 cents, 0.7%) | $3.14 (3.98%) | $5.66 (3.80%) | $9.26 (3.72%) | Charging and subscriptions. Tax is the seller's: no calculation, registration or filing |
| B. A + Tax Basic (0.5%) | $3.54 (4.48%) | $6.41 (4.30%) | $10.51 (4.22%) | Adds calculation and collection where registered. Registration and filing are still the seller's |
| C. A + Tax Complete | $3.14 plus $90 or more a month fixed | $5.66 plus the same | $9.26 plus the same | Adds registration help and filing, from $90 a month for 2 registrations, 200 transactions and 4 filings a year. No per-transaction fee is listed. Whether Basic's 0.5% also applies was not stated |
| D. Managed Payments (A + 3.5%) | $5.91 (7.48%) | $10.88 (7.30%) | $17.98 (7.22%) | Stripe is merchant of record: tax registration, filing and remittance in covered countries, fraud and disputes |
| E. Paddle (5% + 50 cents) | $4.45 (5.63%) | $7.95 (5.34%) | $12.95 (5.20%) | Merchant of record with tax, fraud and disputes. A second vendor and a migration |
| F. Azure Marketplace (3% store fee) | $2.37 (3.00%) | $4.47 (3.00%) | $7.47 (3.00%) | Customers who buy through Microsoft only. Microsoft collects US sales tax. Payment-processing cost is not stated, so compare with A for that channel only |

Lemon Squeezy lists the same 5% + 50 cents as Paddle and is moving to D, so it has no row of its own.

The break-even between C and D is a fixed fee against a percentage: Managed Payments' extra 3.5% equals the $90 monthly
Tax Complete fee at about $2,570 of monthly revenue, which 33 Starter, 18 Growth or 11 Unlimited subscriptions pass.
The $90 plan covers only 2 registrations. With 3 or 4 the $430 plan applies, and the break-even moves to about $12,290 a
month. Below the break-even, D is cheaper than C and takes
the registrations off the seller. Above it C is cheaper and the seller keeps the registrations, the returns and the bank
funding. This ignores the seller's time and the accountant's fees, which usually make D look better than the arithmetic.

## 6. Recommendation

Stay on Stripe and turn on Stripe Tax, with these corrections to the working answer.

1. Tax Basic alone does not register or file. The task that builds Stripe Tax and tax ids at checkout (#132) should
   turn on automatic tax and tax id collection and read registrations from Stripe. Registration, and filing through Tax
   Complete or by hand, stay owner actions.
2. Turn automatic tax on only for locations the owner has registered in. Stripe applies tax only where a registration is
   added, so adding registrations is the switch, and nothing in the product has to know the list.
3. Keep Managed Payments as the fallback and treat revenue as the trigger. If the owner would rather not register, file
   and fund remittances, or monthly revenue is under the break-even for the Tax Complete plan the registrations need
   (about $2,600 for up to 2, about $12,300 for 3 or 4), Managed Payments costs 3.5% more and removes the work. Moving to
   it is a Checkout setting plus Stripe's eligibility review, not a new vendor, though the customer portal and the
   pinned API version are unchecked (section 2.1).
4. Do not move to Paddle or Lemon Squeezy for fees. Paddle is cheaper than Managed Payments by about 2 points but a second
   vendor, a migration and an approval of its own; Lemon Squeezy is converging on Managed Payments.
5. Treat Azure Marketplace as a later channel, not a billing replacement. It costs 3% and moves US sales tax to Microsoft
   for the customers who buy that way, and it needs Entra sign-in, the fulfillment APIs and a landing page.
6. Watch the UK. If the proposed rule of 2027-04-01 becomes law, a UK business with no VAT number is the seller's to
   charge UK VAT to.

The verified numbers do not overturn the working answer. They change it in one place: "Stripe Tax" is a calculation
product for $0.005 on the dollar, and filing is a separate plan the owner has to choose.

## 7. Not verified

- The tier-to-volume pairing of Stripe Billing's monthly plans beyond $620.
- Whether Tax Basic's 0.5% is also charged on top of Tax Complete.
- Stripe's "generally available" wording for Managed Payments (read only in a search result of its post on X) and how an
  applicant-screening product fares in Stripe's eligibility review.
- How Managed Payments treats a business customer's VAT number.
- Whether Stripe's customer portal works under Managed Payments, and which API version Managed Payments needs against
  the provider's pinned `2024-06-20`.
- Whether an Azure Marketplace purchase can count toward a customer's Azure commitment.
- Article 44 of the VAT Directive (the extract tool did not return it), the EU and UK consumer rules for electronic
  services, the One Stop Shop pages, and the New York and Texas nexus pages: seen in search results and not opened.
- Any cell in the state table marked Not verified.

## 8. Sources

Stripe: https://stripe.com/pricing, https://stripe.com/billing/pricing, https://stripe.com/tax,
https://stripe.com/tax/pricing, https://stripe.com/managed-payments, https://docs.stripe.com/tax,
https://docs.stripe.com/tax/registering, https://docs.stripe.com/tax/filing, https://docs.stripe.com/tax/file-with-stripe,
https://docs.stripe.com/tax/checkout/tax-ids, https://docs.stripe.com/payments/managed-payments (and its eligibility,
tax-compliance and how-it-works pages), https://stripe.com/blog/everything-we-announced-at-sessions-2026. Paddle:
https://www.paddle.com/pricing. Lemon Squeezy: https://www.lemonsqueezy.com/pricing,
https://www.lemonsqueezy.com/blog/2026-update. Microsoft: the Partner Center pages named in section 2.2. Tax: the pages named
in sections 3 and 4 and in the table.
