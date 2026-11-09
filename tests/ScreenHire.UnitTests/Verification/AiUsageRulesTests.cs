using ScreenHire.Application.Verification;
using ScreenHire.Domain.Verification;

namespace ScreenHire.UnitTests.Verification;

/// <summary>
/// The rules behind AI usage (docs/architecture/ai-usage.md): what a call cost, when a budget alerts and a cap pauses, which
/// cap and budget are in force, and what a spike is. Pure functions, so every edge is a row here.
/// </summary>
public sealed class AiUsageRulesTests
{
    [Fact]
    public void A_cost_the_provider_reported_wins_over_any_price()
    {
        var (cost, basis) = AiUsageCost.Compute(reported: 0.0123m, inputTokens: 1_000_000, outputTokens: 1_000_000, inputPerMillion: 3m, outputPerMillion: 15m);

        cost.Should().Be(0.0123m);
        basis.Should().Be(AiCostBasis.Reported);
    }

    [Fact]
    public void Without_a_reported_cost_tokens_are_priced_per_million_on_each_side()
    {
        var (cost, basis) = AiUsageCost.Compute(null, inputTokens: 200_000, outputTokens: 50_000, inputPerMillion: 3m, outputPerMillion: 15m);

        cost.Should().Be(0.6m + 0.75m);
        basis.Should().Be(AiCostBasis.Priced);
    }

    [Theory]
    [InlineData(null, null, null, null)] // the provider reported no tokens at all
    [InlineData(1000L, 100L, null, null)] // no price at all
    [InlineData(1000L, 100L, 3.0, null)] // output tokens were used and have no price
    [InlineData(1000L, 100L, null, 15.0)] // input tokens were used and have no price
    public void A_call_nothing_can_price_has_an_unknown_cost_not_a_made_up_one(long? input, long? output, double? inPrice, double? outPrice)
    {
        var (cost, basis) = AiUsageCost.Compute(null, input, output, (decimal?)inPrice, (decimal?)outPrice);

        cost.Should().BeNull();
        basis.Should().Be(AiCostBasis.Unknown);
    }

    [Fact]
    public void A_side_that_used_no_tokens_needs_no_price()
    {
        var (cost, basis) = AiUsageCost.Compute(null, inputTokens: 1_000_000, outputTokens: 0, inputPerMillion: 3m, outputPerMillion: null);

        cost.Should().Be(3m);
        basis.Should().Be(AiCostBasis.Priced);
    }

    [Theory]
    [InlineData(0, 100, new int[0])]
    [InlineData(49, 100, new int[0])]
    [InlineData(50, 100, new[] { 50 })]
    [InlineData(79.9, 100, new[] { 50 })]
    [InlineData(80, 100, new[] { 50, 80 })]
    [InlineData(100, 100, new[] { 50, 80, 100 })]
    [InlineData(250, 100, new[] { 50, 80, 100 })]
    public void A_budget_alerts_at_fifty_eighty_and_a_hundred_percent_of_it(double used, double budget, int[] expected)
    {
        AiUsageLimits.ThresholdsReached((decimal)used, (decimal)budget).Should().Equal(expected);
    }

    [Fact]
    public void No_budget_alerts_nothing()
    {
        AiUsageLimits.ThresholdsReached(1_000_000m, null).Should().BeEmpty();
        AiUsageLimits.ThresholdsReached(1_000_000m, 0m).Should().BeEmpty();
    }

    [Theory]
    [InlineData(99, 100, false)]
    [InlineData(100, 100, true)] // the cap is a limit, not a last allowed amount
    [InlineData(101, 100, true)]
    public void A_cap_is_reached_at_the_cap(double used, double cap, bool reached)
    {
        AiUsageLimits.CapReached((decimal)used, (decimal)cap).Should().Be(reached);
    }

    [Fact]
    public void No_cap_never_pauses_anything()
    {
        AiUsageLimits.CapReached(decimal.MaxValue, null).Should().BeFalse();
    }

