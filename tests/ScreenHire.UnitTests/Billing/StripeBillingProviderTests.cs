using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Billing;
using ScreenHire.Domain.Billing;

namespace ScreenHire.UnitTests.Billing;

/// <summary>
/// The Stripe client against a stub handler standing in for Stripe's servers (https://docs.stripe.com/api): what Checkout
/// and the portal post, how a refusal becomes a <see cref="BillingProviderException"/> that never names the key, and how
/// each signed webhook event becomes a common <see cref="BillingProviderEvent"/>. No network.
/// </summary>
public sealed class StripeBillingProviderTests : IDisposable
{
    private const string SecretKey = "sk_test_fictional_key";
    private const string WebhookSecret = "whsec_fictional_secret";
    private const string StarterPrice = "price_starter_fictional";
    private const string GrowthPrice = "price_growth_fictional";

    private static readonly DateTimeOffset Now = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Uri Success = new("https://app.example.org/settings/billing?checkout=success");
    private static readonly Uri Cancelled = new("https://app.example.org/settings/billing?checkout=cancelled");

    private readonly StubStripe stripe = new();
    private readonly List<StripeBillingProvider> providers = [];
    private readonly Guid organization = Guid.CreateVersion7();

    public void Dispose()
    {
        foreach (var provider in providers)
        {
            provider.Dispose();
        }

        stripe.Dispose();
    }

    private static BillingOptions Options(bool prices = true)
    {
        var options = new BillingOptions { SecretKey = SecretKey, WebhookSecret = WebhookSecret };
        if (prices)
        {
            options.Plans["starter"] = new PlanOptions { StripePriceId = StarterPrice, ActiveJobs = 1 };
            options.Plans["growth"] = new PlanOptions { StripePriceId = GrowthPrice, ActiveJobs = 50 };
        }

        return options;
    }

    private StripeBillingProvider Provider(BillingOptions? options = null)
    {
        options ??= Options();
        var provider = new StripeBillingProvider(Microsoft.Extensions.Options.Options.Create(options), new PlanCatalogue(Microsoft.Extensions.Options.Options.Create(options)), stripe);
        providers.Add(provider);
        return provider;
    }

    private static Plan Plan(string key, BillingOptions? options = null) =>
        new PlanCatalogue(Microsoft.Extensions.Options.Options.Create(options ?? Options())).Find(key)!;

    private CheckoutRequest Checkout(Plan plan, string? customerId = null, string? email = null) =>
        new(organization, plan, customerId, email, Success, Cancelled);

    // ---- Checkout and portal ----

    [Fact]
    public async Task Checkout_posts_a_subscription_session_for_the_plans_price_and_names_the_organization()
    {
        stripe.Answer("""{"id":"cs_fictional","url":"https://checkout.stripe.com/c/pay/cs_fictional"}""");

        var url = await Provider().CreateCheckoutAsync(Checkout(Plan("starter")), TestContext.Current.CancellationToken);

        url.Should().Be(new Uri("https://checkout.stripe.com/c/pay/cs_fictional"));
        var call = stripe.Calls.Should().ContainSingle().Subject;
        call.Method.Should().Be(HttpMethod.Post);
        call.Uri.Should().Be(new Uri("https://api.stripe.com/v1/checkout/sessions"));
        call.ContentType.Should().Be("application/x-www-form-urlencoded");
        call.Form["mode"].Should().Be("subscription");
        call.Form["line_items[0][price]"].Should().Be(StarterPrice);
        call.Form["line_items[0][quantity]"].Should().Be("1");
        call.Form["client_reference_id"].Should().Be(organization.ToString());
        call.Form["metadata[organization_id]"].Should().Be(organization.ToString());
        call.Form["metadata[plan]"].Should().Be("starter");
        call.Form["subscription_data[metadata][organization_id]"].Should().Be(organization.ToString());
        call.Form["subscription_data[metadata][plan]"].Should().Be("starter");
        call.Form["success_url"].Should().Be(Success.ToString());
        call.Form["cancel_url"].Should().Be(Cancelled.ToString());
    }

    [Fact]
    public async Task Checkout_authenticates_with_the_secret_key_and_pins_the_api_version()
    {
        stripe.Answer("""{"url":"https://checkout.stripe.com/c/pay/cs_fictional"}""");

        await Provider().CreateCheckoutAsync(Checkout(Plan("growth")), TestContext.Current.CancellationToken);

        var call = stripe.Calls.Single();
        call.Authorization.Should().Be($"Bearer {SecretKey}");
        call.StripeVersion.Should().Be(StripeBillingProvider.ApiVersion).And.NotBeNullOrEmpty();
        call.Form.Values.Should().NotContain(SecretKey, "the key goes in the header only");
    }

