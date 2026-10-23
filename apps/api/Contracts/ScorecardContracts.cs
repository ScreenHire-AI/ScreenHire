using FluentValidation;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// One reviewer's scorecard for one stage: a labelled rating per criterion and an overall recommendation. Ratings are
/// categories, never a number, and nothing totals or averages them (ADR-0004).
/// </summary>
/// <remarks>
/// <c>ratings</c>: One per criterion of the stage's scorecard as it was when the reviewer filled it, in its order; a
/// rating of null means the reviewer did not rate that criterion.
/// <c>timelineEventId</c>: The <c>ScorecardSubmitted</c> timeline entry, which raised the ReviewerDecision trigger.
/// </remarks>
public sealed record ScorecardEntry(
    Guid Id,
    string StageId,
    Guid ReviewerUserId,
    IReadOnlyList<ScorecardRating> Ratings,
    Recommendation Recommendation,
    string? Note,
    int WorkflowVersion,
    Guid TimelineEventId,
    DateTimeOffset SubmittedAt);

/// <summary>A criterion as the reviewer was asked it, and their rating; null when they did not rate it.</summary>
public sealed record ScorecardRating(string CriterionId, string Label, CriterionRating? Rating);

/// <summary>
/// A reviewer's scorecard for a stage the application has reached and whose workflow stage has a scorecard. Each
/// reviewer gives one per stage: the same submission again returns the stored one, a different one is a 409.
/// </summary>
/// <remarks>
/// <c>ratings</c>: Ratings by criterion id; a criterion left out is not rated. <c>note</c>: Optional, for the hiring team.
/// </remarks>
public sealed record SubmitScorecardRequest(
    string StageId,
    IReadOnlyList<CriterionRatingInput> Ratings,
    Recommendation Recommendation,
    string? Note = null);

/// <summary>The rating for one criterion of the stage's scorecard.</summary>
public sealed record CriterionRatingInput(string CriterionId, CriterionRating Rating);

internal static class ScorecardMappings
{
    public static ScorecardEntry ToEntry(this ApplicationScorecard scorecard) =>
        new(scorecard.Id, scorecard.StageId, scorecard.ReviewerUserId,
            [.. scorecard.Ratings.Select(r => new ScorecardRating(r.CriterionId, r.Label, r.Rating))], scorecard.Recommendation, scorecard.Note,
            scorecard.WorkflowVersion, scorecard.TimelineEventId, scorecard.SubmittedAt);
}

internal sealed class SubmitScorecardRequestValidator : AbstractValidator<SubmitScorecardRequest>
{
    public SubmitScorecardRequestValidator()
    {
        RuleFor(r => r.StageId).NotEmpty().MaximumLength(40);
        RuleFor(r => r.Recommendation).IsInEnum();
        RuleFor(r => r.Note).MaximumLength(ApplicationScorecard.MaxNoteLength);
        RuleFor(r => r.Ratings).NotNull()
            .Must(ratings => ratings is null || ratings.Count <= DefinitionValidator.MaxCriteria)
            .WithMessage($"A scorecard has at most {DefinitionValidator.MaxCriteria} ratings.")
            .Must(ratings => ratings is null || ratings.Where(r => r is not null).Select(r => r.CriterionId).Distinct(StringComparer.Ordinal).Count() == ratings.Count)
            .WithMessage("Rate each criterion once.");
        RuleForEach(r => r.Ratings).NotNull().ChildRules(rating =>
        {
            rating.RuleFor(r => r.CriterionId).NotEmpty().MaximumLength(40);
            rating.RuleFor(r => r.Rating).IsInEnum();
        });
    }
}