    [Fact]
    public void An_organizations_own_cap_wins_and_ScreenHires_key_falls_back_to_the_deployments_default_in_tokens()
    {
        var own = new VerificationSettings { BudgetUnit = UsageUnit.Money, MonthlyCap = 40m };
        var none = new VerificationSettings();

        AiUsageLimits.EffectiveCap(own, 2_000_000).Should().Be((40m, UsageUnit.Money));
        AiUsageLimits.EffectiveCap(none, 2_000_000).Should().Be((2_000_000m, UsageUnit.Tokens));
        AiUsageLimits.EffectiveCap(null, 2_000_000).Should().Be((2_000_000m, UsageUnit.Tokens), "an organization that never saved settings is on ScreenHire's key");
        AiUsageLimits.EffectiveCap(none, 0).Cap.Should().BeNull("a deployment can turn its default cap off with 0");
    }

    [Fact]
    public void An_own_key_has_no_default_budget_or_cap_but_its_own_still_apply()
    {
        var ownKey = new VerificationSettings { LlmSource = LlmSource.OwnKey };
        var capped = new VerificationSettings { LlmSource = LlmSource.OwnKey, MonthlyCap = 500_000m, MonthlyBudget = 250_000m };

        AiUsageLimits.EffectiveCap(ownKey, 2_000_000).Cap.Should().BeNull();
        AiUsageLimits.EffectiveBudget(ownKey, 2_000_000).Budget.Should().BeNull();
        AiUsageLimits.EffectiveCap(capped, 2_000_000).Should().Be((500_000m, UsageUnit.Tokens));
        AiUsageLimits.EffectiveBudget(capped, 2_000_000).Should().Be((250_000m, UsageUnit.Tokens));
    }

    [Fact]
    public void The_budget_and_the_cap_can_each_fall_back_on_their_own()
    {
        var onlyBudget = new VerificationSettings { MonthlyBudget = 1_000m };

        AiUsageLimits.EffectiveBudget(onlyBudget, 2_000_000).Budget.Should().Be(1_000m);
        AiUsageLimits.EffectiveCap(onlyBudget, 2_000_000).Cap.Should().Be(2_000_000m);
    }

    private static readonly long[] Quiet = [100_000, 120_000, 90_000, 110_000, 0, 0, 100_000];

    [Fact]
    public void A_day_three_times_the_recent_average_is_a_spike()
    {
        var average = Quiet.Sum() / (decimal)Quiet.Length; // about 74,285

        AiUsageSpike.IsSpike(300_000, Quiet, 3m, 100_000, 3).Should().BeTrue();
        AiUsageSpike.IsSpike((long)(average * 3m) - 1, Quiet, 3m, 100_000, 3).Should().BeFalse("just under three times the average");
    }

    [Fact]
    public void A_busy_day_below_the_floor_is_no_spike_however_quiet_the_weeks_before_were()
    {
        long[] almostNothing = [10, 0, 20, 0, 30, 0, 10];

        AiUsageSpike.IsSpike(50_000, almostNothing, 3m, 100_000, 3).Should().BeFalse();
    }

    [Fact]
    public void An_organization_with_too_little_history_never_spikes()
    {
        long[] oneDay = [0, 0, 0, 0, 0, 0, 50_000];

        AiUsageSpike.IsSpike(5_000_000, oneDay, 3m, 100_000, 3).Should().BeFalse();
        AiUsageSpike.IsSpike(5_000_000, [], 3m, 100_000, 3).Should().BeFalse();
    }

