using ScreenHire.Api.Endpoints;
using ScreenHire.Application.Billing;
using ScreenHire.Domain.Billing;

namespace ScreenHire.UnitTests.Billing;

/// <summary>
/// The Billing page's "This month so far" (docs/architecture/billing.md, "The Billing page"): the plan's line, the AI credits bought
/// this month and the total, in every state, and the next invoice only while a subscription runs. AI use is prepaid (ADR-0032), so it
/// is on no invoice and the next invoice is the plan alone.
/// </summary>
public sealed class BillingThisMonthTests
{
    private static readonly DateTimeOffset PeriodEnd = new(2026, 12, 4, 0, 0, 0, TimeSpan.Zero);
    private static readonly Plan Growth = new("growth", "Growth", 2, 149m, "USD", 10, false, null);

    private static BillingStatus Status(BillingState state, bool cancels = false, Plan? plan = null) =>
        new(state, plan ?? Growth, null, state is BillingState.Active or BillingState.PastDue ? PeriodEnd : null, cancels, true, 1, 10);

    [Fact]
    public void A_paying_month_adds_the_plan_and_the_credits_bought_and_the_next_invoice_is_the_plan_alone()
    {
        var month = BillingEndpoints.ThisMonth(Status(BillingState.Active), 25m, "2026-11");

        month.PlanName.Should().Be("Growth");
        month.PlanAmount.Should().Be(149m);
        month.CreditsBought.Should().Be(25m);
        month.Total.Should().Be(174m);
        month.NextInvoiceAmount.Should().Be(149m, "credits are paid for when bought and on no invoice");
        month.NextInvoiceAt.Should().Be(PeriodEnd);
        month.Currency.Should().Be("USD");
    }

    [Fact]
    public void On_the_trial_the_plan_costs_nothing_and_no_invoice_is_coming()
    {
        var month = BillingEndpoints.ThisMonth(Status(BillingState.Trialing), 10m, "2026-11");

        month.PlanAmount.Should().Be(0m);
        month.Total.Should().Be(10m);
        month.NextInvoiceAmount.Should().BeNull();
        month.NextInvoiceAt.Should().BeNull();
    }

    [Fact]
    public void The_total_is_the_lines_to_the_cent_so_the_page_adds_up()
    {
        // $149.00 + $12.50 on screen. A sum of unrounded lines would show $161.504 for the same page.
        var month = BillingEndpoints.ThisMonth(Status(BillingState.Active), 12.504m, "2026-11");

        month.Total.Should().Be(161.50m);
    }

    [Fact]
    public void A_plan_set_to_end_has_no_next_invoice_though_this_months_plan_stays_in_the_total()
    {
        var month = BillingEndpoints.ThisMonth(Status(BillingState.Active, cancels: true), 0m, "2026-11");

        month.Total.Should().Be(149m, "this month's plan is paid already");
        month.NextInvoiceAmount.Should().BeNull();
        month.NextInvoiceAt.Should().BeNull();
    }

    [Fact]
    public void An_ended_plan_has_no_plan_line_and_only_the_credits_count()
    {
        var month = BillingEndpoints.ThisMonth(Status(BillingState.Ended), 50m, "2026-11");

        month.PlanName.Should().BeNull();
        month.PlanAmount.Should().BeNull();
        month.Total.Should().Be(50m);
        month.NextInvoiceAmount.Should().BeNull();
    }

    [Fact]
    public void A_plan_in_another_currency_keeps_its_own_total_and_the_dollar_credits_stand_apart()
    {
        var euro = new Plan("growth", "Growth", 2, 139m, "EUR", 10, false, null);

        var month = BillingEndpoints.ThisMonth(Status(BillingState.Active, plan: euro), 25m, "2026-11");

        month.Currency.Should().Be("EUR");
        month.CreditsBought.Should().Be(25m);
        month.Total.Should().Be(139m, "adding dollars to euros would be a wrong number");
    }
}
