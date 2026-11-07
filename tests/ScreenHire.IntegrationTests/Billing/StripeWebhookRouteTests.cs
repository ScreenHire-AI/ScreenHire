using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using ScreenHire.Application.Billing;

namespace ScreenHire.IntegrationTests.Billing;

/// <summary>
/// An AppHost of its own whose API is in Stripe mode (fictional keys, nothing calls Stripe), so Stripe's webhook route is
/// mapped. Stripe wins over the sandbox switch the shared stack turns on (<c>BillingOptions.Mode</c>).
/// </summary>
public sealed class StripeModeFixture : AppHostFixture
{
    public const string WebhookSecret = "whsec_fictional_route_tests";

    protected override void Configure(IDistributedApplicationTestingBuilder builder)
    {
        var api = builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "api");
        builder.CreateResourceBuilder(api).WithEnvironment(context =>
        {
            context.EnvironmentVariables["ScreenHire__Billing__SecretKey"] = "sk_test_fictional_route_tests";
            context.EnvironmentVariables["ScreenHire__Billing__WebhookSecret"] = WebhookSecret;
        });
    }
}

/// <summary>
/// A signed delivery through <c>POST /v1/webhooks/stripe</c> (docs/architecture/billing.md, "Webhooks"): the route's
/// signature check, <c>BillingEventApplier</c> under row-level security and the outbox save, end to end. The unit tests
/// cover the reading of an event and <see cref="BillingWebhookTests"/> covers what the service does with one.
/// </summary>
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
