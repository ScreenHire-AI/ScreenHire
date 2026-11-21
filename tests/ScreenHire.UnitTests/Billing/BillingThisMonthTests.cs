using ScreenHire.Api.Contracts;
using ScreenHire.Api.Endpoints;
using ScreenHire.Application.Billing;
using ScreenHire.Domain.Billing;

namespace ScreenHire.UnitTests.Billing;

/// <summary>
/// The Billing page's "This month so far" (docs/architecture/billing.md, "The Billing page"): each charge on its line and the total,
/// in every state, and the next invoice only while a subscription runs. The total is what the Owner pays, so the trial's credit is
/// taken off and AI use that is not billed here is left out.
/// </summary>
public sealed class BillingThisMonthTests
{
    private static readonly DateTimeOffset PeriodEnd = new(2026, 12, 4, 0, 0, 0, TimeSpan.Zero);
    private static readonly Plan Growth = new("growth", "Growth", 2, 149m, "USD", 10, false, null);

    private static BillingStatus Status(BillingState state, bool cancels = false) =>
        new(state, Growth, null, state is BillingState.Active or BillingState.PastDue ? PeriodEnd : null, cancels, true, 1, 10);

    private static BillingAiUsage Ai(decimal charge, bool metered = true) => new("2026-11", 59, charge, 0, 0, 5m, 0m, metered);

    [Fact]
    public void A_paying_month_adds_the_plan_and_ai_use_and_the_next_invoice_is_the_next_plan_payment_plus_that_use()
    {
        var month = BillingEndpoints.ThisMonth(Status(BillingState.Active), Ai(2.50m), 0m, "2026-11");

        month.PlanName.Should().Be("Growth");
        month.PlanAmount.Should().Be(149m);
        month.AiCalls.Should().Be(59);
        month.AiAmount.Should().Be(2.50m);
        month.Total.Should().Be(151.50m);
        month.NextInvoiceAmount.Should().Be(151.50m);
        month.NextInvoiceAt.Should().Be(PeriodEnd);
        month.Currency.Should().Be("USD");
    }

    [Fact]
    public void On_the_trial_the_plan_costs_nothing_the_allowance_is_taken_off_and_no_invoice_is_coming()
    {
        var month = BillingEndpoints.ThisMonth(Status(BillingState.Trialing), Ai(3.10m), 3.10m, "2026-11");

        month.PlanAmount.Should().Be(0m);
        month.TrialCredit.Should().Be(3.10m);
        month.Total.Should().Be(0m);
        month.NextInvoiceAmount.Should().BeNull();
        month.NextInvoiceAt.Should().BeNull();
    }

    [Fact]
    public void The_total_and_the_next_invoice_are_the_lines_to_the_cent_so_the_page_adds_up()
    {
        // $149.00 + $6.00 - $5.00 on screen: the total is $150.00, never the $150.008 the unrounded lines make.
        var month = BillingEndpoints.ThisMonth(Status(BillingState.Active), Ai(6.004m), 4.996m, "2026-11");

        month.Total.Should().Be(150.00m);
        month.NextInvoiceAmount.Should().Be(150.00m);
        month.AiAmount.Should().Be(6.004m, "the line keeps its digits, so use under a cent can read as such");
    }

    [Fact]
    public void The_credit_never_takes_off_more_than_the_ai_use()
    {
        BillingEndpoints.ThisMonth(Status(BillingState.Trialing), Ai(1m), 5m, "2026-11").TrialCredit.Should().Be(1m);
    }

    [Fact]
    public void A_plan_set_to_end_invoices_only_its_ai_use_and_with_none_no_invoice_is_coming()
    {
        var withUse = BillingEndpoints.ThisMonth(Status(BillingState.Active, cancels: true), Ai(2m), 0m, "2026-11");
        withUse.Total.Should().Be(151m, "this month's plan is paid already");
        withUse.NextInvoiceAmount.Should().Be(2m);

        var without = BillingEndpoints.ThisMonth(Status(BillingState.Active, cancels: true), Ai(0m), 0m, "2026-11");
        without.NextInvoiceAmount.Should().BeNull();
        without.NextInvoiceAt.Should().BeNull();
    }

    [Fact]
    public void An_ended_plan_has_no_plan_line_and_ai_use_that_is_not_billed_here_is_left_out()
    {
        var ended = BillingEndpoints.ThisMonth(Status(BillingState.Ended), Ai(2m), 0m, "2026-11");
        ended.PlanName.Should().BeNull();
        ended.PlanAmount.Should().BeNull();
        ended.Total.Should().Be(2m);

        var unmetered = BillingEndpoints.ThisMonth(Status(BillingState.Active), Ai(2m, metered: false), 0m, "2026-11");
        unmetered.AiAmount.Should().BeNull();
        unmetered.Total.Should().Be(149m);
    }
}
