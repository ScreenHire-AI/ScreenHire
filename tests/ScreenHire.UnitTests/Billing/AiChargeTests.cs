using ScreenHire.Domain.Billing;

namespace ScreenHire.UnitTests.Billing;

/// <summary>
/// What a call on ScreenHire's AI key costs the organization: the provider's price plus the margin, in whole micro-dollars,
/// (docs/architecture/billing.md, "AI usage").
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
        AiCharge.Micros(0.000000416m, 0.20m).Should().Be(0, "0.4992 micro-dollars is below half, so it rounds down");
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

    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
