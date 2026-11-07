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
    IReadOnlyList<BillingPlan> Plans);

/// <summary>
/// A plan. <c>monthlyPrice</c> is null until the owner sets it. <c>activeJobs</c> is the most jobs open at once, null with
/// no limit set; <c>unlimited</c> says there is none by design. <c>canSubscribe</c>: Checkout can start it here.
/// </summary>
public sealed record BillingPlan(string Key, string Name, decimal? MonthlyPrice, string Currency, int? ActiveJobs, bool Unlimited, bool CanSubscribe);

/// <summary>The plan to subscribe to, by its key from the plan list.</summary>
public sealed record StartBillingCheckoutRequest(string Plan);

/// <summary>Where to send the browser next: the payment provider's page, or the sandbox's.</summary>
public sealed record BillingRedirect(string Url);
