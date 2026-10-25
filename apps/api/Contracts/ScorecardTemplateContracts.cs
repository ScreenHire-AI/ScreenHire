using FluentValidation;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence.History;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// A scorecard in the organization's library: a built-in (<c>builtIn</c>, ScreenHire's and read only) or one of its own.
/// A hiring template's stage names it by <c>id</c> (<c>scorecardTemplateId</c>); a job keeps its own copy.
/// </summary>
/// <remarks>
/// <c>id</c>: A built-in's id, such as <c>screening</c>, or an own scorecard's uuid. <c>questions</c> and
/// <c>ratingQuestions</c> are counts; the rating questions are the ones the candidate's rating averages. <c>archived</c>:
/// the organization took its own scorecard out of the pickers; stages that name it keep it.
/// </remarks>
public sealed record ScorecardTemplateSummary(
    string Id, string Name, bool BuiltIn, int Questions, int RatingQuestions, LastUpdate LastUpdated, bool Archived);

/// <summary>One library scorecard with its questions. Every scorecard then asks for an overall recommendation and a note.</summary>
/// <remarks><c>copiedFrom</c>: The library scorecard this one was copied from, by id, if any.</remarks>
public sealed record ScorecardTemplateDetail(
    string Id, string Name, bool BuiltIn, string? CopiedFrom, IReadOnlyList<ScorecardQuestion> Questions, LastUpdate LastUpdated,
    bool Archived);

/// <summary>A new scorecard of the organization's own, optionally a copy of another (<c>copiedFrom</c>, by id).</summary>
public sealed record CreateScorecardTemplateRequest(string Name, IReadOnlyList<ScorecardQuestion> Questions, string? CopiedFrom = null);

/// <summary>An own scorecard's name and questions, replaced whole. Jobs that copied it keep their copy.</summary>
public sealed record UpdateScorecardTemplateRequest(string Name, IReadOnlyList<ScorecardQuestion> Questions);

internal static class ScorecardTemplateMappings
{
    public static ScorecardTemplateSummary ToSummary(this BuiltInScorecard scorecard) =>
        new(scorecard.Id, scorecard.Name, true, scorecard.Questions.Count, RatingQuestions(scorecard.Questions), LastUpdate.ByScreenHire(null), false);

    public static ScorecardTemplateSummary ToSummary(this ScorecardTemplate scorecard, ChangeStamp? lastChange) =>
        new(scorecard.Key, scorecard.Name, false, scorecard.Questions.Count, RatingQuestions(scorecard.Questions),
            LastUpdate.From(lastChange, scorecard.UpdatedAt), scorecard.ArchivedAt is not null);

    public static ScorecardTemplateDetail ToDetail(this BuiltInScorecard scorecard) =>
        new(scorecard.Id, scorecard.Name, true, null, scorecard.Questions, LastUpdate.ByScreenHire(null), false);

    public static ScorecardTemplateDetail ToDetail(this ScorecardTemplate scorecard, ChangeStamp? lastChange) =>
        new(scorecard.Key, scorecard.Name, false, scorecard.CopiedFrom, scorecard.Questions, LastUpdate.From(lastChange, scorecard.UpdatedAt),
            scorecard.ArchivedAt is not null);

    private static int RatingQuestions(IReadOnlyList<ScorecardQuestion> questions) =>
        questions.Count(q => q.Type.IsRating());
}

internal sealed class CreateScorecardTemplateRequestValidator : AbstractValidator<CreateScorecardTemplateRequest>
{
    public CreateScorecardTemplateRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(ScorecardTemplate.MaxNameLength).Must(n => n is null || n.Trim().Length > 0)
            .WithMessage("Name the scorecard.");
        RuleFor(r => r.CopiedFrom).Must(BuiltInScorecards.IsWellFormed).When(r => r.CopiedFrom is not null)
            .WithMessage("Unknown scorecard to copy from.");
        RuleFor(r => r.Questions).Custom((questions, context) =>
        {
            foreach (var error in DefinitionValidator.ValidateScorecardQuestions(questions, "questions"))
            {
                context.AddFailure(error.Path, error.Message);
            }
        });
    }
}

internal sealed class UpdateScorecardTemplateRequestValidator : AbstractValidator<UpdateScorecardTemplateRequest>
{
    public UpdateScorecardTemplateRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(ScorecardTemplate.MaxNameLength).Must(n => n is null || n.Trim().Length > 0)
            .WithMessage("Name the scorecard.");
        RuleFor(r => r.Questions).Custom((questions, context) =>
        {
            foreach (var error in DefinitionValidator.ValidateScorecardQuestions(questions, "questions"))
            {
                context.AddFailure(error.Path, error.Message);
            }
        });
    }
}
