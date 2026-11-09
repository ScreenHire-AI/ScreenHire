using ScreenHire.Domain.Billing;

namespace ScreenHire.UnitTests.Billing;

/// <summary>
/// What a call on ScreenHire's AI key costs the organization: the provider's price plus the margin, in whole micro-dollars,
/// and which calls the free trial's allowance covers (docs/architecture/billing.md, "AI usage").
/// </summary>
public sealed class AiChargeTests
{
    [Theory]
    [InlineData("1.00", "0.20", 1_200_000)]
    [InlineData("1.00", "0", 1_000_000)]
    [InlineData("0.0004", "0.20", 480)]
    [InlineData("0.00001", "0.20", 12)]
    [InlineData("3.12", "0.20", 3_744_000)]
    [InlineData("2.50", "10", 27_500_000)]
    public void A_call_is_charged_its_cost_plus_the_margin_in_micro_dollars(string cost, string margin, long expected)
    {
        AiCharge.Micros(Parse(cost), Parse(margin)).Should().Be(expected);
    }

    [Fact]
    public void The_charge_rounds_half_away_from_zero_to_a_micro_dollar()
    {
        // 0.0000025 plus 20% is 3.0 micro-dollars exactly; 0.00000125 plus 20% is 1.5, which rounds up to 2.
        AiCharge.Micros(0.0000025m, 0.20m).Should().Be(3);
        AiCharge.Micros(0.00000125m, 0.20m).Should().Be(2);
        AiCharge.Micros(0.000000416m, 0.20m).Should().Be(0, "0.4992 micro-dollars is below half, so it rounds down (the planner then waives it: the meter takes no zero)");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    public void A_call_with_no_known_cost_or_none_is_charged_nothing(string? cost)
    {
        AiCharge.Micros(cost is null ? null : Parse(cost), 0.20m).Should().Be(0);
    }

    [Fact]
    public void A_sum_of_charges_is_the_sum_of_each_calls_rounded_charge_so_the_page_and_the_invoice_agree()
    {
        decimal[] costs = [0.0000025m, 0.00000125m, 0.0004m, 3.12m];

        var perCall = costs.Sum(c => AiCharge.Micros(c, 0.20m));

        perCall.Should().Be(3 + 2 + 480 + 3_744_000);
        AiCharge.Dollars(perCall).Should().Be(3.744485m);
    }

    [Fact]
    public void Dollars_and_micro_dollars_convert_both_ways()
    {
        AiCharge.Dollars(1_500_000).Should().Be(1.5m);
        AiCharge.ToMicros(5.00m).Should().Be(5_000_000);
        AiCharge.ToMicros(0.0000005m).Should().Be(1, "half a micro-dollar rounds up");
    }

    private static readonly DateTimeOffset TrialEnds = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static PendingAiCall Call(int minute, long micros, bool afterTrial = false) =>
        new(Guid.CreateVersion7(), (afterTrial ? TrialEnds.AddHours(1) : TrialEnds.AddDays(-5)).AddMinutes(minute), micros);

    [Fact]
    public void Trial_calls_are_waived_in_time_order_while_the_total_stays_within_the_allowance_and_the_rest_is_reported()
    {
        var calls = new[] { Call(1, 2_000_000), Call(2, 2_000_000), Call(3, 2_000_000), Call(4, 500_000) };

        var plan = AiChargePlanner.Plan(calls, TrialEnds, allowanceMicros: 5_000_000, alreadyWaivedMicros: 0);

        plan.Select(d => d.State).Should().Equal(AiChargeState.Waived, AiChargeState.Waived, AiChargeState.Reported, AiChargeState.Reported);
        plan.Select(d => d.Id).Should().Equal(calls.Select(c => c.Id), "decisions come back in time order");
    }

    [Fact]
    public void Once_a_call_would_pass_the_allowance_no_later_trial_call_is_waived_even_a_small_one()
    {
        var plan = AiChargePlanner.Plan([Call(1, 4_000_000), Call(2, 2_000_000), Call(3, 10)], TrialEnds, 5_000_000, 0);

        plan.Select(d => d.State).Should().Equal(AiChargeState.Waived, AiChargeState.Reported, AiChargeState.Reported);
    }

    [Fact]
    public void What_an_earlier_run_waived_counts_against_the_allowance()
    {
        var plan = AiChargePlanner.Plan([Call(1, 1_500_000), Call(2, 600_000)], TrialEnds, 5_000_000, alreadyWaivedMicros: 3_000_000);

        plan.Select(d => d.State).Should().Equal(AiChargeState.Waived, AiChargeState.Reported);
    }

    [Fact]
    public void A_call_after_the_trial_is_reported_in_full_whatever_allowance_is_left()
    {
        var plan = AiChargePlanner.Plan([Call(1, 100, afterTrial: true)], TrialEnds, 5_000_000, 0);

        plan.Should().ContainSingle().Which.State.Should().Be(AiChargeState.Reported);
    }

    [Fact]
    public void A_call_that_cost_nothing_is_waived_because_the_meter_takes_positive_values_only()
    {
        var plan = AiChargePlanner.Plan([Call(1, 0, afterTrial: true), Call(2, 0)], TrialEnds, 0, 0);

        plan.Should().OnlyContain(d => d.State == AiChargeState.Waived && d.Micros == 0);
    }

    [Fact]
    public void With_no_allowance_every_priced_call_is_reported()
    {
        var plan = AiChargePlanner.Plan([Call(1, 10), Call(2, 20)], TrialEnds, allowanceMicros: 0, alreadyWaivedMicros: 0);

        plan.Should().OnlyContain(d => d.State == AiChargeState.Reported);
    }

    [Fact]
    public void The_same_calls_always_get_the_same_decisions_whatever_order_they_arrive_in()
    {
        var calls = new[] { Call(1, 2_000_000), Call(2, 2_000_000), Call(3, 2_000_000) };

        var forward = AiChargePlanner.Plan(calls, TrialEnds, 5_000_000, 0);
        var backward = AiChargePlanner.Plan(calls.Reverse(), TrialEnds, 5_000_000, 0);

        backward.Should().Equal(forward);
    }

    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
