using ScreenHire.Application.Verification;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Verification;
using ScreenHire.Infrastructure.Verification.Llm;

namespace ScreenHire.UnitTests.Verification;

/// <summary>
/// The rules behind AI usage (docs/architecture/ai-usage.md): what a call cost, the one amount it is billed at, and what a spike is. Pure functions, so every edge is a row here.
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
        RunNotes.KindOf("consistency", RunNotes.AiCreditsPaused).Should().Be(CheckNotRunKind.Paused);
        RunNotes.KindOf("consistency", CheckGroups.TurnedOffNote(CheckGroup.Answers)).Should().Be(CheckNotRunKind.TurnedOff);
        RunNotes.AiChecksPaused.Should().Contain("says nothing about the candidate");
        RunNotes.AiCreditsPaused.Should().Contain("says nothing about the candidate");
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
    public void The_one_amount_for_a_call_is_billed_on_ScreenHires_key_and_the_providers_price_on_an_own_key()
    {
        AiCharge.Billed(0.0001234m, AiKeySource.BuiltIn, 0.2m).Should().Be(0.000148m, "the cost plus 20%, to the micro-dollar the ledger records");
        AiCharge.Billed(0.0001234m, AiKeySource.OwnKey, 0.2m).Should().Be(0.0001234m, "an own key is never billed: its amount is the provider's price");
        AiCharge.Billed(null, AiKeySource.BuiltIn, 0.2m).Should().BeNull("an unknown cost stays unknown");
        AiCharge.Billed(1m, AiKeySource.BuiltIn, 0m).Should().Be(1m);
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