    [Fact]
    public void The_ledger_is_kept_to_the_start_of_the_previous_month_whatever_retention_says()
    {
        var now = new DateTimeOffset(2026, 11, 9, 4, 42, 0, TimeSpan.Zero);
        var previousMonth = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        PurgeOrganizationAiUsageHandler.CutoffFor(null, now).Should().BeNull("retention off keeps everything");
        PurgeOrganizationAiUsageHandler.CutoffFor(now.AddMonths(-1), now).Should().Be(previousMonth, "a one month retention still leaves last month's calls for billing");
        PurgeOrganizationAiUsageHandler.CutoffFor(now.AddMonths(-6), now).Should().Be(now.AddMonths(-6), "a longer retention is the organization's own");
    }

    [Fact]
    public void A_paused_AI_check_is_told_apart_from_a_group_turned_off_and_from_a_trusted_source()
    {
        RunNotes.KindOf("consistency", RunNotes.AiChecksPaused).Should().Be(CheckNotRunKind.Paused);
        RunNotes.KindOf("consistency", CheckGroups.TurnedOffNote(CheckGroup.Answers)).Should().Be(CheckNotRunKind.TurnedOff);
        RunNotes.AiChecksPaused.Should().Contain("says nothing about the candidate");
    }

    [Fact]
    public void A_paused_check_can_be_asked_again_and_a_completed_one_cannot()
    {
        var now = DateTimeOffset.UnixEpoch;
        var check = ConsistencyCheck.Claim(Guid.CreateVersion7(), Guid.CreateVersion7(), "v1", now);
        check.Complete(new ConsistencyCheckOutcome(ConsistencyCheckStatus.Paused, RunNotes.AiChecksPaused), now);

        check.Reclaim(now.AddDays(1));

        check.Status.Should().Be(ConsistencyCheckStatus.Running);
        check.Note.Should().BeNull();
        check.CompletedAt.Should().BeNull();
        check.Complete(new ConsistencyCheckOutcome(ConsistencyCheckStatus.Completed), now.AddDays(1));
        FluentActions.Invoking(() => check.Reclaim(now.AddDays(2))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_csv_neutralises_formulas_quotes_what_needs_it_and_leaves_numbers_alone()
    {
        var csv = new AiUsageCsv();
        csv.Row("a", "=HYPERLINK(\"x\")", "+1", "-1", "@x", "plain, comma", "line\nbreak", 12L, 0.0042m, null, new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero));

        var bytes = csv.ToBytes();
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        text.TrimStart((char)0xFEFF).Should().Be("a,\"'=HYPERLINK(\"\"x\"\")\",'+1,'-1,'@x,\"plain, comma\",\"line\nbreak\",12,0.0042,,2026-11-08T10:00:00.000Z\r\n");
    }

    [Fact]
    public void A_cost_OpenRouter_reports_is_read_from_the_chat_answer_and_nothing_else_is_taken_for_one()
    {
        ReportedCostCapture.Parse("""{"usage":{"prompt_tokens":5,"cost":0.00123}}""").Should().Be(0.00123m);
        ReportedCostCapture.Parse("""{"usage":{"prompt_tokens":5}}""").Should().BeNull();
        ReportedCostCapture.Parse("""{"usage":{"cost":"free"}}""").Should().BeNull();
        ReportedCostCapture.Parse("""{"usage":{"cost":-1}}""").Should().BeNull();
        ReportedCostCapture.Parse("not json").Should().BeNull();
    }

    [Fact]
    public void The_sweeps_refuse_settings_they_cannot_work_with()
    {
        new AiUsageOptionsCheck().Validate(null, new AiUsageOptions()).Succeeded.Should().BeTrue();
        new AiUsageOptionsCheck().Validate(null, new AiUsageOptions { Spike = new SpikeOptions { Factor = 1m } }).Failed.Should().BeTrue();
        new AiUsageOptionsCheck().Validate(null, new AiUsageOptions { Spike = new SpikeOptions { LookbackDays = 3, MinimumActiveDays = 4 } }).Failed.Should().BeTrue();
        new AiUsageOptionsCheck().Validate(null, new AiUsageOptions { ResumeBatchSize = 0 }).Failed.Should().BeTrue();
    }
}
