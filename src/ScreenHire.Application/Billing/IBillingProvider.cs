using ScreenHire.Domain.Billing;

namespace ScreenHire.Application.Billing;

/// <summary>
/// The payment provider behind billing (ADR-0029, ADR-0032): Stripe, or the local sandbox. It starts a subscription (Checkout),
/// sends the owner to the provider's own page to change or cancel it (the portal), takes payment for AI credits (a credit
/// Checkout, an auto-reload charge), and turns the provider's signed webhook into a <see cref="BillingProviderEvent"/> or a
/// <see cref="CreditPaymentEvent"/>. It decides nothing about what a plan allows or what a balance is.
/// </summary>
public interface IBillingProvider
{
    /// <summary>The provider's page where the owner enters payment details for <see cref="CheckoutRequest.Plan"/>.</summary>
    Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>The provider's page where the owner changes the card, switches plan or cancels.</summary>
    Task<Uri> CreatePortalAsync(PortalRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The customer's invoices, newest first, drafts left out. Empty for an organization with no customer yet. Throws
    /// <see cref="BillingProviderException"/> when the provider refuses or cannot be reached.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoice>> ListInvoicesAsync(InvoiceListRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The payment method the subscription is charged to: the subscription's own default, else the customer's. Null when there is
    /// none or no customer yet. Throws <see cref="BillingProviderException"/> when the provider refuses or cannot be reached.
    /// </summary>
    Task<ProviderPaymentMethod?> GetPaymentMethodAsync(PaymentMethodRequest request, CancellationToken cancellationToken);

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
    /// The provider's page where the owner pays for <see cref="CreditCheckoutRequest.Amount"/> of AI credits, once, saving the card for
    /// auto-reload. Paying there grants nothing by itself: the credits come from the provider's signed confirmation (ADR-0032).
    /// </summary>
    Task<Uri> CreateCreditCheckoutAsync(CreditCheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Charges the saved card for an auto-reload, off-session. A refusal (a declined card, a card that needs the owner to confirm) is
    /// <see cref="CreditChargeStatus.Declined"/>, with nothing left to collect, and never retried by the provider. Throws
    /// <see cref="BillingProviderException"/> only when the provider could not be reached or answered something unexpected, so a
    /// retry with the same purchase is the same charge.
    /// </summary>
    Task<CreditCharge> ChargeCreditsAsync(CreditChargeRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The card a credit Checkout's payment saved for later off-session use, read from the provider's payment. Null when there is none.
    /// Throws <see cref="BillingProviderException"/> when the provider refuses or cannot be reached.
    /// </summary>
    Task<string?> SavedPaymentMethodAsync(string paymentIntentId, CancellationToken cancellationToken);

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

/// <summary>
/// Opens the portal for the owner. <c>Intent</c> says which control opened it: Stripe opens the same portal whatever it is, and the
/// sandbox, whose portal acts at once, cancels only for <see cref="BillingPortalIntent.Cancel"/> and <see cref="BillingPortalIntent.Manage"/>.
/// </summary>
public sealed record PortalRequest(Guid OrganizationId, string? CustomerId, Uri ReturnUrl, BillingPortalIntent Intent = BillingPortalIntent.Manage);

/// <summary>What the owner opened the payment portal to do.</summary>
public enum BillingPortalIntent
{
    /// <summary>Anything the portal offers.</summary>
    Manage,

    /// <summary>Change the card the plan is charged to.</summary>
    ChangeCard,

    /// <summary>Cancel the plan.</summary>
    Cancel,
}

/// <summary>
/// Whose invoices to list. <c>Plan</c>, <c>Status</c> and <c>CurrentPeriodEnd</c> are what the account last heard; the sandbox makes
/// its sample invoices from them, and Stripe knows its own.
/// </summary>
public sealed record InvoiceListRequest(
    Guid OrganizationId, string? CustomerId, Plan? Plan, SubscriptionStatus Status, DateTimeOffset? CurrentPeriodEnd, DateTimeOffset Now);

/// <summary>Whose payment method to read: the customer, and the subscription whose own default comes first.</summary>
public sealed record PaymentMethodRequest(Guid OrganizationId, string? CustomerId, string? SubscriptionId);

/// <summary>Where an invoice stands, in words a customer uses.</summary>
public enum BillingInvoiceStatus
{
    /// <summary>Paid in full.</summary>
    Paid,

    /// <summary>Sent and not paid yet, or a payment failed and it is still owed.</summary>
    Due,

    /// <summary>Cancelled: nothing is owed.</summary>
    Void,
}

/// <summary>One invoice: when it was made, its total in <c>Currency</c>, where it stands, and the address of its PDF when there is one.</summary>
public sealed record ProviderInvoice(string Id, DateTimeOffset CreatedAt, decimal Amount, string Currency, BillingInvoiceStatus Status, Uri? PdfUrl);

/// <summary>
/// A payment method on file: its <c>Type</c> as the provider names it (<c>card</c>, or a bank debit), and for a card its network
/// (<c>Brand</c>, such as <c>visa</c>) and the last four digits. Never the full number.
/// </summary>
public sealed record ProviderPaymentMethod(string Type, string? Brand, string? Last4);

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

/// <summary>
/// A credit Checkout: the purchase the payment is for (its id travels in the provider's metadata and is what the grant is keyed on),
/// the US dollars of credit, the provider's customer when there is one, and where the browser goes after.
/// </summary>
public sealed record CreditCheckoutRequest(
    Guid OrganizationId, Guid PurchaseId, decimal Amount, string? CustomerId, string? CustomerEmail, Uri SuccessUrl, Uri CancelUrl);

/// <summary>An auto-reload charge: the purchase, the US dollars of credit, and the customer and saved card to charge.</summary>
public sealed record CreditChargeRequest(Guid OrganizationId, Guid PurchaseId, decimal Amount, string CustomerId, string PaymentMethodId);

/// <summary>How an auto-reload charge went.</summary>
public enum CreditChargeStatus
{
    /// <summary>The provider took the payment. Stripe still confirms it by webhook, which grants; the sandbox grants at once.</summary>
    Succeeded,

    /// <summary>The provider took the request and has not settled it yet (a bank debit): its webhook grants or nothing does.</summary>
    Pending,

    /// <summary>Refused: a declined card or one that needs the owner. Nothing is left open to collect.</summary>
    Declined,
}

/// <summary>An auto-reload charge's outcome, the provider's id for the payment, and its refusal code when declined.</summary>
public sealed record CreditCharge(CreditChargeStatus Status, string? PaymentId, string? FailureCode = null);

/// <summary>
/// The provider confirming that a credit purchase was paid: the event's id (recorded so a redelivery does nothing), the organization
/// and purchase from the metadata the purchase put on the payment, the provider's id for the payment, the payment intent whose card
/// a Checkout saved, and the customer the payment was made by.
/// </summary>
public sealed record CreditPaymentEvent(
    string EventId, DateTimeOffset CreatedAt, Guid OrganizationId, Guid PurchaseId, string ProviderPaymentId, string? PaymentIntentId, string? CustomerId);

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

/// <summary>
/// A webhook delivery. <c>SignatureValid</c> false means it is not signed by the provider: answer 401 and read nothing. A delivery is
/// a subscription <c>Event</c>, a <c>Credit</c> payment, or neither (a type this app ignores).
/// </summary>
public readonly record struct BillingWebhook(bool SignatureValid, BillingProviderEvent? Event, CreditPaymentEvent? Credit = null);

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
