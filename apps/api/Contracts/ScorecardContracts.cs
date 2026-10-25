using System.Text.Json.Serialization;
using FluentValidation;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// One reviewer's scorecard for one stage: an answer to each question as it was asked, and an overall recommendation.
/// Its rating answers make the candidate's rating (<c>ApplicationRating</c>, ADR-0013); nothing else on it does.
/// </summary>
/// <remarks>
/// <c>answers</c>: One per question of the stage's scorecard as it was when the reviewer filled it, in its order; an
/// answer with no value means the reviewer left it unanswered.
/// <c>rating</c>: This scorecard's part of the candidate's rating, from 1 to 5 to one decimal: the mean of its answered
/// rating questions. Null when it has none, and on a v0.2 scorecard.
/// <c>ratings</c>: A v0.2 scorecard's labelled ratings, one per criterion; empty on a scorecard with answers. Those
/// labels were never numbers, so a v0.2 scorecard never counts toward the rating.
/// <c>timelineEventId</c>: The <c>ScorecardSubmitted</c> timeline entry, which raised the ReviewerDecision trigger.
/// </remarks>
public sealed record ScorecardEntry(
    Guid Id,
    string StageId,
    Guid ReviewerUserId,
    IReadOnlyList<ScorecardAnswerEntry> Answers,
    decimal? Rating,
    IReadOnlyList<ScorecardRating> Ratings,
    Recommendation Recommendation,
    string? Note,
    int WorkflowVersion,
    Guid TimelineEventId,
    DateTimeOffset SubmittedAt);

/// <summary>
/// A question as the reviewer was asked it, and their answer. <c>value</c> is "yes" or "no" for the yes-or-no kinds, an
/// option id for a choice and the text for the text kinds; <c>values</c> are a multi-choice answer's option ids;
/// <c>rating</c> is the number picked on a 1 to 5 or 1 to 10 scale. All empty means it was not answered.
/// </summary>
/// <remarks><c>options</c>: A choice question's options as they were, so the answer keeps its words.</remarks>
public sealed record ScorecardAnswerEntry(
    string QuestionId,
    string Label,
    ScorecardQuestionType Type,
    string? Value,
    IReadOnlyList<string>? Values,
    int? Rating,
    IReadOnlyList<QuestionOption>? Options);

/// <summary>A v0.2 criterion as the reviewer was asked it, and their labelled rating; null when they did not rate it.</summary>
public sealed record ScorecardRating(string CriterionId, string Label, CriterionRating? Rating);

/// <summary>
/// A reviewer's scorecard for a stage the application has reached and whose workflow stage has a scorecard. Each
/// reviewer gives one per stage: the same submission again returns the stored one, a different one is a 409.
/// </summary>
/// <remarks>
/// <c>answers</c>: Answers by question; a question left out is unanswered, which a required question may not be.
/// <c>note</c>: Optional, for the hiring team.
/// </remarks>
// The recommendation is JsonRequired: left out, it would bind to its first member (StrongNo) and store a judgement
// nobody gave, in a scorecard that cannot be changed.
public sealed record SubmitScorecardRequest(
    string StageId,
    IReadOnlyList<ScorecardAnswerInput> Answers,
    [property: JsonRequired] Recommendation Recommendation,
    string? Note = null);

/// <summary>
/// The answer to one question: <c>value</c> for the yes-or-no kinds ("yes" or "no"), a choice (an option id) and the
/// text kinds, <c>values</c> for a multi-choice (option ids) and <c>rating</c> for a number scale.
/// </summary>
public sealed record ScorecardAnswerInput(string QuestionId, string? Value = null, IReadOnlyList<string>? Values = null, int? Rating = null);

internal static class ScorecardMappings
{
    public static ScorecardEntry ToEntry(this ApplicationScorecard scorecard) =>
        new(scorecard.Id, scorecard.StageId, scorecard.ReviewerUserId,
            [.. scorecard.Answers.Select(a => new ScorecardAnswerEntry(a.QuestionId, a.Label, a.Type, a.Value, a.Values, a.Rating, a.Options))],
            OwnRating(scorecard),
            [.. scorecard.Ratings.Select(r => new ScorecardRating(r.CriterionId, r.Label, r.Rating))], scorecard.Recommendation, scorecard.Note,
            scorecard.WorkflowVersion, scorecard.TimelineEventId, scorecard.SubmittedAt);

    public static Dictionary<string, ScorecardAnswerValue> ToAnswers(this SubmitScorecardRequest request) =>
        request.Answers.ToDictionary(a => a.QuestionId, a => new ScorecardAnswerValue(a.Value, a.Values, a.Rating), StringComparer.Ordinal);

    private static decimal? OwnRating(ApplicationScorecard scorecard) =>
        RatingRule.Of(scorecard.Answers) is { } value ? RatingRule.Round(value) : null;
}

internal sealed class SubmitScorecardRequestValidator : AbstractValidator<SubmitScorecardRequest>
{
    public SubmitScorecardRequestValidator()
    {
        RuleFor(r => r.StageId).NotEmpty().MaximumLength(40);
        RuleFor(r => r.Recommendation).IsInEnum();
        RuleFor(r => r.Note).MaximumLength(ApplicationScorecard.MaxNoteLength);
        RuleFor(r => r.Answers).NotNull()
            .Must(answers => answers is null || answers.Count <= DefinitionValidator.MaxScorecardQuestions)
            .WithMessage($"A scorecard has at most {DefinitionValidator.MaxScorecardQuestions} answers.")
            .Must(answers => answers is null || answers.Where(a => a is not null).Select(a => a.QuestionId).Distinct(StringComparer.Ordinal).Count() == answers.Count)
            .WithMessage("Answer each question once.");
        RuleForEach(r => r.Answers).NotNull().ChildRules(answer =>
        {
            answer.RuleFor(a => a.QuestionId).NotEmpty().MaximumLength(40);
            answer.RuleFor(a => a.Value).MaximumLength(ScorecardAnswers.MaxLongText);
            answer.RuleFor(a => a.Values).Must(values => values is null || values.Count <= DefinitionValidator.MaxOptions)
                .WithMessage($"At most {DefinitionValidator.MaxOptions} options.");
        });
    }
}
