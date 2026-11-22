using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Billing;

namespace ScreenHire.Application.Billing;

/// <summary>
/// Stripe (https://docs.stripe.com/api) over plain HTTP: Checkout in subscription mode for a plan and in payment mode for AI credits,
/// an auto-reload as an invoice paid off-session, the Customer Portal and the signed webhook (ADR-0029, ADR-0032). No Stripe SDK: three calls and one signature do not earn a package, and the API version is pinned
/// here, not by whichever SDK release is installed. Nothing here logs the key or a customer.
/// </summary>
/// <remarks>
/// One long-lived <see cref="HttpClient"/> of its own rather than one from the HTTP client factory, as
/// <c>SlackApiClient</c> does: the service defaults add the standard resilience handler to every factory client, and a
/// retry could create a second Checkout session. The person clicking again is the retry.
/// </remarks>
public sealed class StripeBillingProvider : IBillingProvider, IDisposable
{
    /// <summary>
    /// The Stripe API version this code is written against. The header pins our calls. A webhook payload follows the version of
    /// its endpoint, or the account's default, so the endpoint is created with this version (docs/operations/deploy.md).
    /// </summary>
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
        };
        form.AddRange(
        [
            new("success_url", request.SuccessUrl.ToString()),
            new("cancel_url", request.CancelUrl.ToString()),
            new("client_reference_id", organization),
            new("metadata[organization_id]", organization),
            new("metadata[plan]", request.Plan.Key),
            new("subscription_data[metadata][organization_id]", organization),
            new("subscription_data[metadata][plan]", request.Plan.Key),
        ]);
        if (!string.IsNullOrEmpty(request.CustomerId))
        {
            form.Add(new("customer", request.CustomerId));
        }
        else if (!string.IsNullOrEmpty(request.CustomerEmail))
        {
            form.Add(new("customer_email", request.CustomerEmail));
        }

        AddTax(form, hasCustomer: !string.IsNullOrEmpty(request.CustomerId));
        return await SessionUrlAsync("checkout/sessions", form, cancellationToken);
    }

    // Stripe Tax at Checkout (https://docs.stripe.com/tax/checkout/page, /tax/checkout/tax-ids). Tax needs the customer's
    // location, so the billing address is always collected. A session for a Stripe customer that already exists does not save
    // what the page collected unless told to: customer_update[address] keeps the tax on the address just entered, and
    // customer_update[name] stores the business name that goes with a tax id. A new customer (customer_email) is created by
    // Checkout in subscription mode and keeps both. The prices stay tax-exclusive: that is tax_behavior on the Stripe price,
    // which the owner sets (docs/architecture/billing.md, Owner actions), not something this request can say.
    private void AddTax(List<KeyValuePair<string, string>> form, bool hasCustomer)
    {
        if (!options.Stripe.Tax.Enabled)
        {
            return;
        }

        form.Add(new("automatic_tax[enabled]", "true"));
        form.Add(new("billing_address_collection", "required"));
        if (hasCustomer)
        {
            form.Add(new("customer_update[address]", "auto"));
        }

        if (options.CollectTaxIds)
        {
            form.Add(new("tax_id_collection[enabled]", "true"));
            if (hasCustomer)
            {
                form.Add(new("customer_update[name]", "auto"));
            }
        }
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

    /// <summary>
    /// The customer's invoices (https://docs.stripe.com/api/invoices/list), the newest 24, newest first as Stripe lists them. A draft
    /// is not an invoice the customer has seen, so it is left out. <c>paid</c> is Paid, <c>open</c> and <c>uncollectible</c> are Due
    /// (an uncollectible invoice is still owed; Stripe has only stopped retrying it) and <c>void</c> is Void. The amount is the
    /// invoice's <c>total</c>, tax included, and the link is Stripe's own <c>invoice_pdf</c>.
    /// </summary>
    public async Task<IReadOnlyList<ProviderInvoice>> ListInvoicesAsync(InvoiceListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.CustomerId))
        {
            return [];
        }

        var list = await SendAsync(HttpMethod.Get, $"invoices?customer={Uri.EscapeDataString(request.CustomerId)}&limit=24", null, null, cancellationToken);
        var invoices = new List<ProviderInvoice>();
        if (!list.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return invoices;
        }

        foreach (var invoice in data.EnumerateArray())
        {
            BillingInvoiceStatus? status = Text(invoice, "status") switch
            {
                "paid" => BillingInvoiceStatus.Paid,
                "open" or "uncollectible" => BillingInvoiceStatus.Due,
                "void" => BillingInvoiceStatus.Void,
                _ => null,
            };
            if (status is not { } known || Text(invoice, "id") is not { } id || Text(invoice, "currency") is not { } currency
                || !invoice.TryGetProperty("created", out var created) || created.ValueKind != JsonValueKind.Number || !created.TryGetInt64(out var createdAt)
                || !invoice.TryGetProperty("total", out var total) || total.ValueKind != JsonValueKind.Number || !total.TryGetInt64(out var totalMinor))
            {
                continue;
            }

            invoices.Add(new ProviderInvoice(
                id,
                DateTimeOffset.FromUnixTimeSeconds(createdAt),
                MajorUnits(totalMinor, currency),
                currency.ToUpperInvariant(),
                known,
                Uri.TryCreate(Text(invoice, "invoice_pdf"), UriKind.Absolute, out var pdf) && pdf.Scheme == Uri.UriSchemeHttps ? pdf : null));
        }

        return invoices;
    }

    /// <summary>
    /// The payment method the subscription is charged to. Stripe charges the subscription's <c>default_payment_method</c> first and
    /// the customer's <c>invoice_settings.default_payment_method</c> when it has none
    /// (https://docs.stripe.com/api/subscriptions/update#update_subscription-default_payment_method), so both are read, expanded, in
    /// one call. A card gives its network and last four digits; any other method gives only its type.
    /// </summary>
    public async Task<ProviderPaymentMethod?> GetPaymentMethodAsync(PaymentMethodRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        JsonElement? method = null;
        if (!string.IsNullOrEmpty(request.SubscriptionId))
        {
            var subscription = await SendAsync(
                HttpMethod.Get,
                $"subscriptions/{Uri.EscapeDataString(request.SubscriptionId)}?expand[]=default_payment_method&expand[]=customer.invoice_settings.default_payment_method",
                null, null, cancellationToken);
            method = ObjectAt(subscription, "default_payment_method")
                ?? (ObjectAt(subscription, "customer") is { } customer && ObjectAt(customer, "invoice_settings") is { } settings
                    ? ObjectAt(settings, "default_payment_method")
                    : null);
        }
        else if (!string.IsNullOrEmpty(request.CustomerId))
        {
            var customer = await SendAsync(
                HttpMethod.Get, $"customers/{Uri.EscapeDataString(request.CustomerId)}?expand[]=invoice_settings.default_payment_method", null, null, cancellationToken);
            method = ObjectAt(customer, "invoice_settings") is { } settings ? ObjectAt(settings, "default_payment_method") : null;
        }

        if (method is not { } found || Text(found, "type") is not { } type)
        {
            return null;
        }

        var card = type == "card" ? ObjectAt(found, "card") : null;
        return new ProviderPaymentMethod(type, card is { } c ? Text(c, "brand") : null, card is { } d ? Text(d, "last4") : null);
    }

    // An expanded object at `name`, or null when it is missing, null or still an id.
    private static JsonElement? ObjectAt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    /// <summary>
    /// A credit Checkout in payment mode (https://docs.stripe.com/api/checkout/sessions/create): one line of "AI credits" in US dollars,
    /// priced inline (no Stripe price to keep in step), tax-exclusive like the plans. The card is saved for off-session use
    /// (<c>setup_future_usage</c>), so auto-reload can charge it. A trial organization has no customer yet, so Checkout makes one
    /// (<c>customer_creation=always</c>). The invoice Checkout creates carries the organization but not the purpose, so its
    /// <c>invoice.paid</c> grants nothing: only the session's own events grant a Checkout purchase, and they carry the payment intent
    /// whose card was saved. The purchase id is in the session's metadata and is what the grant is keyed on.
    /// </summary>
    public async Task<Uri> CreateCreditCheckoutAsync(CreditCheckoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organization = request.OrganizationId.ToString();
        var purchase = request.PurchaseId.ToString();
        var form = new List<KeyValuePair<string, string>>
        {
            new("mode", "payment"),
            new("line_items[0][price_data][currency]", "usd"),
            new("line_items[0][price_data][unit_amount]", Cents(request.Amount)),
            new("line_items[0][price_data][product_data][name]", "AI credits"),
            new("line_items[0][price_data][tax_behavior]", "exclusive"),
            new("line_items[0][quantity]", "1"),
            new("payment_intent_data[setup_future_usage]", "off_session"),
            new("payment_intent_data[metadata][organization_id]", organization),
            new("payment_intent_data[metadata][purchase_id]", purchase),
            new("invoice_creation[enabled]", "true"),
            new("invoice_creation[invoice_data][metadata][organization_id]", organization),
            new("success_url", request.SuccessUrl.ToString()),
            new("cancel_url", request.CancelUrl.ToString()),
            new("client_reference_id", organization),
            new("metadata[organization_id]", organization),
            new("metadata[purpose]", CreditPurpose),
            new("metadata[purchase_id]", purchase),
        };
        if (!string.IsNullOrEmpty(request.CustomerId))
        {
            form.Add(new("customer", request.CustomerId));
        }
        else
        {
            form.Add(new("customer_creation", "always"));
            if (!string.IsNullOrEmpty(request.CustomerEmail))
            {
                form.Add(new("customer_email", request.CustomerEmail));
            }
        }

        AddTax(form, hasCustomer: !string.IsNullOrEmpty(request.CustomerId));
        return await SessionUrlAsync("checkout/sessions", form, cancellationToken);
    }

    /// <summary>
    /// An auto-reload as a Stripe invoice paid off-session with the saved card (https://docs.stripe.com/api/invoices/pay), so Stripe Tax
    /// applies under <c>Stripe:Tax:Enabled</c> as it does at Checkout, and the charge is among the customer's invoices. Four calls, each
    /// with an idempotency key from the purchase, so a retry after a timeout is the same invoice and the same charge: a draft that leaves
    /// the customer's other pending items out and never advances on its own (<c>auto_advance=false</c>, so Stripe does not retry a refused
    /// payment), its one line, finalize, and pay. A refusal voids the invoice, so nothing stays open to collect.
    /// </summary>
    public async Task<CreditCharge> ChargeCreditsAsync(CreditChargeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = $"credit-reload-{request.PurchaseId:N}";
        var draft = new List<KeyValuePair<string, string>>
        {
            new("customer", request.CustomerId),
            new("currency", "usd"),
            new("collection_method", "charge_automatically"),
            new("auto_advance", "false"),
            new("pending_invoice_items_behavior", "exclude"),
            new("description", "AI credits, auto-reload"),
            new("metadata[organization_id]", request.OrganizationId.ToString()),
            new("metadata[purpose]", CreditPurpose),
            new("metadata[purchase_id]", request.PurchaseId.ToString()),
        };
        if (options.Stripe.Tax.Enabled)
        {
            draft.Add(new("automatic_tax[enabled]", "true"));
        }

        var invoice = await SendAsync(HttpMethod.Post, "invoices", draft, $"{key}-invoice", cancellationToken);
        var invoiceId = Text(invoice, "id") ?? throw new BillingProviderException("Stripe answered without an invoice id.");
        await SendAsync(
            HttpMethod.Post,
            "invoiceitems",
            [
                new("customer", request.CustomerId),
                new("invoice", invoiceId),
                new("currency", "usd"),
                new("amount", Cents(request.Amount)),
                new("description", "AI credits"),
                new("tax_behavior", "exclusive"),
            ],
            $"{key}-item",
            cancellationToken);
        await SendAsync(HttpMethod.Post, $"invoices/{Uri.EscapeDataString(invoiceId)}/finalize", [], $"{key}-finalize", cancellationToken);
        try
        {
            var paid = await SendAsync(
                HttpMethod.Post,
                $"invoices/{Uri.EscapeDataString(invoiceId)}/pay",
                [new("payment_method", request.PaymentMethodId), new("off_session", "true")],
                $"{key}-pay",
                cancellationToken);
            return new CreditCharge(Text(paid, "status") == "paid" ? CreditChargeStatus.Succeeded : CreditChargeStatus.Pending, invoiceId);
        }
        catch (BillingProviderException e) when (e.Status == 402)
        {
            // A declined card, or one that needs the owner (3-D Secure): Stripe answers 402 with the reason. Void the invoice so it is not
            // left open, and say so; the caller turns auto-reload off. Never retried here, and Stripe does not retry it either.
            await SendAsync(HttpMethod.Post, $"invoices/{Uri.EscapeDataString(invoiceId)}/void", [], $"{key}-void", cancellationToken);
            return new CreditCharge(CreditChargeStatus.Declined, invoiceId, e.Code ?? "declined");
        }
    }

    /// <summary>The payment method of a Checkout's payment intent, which <c>setup_future_usage</c> saved to the customer.</summary>
    public async Task<string?> SavedPaymentMethodAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentIntentId);
        var intent = await SendAsync(HttpMethod.Get, $"payment_intents/{Uri.EscapeDataString(paymentIntentId)}", null, null, cancellationToken);
        return Text(intent, "payment_method");
    }

    /// <summary>The <c>metadata.purpose</c> that marks a payment as a credit purchase.</summary>
    public const string CreditPurpose = "ai-credits";

    // US dollars to whole cents. The amounts are dollars to the cent, so nothing is lost.
    private static string Cents(decimal dollars) =>
        ((long)Math.Round(dollars * 100m, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Stripe's preview of the invoice the subscription would have with the new price, at <see cref="PlanChangeRequest.ProrationDate"/>
    /// (https://docs.stripe.com/api/invoices/create_preview, https://docs.stripe.com/billing/subscriptions/prorations). The preview
    /// holds the prorations (the credit for the unused time on the old price and the charge for the rest of the period on the
    /// new one, both marked <c>proration</c>) and the first full period at the new price. The prorations are what
    /// <see cref="ChangePlanAsync"/> invoices at once, so they are the amount due now. The full period is the next regular charge.
    /// </summary>
    public async Task<PlanChangePreview> PreviewPlanChangeAsync(PlanChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var price = PriceOf(request.NewPlan);
        var form = new List<KeyValuePair<string, string>>
        {
            new("subscription", request.SubscriptionId),
            new("subscription_details[items][0][id]", await SubscriptionItemIdAsync(request.SubscriptionId, cancellationToken)),
            new("subscription_details[items][0][price]", price),
            new("subscription_details[proration_date]", request.ProrationDate.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        };
        if (!string.IsNullOrEmpty(request.CustomerId))
        {
            form.Add(new("customer", request.CustomerId));
        }

        var invoice = await SendAsync(HttpMethod.Post, "invoices/create_preview", form, null, cancellationToken);
        try
        {
            return ReadPreview(invoice, request.ProrationDate);
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new BillingProviderException("Stripe's preview could not be read.", e);
        }
    }

    /// <summary>
    /// Updates the subscription's one plan item to the new price, invoiced now (<c>always_invoice</c>) with the preview's proration
    /// date. <c>error_if_incomplete</c> makes a payment that fails or needs the owner an error that leaves the subscription as it
    /// was. The idempotency key repeats for the same subscription, price and date, so a second click on Confirm is not a second
    /// charge. The new plan reaches the account through the <c>customer.subscription.updated</c> webhook.
    /// </summary>
    public async Task ChangePlanAsync(PlanChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var price = PriceOf(request.NewPlan);
        var date = request.ProrationDate.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        List<KeyValuePair<string, string>> form =
        [
            new("items[0][id]", await SubscriptionItemIdAsync(request.SubscriptionId, cancellationToken)),
            new("items[0][price]", price),
            new("proration_behavior", "always_invoice"),
            new("proration_date", date),
            new("payment_behavior", "error_if_incomplete"),
            new("metadata[plan]", request.NewPlan.Key),
        ];
        await SendAsync(
            HttpMethod.Post, $"subscriptions/{Uri.EscapeDataString(request.SubscriptionId)}", form,
            $"plan-change-{request.SubscriptionId}-{price}-{date}", cancellationToken);
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
            return ReadCreditPayment(document.RootElement) is { } credit
                ? new BillingWebhook(true, null, credit)
                : new BillingWebhook(true, ReadEvent(document.RootElement));
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
            case "checkout.session.completed" when Text(item, "mode") is "subscription" or null:
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

    /// <summary>
    /// A credit purchase that Stripe confirms as paid, or null for any other event. A Checkout purchase is confirmed by its session:
    /// <c>checkout.session.completed</c> once <c>payment_status</c> is <c>paid</c>, or <c>checkout.session.async_payment_succeeded</c> for a
    /// method that settles later (https://docs.stripe.com/checkout/fulfillment). An auto-reload is confirmed by its invoice's
    /// <c>invoice.paid</c>. Both carry <c>metadata.purpose</c> and the purchase id, which the grant is keyed on. A credit session that is
    /// not paid yet is neither a credit nor a subscription event.
    /// </summary>
    private static CreditPaymentEvent? ReadCreditPayment(JsonElement root)
    {
        if (Text(root, "id") is not { } id || Text(root, "type") is not { } type
            || !root.TryGetProperty("data", out var data) || !data.TryGetProperty("object", out var item)
            || !item.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object
            || Text(metadata, "purpose") != CreditPurpose
            || !Guid.TryParse(Text(metadata, "organization_id"), out var organization) || !Guid.TryParse(Text(metadata, "purchase_id"), out var purchase))
        {
            return null;
        }

        var created = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("created").GetInt64());
        return type switch
        {
            "checkout.session.completed" or "checkout.session.async_payment_succeeded"
                when Text(item, "payment_status") == "paid" && Text(item, "payment_intent") is { } intent =>
                new CreditPaymentEvent(id, created, organization, purchase, intent, intent, Text(item, "customer")),
            "invoice.paid" when Text(item, "id") is { } invoice =>
                new CreditPaymentEvent(id, created, organization, purchase, invoice, null, Text(item, "customer")),
            _ => null,
        };
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
        // created with. So a price no plan lists names no plan (the stored one stands), and the metadata covers only an event
        // that carries no items (the Checkout session).
        if (PlanItem(item) is { } first && first.TryGetProperty("price", out var price) && Text(price, "id") is { } priceId)
        {
            return catalogue.Plans.FirstOrDefault(p => p.StripePriceId == priceId)?.Key;
        }

        return item.TryGetProperty("metadata", out var metadata) && Text(metadata, "plan") is { } key ? catalogue.Find(key)?.Key : null;
    }

    // API version 2025-03-31 moved the period from the subscription to its items. An endpoint created at another version than
    // ApiVersion sends the later shape, and the next payment must not silently disappear because of it.
    private DateTimeOffset? PeriodEnd(JsonElement item)
    {
        if (!item.TryGetProperty("current_period_end", out var end) && PlanItem(item) is { } first)
        {
            first.TryGetProperty("current_period_end", out end);
        }

        return end.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(end.GetInt64()) : null;
    }

    // The item that carries one of the plans' prices, else the first. A subscription made before ADR-0032 also has a metered item
    // for AI use, and Stripe does not promise which one is listed first.
    private JsonElement? PlanItem(JsonElement item)
    {
        if (!item.TryGetProperty("items", out var items) || !items.TryGetProperty("data", out var list) || list.GetArrayLength() == 0)
        {
            return null;
        }

        var planPrices = PlanPrices();
        foreach (var candidate in list.EnumerateArray())
        {
            if (candidate.TryGetProperty("price", out var price) && Text(price, "id") is { } id && planPrices.Contains(id))
            {
                return candidate;
            }
        }

        return list[0];
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private async Task<Uri> SessionUrlAsync(string path, List<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        var session = await SendAsync(HttpMethod.Post, path, form, null, cancellationToken);
        return Uri.TryCreate(Text(session, "url"), UriKind.Absolute, out var url)
            ? url
            : throw new BillingProviderException("Stripe answered without a page to send the owner to.");
    }

    private static string PriceOf(Plan plan) =>
        string.IsNullOrWhiteSpace(plan.StripePriceId)
            ? throw new BillingProviderException($"The {plan.Name} plan has no Stripe price set (ScreenHire:Billing:Plans:{plan.Key}:StripePriceId).")
            : plan.StripePriceId;

    // The item that carries one of our plans' prices, not simply the first, so another item on the subscription (a later add-on)
    // is left alone. Stripe needs its id to replace the price instead of adding a second item.
    private HashSet<string?> PlanPrices() =>
        catalogue.Plans.Select(p => p.StripePriceId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();

    // An invoice line's price id: `price.id` at the pinned version, `pricing.price_details.price` from the later ones. Null when the
    // line names none (a line that is not a priced item), which is then counted as before.
    private static string? PriceOfLine(JsonElement line)
    {
        if (line.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Object && Text(price, "id") is { } id)
        {
            return id;
        }

        return line.TryGetProperty("pricing", out var pricing) && pricing.TryGetProperty("price_details", out var details) ? Text(details, "price") : null;
    }

    private async Task<string> SubscriptionItemIdAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        var subscription = await SendAsync(HttpMethod.Get, $"subscriptions/{Uri.EscapeDataString(subscriptionId)}", null, null, cancellationToken);
        var planPrices = PlanPrices();
        if (subscription.TryGetProperty("items", out var items) && items.TryGetProperty("data", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.TryGetProperty("price", out var price) && Text(price, "id") is { } priceId && planPrices.Contains(priceId)
                    && Text(item, "id") is { } itemId)
                {
                    return itemId;
                }
            }
        }

        throw new BillingProviderException("The Stripe subscription has no item on one of the plans' prices.");
    }

    private PlanChangePreview ReadPreview(JsonElement invoice, DateTimeOffset prorationDate)
    {
        long prorations = 0;
        long regular = 0;
        long? regularStart = null;
        foreach (var line in invoice.GetProperty("lines").GetProperty("data").EnumerateArray())
        {
            // A line on a price that is not a plan's (a metered item a subscription made before ADR-0032 may still carry) is not the
            // plan: it is neither a proration nor the next plan charge, and its period would pull the next payment's date back.
            if (PriceOfLine(line) is { } linePrice && !PlanPrices().Contains(linePrice))
            {
                continue;
            }

            var amount = line.GetProperty("amount").GetInt64();
            if (line.TryGetProperty("proration", out var proration) && proration.ValueKind == JsonValueKind.True)
            {
                // Stripe's advice for isolating the prorations being previewed: the ones whose period starts at the proration date.
                // Any other proration line is an earlier pending item, which a classic-billing-mode subscription (what Checkout
                // creates at the pinned API version) leaves out of the change's invoice
                // (https://docs.stripe.com/billing/subscriptions/pending-updates). Flexible billing mode would invoice it too.
                if (line.GetProperty("period").GetProperty("start").GetInt64() == prorationDate.ToUnixTimeSeconds())
                {
                    prorations += amount;
                }

                continue;
            }

            regular += amount;
            var start = line.GetProperty("period").GetProperty("start").GetInt64();
            regularStart = regularStart is { } earliest ? Math.Min(earliest, start) : start;
        }

        // The first full period at the new price is always on the preview. Without it there is no next payment to show.
        if (regularStart is not { } next)
        {
            throw new BillingProviderException("Stripe's preview had no next payment.");
        }

        var currency = Text(invoice, "currency") ?? throw new BillingProviderException("Stripe's preview had no currency.");
        return new PlanChangePreview(
            Math.Max(0m, MajorUnits(prorations, currency)),
            MajorUnits(regular, currency),
            DateTimeOffset.FromUnixTimeSeconds(next),
            currency.ToUpperInvariant(),
            prorationDate);
    }

    // Stripe amounts are whole minor units, and Stripe's own list says how many a currency has
    // (https://docs.stripe.com/currencies#special-cases). .NET has no table of ISO 4217 minor units, so the exceptions are kept
    // here: these have none, and these five have three. Every other currency has two.
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF",
    };

    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase) { "BHD", "JOD", "KWD", "OMR", "TND" };

    private static decimal MajorUnits(long minor, string currency) =>
        ZeroDecimalCurrencies.Contains(currency) ? minor : ThreeDecimalCurrencies.Contains(currency) ? minor / 1000m : minor / 100m;

    private async Task<JsonElement> SendAsync(
        HttpMethod method, string path, List<KeyValuePair<string, string>>? form, string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.SecretKey);
        request.Headers.Add("Stripe-Version", ApiVersion);
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (!response.IsSuccessStatusCode)
            {
                var code = document.RootElement.TryGetProperty("error", out var error) ? Text(error, "code") ?? Text(error, "type") : null;
                throw new BillingProviderException($"Stripe answered {(int)response.StatusCode} {code ?? "unknown"}.")
                {
                    Status = (int)response.StatusCode,
                    Code = document.RootElement.TryGetProperty("error", out var failure) ? Text(failure, "code") : null,
                };
            }

            return document.RootElement.Clone();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new BillingProviderException($"Stripe could not be reached ({e.GetType().Name}).", e);
        }
    }
}
