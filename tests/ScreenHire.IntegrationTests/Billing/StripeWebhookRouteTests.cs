using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Billing;
using ScreenHire.Domain.Billing;

namespace ScreenHire.IntegrationTests.Billing;

/// <summary>
/// An AppHost of its own whose API and worker are in Stripe mode (fictional keys), so Stripe's webhook route is mapped and the worker
/// grants as it does in production. Stripe wins over the sandbox switch the shared stack turns on (<c>BillingOptions.Mode</c>). Stripe's
/// address is a loopback port nothing listens on, so a call that reaches it fails at once, and no test calls the real Stripe.
/// </summary>
public sealed class StripeModeFixture : AppHostFixture
{
    public const string WebhookSecret = "whsec_fictional_route_tests";

    protected override void Configure(IDistributedApplicationTestingBuilder builder)
    {
        // Both hosts get both keys and the address, so both work out the same mode (BillingOptions.Mode), as the AppHost does for Stripe.
        foreach (var name in new[] { "api", "worker" })
        {
            var host = builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == name);
            builder.CreateResourceBuilder(host).WithEnvironment(context =>
            {
                context.EnvironmentVariables["ScreenHire__Billing__SecretKey"] = "sk_test_fictional_route_tests";
                context.EnvironmentVariables["ScreenHire__Billing__WebhookSecret"] = WebhookSecret;
                context.EnvironmentVariables["ScreenHire__Billing__ApiBaseUrl"] = "http://127.0.0.1:9/v1/";
            });
        }
    }
}

/// <summary>
/// A signed delivery through <c>POST /v1/webhooks/stripe</c> (docs/architecture/billing.md, "Webhooks"): the route's
/// signature check, <c>BillingEventApplier</c> under row-level security and the outbox save, end to end. The unit tests
/// cover the reading of an event and <see cref="BillingWebhookTests"/> covers what the service does with one.
/// </summary>
[Collection<OwnStackRun>]
public sealed class StripeWebhookRouteTests(StripeModeFixture fixture) : IClassFixture<StripeModeFixture>
{
    private const string Path = "/v1/webhooks/stripe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_signed_subscription_update_activates_the_organization_it_names()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await owner.BillingAsync()).Text("state").Should().Be("Trialing");

        var status = await DeliverAsync(SubscriptionUpdated("evt_route_active", owner.OrganizationId, "starter"));

