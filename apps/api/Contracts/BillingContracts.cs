using ScreenHire.Domain.Billing;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// An organization's billing for its Owner (docs/architecture/billing.md). <c>available</c> is false when this ScreenHire
/// has no payment provider, and then nothing else is set and nothing is limited. <c>state</c> is where the organization
/// stands: on its free trial, paying, paying late, or ended (everything stays readable and open jobs stay open; only
/// publishing a new job stops). <c>trialEndsAt</c> is set during the trial. <c>nextPaymentAt</c> and
/// <c>nextPaymentAmount</c> are set while a subscription renews, and <c>cancelsAtPeriodEnd</c> says it will not.
/// <c>openJobs</c> counts the jobs open now and <c>activeJobLimit</c> is the plan's limit, null when it has none.
/// <c>canManage</c>: the provider has a customer, so the payment portal can open. <c>sandbox</c>: the local fake, not Stripe.
/// <c>taxAtCheckout</c>: Checkout calculates tax on top of the prices, so the page says "plus tax where it applies".
/// <c>aiUsage</c>: this month's use of ScreenHire's AI key and what it costs (null while billing is off).
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
    BillingAiUsage? AiUsage);

/// <summary>
/// This UTC month's use of ScreenHire's AI key, in US dollars (docs/architecture/billing.md, "AI usage"). <c>providerCost</c> is what
/// the provider charged ScreenHire for the calls and <c>charge</c> is what the organization pays: each call's cost plus
/// <c>marginPercent</c> (20 is "plus 20%"), rounded to a micro-dollar per call. A call whose cost is unknown
/// (<c>callsWithUnknownCost</c>) is counted and charges nothing. <c>ownKeyCalls</c> are calls on the organization's own key, never
/// charged. <c>trialAllowance</c> is what the free trial includes and <c>trialAllowanceUsed</c> what it has already covered.
/// <c>metered</c>: the payment provider can bill the use (the sandbox always can; Stripe once the metered price is set).
/// </summary>
public sealed record BillingAiUsage(
    string Month,
    int BuiltInCalls,
    long Tokens,
    decimal ProviderCost,
    decimal Charge,
    decimal MarginPercent,
    int CallsWithUnknownCost,
    int OwnKeyCalls,
    decimal TrialAllowance,
    decimal TrialAllowanceUsed,
    bool Metered);

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