    [Fact]
    public async Task Checkout_reuses_the_customer_when_there_is_one_and_otherwise_prefills_the_email()
    {
        stripe.Answer("""{"url":"https://checkout.stripe.com/a"}""");
        stripe.Answer("""{"url":"https://checkout.stripe.com/b"}""");
        stripe.Answer("""{"url":"https://checkout.stripe.com/c"}""");
        var provider = Provider();
        var ct = TestContext.Current.CancellationToken;

        await provider.CreateCheckoutAsync(Checkout(Plan("starter"), customerId: "cus_fictional", email: "owner@example.com"), ct);
        await provider.CreateCheckoutAsync(Checkout(Plan("starter"), email: "owner@example.com"), ct);
        await provider.CreateCheckoutAsync(Checkout(Plan("starter")), ct);

        stripe.Calls[0].Form["customer"].Should().Be("cus_fictional");
        stripe.Calls[0].Form.Should().NotContainKey("customer_email");
        stripe.Calls[1].Form["customer_email"].Should().Be("owner@example.com");
        stripe.Calls[1].Form.Should().NotContainKey("customer");
        stripe.Calls[2].Form.Should().NotContainKey("customer").And.NotContainKey("customer_email");
    }

    private static readonly string[] TaxFields =
    [
        "automatic_tax[enabled]", "billing_address_collection", "tax_id_collection[enabled]", "customer_update[address]", "customer_update[name]",
    ];

    [Fact]
    public async Task Checkout_with_tax_off_sends_no_tax_fields_at_all()
    {
        stripe.Answer("""{"url":"https://checkout.stripe.com/a"}""");
        stripe.Answer("""{"url":"https://checkout.stripe.com/b"}""");
        var provider = Provider();
        var ct = TestContext.Current.CancellationToken;

        await provider.CreateCheckoutAsync(Checkout(Plan("starter"), email: "owner@example.com"), ct);
        await provider.CreateCheckoutAsync(Checkout(Plan("starter"), customerId: "cus_fictional"), ct);

        stripe.Calls.Should().HaveCount(2).And.OnlyContain(call => TaxFields.All(field => !call.Form.ContainsKey(field)));
    }

    [Fact]
    public async Task Checkout_with_tax_on_asks_stripe_to_calculate_tax_and_collects_the_billing_address_and_a_tax_id()
    {
        stripe.Answer("""{"url":"https://checkout.stripe.com/a"}""");
        var options = Options();
        options.Stripe.Tax.Enabled = true;

        await Provider(options).CreateCheckoutAsync(Checkout(Plan("starter", options), email: "owner@example.com"), TestContext.Current.CancellationToken);

        var form = stripe.Calls.Single().Form;
        form["automatic_tax[enabled]"].Should().Be("true");
        form["billing_address_collection"].Should().Be("required");
        form["tax_id_collection[enabled]"].Should().Be("true", "collecting tax ids is on unless switched off");
        form.Should().NotContainKey("customer_update[address]", "Stripe rejects customer_update without a customer")
            .And.NotContainKey("customer_update[name]");
    }

    [Fact]
    public async Task Checkout_with_tax_on_for_an_existing_customer_saves_the_address_and_business_name_it_collects()
    {
        stripe.Answer("""{"url":"https://checkout.stripe.com/a"}""");
        var options = Options();
        options.Stripe.Tax.Enabled = true;

        await Provider(options).CreateCheckoutAsync(Checkout(Plan("starter", options), customerId: "cus_fictional"), TestContext.Current.CancellationToken);

        var form = stripe.Calls.Single().Form;
        form["customer"].Should().Be("cus_fictional");
        form["automatic_tax[enabled]"].Should().Be("true");
        form["customer_update[address]"].Should().Be("auto");
        form["customer_update[name]"].Should().Be("auto");
        form["tax_id_collection[enabled]"].Should().Be("true");
    }

    [Fact]
    public async Task Checkout_with_tax_on_and_tax_ids_off_calculates_tax_but_collects_no_tax_id_and_no_name()
    {
        stripe.Answer("""{"url":"https://checkout.stripe.com/a"}""");
        var options = Options();
        options.Stripe.Tax.Enabled = true;
        options.Stripe.Tax.CollectTaxIds = false;

        await Provider(options).CreateCheckoutAsync(Checkout(Plan("starter", options), customerId: "cus_fictional"), TestContext.Current.CancellationToken);

        var form = stripe.Calls.Single().Form;
        form["automatic_tax[enabled]"].Should().Be("true");
        form["billing_address_collection"].Should().Be("required");
        form["customer_update[address]"].Should().Be("auto");
        form.Should().NotContainKey("tax_id_collection[enabled]").And.NotContainKey("customer_update[name]");
    }

    [Fact]
    public async Task Checkout_with_tax_ids_on_but_tax_off_collects_nothing_because_tax_ids_ride_on_tax()
    {
        stripe.Answer("""{"url":"https://checkout.stripe.com/a"}""");
        var options = Options();
        options.Stripe.Tax.CollectTaxIds = true;

        await Provider(options).CreateCheckoutAsync(Checkout(Plan("starter", options)), TestContext.Current.CancellationToken);

        stripe.Calls.Single().Form.Keys.Should().NotIntersectWith(TaxFields);
    }

