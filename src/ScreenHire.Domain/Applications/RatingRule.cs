using ScreenHire.Domain.Hiring;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// The candidate's rating: one to five stars made from people's scorecards (ADR-0013 decision 13, workflow-engine.md
/// section 8). It reads rating answers and nothing else: never integrity results, AI signals or the stage, which is a
/// decision already made. It informs people, who decide; no automation reads it (<see cref="FactSchema.IsRating"/>), and
/// the API leaves it out for a reviewer who still has their own scorecard to give (<see cref="ReviewerGate"/>).
/// </summary>
/// <remarks>
/// Each rating answer maps onto 1 to 5: a 1 to 5 answer as it is, a 1 to 10 answer spread so that 1 counts as 1 and 10
/// as 5, a rating Yes as 5 and No as 1. Each scorecard counts once, as the mean of its answered rating questions; the
/// rating is the mean of those, to one decimal. A scorecard with no answered rating question, and a v0.2 scorecard,
/// counts for nothing. Nothing derived is stored, so the rule lives here only.
/// </remarks>
public static class RatingRule
{
    /// <summary>A rating answer on the 1 to 5 scale, or null for an answer that is not a rating or is unanswered.</summary>
    public static decimal? Points(ScorecardAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return answer switch
        {
            { Type: ScorecardQuestionType.Rating5, Rating: { } five and >= 1 and <= 5 } => five,
            { Type: ScorecardQuestionType.Rating10, Rating: { } ten and >= 1 and <= 10 } => 1 + ((ten - 1) * 4m / 9),
            { Type: ScorecardQuestionType.RatingYesNo, Value: "yes" } => 5,
            { Type: ScorecardQuestionType.RatingYesNo, Value: "no" } => 1,
            _ => null,
        };
    }

    /// <summary>One scorecard's value: the mean of its answered rating questions, or null when it has none.</summary>
    public static decimal? Of(IEnumerable<ScorecardAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var points = answers.Select(Points).OfType<decimal>().ToList();
        return points.Count == 0 ? null : points.Average();
    }

    /// <summary>
    /// The candidate's rating from each scorecard's value (<see cref="Of"/>): their mean to one decimal and how many
    /// counted, or null when none did.
    /// </summary>
    public static CandidateRating? Candidate(IEnumerable<decimal?> scorecards)
    {
        ArgumentNullException.ThrowIfNull(scorecards);
        var values = scorecards.OfType<decimal>().ToList();
        return values.Count == 0 ? null : new(Round(values.Average()), values.Count);
    }

    /// <summary>A value as it is shown: one decimal, halves away from zero.</summary>
    public static decimal Round(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}

/// <summary>The candidate's rating: from 1 to 5, to one decimal, and the number of scorecards it comes from.</summary>
public sealed record CandidateRating(decimal Value, int Scorecards);
