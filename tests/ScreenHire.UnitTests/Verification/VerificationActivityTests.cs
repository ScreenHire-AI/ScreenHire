using ScreenHire.Application.Verification;
using ScreenHire.Domain.Verification;

namespace ScreenHire.UnitTests.Verification;

/// <summary>
/// The rules behind the Verification area's activity log (verification-engine.md 10.12): what a run or call ended as, which group a
/// call belongs to, the cursor between pages, and how check runs and model calls become one newest-first page.
/// </summary>
public sealed class VerificationActivityTests
{
    [Fact]
    public void A_run_that_wrote_signals_found_something()
    {
        VerificationActivity.OutcomeOfRun("network", 2, null).Should().Be(VerificationActivityOutcome.Found);
    }

    [Fact]
    public void A_run_with_no_signals_and_no_note_completed()
    {
        VerificationActivity.OutcomeOfRun("network", 0, null).Should().Be(VerificationActivityOutcome.Completed);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("identity")]
    [InlineData("documents")]
    [InlineData("answers")]
    [InlineData("consistency")]
    [InlineData("duplicates")]
    [InlineData("automation")]
    [InlineData("behavioural")]
    public void A_run_whose_group_is_turned_off_did_not_run(string analyzer)
    {
        var note = CheckGroups.TurnedOffNote(CheckGroups.Of(analyzer)!.Value);

        VerificationActivity.OutcomeOfRun(analyzer, 0, note).Should().Be(VerificationActivityOutcome.NotRun);
    }

    [Fact]
    public void A_paused_AI_check_and_a_run_that_could_not_look_did_not_run()
    {
        VerificationActivity.OutcomeOfRun("consistency", 0, RunNotes.AiChecksPaused).Should().Be(VerificationActivityOutcome.NotRun);
        VerificationActivity.OutcomeOfRun("network", 0, "No IP intelligence is configured.").Should().Be(VerificationActivityOutcome.NotRun);
    }

    [Fact]
    public void A_run_the_trusted_list_stopped_a_signal_in_looked_and_completed()
    {
        foreach (var note in RunNotes.TrustedNotes)
        {
            VerificationActivity.OutcomeOfRun("duplicates", 0, note).Should().Be(VerificationActivityOutcome.Completed, note);
        }
    }

    [Fact]
    public void A_model_call_completed_or_failed()
    {
        VerificationActivity.OutcomeOfCall(AiCallOutcome.Ok).Should().Be(VerificationActivityOutcome.Completed);
        VerificationActivity.OutcomeOfCall(AiCallOutcome.Error).Should().Be(VerificationActivityOutcome.Failed);
        VerificationActivity.OutcomeOfCall(AiCallOutcome.TimedOut).Should().Be(VerificationActivityOutcome.Failed);
    }

    [Fact]
    public void The_consistency_check_belongs_to_Answers_and_a_connection_test_to_no_group()
    {
        VerificationActivity.GroupOfCall(AiUsageChecks.ConsistencyExtraction).Should().Be(CheckGroup.Answers);
        VerificationActivity.GroupOfCall(AiUsageChecks.ConsistencyComparison).Should().Be(CheckGroup.Answers);
        VerificationActivity.GroupOfCall(AiUsageChecks.ModelTest).Should().BeNull();
    }

    [Fact]
    public void Every_analyzer_is_in_exactly_one_group_the_log_can_filter_by()
    {
        var all = Enum.GetValues<CheckGroup>().SelectMany(VerificationActivity.AnalyzersOf).ToList();

        all.Should().BeEquivalentTo(CheckGroups.ByAnalyzer.Keys);
        VerificationActivity.AnalyzersOf(CheckGroup.FormUse).Should().BeEquivalentTo("automation", "behavioural");
    }

    [Fact]
    public void A_cursor_round_trips_its_time_and_id()
    {
        var cursor = new VerificationActivity.Cursor(new DateTimeOffset(2026, 11, 9, 10, 30, 0, 123, TimeSpan.Zero).AddTicks(4567), Guid.CreateVersion7());

        VerificationActivity.Cursor.TryParse(cursor.Encode(), out var parsed).Should().BeTrue();

        parsed.Should().Be(cursor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("123")]
    [InlineData("-5.00000000000000000000000000000001")]
    [InlineData("123.not-a-guid")]
    [InlineData("123.00000000000000000000000000000000")]
    [InlineData("99999999999999999999.00000000000000000000000000000001")]
    [InlineData("1.2.3")]
    public void A_cursor_that_is_not_one_a_page_gave_is_refused(string? text)
    {
        VerificationActivity.Cursor.TryParse(text, out _).Should().BeFalse();
    }

    private sealed record Item(string Name, DateTimeOffset At, Guid Id);

    private static readonly DateTimeOffset Noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Two_sources_merge_newest_first_and_a_page_keeps_only_its_limit()
    {
        var runs = new[] { new Item("run-1", Noon.AddMinutes(-1), Guid.CreateVersion7()), new Item("run-2", Noon.AddMinutes(-5), Guid.CreateVersion7()) };
        var calls = new[] { new Item("call-1", Noon.AddMinutes(-3), Guid.CreateVersion7()), new Item("call-2", Noon.AddMinutes(-9), Guid.CreateVersion7()) };

        var (items, more) = VerificationActivity.MergePage(runs, calls, 3, i => i.At, i => i.Id);

        items.Select(i => i.Name).Should().Equal("run-1", "call-1", "run-2");
        more.Should().BeTrue();
    }

    [Fact]
    public void A_page_that_holds_everything_says_there_is_no_more()
    {
        var (items, more) = VerificationActivity.MergePage([new Item("only", Noon, Guid.CreateVersion7())], [], 5, i => i.At, i => i.Id);

        items.Should().HaveCount(1);
        more.Should().BeFalse();
    }

    [Fact]
    public void Entries_at_the_same_instant_order_by_id_as_the_database_orders_uuids()
    {
        var low = Guid.Parse("00000000-0000-7000-8000-000000000001");
        var high = Guid.Parse("ffffffff-0000-7000-8000-000000000001");

        var (items, _) = VerificationActivity.MergePage([new Item("low", Noon, low)], [new Item("high", Noon, high)], 2, i => i.At, i => i.Id);

        items.Select(i => i.Name).Should().Equal("high", "low");
        VerificationActivity.CompareIds(low, high).Should().BeNegative();
    }
}
