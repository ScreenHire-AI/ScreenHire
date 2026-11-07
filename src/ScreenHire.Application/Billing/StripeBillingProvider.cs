using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Billing;

namespace ScreenHire.Application.Billing;

/// <summary>
/// Stripe (https://docs.stripe.com/api) over plain HTTP: Checkout in subscription mode, the Customer Portal and the signed
/// webhook (ADR-0029). No Stripe SDK: three calls and one signature do not earn a package, and the API version is pinned
/// here, not by whichever SDK release is installed. Nothing here logs the key or a customer.
/// </summary>
/// <remarks>
/// One long-lived <see cref="HttpClient"/> of its own rather than one from the HTTP client factory, as
/// <c>SlackApiClient</c> does: the service defaults add the standard resilience handler to every factory client, and a
/// retry could create a second Checkout session. The person clicking again is the retry.
/// </remarks>
public sealed class StripeBillingProvider : IBillingProvider, IDisposable
{
    /// <summary>The Stripe API version every call and every webhook payload this code reads is written against.</summary>
    public const string ApiVersion = "2024-06-20";

    private readonly BillingOptions options;
    private readonly PlanCatalogue catalogue;
    private readonly HttpClient client;

    public StripeBillingProvider(IOptions<BillingOptions> options, PlanCatalogue catalogue)
        : this(options, catalogue, new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
    }

    /// <summary>With <paramref name="handler"/> in place of the network, for tests.</summary>
    public StripeBillingProvider(IOptions<BillingOptions> options, PlanCatalogue catalogue, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        this.catalogue = catalogue;
        client = new HttpClient(handler) { BaseAddress = this.options.ApiBaseUrl, Timeout = this.options.Timeout };
    }

