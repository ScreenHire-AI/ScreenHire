using ScreenHire.Domain.Billing;

namespace ScreenHire.Application.Billing;

/// <summary>
/// The payment provider behind billing (ADR-0029): Stripe, or the local sandbox. It starts a subscription (Checkout),
/// sends the owner to the provider's own page to change or cancel it (the portal), and turns the provider's signed
/// webhook into a <see cref="BillingProviderEvent"/>. It decides nothing about what a plan allows.
/// </summary>
public interface IBillingProvider
{
    /// <summary>The provider's page where the owner enters payment details for <see cref="CheckoutRequest.Plan"/>.</summary>
    Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>The provider's page where the owner changes the card, switches plan or cancels.</summary>
    Task<Uri> CreatePortalAsync(PortalRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// What switching the subscription to <see cref="PlanChangeRequest.NewPlan"/> would cost, priced as of
    /// <see cref="PlanChangeRequest.ProrationDate"/>. Changes nothing.
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(PlanChangeRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Switches the subscription to <see cref="PlanChangeRequest.NewPlan"/>, invoiced at once and prorated as of
    /// <see cref="PlanChangeRequest.ProrationDate"/>, so the charge is the one previewed with the same date. A payment that
    /// needs the owner (a declined card, a 3-D Secure step) fails the call and leaves the subscription as it was.
    /// </summary>
    Task ChangePlanAsync(PlanChangeRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reports AI use on ScreenHire's key to the provider's meter, so the subscription's next invoice charges it. Events are sent one at a
    /// time in order, and the first failure stops the batch: the result lists the ids the provider took (and so will bill) and the
    /// failure, if any, so the caller records what was sent before it retries the rest. An id the provider has already seen counts as
    /// taken. Never throws for a refusal or an unreachable provider; that is in the result.
    /// </summary>
    Task<AiUsageReportResult> ReportAiUsageAsync(AiUsageReport request, CancellationToken cancellationToken);

    /// <summary>
    /// Checks a webhook's signature and reads the event. <see cref="BillingWebhook.Event"/> is null for a valid delivery
    /// this app does not act on (an event type it ignores), so the provider stops retrying it.
    /// </summary>
    BillingWebhook ReadWebhook(string body, string? signature, DateTimeOffset now);
}

/// <summary>
/// What Checkout needs: the plan to subscribe to (with its price id when the provider needs one), the provider's customer
/// when the organization already has one so a second Checkout reuses it, and where the provider sends the browser after.
/// </summary>
public sealed record CheckoutRequest(Guid OrganizationId, Plan Plan, string? CustomerId, string? CustomerEmail, Uri SuccessUrl, Uri CancelUrl);

public sealed record PortalRequest(Guid OrganizationId, string? CustomerId, Uri ReturnUrl);

/// <summary>
/// A switch from <c>CurrentPlan</c> to <c>NewPlan</c> on a live subscription. <c>CurrentPeriodEnd</c> is where the account
/// last saw the period end (the sandbox prorates against it; Stripe knows its own). <c>ProrationDate</c> fixes the instant
/// the proration is worked out at, so a preview and the change that follows it agree to the cent.
/// </summary>
public sealed record PlanChangeRequest(
    Guid OrganizationId, string? CustomerId, string SubscriptionId, Plan CurrentPlan, Plan NewPlan, DateTimeOffset? CurrentPeriodEnd, DateTimeOffset ProrationDate);

/// <summary>
/// The cost of a plan change in the plan's currency: <c>AmountDueNow</c> is the prorated difference charged when the owner
/// confirms (never negative: a credit goes to the customer's balance and shows as nothing due), <c>NextPaymentAmount</c> is the
/// regular charge on <c>NextPaymentAt</c>, and <c>ProrationDate</c> is the instant both were priced at.
/// </summary>
public sealed record PlanChangePreview(decimal AmountDueNow, decimal NextPaymentAmount, DateTimeOffset NextPaymentAt, string Currency, DateTimeOffset ProrationDate);

/// <summary>One AI call to meter: the ledger call's id (the event's identifier at the provider) and its charge in micro-dollars (positive).</summary>
public sealed record AiUsageEvent(Guid Id, long Micros);

/// <summary>
/// Usage to meter for one organization's provider customer, stamped <c>At</c> (the moment of the report, from the host's clock).
/// <c>CustomerId</c> is null in the sandbox, which has no customers.
/// </summary>
public sealed record AiUsageReport(Guid OrganizationId, string? CustomerId, IReadOnlyList<AiUsageEvent> Events, DateTimeOffset At);

/// <summary>The ids the provider took, in order, and what stopped the rest (null when every event went through).</summary>
public sealed record AiUsageReportResult(IReadOnlyList<Guid> Accepted, BillingProviderException? Failure);

/// <summary>
/// What a provider told us, in its own terms made common. A null field says nothing about that fact and leaves it as it is.
/// <c>EventId</c> makes a redelivery a no-op, <c>CreatedAt</c> keeps an older event from undoing a newer one, and
/// <c>OrganizationId</c> comes from the metadata put on the Checkout (null: not ours, ignored).
/// </summary>
public sealed record BillingProviderEvent(
    string EventId,
    DateTimeOffset CreatedAt,
    Guid? OrganizationId,
    string? CustomerId,
    string? SubscriptionId,
    string? PlanKey,
    SubscriptionStatus? Status,
    DateTimeOffset? CurrentPeriodEnd,
    bool? CancelsAtPeriodEnd);

/// <summary>A webhook delivery. <c>SignatureValid</c> false means it is not signed by the provider: answer 401 and read nothing.</summary>
public readonly record struct BillingWebhook(bool SignatureValid, BillingProviderEvent? Event);

/// <summary>The provider refused a call or could not be reached. The message names no key and no customer.</summary>
public sealed class BillingProviderException : Exception
{
    public BillingProviderException()
        : this("The payment provider could not be reached.")
    {
    }

    public BillingProviderException(string message)
        : base(message)
    {
    }

    public BillingProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The provider's HTTP status when it answered, else null.</summary>
    public int? Status { get; init; }

    /// <summary>The provider's error code when it gave one (for example <c>duplicate_meter_event</c>), else null.</summary>
    public string? Code { get; init; }
}
