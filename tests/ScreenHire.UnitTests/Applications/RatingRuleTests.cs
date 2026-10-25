using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.UnitTests.Applications;

/// <summary>
/// The candidate's rating (ADR-0013, workflow-engine.md section 8): how each rating answer maps onto 1 to 5, how a
/// scorecard's value and the candidate's mean are made, and what counts for nothing.
/// </summary>
public sealed class RatingRuleTests
{
    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(3, 3.0)]
    [InlineData(5, 5.0)]
    public void A_1_to_5_answer_counts_as_it_is(int answer, double points) =>
        RatingRule.Points(Answer(ScorecardQuestionType.Rating5, rating: answer)).Should().Be((decimal)points);

    // Spread across the five stars: 1 counts as 1 and 10 as 5, so a 1 to 10 scale never falls below one star.
    [Theory]
    [InlineData(1, "1")]
    [InlineData(10, "5")]
    [InlineData(4, "2.3333")]
    [InlineData(7, "3.6667")]
    public void A_1_to_10_answer_is_spread_from_1_to_5(int answer, string points) =>
        Math.Round(RatingRule.Points(Answer(ScorecardQuestionType.Rating10, rating: answer))!.Value, 4).Should().Be(decimal.Parse(points, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void A_rating_yes_counts_5_and_no_counts_1()
    {
        RatingRule.Points(Answer(ScorecardQuestionType.RatingYesNo, "yes")).Should().Be(5);
        RatingRule.Points(Answer(ScorecardQuestionType.RatingYesNo, "no")).Should().Be(1);
    }

    [Theory]
    [InlineData(ScorecardQuestionType.YesNo, "yes", null)]
    [InlineData(ScorecardQuestionType.SingleChoice, "senior", null)]
    [InlineData(ScorecardQuestionType.ShortText, "5", null)]
    [InlineData(ScorecardQuestionType.Rating5, null, null)]
    [InlineData(ScorecardQuestionType.RatingYesNo, null, null)]
    public void Only_an_answered_rating_question_counts(ScorecardQuestionType type, string? value, int? rating) =>
        RatingRule.Points(Answer(type, value, rating)).Should().BeNull();

    [Fact]
    public void A_scorecard_is_the_mean_of_its_answered_rating_questions()
    {
        RatingRule.Of(
        [
            Answer(ScorecardQuestionType.Rating5, rating: 4),
            Answer(ScorecardQuestionType.Rating10, rating: 10),
            Answer(ScorecardQuestionType.RatingYesNo, "no"),
            Answer(ScorecardQuestionType.Rating5),
            Answer(ScorecardQuestionType.YesNo, "yes"),
        ]).Should().Be(10m / 3);
    }

    [Fact]
    public void A_scorecard_with_no_answered_rating_question_counts_for_nothing()
    {
        RatingRule.Of([Answer(ScorecardQuestionType.LongText, "Strong exercise."), Answer(ScorecardQuestionType.Rating5)]).Should().BeNull();
        RatingRule.Of([]).Should().BeNull();
    }

    // Each scorecard counts once: three scorecards of 4.3, 3.7 and 4.3 make 4.1, however many questions each had.
    [Fact]
    public void The_rating_is_the_mean_of_the_scorecards_to_one_decimal()
    {
        var rating = RatingRule.Candidate([4.3m, 3.7m, 4.3m, null]);

        rating.Should().Be(new CandidateRating(4.1m, 3));
    }

    [Fact]
    public void The_rating_rounds_halves_away_from_zero()
    {
        RatingRule.Candidate([4.25m]).Should().Be(new CandidateRating(4.3m, 1));
        RatingRule.Candidate([3m, 4m]).Should().Be(new CandidateRating(3.5m, 2));
        RatingRule.Candidate([1m + (6m * 4 / 9)]).Should().Be(new CandidateRating(3.7m, 1));
    }

    [Fact]
    public void No_counting_scorecard_is_not_rated_yet() =>
        RatingRule.Candidate([null, null]).Should().BeNull();

    // A v0.2 scorecard has labelled ratings and no answers, so it counts for nothing.
    [Fact]
    public void A_v0_2_scorecard_never_counts()
    {
        var old = new ApplicationScorecard
        {
            StageId = "interview",
            Ratings = [new CriterionScore("role_skills", "Skills the role needs", CriterionRating.StronglyDemonstrated)],
        };

        RatingRule.Of(old.Answers).Should().BeNull();
    }

    private static ScorecardAnswer Answer(ScorecardQuestionType type, string? value = null, int? rating = null) =>
        new("q", "Question", type, value, Rating: rating);
}