    [Fact]
    public void Tax_is_off_by_default_and_tax_ids_follow_tax()
    {
        var options = new BillingOptions();
        options.Stripe.Tax.Enabled.Should().BeFalse("tax needs the owner's Stripe Tax registrations first");
        options.Stripe.Tax.CollectTaxIds.Should().BeTrue();
        options.CollectTaxIds.Should().BeFalse("with tax off nothing is collected");

        options.Stripe.Tax.Enabled = true;
        options.CollectTaxIds.Should().BeTrue();

        options.Stripe.Tax.CollectTaxIds = false;
        options.CollectTaxIds.Should().BeFalse();
    }

    [Fact]
    public async Task Checkout_for_a_plan_with_no_stripe_price_throws_and_calls_nobody()
    {
        var options = Options(prices: false);
        var provider = Provider(options);

        var act = () => provider.CreateCheckoutAsync(Checkout(Plan("starter", options)), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<BillingProviderException>()).Which.Message.Should().Contain("StripePriceId").And.NotContain(SecretKey);
        stripe.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refusal_becomes_an_exception_that_names_the_status_and_code_but_not_the_key_or_the_body()
    {
        stripe.Answer(HttpStatusCode.Unauthorized, $$$"""{"error":{"type":"invalid_request_error","code":"api_key_expired","message":"Invalid API Key provided: {{{SecretKey}}}"}}""");

        var act = () => Provider().CreateCheckoutAsync(Checkout(Plan("starter")), TestContext.Current.CancellationToken);

        var message = (await act.Should().ThrowAsync<BillingProviderException>()).Which.Message;
        message.Should().Contain("401").And.Contain("api_key_expired").And.NotContain(SecretKey).And.NotContain("Invalid API Key");
    }

    [Fact]
    public async Task A_refusal_with_no_json_error_is_still_an_exception()
    {
        stripe.Answer(HttpStatusCode.BadGateway, "{}");

        var act = () => Provider().CreateCheckoutAsync(Checkout(Plan("starter")), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<BillingProviderException>()).Which.Message.Should().Contain("502").And.NotContain(SecretKey);
    }

    [Fact]
    public async Task An_answer_that_is_not_json_or_has_no_page_is_an_exception()
    {
        stripe.Answer(HttpStatusCode.OK, "<html>gateway</html>");
        stripe.Answer(HttpStatusCode.OK, """{"id":"cs_fictional"}""");
        var provider = Provider();
        var ct = TestContext.Current.CancellationToken;

        await FluentActions.Awaiting(() => provider.CreateCheckoutAsync(Checkout(Plan("starter")), ct)).Should().ThrowAsync<BillingProviderException>();
        await FluentActions.Awaiting(() => provider.CreateCheckoutAsync(Checkout(Plan("starter")), ct)).Should().ThrowAsync<BillingProviderException>();
    }

    [Fact]
    public async Task A_network_failure_is_an_exception_that_does_not_carry_the_key()
    {
        stripe.Fail(new HttpRequestException($"connection to api.stripe.com refused for {SecretKey}"));

        var act = () => Provider().CreateCheckoutAsync(Checkout(Plan("starter")), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BillingProviderException>()).Which;
        exception.Message.Should().Contain(nameof(HttpRequestException)).And.NotContain(SecretKey);
    }

    [Fact]
    public async Task The_portal_needs_a_customer()
    {
        var provider = Provider();

        await FluentActions.Awaiting(() => provider.CreatePortalAsync(new PortalRequest(organization, null, Success), TestContext.Current.CancellationToken))
            .Should().ThrowAsync<BillingProviderException>();
        await FluentActions.Awaiting(() => provider.CreatePortalAsync(new PortalRequest(organization, "", Success), TestContext.Current.CancellationToken))
            .Should().ThrowAsync<BillingProviderException>();
        stripe.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task The_portal_posts_the_customer_and_the_return_page()
    {
        stripe.Answer("""{"url":"https://billing.stripe.com/p/session/fictional"}""");

        var url = await Provider().CreatePortalAsync(new PortalRequest(organization, "cus_fictional", Success), TestContext.Current.CancellationToken);

        url.Should().Be(new Uri("https://billing.stripe.com/p/session/fictional"));
        var call = stripe.Calls.Single();
        call.Uri.Should().Be(new Uri("https://api.stripe.com/v1/billing_portal/sessions"));
        call.Form["customer"].Should().Be("cus_fictional");
        call.Form["return_url"].Should().Be(Success.ToString());
        call.Authorization.Should().Be($"Bearer {SecretKey}");
        call.StripeVersion.Should().Be(StripeBillingProvider.ApiVersion);
    }

    // ---- Plan change ----

    private static readonly DateTimeOffset ProrationDate = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly long NextPeriodStart = new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private PlanChangeRequest Change(string from, string to, BillingOptions? options = null) =>
        new(organization, "cus_fictional", "sub_fictional", Plan(from, options), Plan(to, options), null, ProrationDate);

    // The shape of 2024-06-20's subscription: its items, each with the price it carries.
    private static string SubscriptionJson(params (string ItemId, string PriceId)[] items) =>
        new JsonObject
        {
            ["id"] = "sub_fictional",
            ["items"] = new JsonObject
            {
                ["data"] = new JsonArray(items.Select(i => (JsonNode)new JsonObject { ["id"] = i.ItemId, ["price"] = new JsonObject { ["id"] = i.PriceId } }).ToArray()),
            },
        }.ToJsonString();

    private static JsonObject Line(long amount, bool proration, long periodStart) =>
        new() { ["amount"] = amount, ["proration"] = proration, ["period"] = new JsonObject { ["start"] = periodStart, ["end"] = periodStart + 2_592_000 } };

    // An upcoming invoice as the preview returns it: the credit for unused time, the charge for the rest of the period, and the next full period.
    private static string PreviewJson(string currency, params JsonObject[] lines) =>
        new JsonObject { ["currency"] = currency, ["lines"] = new JsonObject { ["data"] = new JsonArray(lines.Select(l => (JsonNode)l).ToArray()) } }.ToJsonString();

    private static string Seconds(DateTimeOffset at) => at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_preview_finds_the_plan_item_among_the_subscriptions_and_asks_for_the_new_price_at_the_proration_date()
    {
        stripe.Answer(SubscriptionJson(("si_addon", "price_addon_fictional"), ("si_plan", StarterPrice)));
        stripe.Answer(PreviewJson("usd", Line(-1000, true, ProrationDate.ToUnixTimeSeconds()), Line(4500, true, ProrationDate.ToUnixTimeSeconds()), Line(14900, false, NextPeriodStart)));

        await Provider().PreviewPlanChangeAsync(Change("starter", "growth"), TestContext.Current.CancellationToken);

        stripe.Calls.Should().HaveCount(2);
        var lookup = stripe.Calls[0];
        lookup.Method.Should().Be(HttpMethod.Get);
        lookup.Uri.Should().Be(new Uri("https://api.stripe.com/v1/subscriptions/sub_fictional"));
        var preview = stripe.Calls[1];
        preview.Method.Should().Be(HttpMethod.Post);
        preview.Uri.Should().Be(new Uri("https://api.stripe.com/v1/invoices/create_preview"));
        preview.Form["subscription"].Should().Be("sub_fictional");
        preview.Form["customer"].Should().Be("cus_fictional");
        preview.Form["subscription_details[items][0][id]"].Should().Be("si_plan", "the add-on item is left alone");
        preview.Form["subscription_details[items][0][price]"].Should().Be(GrowthPrice);
        preview.Form["subscription_details[proration_date]"].Should().Be(Seconds(ProrationDate));
        preview.Authorization.Should().Be($"Bearer {SecretKey}");
        preview.StripeVersion.Should().Be(StripeBillingProvider.ApiVersion);
        stripe.Calls.Should().OnlyContain(c => c.Form.Values.All(v => v != SecretKey));
    }

    [Fact]
    public async Task A_preview_reads_the_prorations_as_due_now_and_the_full_period_as_the_next_payment()
    {
        stripe.Answer(SubscriptionJson(("si_plan", StarterPrice)));
        stripe.Answer(PreviewJson("usd", Line(-1000, true, ProrationDate.ToUnixTimeSeconds()), Line(4500, true, ProrationDate.ToUnixTimeSeconds()), Line(14900, false, NextPeriodStart)));

        var preview = await Provider().PreviewPlanChangeAsync(Change("starter", "growth"), TestContext.Current.CancellationToken);

        preview.AmountDueNow.Should().Be(35.00m);
        preview.NextPaymentAmount.Should().Be(149.00m);
        preview.NextPaymentAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(NextPeriodStart));
        preview.Currency.Should().Be("USD");
        preview.ProrationDate.Should().Be(ProrationDate);
    }

    [Fact]
    public async Task A_downgrade_owes_nothing_now_because_its_credit_stays_on_the_account()
    {
        stripe.Answer(SubscriptionJson(("si_plan", GrowthPrice)));
        stripe.Answer(PreviewJson("usd", Line(-7450, true, ProrationDate.ToUnixTimeSeconds()), Line(2150, true, ProrationDate.ToUnixTimeSeconds()), Line(7900, false, NextPeriodStart)));

        var preview = await Provider().PreviewPlanChangeAsync(Change("growth", "starter"), TestContext.Current.CancellationToken);

        preview.AmountDueNow.Should().Be(0m);
        preview.NextPaymentAmount.Should().Be(79.00m);
    }

    [Fact]
    public async Task A_pending_proration_from_another_moment_is_not_part_of_the_amount_due_now()
    {
        stripe.Answer(SubscriptionJson(("si_plan", StarterPrice)));
        stripe.Answer(PreviewJson(
            "usd",
            Line(-1000, true, ProrationDate.ToUnixTimeSeconds()),
            Line(4500, true, ProrationDate.ToUnixTimeSeconds()),
            Line(9999, true, ProrationDate.ToUnixTimeSeconds() - 86_400),
            Line(14900, false, NextPeriodStart)));

        var preview = await Provider().PreviewPlanChangeAsync(Change("starter", "growth"), TestContext.Current.CancellationToken);

        preview.AmountDueNow.Should().Be(35.00m, "only the prorations of this change, at its proration date, are charged by it");
    }

    [Fact]
    public async Task A_zero_decimal_currency_is_not_divided_by_a_hundred()
    {
        stripe.Answer(SubscriptionJson(("si_plan", StarterPrice)));
        stripe.Answer(PreviewJson("jpy", Line(3500, true, ProrationDate.ToUnixTimeSeconds()), Line(14900, false, NextPeriodStart)));

        var preview = await Provider().PreviewPlanChangeAsync(Change("starter", "growth"), TestContext.Current.CancellationToken);

        preview.AmountDueNow.Should().Be(3500m);
        preview.NextPaymentAmount.Should().Be(14900m);
        preview.Currency.Should().Be("JPY");
    }

    [Fact]
    public async Task A_preview_with_no_full_period_line_is_an_exception()
    {
        stripe.Answer(SubscriptionJson(("si_plan", StarterPrice)));
        stripe.Answer(PreviewJson("usd", Line(3500, true, ProrationDate.ToUnixTimeSeconds())));

        var act = () => Provider().PreviewPlanChangeAsync(Change("starter", "growth"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<BillingProviderException>();
    }

    [Fact]
    public async Task A_subscription_with_no_item_on_a_plan_price_is_an_exception_and_nothing_is_priced()
    {
        stripe.Answer(SubscriptionJson(("si_other", "price_not_ours_fictional")));

        var act = () => Provider().PreviewPlanChangeAsync(Change("starter", "growth"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<BillingProviderException>();
        stripe.Calls.Should().ContainSingle("only the lookup was made");
    }

    [Fact]
    public async Task A_plan_change_to_a_plan_with_no_stripe_price_is_an_exception_and_calls_nobody()
    {
        var options = Options();
        options.Plans["growth"] = new PlanOptions { StripePriceId = "", ActiveJobs = 50 };
        var provider = Provider(options);
        var change = Change("starter", "growth", options);
        var ct = TestContext.Current.CancellationToken;

        (await FluentActions.Awaiting(() => provider.PreviewPlanChangeAsync(change, ct)).Should().ThrowAsync<BillingProviderException>())
            .Which.Message.Should().Contain("StripePriceId");
        await FluentActions.Awaiting(() => provider.ChangePlanAsync(change, ct)).Should().ThrowAsync<BillingProviderException>();
        stripe.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_plan_change_replaces_the_price_invoiced_at_once_with_the_previews_date_and_fails_on_an_unpaid_one()
    {
        stripe.Answer(SubscriptionJson(("si_plan", StarterPrice)));
        stripe.Answer("""{"id":"sub_fictional"}""");

        await Provider().ChangePlanAsync(Change("starter", "growth"), TestContext.Current.CancellationToken);

        var update = stripe.Calls[1];
        update.Method.Should().Be(HttpMethod.Post);
        update.Uri.Should().Be(new Uri("https://api.stripe.com/v1/subscriptions/sub_fictional"));
        update.Form["items[0][id]"].Should().Be("si_plan");
        update.Form["items[0][price]"].Should().Be(GrowthPrice);
        update.Form["proration_behavior"].Should().Be("always_invoice");
        update.Form["proration_date"].Should().Be(Seconds(ProrationDate));
        update.Form["payment_behavior"].Should().Be("error_if_incomplete");
        update.Form["metadata[plan]"].Should().Be("growth");
        update.IdempotencyKey.Should().Be($"plan-change-sub_fictional-{GrowthPrice}-{Seconds(ProrationDate)}", "a second click on Confirm is the same change");
    }

    [Fact]
    public async Task A_three_decimal_currency_is_divided_by_a_thousand()
    {
        stripe.Answer(SubscriptionJson(("si_plan", StarterPrice)));
        stripe.Answer(PreviewJson("kwd", Line(3500, true, ProrationDate.ToUnixTimeSeconds()), Line(14900, false, NextPeriodStart)));

        var preview = await Provider().PreviewPlanChangeAsync(Change("starter", "growth"), TestContext.Current.CancellationToken);

        preview.AmountDueNow.Should().Be(3.5m);
        preview.NextPaymentAmount.Should().Be(14.9m);
    }

    [Fact]
    public async Task A_declined_plan_change_is_an_exception_that_names_no_key()
    {
        stripe.Answer(SubscriptionJson(("si_plan", StarterPrice)));
        stripe.Answer(HttpStatusCode.PaymentRequired, $$$"""{"error":{"type":"card_error","code":"card_declined","message":"Your card was declined. {{{SecretKey}}}"}}""");

        var act = () => Provider().ChangePlanAsync(Change("starter", "growth"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<BillingProviderException>()).Which.Message.Should().Contain("402").And.Contain("card_declined").And.NotContain(SecretKey);
    }

    // ---- Webhooks ----

    private BillingWebhook Read(JsonObject evt, string? secret = WebhookSecret, DateTimeOffset? signedAt = null, BillingOptions? options = null)
    {
        var body = evt.ToJsonString();
        var header = secret is null ? null : StripeSignature.HeaderFor(secret, signedAt ?? Now, body);
        return Provider(options).ReadWebhook(body, header, Now);
    }

    private static JsonObject Event(string id, string type, JsonObject item, long created = 1_772_442_000) => new()
    {
        ["id"] = id,
        ["type"] = type,
        ["created"] = created,
        ["data"] = new JsonObject { ["object"] = item },
    };

    private JsonObject Meta(string? plan = "starter") => new()
    {
        ["organization_id"] = organization.ToString(),
        ["plan"] = plan,
    };

    [Fact]
    public void A_paid_checkout_completing_activates_the_plan_in_its_metadata()
    {
        var webhook = Read(Event("evt_1", "checkout.session.completed", new JsonObject
        {
            ["client_reference_id"] = organization.ToString(),
            ["metadata"] = Meta("starter"),
            ["customer"] = "cus_fictional",
            ["subscription"] = "sub_fictional",
            ["payment_status"] = "paid",
        }));

        webhook.SignatureValid.Should().BeTrue();
        var evt = webhook.Event!;
        evt.EventId.Should().Be("evt_1");
        evt.CreatedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_772_442_000));
        evt.OrganizationId.Should().Be(organization);
        evt.CustomerId.Should().Be("cus_fictional");
        evt.SubscriptionId.Should().Be("sub_fictional");
        evt.PlanKey.Should().Be("starter");
        evt.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Theory]
    [InlineData("paid", SubscriptionStatus.Active)]
    [InlineData("no_payment_required", SubscriptionStatus.Active)]
    [InlineData("unpaid", null)]
    public void A_checkout_activates_only_when_it_is_paid_for(string paymentStatus, SubscriptionStatus? expected)
    {
        var evt = Read(Event("evt_1", "checkout.session.completed", new JsonObject
        {
            ["metadata"] = Meta(),
            ["payment_status"] = paymentStatus,
        })).Event!;

        evt.Status.Should().Be(expected);
    }

    [Fact]
    public void The_organization_comes_from_the_checkouts_client_reference_when_the_metadata_has_none()
    {
        var evt = Read(Event("evt_1", "checkout.session.completed", new JsonObject
        {
            ["client_reference_id"] = organization.ToString(),
            ["payment_status"] = "paid",
        })).Event!;

        evt.OrganizationId.Should().Be(organization);
        evt.PlanKey.Should().BeNull();
    }

    [Fact]
    public void An_event_that_names_no_organization_reads_with_none()
    {
        Read(Event("evt_1", "customer.subscription.updated", new JsonObject { ["id"] = "sub_fictional", ["status"] = "active", ["metadata"] = new JsonObject() }))
            .Event!.OrganizationId.Should().BeNull();
        Read(Event("evt_2", "customer.subscription.updated", new JsonObject { ["id"] = "sub_fictional", ["metadata"] = new JsonObject { ["organization_id"] = "not-a-guid" } }))
            .Event!.OrganizationId.Should().BeNull();
    }

    [Fact]
    public void A_subscription_update_carries_status_period_end_and_the_cancel_flag()
    {
        var periodEnd = 1_775_120_400L;

        var evt = Read(Event("evt_2", "customer.subscription.updated", new JsonObject
        {
            ["id"] = "sub_fictional",
            ["customer"] = "cus_fictional",
            ["status"] = "past_due",
            ["current_period_end"] = periodEnd,
            ["cancel_at_period_end"] = true,
            ["metadata"] = Meta("growth"),
        })).Event!;

        evt.SubscriptionId.Should().Be("sub_fictional");
        evt.CustomerId.Should().Be("cus_fictional");
        evt.Status.Should().Be(SubscriptionStatus.PastDue);
        evt.CurrentPeriodEnd.Should().Be(DateTimeOffset.FromUnixTimeSeconds(periodEnd));
        evt.CancelsAtPeriodEnd.Should().BeTrue();
        evt.PlanKey.Should().Be("growth");
        evt.OrganizationId.Should().Be(organization);
    }

    [Fact]
    public void The_period_end_comes_from_the_first_item_at_an_api_version_that_moved_it_off_the_subscription()
    {
        var periodEnd = 1_775_120_400L;

        var evt = Read(Event("evt_2b", "customer.subscription.updated", new JsonObject
        {
            ["id"] = "sub_fictional",
            ["status"] = "active",
            ["metadata"] = Meta("growth"),
            ["items"] = new JsonObject { ["data"] = new JsonArray(new JsonObject { ["current_period_end"] = periodEnd, ["price"] = new JsonObject { ["id"] = GrowthPrice } }) },
        })).Event!;

        evt.CurrentPeriodEnd.Should().Be(DateTimeOffset.FromUnixTimeSeconds(periodEnd));
    }

    [Fact]
    public void A_subscription_update_without_the_optional_fields_says_nothing_about_them()
    {
        var evt = Read(Event("evt_2", "customer.subscription.updated", new JsonObject
        {
            ["id"] = "sub_fictional",
            ["status"] = "incomplete",
            ["metadata"] = Meta(),
        })).Event!;

        evt.Status.Should().BeNull("incomplete says nothing yet");
        evt.CurrentPeriodEnd.Should().BeNull();
        evt.CancelsAtPeriodEnd.Should().BeNull();
    }

    [Fact]
    public void A_created_subscription_in_its_own_stripe_trial_is_active()
    {
        Read(Event("evt_3", "customer.subscription.created", new JsonObject { ["id"] = "sub_fictional", ["status"] = "trialing", ["metadata"] = Meta() }))
            .Event!.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public void A_deleted_subscription_ends_and_no_longer_cancels_at_period_end()
    {
        var evt = Read(Event("evt_4", "customer.subscription.deleted", new JsonObject
        {
            ["id"] = "sub_fictional",
            ["customer"] = "cus_fictional",
            ["status"] = "canceled",
            ["cancel_at_period_end"] = true,
            ["metadata"] = Meta(),
        })).Event!;

        evt.Status.Should().Be(SubscriptionStatus.Ended);
        evt.CancelsAtPeriodEnd.Should().BeFalse();
        evt.SubscriptionId.Should().Be("sub_fictional");
    }

    [Fact]
    public void The_plan_comes_from_the_price_when_the_metadata_has_none_because_a_portal_switch_changes_only_the_price()
    {
        var evt = Read(Event("evt_5", "customer.subscription.updated", new JsonObject
        {
            ["id"] = "sub_fictional",
            ["status"] = "active",
            ["metadata"] = new JsonObject { ["organization_id"] = organization.ToString() },
            ["items"] = new JsonObject { ["data"] = new JsonArray(new JsonObject { ["price"] = new JsonObject { ["id"] = GrowthPrice } }) },
        })).Event!;

        evt.PlanKey.Should().Be("growth");
    }

    [Fact]
    public void The_price_wins_over_stale_metadata_after_a_portal_switch()
    {
        // The subscription was created on Starter, so its metadata still says starter; the owner switched to Growth in the portal.
        var evt = Read(Event("evt_5b", "customer.subscription.updated", new JsonObject
        {
            ["id"] = "sub_fictional",
            ["status"] = "active",
            ["metadata"] = Meta("starter"),
            ["items"] = new JsonObject { ["data"] = new JsonArray(new JsonObject { ["price"] = new JsonObject { ["id"] = GrowthPrice } }) },
        })).Event!;

        evt.PlanKey.Should().Be("growth");
    }

    [Fact]
    public void A_price_no_plan_sells_names_no_plan_even_when_the_metadata_does_so_the_stored_plan_stands()
    {
        // Created on Growth, so the metadata says growth, then switched in the portal to a price the catalogue no longer lists.
        Read(Event("evt_5c", "customer.subscription.updated", new JsonObject
        {
            ["id"] = "sub_fictional",
            ["status"] = "active",
            ["metadata"] = Meta("growth"),
            ["items"] = new JsonObject { ["data"] = new JsonArray(new JsonObject { ["price"] = new JsonObject { ["id"] = "price_retired" } }) },
        })).Event!.PlanKey.Should().BeNull();
    }

    [Fact]
    public void A_price_no_plan_sells_and_a_plan_key_no_plan_has_read_as_no_plan()
    {
        Read(Event("evt_6", "customer.subscription.updated", new JsonObject
        {
            ["id"] = "sub_fictional",
            ["metadata"] = new JsonObject { ["organization_id"] = organization.ToString() },
            ["items"] = new JsonObject { ["data"] = new JsonArray(new JsonObject { ["price"] = new JsonObject { ["id"] = "price_unknown" } }) },
        })).Event!.PlanKey.Should().BeNull();
        Read(Event("evt_7", "customer.subscription.updated", new JsonObject { ["id"] = "sub_fictional", ["metadata"] = Meta("platinum") }))
            .Event!.PlanKey.Should().BeNull();
        Read(Event("evt_8", "customer.subscription.updated", new JsonObject
        {
            ["id"] = "sub_fictional",
            ["metadata"] = new JsonObject { ["organization_id"] = organization.ToString() },
            ["items"] = new JsonObject { ["data"] = new JsonArray() },
        })).Event!.PlanKey.Should().BeNull();
    }

    [Theory]
    [InlineData("invoice.paid")]
    [InlineData("customer.created")]
    [InlineData("payment_intent.succeeded")]
    public void A_signed_event_of_a_type_the_app_ignores_is_valid_with_no_event(string type)
    {
        var webhook = Read(Event("evt_9", type, new JsonObject { ["id"] = "x" }));

        webhook.SignatureValid.Should().BeTrue("Stripe must get a 200 so it stops retrying");
        webhook.Event.Should().BeNull();
    }

    [Fact]
    public void A_delivery_signed_with_the_wrong_secret_or_none_is_not_valid_and_reads_nothing()
    {
        var evt = Event("evt_10", "customer.subscription.deleted", new JsonObject { ["id"] = "sub_fictional", ["metadata"] = Meta() });

        var wrong = Read(evt, secret: "whsec_someone_else");
        var unsigned = Read(evt, secret: null);

        wrong.SignatureValid.Should().BeFalse();
        wrong.Event.Should().BeNull();
        unsigned.SignatureValid.Should().BeFalse();
        unsigned.Event.Should().BeNull();
    }

    [Fact]
    public void A_replayed_delivery_signed_long_ago_is_not_valid()
    {
        Read(Event("evt_11", "customer.subscription.deleted", new JsonObject { ["id"] = "sub_fictional", ["metadata"] = Meta() }), signedAt: Now.AddHours(-1))
            .SignatureValid.Should().BeFalse();
    }

    [Fact]
    public void A_body_changed_after_signing_is_not_valid()
    {
        var provider = Provider();
        var body = Event("evt_12", "customer.subscription.updated", new JsonObject { ["id"] = "sub_fictional", ["status"] = "active", ["metadata"] = Meta() }).ToJsonString();
        var header = StripeSignature.HeaderFor(WebhookSecret, Now, body);

        provider.ReadWebhook(body.Replace("active", "canceled", StringComparison.Ordinal), header, Now).SignatureValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"id":"evt_13","type":"customer.subscription.updated"}""")]
    [InlineData("""{"id":"evt_13","type":"customer.subscription.updated","created":"yesterday","data":{"object":{}}}""")]
    public void A_signed_body_that_is_not_an_event_the_app_understands_is_valid_with_no_event(string body)
    {
        var webhook = Provider().ReadWebhook(body, StripeSignature.HeaderFor(WebhookSecret, Now, body), Now);

        webhook.SignatureValid.Should().BeTrue();
        webhook.Event.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"id":"evt_14","type":"customer.subscription.updated","created":99999999999999999,"data":{"object":{"id":"sub_fictional","status":"active"}}}""")]
    [InlineData("""{"id":"evt_15","type":"customer.subscription.updated","created":1772442000,"data":{"object":{"id":"sub_fictional","status":"active","current_period_end":99999999999999999}}}""")]
    [InlineData("""{"id":"evt_16","type":"customer.subscription.deleted","created":1772442000,"data":{"object":{"id":"sub_fictional","current_period_end":-99999999999999999}}}""")]
    public void A_signed_event_with_a_time_outside_the_representable_range_is_valid_with_no_event_and_does_not_throw(string body)
    {
        var provider = Provider();
        var header = StripeSignature.HeaderFor(WebhookSecret, Now, body);

        var webhook = provider.ReadWebhook(body, header, Now);

        webhook.SignatureValid.Should().BeTrue();
        webhook.Event.Should().BeNull();
    }

    [Theory]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("trialing", SubscriptionStatus.Active)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("canceled", SubscriptionStatus.Ended)]
    [InlineData("unpaid", SubscriptionStatus.Ended)]
    [InlineData("incomplete_expired", SubscriptionStatus.Ended)]
    [InlineData("incomplete", null)]
    [InlineData("paused", null)]
    [InlineData("something_new", null)]
    [InlineData(null, null)]
    public void Stripes_subscription_status_maps_to_ours(string? stripeStatus, SubscriptionStatus? expected)
    {
        StripeBillingProvider.StatusOf(stripeStatus).Should().Be(expected);
    }

    // ---- The stand-in for Stripe ----

    private sealed record Call(HttpMethod Method, Uri Uri, string? Authorization, string? StripeVersion, string? ContentType, Dictionary<string, string> Form, string? IdempotencyKey);

    private sealed class StubStripe : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> answers = new();

        public List<Call> Calls { get; } = [];

        public void Answer(string json) => Answer(HttpStatusCode.OK, json);

        public void Answer(HttpStatusCode status, string body) =>
            answers.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body) });

        public void Fail(Exception exception) => answers.Enqueue(() => throw exception);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = request.Content is null
                ? []
                : QueryHelpers.ParseQuery(await request.Content.ReadAsStringAsync(cancellationToken)).ToDictionary(p => p.Key, p => p.Value.ToString());
            Calls.Add(new Call(
                request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Stripe-Version", out var version) ? version.Single() : null,
                request.Content?.Headers.ContentType?.MediaType, form,
                request.Headers.TryGetValues("Idempotency-Key", out var key) ? key.Single() : null));
            return answers.Dequeue()();
        }
    }
}