    public async Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Plan.StripePriceId))
        {
            throw new BillingProviderException($"The {request.Plan.Name} plan has no Stripe price set (ScreenHire:Billing:Plans:{request.Plan.Key}:StripePriceId).");
        }

        var organization = request.OrganizationId.ToString();
        var form = new List<KeyValuePair<string, string>>
        {
            new("mode", "subscription"),
            new("line_items[0][price]", request.Plan.StripePriceId),
            new("line_items[0][quantity]", "1"),
            new("success_url", request.SuccessUrl.ToString()),
            new("cancel_url", request.CancelUrl.ToString()),
            new("client_reference_id", organization),
            new("metadata[organization_id]", organization),
            new("metadata[plan]", request.Plan.Key),
            new("subscription_data[metadata][organization_id]", organization),
            new("subscription_data[metadata][plan]", request.Plan.Key),
        };
        if (!string.IsNullOrEmpty(request.CustomerId))
        {
            form.Add(new("customer", request.CustomerId));
        }
        else if (!string.IsNullOrEmpty(request.CustomerEmail))
        {
            form.Add(new("customer_email", request.CustomerEmail));
        }

        return await SessionUrlAsync("checkout/sessions", form, cancellationToken);
    }

    public async Task<Uri> CreatePortalAsync(PortalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.CustomerId))
        {
            throw new BillingProviderException("There is no Stripe customer to open the portal for yet.");
        }

        return await SessionUrlAsync(
            "billing_portal/sessions",
            [new("customer", request.CustomerId), new("return_url", request.ReturnUrl.ToString())],
            cancellationToken);
    }

    public BillingWebhook ReadWebhook(string body, string? signature, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!StripeSignature.IsValid(options.WebhookSecret, signature, body, now))
        {
            return new BillingWebhook(false, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return new BillingWebhook(true, ReadEvent(document.RootElement));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentOutOfRangeException or OverflowException)
        {
            // Signed by Stripe but not an event this code understands: acknowledge it so Stripe stops retrying.
            return new BillingWebhook(true, null);
        }
    }

    public void Dispose() => client.Dispose();

    /// <summary>The event types this app acts on become a common event; every other type is null.</summary>
    private BillingProviderEvent? ReadEvent(JsonElement root)
    {
        var id = root.GetProperty("id").GetString();
        var type = root.GetProperty("type").GetString();
        var created = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("created").GetInt64());
        if (id is null || !root.TryGetProperty("data", out var data) || !data.TryGetProperty("object", out var item))
        {
            return null;
        }

        switch (type)
        {
            case "checkout.session.completed":
                var paid = Text(item, "payment_status") is "paid" or "no_payment_required";
                return new BillingProviderEvent(
                    id, created, OrganizationOf(item), Text(item, "customer"), Text(item, "subscription"), PlanKeyOf(item),
                    paid ? SubscriptionStatus.Active : null, null, null);
            case "customer.subscription.created" or "customer.subscription.updated":
                return new BillingProviderEvent(
                    id, created, OrganizationOf(item), Text(item, "customer"), Text(item, "id"), PlanKeyOf(item),
                    StatusOf(Text(item, "status")), PeriodEnd(item), item.TryGetProperty("cancel_at_period_end", out var cancels) ? cancels.GetBoolean() : null);
            case "customer.subscription.deleted":
                return new BillingProviderEvent(
                    id, created, OrganizationOf(item), Text(item, "customer"), Text(item, "id"), PlanKeyOf(item),
                    SubscriptionStatus.Ended, PeriodEnd(item), false);
            default:
                return null;
        }
    }

    /// <summary>Stripe's subscription status as ours: null for the ones that say nothing yet (incomplete, paused).</summary>
    public static SubscriptionStatus? StatusOf(string? stripeStatus) => stripeStatus switch
    {
        "active" or "trialing" => SubscriptionStatus.Active,
        "past_due" => SubscriptionStatus.PastDue,
        "canceled" or "unpaid" or "incomplete_expired" => SubscriptionStatus.Ended,
        _ => null,
    };

    private static Guid? OrganizationOf(JsonElement item)
    {
        if (item.TryGetProperty("metadata", out var metadata) && Guid.TryParse(Text(metadata, "organization_id"), out var fromMetadata))
        {
            return fromMetadata;
        }

        return Guid.TryParse(Text(item, "client_reference_id"), out var reference) ? reference : null;
    }

    private string? PlanKeyOf(JsonElement item)
    {
        // The price is the truth: a plan switched in the portal changes the price but keeps the metadata the subscription was
        // created with. The metadata covers an event that carries no items (the Checkout session).
        if (item.TryGetProperty("items", out var items) && items.TryGetProperty("data", out var list) && list.GetArrayLength() > 0
            && list[0].TryGetProperty("price", out var price) && Text(price, "id") is { } priceId
            && catalogue.Plans.FirstOrDefault(p => p.StripePriceId == priceId) is { } byPrice)
        {
            return byPrice.Key;
        }

        return item.TryGetProperty("metadata", out var metadata) && Text(metadata, "plan") is { } key ? catalogue.Find(key)?.Key : null;
    }

    private static DateTimeOffset? PeriodEnd(JsonElement item) =>
        item.TryGetProperty("current_period_end", out var end) && end.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(end.GetInt64())
            : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private async Task<Uri> SessionUrlAsync(string path, List<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.SecretKey);
        request.Headers.Add("Stripe-Version", ApiVersion);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (!response.IsSuccessStatusCode)
            {
                var code = document.RootElement.TryGetProperty("error", out var error) ? Text(error, "code") ?? Text(error, "type") : null;
                throw new BillingProviderException($"Stripe answered {(int)response.StatusCode} {code ?? "unknown"}.");
            }

            return Uri.TryCreate(Text(document.RootElement, "url"), UriKind.Absolute, out var url)
                ? url
                : throw new BillingProviderException("Stripe answered without a page to send the owner to.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new BillingProviderException($"Stripe could not be reached ({e.GetType().Name}).", e);
        }
    }
}
