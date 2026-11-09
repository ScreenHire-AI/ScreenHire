using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.Billing;

/// <summary>An AppHost of its own with <c>ScreenHire:Billing:Stripe:Tax:Enabled</c> on, so the sandbox checkout asks for tax.</summary>
public sealed class TaxOnFixture : AppHostFixture
{
    protected override IReadOnlyList<string> ExtraArguments => ["--ScreenHire:Billing:Stripe:Tax:Enabled=true"];
}

/// <summary>
/// Tax at Checkout through the local sandbox (docs/architecture/billing.md): with <c>Stripe:Tax:Enabled</c> the overview says
/// tax is added at checkout and the sandbox's checkout records that it asked for tax and a tax id, in the query of the address
/// it sends the browser back to. The shared stack has tax off, which <see cref="BillingTests"/> runs on.
/// </summary>
public sealed class BillingTaxTests(TaxOnFixture fixture) : IClassFixture<TaxOnFixture>
{
    [Fact]
    public async Task The_overview_says_tax_is_added_at_checkout_and_the_sandbox_checkout_asks_for_tax_and_a_tax_id()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await owner.BillingAsync()).At("taxAtCheckout").GetBoolean().Should().BeTrue();

        var checkout = (await owner.PostAsync($"{BillingTestSupport.Overview}/checkout", new JsonObject { ["plan"] = "starter" })).Expect(HttpStatusCode.OK);
        var (status, location) = await fixture.FollowAsync(checkout.Text("url"));

        status.Should().Be(HttpStatusCode.Redirect);
        location!.Query.Should().Contain("checkout=success").And.Contain("tax=requested").And.Contain("taxId=requested");
        (await owner.BillingAsync()).Text("state").Should().Be("Active", "asking for tax does not stop the plan starting");
    }
}
