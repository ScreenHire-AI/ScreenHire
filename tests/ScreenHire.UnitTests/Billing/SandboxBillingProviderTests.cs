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

    // A 31-day period, 1 January to 1 February.
    private static readonly DateTimeOffset PeriodEnd = new(2100, 2, 1, 0, 0, 0, TimeSpan.Zero);

    private static Plan Growth() => new("growth", "Growth", 2, 99m, "USD", 25, false, null);

    private Task<PlanChangePreview> Preview(Plan from, Plan to, DateTimeOffset on, DateTimeOffset? periodEnd = null) =>
        provider.PreviewPlanChangeAsync(new PlanChangeRequest(organization, null, "sub_fictional", from, to, periodEnd ?? PeriodEnd, on), TestContext.Current.CancellationToken);

    [Fact]
    public async Task An_upgrade_halfway_through_the_period_costs_the_price_difference_for_the_days_left()
    {
        var preview = await Preview(Starter(), Growth(), new DateTimeOffset(2100, 1, 17, 9, 0, 0, TimeSpan.Zero));

        // (99 - 29) x 15 days left of 31, to the cent.
        preview.AmountDueNow.Should().Be(33.87m);
        preview.NextPaymentAmount.Should().Be(99m);
        preview.NextPaymentAt.Should().Be(PeriodEnd);
        preview.Currency.Should().Be("USD");
        preview.ProrationDate.Should().Be(new DateTimeOffset(2100, 1, 17, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task An_upgrade_on_the_first_day_costs_the_whole_difference_and_on_the_last_day_one_day_of_it()
    {
        var first = await Preview(Starter(), Growth(), new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var last = await Preview(Starter(), Growth(), new DateTimeOffset(2100, 1, 31, 23, 0, 0, TimeSpan.Zero));

        first.AmountDueNow.Should().Be(70m);
        last.AmountDueNow.Should().Be(2.26m);
    }

    [Fact]
    public async Task A_period_end_that_has_passed_rolls_on_by_whole_months_so_the_next_payment_is_never_in_the_past()
    {
        var atTheEnd = await Preview(Starter(), Growth(), PeriodEnd);
        var twoMonthsOn = await Preview(Starter(), Growth(), new DateTimeOffset(2100, 3, 17, 9, 0, 0, TimeSpan.Zero));

        atTheEnd.NextPaymentAt.Should().Be(new DateTimeOffset(2100, 3, 1, 0, 0, 0, TimeSpan.Zero), "the next period has begun");
        atTheEnd.AmountDueNow.Should().Be(70m, "all of the new period is left");
        twoMonthsOn.NextPaymentAt.Should().Be(new DateTimeOffset(2100, 4, 1, 0, 0, 0, TimeSpan.Zero));
        // (99 - 29) x 15 days left of 31 (17 March to 1 April), to the cent.
        twoMonthsOn.AmountDueNow.Should().Be(33.87m);
    }

    [Fact]
    public async Task A_downgrade_owes_nothing_now_and_the_lower_price_is_next()
    {
        var preview = await Preview(Growth(), Starter(), new DateTimeOffset(2100, 1, 17, 9, 0, 0, TimeSpan.Zero));

        preview.AmountDueNow.Should().Be(0m);
        preview.NextPaymentAmount.Should().Be(29m);
    }

    [Fact]
    public async Task A_change_with_no_known_period_end_is_priced_as_a_month_from_the_proration_date()
    {
        var on = new DateTimeOffset(2100, 1, 17, 9, 0, 0, TimeSpan.Zero);
        var preview = await provider.PreviewPlanChangeAsync(
            new PlanChangeRequest(organization, null, "sub_fictional", Starter(), Growth(), null, on), TestContext.Current.CancellationToken);

        preview.NextPaymentAt.Should().Be(on.AddMonths(1));
        preview.AmountDueNow.Should().Be(70m, "a whole period is left");
    }

    [Fact]
    public async Task Changing_the_plan_has_nothing_to_call()
    {
        var change = new PlanChangeRequest(organization, null, "sub_fictional", Starter(), Growth(), PeriodEnd, Start);

        await provider.Invoking(p => p.ChangePlanAsync(change, TestContext.Current.CancellationToken)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Reported_usage_is_taken_whole_and_in_order_because_the_sandbox_charges_nobody()
    {
        var ids = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var report = new AiUsageReport(organization, null, [.. ids.Select((id, i) => new AiUsageEvent(id, 10 * (i + 1)))], Start);

        var result = await provider.ReportAiUsageAsync(report, TestContext.Current.CancellationToken);

        result.Accepted.Should().Equal(ids);
        result.Failure.Should().BeNull();
    }

    [Fact]
    public void The_sandbox_has_no_webhook_so_a_post_to_the_provider_reads_as_unsigned()
    {
        var webhook = provider.ReadWebhook("{}", "t=1,v1=abc", clock.Now);

        webhook.SignatureValid.Should().BeFalse();
        webhook.Event.Should().BeNull();
    }
}
