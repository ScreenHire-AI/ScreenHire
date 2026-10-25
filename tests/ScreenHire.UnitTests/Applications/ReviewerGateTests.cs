using ScreenHire.Domain.Applications;

namespace ScreenHire.UnitTests.Applications;

/// <summary>
/// Whether a reviewer still has their own scorecard to give, so the rating and the other scorecards are held back from
/// them (ADR-0013: a reviewer sees the rating only after submitting their own).
/// </summary>
public sealed class ReviewerGateTests
{
    private static readonly DateTimeOffset Assigned = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Nothing_is_held_back_without_an_open_review() =>
        ReviewerGate.Withholds([], []).Should().BeFalse();

    [Fact]
    public void An_open_review_holds_the_rating_back_until_its_stage_is_scored()
    {
        OpenReview[] review = [new("interview", Assigned)];

        ReviewerGate.Withholds(review, []).Should().BeTrue();
        ReviewerGate.Withholds(review, [new GivenScorecard("interview", Assigned.AddHours(1))]).Should().BeFalse();
    }

    [Fact]
    public void An_earlier_stage_s_scorecard_does_not_answer_a_new_review()
    {
        OpenReview[] review = [new("interview", Assigned)];

        ReviewerGate.Withholds(review, [new GivenScorecard("screening", Assigned.AddDays(-2))]).Should().BeTrue();
    }

    // A reminder after the reviewer already scored the stage holds nothing back.
    [Fact]
    public void A_review_of_a_stage_already_scored_is_answered()
    {
        ReviewerGate.Withholds([new OpenReview("interview", Assigned)], [new GivenScorecard("interview", Assigned.AddDays(-1))]).Should().BeFalse();
    }

    // A review asked for in a stage with no scorecard (Applied) is answered by the reviewer's next scorecard anywhere.
    [Fact]
    public void A_review_in_a_stage_without_a_scorecard_is_answered_by_the_next_scorecard()
    {
        OpenReview[] review = [new("applied", Assigned)];

        ReviewerGate.Withholds(review, [new GivenScorecard("screening", Assigned.AddHours(2))]).Should().BeFalse();
        ReviewerGate.Withholds([new OpenReview(null, Assigned)], [new GivenScorecard("screening", Assigned.AddHours(2))]).Should().BeFalse();
    }

    [Fact]
    public void Every_open_review_must_be_answered()
    {
        OpenReview[] reviews = [new("screening", Assigned), new("interview", Assigned.AddDays(3))];

        ReviewerGate.Withholds(reviews, [new GivenScorecard("screening", Assigned.AddHours(1))]).Should().BeTrue();
    }
}
