using System.Web;
using Microsoft.AspNetCore.DataProtection;
using ScreenHire.Application.Billing;
using ScreenHire.Domain.Billing;
using ScreenHire.UnitTests.Distribution;

namespace ScreenHire.UnitTests.Billing;

/// <summary>
/// The sandbox provider's links: relative addresses on the API that carry a signed, expiring <c>state</c>, so the anonymous
/// routes behind them cannot be pointed at another organization or plan by editing the address. Data Protection's
/// time-limited protector also compares the expiry with the real clock, so these tests run at a fixed instant far in the
/// future and only the injected clock can expire a state.
/// </summary>
public sealed class SandboxBillingProviderTests
{
    private static readonly DateTimeOffset Start = new(2100, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Uri Success = new("https://app.example.org/settings/billing?checkout=success");
    private static readonly Uri Cancelled = new("https://app.example.org/settings/billing?checkout=cancelled");

    private readonly FixedTime clock = new(Start);
    private readonly SandboxBillingProvider provider;
    private readonly Guid organization = Guid.CreateVersion7();

    public SandboxBillingProviderTests() => provider = Provider();

    private SandboxBillingProvider Provider(BillingOptions? options = null) =>
        new(new EphemeralDataProtectionProvider(), clock, Microsoft.Extensions.Options.Options.Create(options ?? new BillingOptions()));

    private static Plan Starter() => new("starter", "Starter", 1, 29m, "USD", 1, false, null);

    private static string StateOf(Uri link) => HttpUtility.ParseQueryString(link.OriginalString[link.OriginalString.IndexOf('?', StringComparison.Ordinal)..])["state"]!;

    private async Task<string> CheckoutStateAsync()
    {
        var link = await provider.CreateCheckoutAsync(new CheckoutRequest(organization, Starter(), null, null, Success, Cancelled), TestContext.Current.CancellationToken);
        return StateOf(link);
    }

    [Fact]
    public async Task Checkout_is_a_relative_link_to_the_sandbox_page_and_its_state_names_the_organization_plan_and_return_page()
    {
        var link = await provider.CreateCheckoutAsync(new CheckoutRequest(organization, Starter(), null, null, Success, Cancelled), TestContext.Current.CancellationToken);

        link.IsAbsoluteUri.Should().BeFalse("the dashboard proxies /v1 to the API, so the link works on its own origin");
        link.OriginalString.Should().StartWith("/v1/billing/sandbox/checkout?state=");
        var state = provider.Read(StateOf(link));
        state.Should().NotBeNull();
        state!.OrganizationId.Should().Be(organization);
        state.PlanKey.Should().Be("starter");
        state.ReturnUrl.Should().Be(Success.ToString());
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, false)]
    public async Task The_checkout_state_records_whether_tax_and_a_tax_id_were_asked_for(bool tax, bool taxIds, bool expectedTax, bool expectedTaxIds)
    {
        var options = new BillingOptions();
        options.Stripe.Tax.Enabled = tax;
        options.Stripe.Tax.CollectTaxIds = taxIds;
        var taxed = Provider(options);

        var link = await taxed.CreateCheckoutAsync(new CheckoutRequest(organization, Starter(), null, null, Success, Cancelled), TestContext.Current.CancellationToken);

        var state = taxed.Read(StateOf(link))!;
        state.TaxRequested.Should().Be(expectedTax);
        state.TaxIdsRequested.Should().Be(expectedTaxIds, "a tax id is only asked for with tax on");
    }

    [Fact]
    public async Task The_portal_link_carries_no_plan_and_returns_to_the_billing_page()
    {
        var link = await provider.CreatePortalAsync(new PortalRequest(organization, "cus_fictional", Success), TestContext.Current.CancellationToken);

        link.IsAbsoluteUri.Should().BeFalse();
        link.OriginalString.Should().StartWith("/v1/billing/sandbox/portal?state=");
        var state = provider.Read(StateOf(link))!;
        state.OrganizationId.Should().Be(organization);
        state.PlanKey.Should().BeNull();
        state.ReturnUrl.Should().Be(Success.ToString());
    }

    [Fact]
    public async Task A_state_reads_until_its_lifetime_has_passed_and_not_after()
    {
        var state = await CheckoutStateAsync();

        clock.Now = Start + SandboxBillingProvider.Lifetime - TimeSpan.FromSeconds(1);
        provider.Read(state).Should().NotBeNull("the person is still on the page");

        clock.Now = Start + SandboxBillingProvider.Lifetime + TimeSpan.FromSeconds(1);
        provider.Read(state).Should().BeNull("a link is not good forever");
    }

    [Fact]
    public async Task A_tampered_state_does_not_read()
    {
        var state = await CheckoutStateAsync();
        var flipped = state[..^3] + (state[^3] == 'A' ? 'B' : 'A') + state[^2..];

        provider.Read(flipped).Should().BeNull();
        provider.Read(state[..^4]).Should().BeNull();
        provider.Read(state + "AAAA").Should().BeNull();
    }

    [Fact]
    public async Task A_state_issued_by_another_key_ring_does_not_read()
    {
        var foreign = Provider();
        var state = StateOf(await foreign.CreateCheckoutAsync(new CheckoutRequest(organization, Starter(), null, null, Success, Cancelled), TestContext.Current.CancellationToken));

        provider.Read(state).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-state")]
    [InlineData("%%%")]
    public void Missing_or_made_up_states_do_not_read(string? state)
    {
        provider.Read(state).Should().BeNull();
    }

    [Fact]
    public void A_state_longer_than_any_the_provider_issues_is_refused_without_trying_it()
    {
        provider.Read(new string('A', 4001)).Should().BeNull();
    }

    [Fact]
    public void The_sandbox_has_no_webhook_so_a_post_to_the_provider_reads_as_unsigned()
    {
        var webhook = provider.ReadWebhook("{}", "t=1,v1=abc", clock.Now);

        webhook.SignatureValid.Should().BeFalse();
        webhook.Event.Should().BeNull();
    }
}