        status.Should().Be(HttpStatusCode.OK);
        var billing = await owner.BillingAsync();
        billing.Text("state").Should().Be("Active");
        billing.Text("currentPlan.key").Should().Be("starter");
    }

    [Fact]
    public async Task A_wrong_signature_is_a_401_and_changes_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var body = SubscriptionUpdated("evt_route_forged", owner.OrganizationId, "starter");

        var status = await DeliverAsync(body, StripeSignature.HeaderFor("whsec_some_other_secret", DateTimeOffset.UtcNow, body));

        status.Should().Be(HttpStatusCode.Unauthorized);
        (await owner.BillingAsync()).Text("state").Should().Be("Trialing");
    }

    [Fact]
    public async Task A_delivery_with_no_signature_header_is_a_401_and_changes_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var status = await DeliverAsync(SubscriptionUpdated("evt_route_unsigned", owner.OrganizationId, "starter"), header: null);

        status.Should().Be(HttpStatusCode.Unauthorized);
        (await owner.BillingAsync()).Text("state").Should().Be("Trialing");
    }

    [Fact]
    public async Task A_signed_redelivery_of_the_same_event_id_is_a_200_and_changes_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await DeliverAsync(SubscriptionUpdated("evt_route_repeat", owner.OrganizationId, "starter"))).Should().Be(HttpStatusCode.OK);

        // Same id, but the body now says growth: only a repeat that is skipped leaves the plan on starter.
        var status = await DeliverAsync(SubscriptionUpdated("evt_route_repeat", owner.OrganizationId, "growth"));

        status.Should().Be(HttpStatusCode.OK);
        var billing = await owner.BillingAsync();
        billing.Text("state").Should().Be("Active");
        billing.Text("currentPlan.key").Should().Be("starter");
    }

    [Fact]
    public async Task A_signed_event_for_an_organization_that_does_not_exist_is_a_200()
    {
        var status = await DeliverAsync(SubscriptionUpdated("evt_route_unknown", Guid.NewGuid(), "starter"));

        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_signed_event_for_one_organization_leaves_another_organizations_billing_untouched()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();

        var status = await DeliverAsync(SubscriptionUpdated("evt_route_first_only", first.OrganizationId, "starter"));

        status.Should().Be(HttpStatusCode.OK);
        (await first.BillingAsync()).Text("state").Should().Be("Active");
        (await second.BillingAsync()).Text("state").Should().Be("Trialing");
    }

    // ---- Credit purchases (ADR-0032): the signed event is what adds the credit ----

    [Fact]
    public async Task A_signed_paid_credit_session_adds_the_credit_once_however_often_or_by_which_event_it_is_delivered()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var starting = (await owner.CreditsAsync()).At("balance").GetDecimal();
        var purchase = await SeedPurchaseAsync(owner.OrganizationId, CreditPurchaseSource.Checkout, 25m, null);
        var completed = CreditEvent("evt_credit_completed", "checkout.session.completed", CreditSession(owner.OrganizationId, purchase.Id));

        (await DeliverAsync(completed)).Should().Be(HttpStatusCode.OK);

        await WaitForBalanceAsync(owner, starting + 25m);
        // The same event again, the later event for a method that settles slowly, and the invoice Checkout made, which carries no purpose.
        (await DeliverAsync(completed)).Should().Be(HttpStatusCode.OK);
        (await DeliverAsync(CreditEvent("evt_credit_async", "checkout.session.async_payment_succeeded", CreditSession(owner.OrganizationId, purchase.Id)))).Should().Be(HttpStatusCode.OK);
        (await DeliverAsync(CreditEvent("evt_credit_invoice", "invoice.paid", new JsonObject
        {
            ["id"] = "in_fictional_checkout",
            ["metadata"] = new JsonObject { ["organization_id"] = owner.OrganizationId.ToString() },
        }))).Should().Be(HttpStatusCode.OK);
        await Wait.ForNothingAsync(TimeSpan.FromSeconds(2), "a second grant for a purchase that was paid", Ct);

        (await owner.CreditsAsync()).At("balance").GetDecimal().Should().Be(starting + 25m);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        (await db.CreditEntries.AsNoTracking().CountAsync(e => e.PurchaseId == purchase.Id, Ct)).Should().Be(1);
        (await db.CreditPurchases.AsNoTracking().SingleAsync(Ct)).Status.Should().Be(CreditPurchaseStatus.Paid);
        (await db.BillingAccounts.AsNoTracking().SingleAsync(Ct)).ProviderCustomerId.Should().Be("cus_fictional_credit", "the customer Checkout made is kept for the next purchase");
    }

    [Fact]
    public async Task A_credit_session_that_is_not_paid_a_forged_one_and_another_organizations_purchase_add_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var starting = (await owner.CreditsAsync()).At("balance").GetDecimal();
        var purchase = await SeedPurchaseAsync(owner.OrganizationId, CreditPurchaseSource.Checkout, 25m, null);

        var unpaid = CreditEvent("evt_credit_unpaid", "checkout.session.completed", CreditSession(owner.OrganizationId, purchase.Id, "unpaid"));
        (await DeliverAsync(unpaid)).Should().Be(HttpStatusCode.OK);
        var forged = CreditEvent("evt_credit_forged", "checkout.session.completed", CreditSession(owner.OrganizationId, purchase.Id));
        (await DeliverAsync(forged, StripeSignature.HeaderFor("whsec_some_other_secret", DateTimeOffset.UtcNow, forged))).Should().Be(HttpStatusCode.Unauthorized);
        // Another organization's id on this organization's purchase: nothing is found under that organization's row-level security.
        (await DeliverAsync(CreditEvent("evt_credit_crossed", "checkout.session.completed", CreditSession(other.OrganizationId, purchase.Id)))).Should().Be(HttpStatusCode.OK);
        await Wait.ForNothingAsync(TimeSpan.FromSeconds(2), "a grant for a payment that was not made, was forged or named another organization", Ct);

        (await owner.CreditsAsync()).At("balance").GetDecimal().Should().Be(starting);
        (await other.CreditsAsync()).At("balance").GetDecimal().Should().Be(starting);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        (await db.CreditPurchases.AsNoTracking().SingleAsync(Ct)).Status.Should().Be(CreditPurchaseStatus.Pending);
    }

    [Fact]
    public async Task An_auto_reloads_paid_invoice_adds_the_credit_once_though_delivered_twice()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var starting = (await owner.CreditsAsync()).At("balance").GetDecimal();
        var purchase = await SeedPurchaseAsync(owner.OrganizationId, CreditPurchaseSource.AutoReload, 50m, "in_fictional_reload");
        JsonObject Invoice() => new()
        {
            ["id"] = "in_fictional_reload",
            ["customer"] = "cus_fictional_credit",
            ["metadata"] = new JsonObject
            {
                ["organization_id"] = owner.OrganizationId.ToString(),
                ["purpose"] = "ai-credits",
                ["purchase_id"] = purchase.Id.ToString(),
            },
        };

        (await DeliverAsync(CreditEvent("evt_reload_paid", "invoice.paid", Invoice()))).Should().Be(HttpStatusCode.OK);

        await WaitForBalanceAsync(owner, starting + 50m);
        (await DeliverAsync(CreditEvent("evt_reload_paid_again", "invoice.paid", Invoice()))).Should().Be(HttpStatusCode.OK);
        await Wait.ForNothingAsync(TimeSpan.FromSeconds(2), "a second grant for the same reload", Ct);
        (await owner.CreditsAsync()).At("balance").GetDecimal().Should().Be(starting + 50m);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        var grant = await db.CreditEntries.AsNoTracking().SingleAsync(e => e.PurchaseId == purchase.Id, Ct);
        grant.Kind.Should().Be(CreditEntryKind.AutoReload);
        (await db.CreditPurchases.AsNoTracking().SingleAsync(Ct)).Status.Should().Be(CreditPurchaseStatus.Paid);
    }

    private static JsonObject CreditSession(Guid organization, Guid purchase, string paymentStatus = "paid") => new()
    {
        ["id"] = "cs_fictional_credit",
        ["mode"] = "payment",
        ["payment_status"] = paymentStatus,
        ["payment_intent"] = "pi_fictional_credit",
        ["customer"] = "cus_fictional_credit",
        ["metadata"] = new JsonObject
        {
            ["organization_id"] = organization.ToString(),
            ["purpose"] = "ai-credits",
            ["purchase_id"] = purchase.ToString(),
        },
    };

    private static string CreditEvent(string eventId, string type, JsonObject item) => new JsonObject
    {
        ["id"] = eventId,
        ["type"] = type,
        ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ["data"] = new JsonObject { ["object"] = item },
    }.ToJsonString();

    private async Task<CreditPurchase> SeedPurchaseAsync(Guid organizationId, CreditPurchaseSource source, decimal amount, string? providerPaymentId)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        var purchase = new CreditPurchase { OrganizationId = organizationId, Source = source, Amount = amount, CreatedAt = DateTimeOffset.UtcNow };
        if (providerPaymentId is not null)
        {
            purchase.RecordProviderPayment(providerPaymentId);
        }

        db.CreditPurchases.Add(purchase);
        await db.SaveChangesAsync(Ct);
        return purchase;
    }

    private static async Task WaitForBalanceAsync(Tenant owner, decimal balance) =>
        await Messaging.WolverineStore.WaitUntilAsync(
            async () => (await owner.CreditsAsync()).At("balance").GetDecimal() == balance, $"the worker to grant the credit and the balance to reach {balance}", Ct);

    private static string SubscriptionUpdated(string eventId, Guid organization, string plan) => new JsonObject
    {
        ["id"] = eventId,
        ["type"] = "customer.subscription.updated",
        ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ["data"] = new JsonObject
        {
            ["object"] = new JsonObject
            {
                ["id"] = "sub_fictional_route",
                ["customer"] = "cus_fictional_route",
                ["status"] = "active",
                ["metadata"] = new JsonObject { ["organization_id"] = organization.ToString(), ["plan"] = plan },
            },
        },
    }.ToJsonString();

    // The route checks the signature's timestamp against the API's own clock (StripeSignature.MaxSkew), so a delivery is
    // signed with the current time; the event bodies carry no other time dependence.
    private const string SignedNow = "<signed now>";

    private async Task<HttpStatusCode> DeliverAsync(string body, string? header = SignedNow)
    {
        using var anonymous = fixture.CreateHttpClient("api");
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        header = header == SignedNow
            ?StripeSignature.HeaderFor(StripeModeFixture.WebhookSecret, DateTimeOffset.UtcNow, body)
            : header;
        if (header is not null)
        {
            request.Headers.Add(StripeSignature.Header, header);
        }

        using var response = await anonymous.SendAsync(request, Ct);
        return response.StatusCode;
    }
}
