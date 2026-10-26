using ScreenHire.Domain.Hiring;

namespace ScreenHire.Domain.Applications;

/// <summary>A problem with one answer of a scorecard submission, by question id.</summary>
public sealed record ScorecardAnswerError(string QuestionId, string Message);

/// <summary>
/// Checks a reviewer's answers against the questions of a job's scorecard and turns them into the answers a submitted
/// scorecard keeps (<see cref="ApplicationScorecard.Answers"/>). Each kind takes one shape of value: "yes" or "no" for the
/// yes-or-no kinds, an option id for a choice, option ids for a multi-choice, text for the text kinds and a number in
/// range for a number scale. A required question must be answered; any other may be left out.
/// </summary>
public static class ScorecardAnswers
{
    public const int MaxShortText = 500;

    public const int MaxLongText = 10_000;

    /// <summary>The problems with <paramref name="answers"/>, keyed by question id, for <paramref name="scorecard"/>.</summary>
    public static IReadOnlyList<ScorecardAnswerError> Check(Scorecard scorecard, IReadOnlyDictionary<string, ScorecardAnswerValue> answers)
    {
        ArgumentNullException.ThrowIfNull(scorecard);
        ArgumentNullException.ThrowIfNull(answers);
        var errors = new List<ScorecardAnswerError>();
        var questions = scorecard.Questions.ToDictionary(q => q.Id, StringComparer.Ordinal);
        foreach (var id in answers.Keys.Where(id => !questions.ContainsKey(id)))
        {
            errors.Add(new(id, "The scorecard has no question with this id."));
        }

        foreach (var question in scorecard.Questions)
        {
            var given = Tidy(answers.GetValueOrDefault(question.Id));
            if (Problem(question, given) is { } problem)
            {
                errors.Add(new(question.Id, problem));
            }
        }

        return errors;
    }

    /// <summary>One answer per question of <paramref name="scorecard"/>, in order, from answers that passed <see cref="Check"/>.</summary>
    public static IReadOnlyList<ScorecardAnswer> Keep(Scorecard scorecard, IReadOnlyDictionary<string, ScorecardAnswerValue> answers)
    {
        ArgumentNullException.ThrowIfNull(scorecard);
        ArgumentNullException.ThrowIfNull(answers);
        return
        [
            .. scorecard.Questions.Select(q =>
            {
                var given = Tidy(answers.GetValueOrDefault(q.Id));
                return new ScorecardAnswer(q.Id, q.Label, q.Type, given.Value, given.Values, given.Rating,
                    q.Type is ScorecardQuestionType.SingleChoice or ScorecardQuestionType.MultiChoice ? q.Options : null);
            }),
        ];
    }

    /// <summary>
    /// An answer as it is kept: text trimmed, a blank value or an empty list treated as no answer, so a retry with the
    /// same meaning compares equal.
    /// </summary>
    public static ScorecardAnswerValue Tidy(ScorecardAnswerValue? given) =>
        given is null
            ? ScorecardAnswerValue.None
            : new(
                string.IsNullOrWhiteSpace(given.Value) ? null : given.Value.Trim(),
                given.Values is { Count: > 0 } values ? values : null,
                given.Rating);

    private static string? Problem(ScorecardQuestion question, ScorecardAnswerValue given)
    {
        var answered = given.Value is not null || given.Values is not null || given.Rating is not null;
        if (!answered)
        {
            return question.Required ? "Answer this question." : null;
        }

        var options = question.Options?.Select(o => o.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        return question.Type switch
        {
            ScorecardQuestionType.YesNo or ScorecardQuestionType.RatingYesNo =>
                given is { Value: "yes" or "no", Values: null, Rating: null } ? null : "Answer yes or no.",
            ScorecardQuestionType.SingleChoice =>
                given is { Value: { } option, Values: null, Rating: null } && options.Contains(option) ? null : "Pick one of the question's options.",
            ScorecardQuestionType.MultiChoice =>
                given is { Value: null, Rating: null, Values: { } picked }
                && picked.All(p => p is not null && options.Contains(p)) && picked.Distinct(StringComparer.Ordinal).Count() == picked.Count
                    ? null
                    : "Pick any of the question's options, each once.",
            ScorecardQuestionType.ShortText or ScorecardQuestionType.LongText =>
                given is { Value: { } text, Values: null, Rating: null }
                    ? text.Length <= Limit(question.Type) ? null : $"At most {Limit(question.Type)} characters."
                    : "Answer in words.",
            ScorecardQuestionType.Rating5 =>
                given is { Rating: >= 1 and <= 5, Value: null, Values: null } ? null : "Pick a number from 1 to 5.",
            ScorecardQuestionType.Rating10 =>
                given is { Rating: >= 1 and <= 10, Value: null, Values: null } ? null : "Pick a number from 1 to 10.",
            _ => "Unknown answer kind.",
        };
    }

    private static int Limit(ScorecardQuestionType type) => type == ScorecardQuestionType.ShortText ? MaxShortText : MaxLongText;
}
