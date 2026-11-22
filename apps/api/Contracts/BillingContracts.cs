using ScreenHire.Application.Billing;
using ScreenHire.Domain.Billing;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// An organization's billing for its Owner (docs/architecture/billing.md). <c>available</c> is false when this ScreenHire
/// has no payment provider, and then nothing else is set and nothing is limited. <c>state</c> is where the organization
/// stands: on its free trial, paying, paying late, or ended (everything stays readable and open jobs stay open; only
/// publishing a new job stops). <c>trialEndsAt</c> is set during the trial. <c>nextPaymentAt</c> and
/// <c>nextPaymentAmount</c> are set while a subscription renews, and <c>cancelsAtPeriodEnd</c> says it will not.
/// <c>openJobs</c> counts the jobs open now and <c>activeJobLimit</c> is the plan's limit, null when it has none.
/// <c>canManage</c>: there is a subscription, so the payment portal can open. <c>sandbox</c>: the local fake, not Stripe.
/// <c>taxAtCheckout</c>: Checkout calculates tax on top of the prices, so the page says "plus tax where it applies".
/// <c>aiUsage</c>: this month's use of ScreenHire's AI key and what it drew from the credits (null while billing is off). <c>thisMonth</c>: what the
/// organization pays this month so far, each charge on its line and added up, and the next invoice (null while billing is off).
/// </summary>
public sealed record BillingOverview(
    bool Available,
    bool Sandbox,
    BillingState? State,
    BillingPlan? CurrentPlan,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset? NextPaymentAt,
    decimal? NextPaymentAmount,
    bool CancelsAtPeriodEnd,
    int OpenJobs,
    int? ActiveJobLimit,
    bool CanManage,
    bool TaxAtCheckout,
    IReadOnlyList<BillingPlan> Plans,
    BillingAiUsage? AiUsage,
    BillingThisMonth? ThisMonth);

/// <summary>
/// This UTC month's use of ScreenHire's AI key (docs/architecture/billing.md, "AI credits"). <c>charge</c> is what it drew from the
/// prepaid AI credits in US dollars, the one amount for AI use that its AI use section shows too, and it never names the provider's
/// price or a margin. A call whose cost is unknown (<c>callsWithUnknownCost</c>) is counted and draws nothing. <c>ownKeyCalls</c> are
/// calls on the organization's own key, which never draw on the credits.
/// </summary>
public sealed record BillingAiUsage(string Month, int BuiltInCalls, decimal Charge, int CallsWithUnknownCost, int OwnKeyCalls);

/// <summary>
/// This UTC month so far, added up (docs/architecture/billing.md, "The Billing page"), in <c>currency</c>. <c>planName</c> and
/// <c>planAmount</c> are the plan's line: its monthly price while subscribed, 0 on the free trial, and null with no plan (ended).
/// <c>creditsBought</c> is the AI credits bought this month in US dollars, each purchase a charge of its own. <c>total</c> is the plan
/// plus the credits (when the plan is in US dollars), each to the cent first, so the lines shown add up to it. <c>nextInvoiceAmount</c>
/// and <c>nextInvoiceAt</c> estimate the plan's next invoice while a subscription runs, so the page says "about"; null when no invoice
/// is coming. AI use is prepaid and on no invoice.
/// </summary>
public sealed record BillingThisMonth(
    string Month,
    string Currency,
    string? PlanName,
    decimal? PlanAmount,
    decimal CreditsBought,
    decimal Total,
    decimal? NextInvoiceAmount,
    DateTimeOffset? NextInvoiceAt);

/// <summary>
/// One invoice: <c>date</c> it was made, its <c>amount</c> (tax included) in <c>currency</c>, where it stands, and <c>pdfUrl</c>, the
/// address of its PDF (absolute for the payment provider, a path of this site for the local sandbox), null when there is none.
/// </summary>
public sealed record BillingInvoice(string Id, DateTimeOffset Date, decimal Amount, string Currency, BillingInvoiceStatus Status, string? PdfUrl);

/// <summary>The organization's invoices, newest first. Empty before it has chosen a plan.</summary>
public sealed record BillingInvoiceList(IReadOnlyList<BillingInvoice> Invoices);

/// <summary>
/// The payment method the plan is charged to, or <c>method</c> null when there is none on file yet. A card has its network
/// (<c>brand</c>, such as <c>visa</c>) and its last four digits; another method has only its <c>type</c>.
/// </summary>
public sealed record BillingPaymentMethodView(BillingPaymentMethod? Method);

/// <summary>A payment method on file: its type as the provider names it (<c>card</c> for a card), and for a card its network and last four digits.</summary>
public sealed record BillingPaymentMethod(string Type, string? Brand, string? Last4);

/// <summary>
/// A plan. <c>monthlyPrice</c> is null until the owner sets it. <c>activeJobs</c> is the most jobs open at once, null with
/// no limit set; <c>unlimited</c> says there is none by design. <c>canSubscribe</c>: Checkout can start it here.
/// </summary>
public sealed record BillingPlan(string Key, string Name, decimal? MonthlyPrice, string Currency, int? ActiveJobs, bool Unlimited, bool CanSubscribe);

/// <summary>The plan to subscribe to, by its key from the plan list.</summary>
public sealed record StartBillingCheckoutRequest(string Plan);

/// <summary>The plan to price a switch to, by its key from the plan list.</summary>
public sealed record PreviewBillingPlanChangeRequest(string Plan);

/// <summary>
/// What switching the live subscription to <c>plan</c> costs, in <c>currency</c>. <c>amountDueNow</c> is the prorated difference
/// charged when the owner confirms (0 when the new plan costs less: the credit stays on the account), then
/// <c>nextPaymentAmount</c> a month on <c>nextPaymentAt</c>. <c>prorationDate</c> is the instant both were priced at; send it back
/// with the confirmation so the charge is the one shown.
/// </summary>
public sealed record BillingPlanChangePreview(
    string Plan, decimal AmountDueNow, decimal NextPaymentAmount, DateTimeOffset NextPaymentAt, string Currency, DateTimeOffset ProrationDate);

/// <summary>Confirms the switch to <c>plan</c> priced at <c>prorationDate</c>, as the preview returned it.</summary>
public sealed record ChangeBillingPlanRequest(string Plan, DateTimeOffset ProrationDate);

/// <summary>Where to send the browser next: the payment provider's page, or the sandbox's.</summary>
public sealed record BillingRedirect(string Url);
