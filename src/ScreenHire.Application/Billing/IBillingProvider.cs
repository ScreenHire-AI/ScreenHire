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
}
