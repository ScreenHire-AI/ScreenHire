using FluentValidation;
using ScreenHire.Domain.Verification;
using ScreenHire.Domain.Views;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// What a view filters and how it sorts. <c>baseView</c> is a built-in view this one narrows; <c>jobIds</c> and
/// <c>stages</c> pick jobs and stages (stage names on the Candidates page, stage ids on a job's table); <c>authenticity</c>
/// is an authenticity level name or <c>Pending</c>; <c>aiAssistance</c> an AI assistance level name; <c>sortColumn</c> the
/// sorted column, or none for the table's own order. Search text is not saved.
/// </summary>
public sealed record ViewFilters(
    string? BaseView, IReadOnlyList<Guid> JobIds, IReadOnlyList<string> Stages, string? Authenticity, string? AiAssistance, string? Tag,
    string? SortColumn, bool SortDescending)
{
    internal ViewDefinition ToDefinition() => new()
    {
        BaseView = BaseView,
        JobIds = [.. JobIds.Distinct()],
        Stages = [.. Stages.Distinct()],
        Authenticity = Authenticity,
        AiAssistance = AiAssistance,
        Tag = Tag,
        SortColumn = SortColumn,
        SortDescending = SortDescending,
    };

    internal static ViewFilters From(ViewDefinition definition) => new(
        definition.BaseView, definition.JobIds, definition.Stages, definition.Authenticity, definition.AiAssistance, definition.Tag,
        definition.SortColumn, definition.SortDescending);
}

/// <summary>
/// A saved view as the signed-in member sees it: their own, and the team's shared ones. <c>mine</c> is whether they saved
/// it; <c>canEdit</c> whether they may rename, share or delete it (its owner, or a role that edits anyone's views).
/// </summary>
public sealed record SavedViewSummary(
    Guid Id, string Name, ViewScope Scope, Guid? JobId, bool Shared, bool Mine, bool CanEdit, ViewFilters Filters, DateTimeOffset UpdatedAt)
{
    internal static SavedViewSummary From(SavedFilterView view, Guid? userId, bool canEdit) =>
        new(view.Id, view.Name, view.Scope, view.JobId, view.IsShared, view.OwnerUserId == userId, canEdit, ViewFilters.From(view.Definition), view.UpdatedAt);
}

/// <summary>The views the member sees, oldest first, and the ids of the built-in views they hid.</summary>
public sealed record ViewList(IReadOnlyList<SavedViewSummary> Views, IReadOnlyList<string> HiddenBuiltIns);

/// <summary>Saves the current filters and sort as a view; a job view names its job, a candidates view takes none.</summary>
public sealed record CreateViewRequest(string Name, ViewScope Scope, Guid? JobId, bool Shared, ViewFilters Filters);

/// <summary>Renames a view, shares it or makes it private; a member left out stays as it is.</summary>
public sealed record UpdateViewRequest(string? Name, bool? Shared);

/// <summary>Replaces the built-in views the member hides; each id once.</summary>
public sealed record UpdateHiddenViewsRequest(IReadOnlyList<string> HiddenBuiltIns);

internal sealed class CreateViewRequestValidator : AbstractValidator<CreateViewRequest>
{
    public const int MaxListed = 50;

    public CreateViewRequestValidator()
    {
        RuleFor(r => r.Name).ViewName();
        RuleFor(r => r.Scope).IsInEnum();
        RuleFor(r => r.JobId).NotEmpty().When(r => r.Scope == ViewScope.Job).WithMessage("Name the job this view is for.");
        RuleFor(r => r.JobId).Null().When(r => r.Scope == ViewScope.Candidates).WithMessage("A candidates view is not for one job.");
        RuleFor(r => r.Filters).NotNull().SetValidator(new ViewFiltersValidator());
    }
}

internal sealed class UpdateViewRequestValidator : AbstractValidator<UpdateViewRequest>
{
    public UpdateViewRequestValidator()
    {
        RuleFor(r => r.Name!).ViewName().When(r => r.Name is not null);
        RuleFor(r => r).Must(r => r.Name is not null || r.Shared is not null).WithMessage("Change the name or whether it is shared.");
    }
}

internal sealed class UpdateHiddenViewsRequestValidator : AbstractValidator<UpdateHiddenViewsRequest>
{
    public UpdateHiddenViewsRequestValidator()
    {
        RuleFor(r => r.HiddenBuiltIns).NotNull();
        RuleForEach(r => r.HiddenBuiltIns).Must(BuiltInViews.Exists).WithMessage("There is no built-in view with this id.");
        RuleFor(r => r.HiddenBuiltIns).Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Name each view once.");
    }
}

internal sealed class ViewFiltersValidator : AbstractValidator<ViewFilters>
{
    public ViewFiltersValidator()
    {
        RuleFor(f => f.BaseView).Must(id => id is null || BuiltInViews.Exists(id)).WithMessage("There is no built-in view with this id.");
        RuleFor(f => f.JobIds).NotNull().Must(ids => ids.Count <= CreateViewRequestValidator.MaxListed).WithMessage("Pick at most 50 jobs.");
        RuleFor(f => f.Stages).NotNull().Must(stages => stages.Count <= CreateViewRequestValidator.MaxListed).WithMessage("Pick at most 50 stages.");
        RuleForEach(f => f.Stages).NotEmpty().MaximumLength(100);
        RuleFor(f => f.Authenticity)
            .Must(value => value is null || value == "Pending" || Enum.GetNames<AuthenticityLevel>().Contains(value))
            .WithMessage("Unknown authenticity level.");
        RuleFor(f => f.AiAssistance)
            .Must(value => value is null || Enum.GetNames<AiAssistanceLevel>().Contains(value))
            .WithMessage("Unknown AI assistance level.");
        RuleFor(f => f.Tag).MaximumLength(100);
        RuleFor(f => f.SortColumn).MaximumLength(40);
    }
}

internal static class ViewValidationRules
{
    /// <summary>A name people read in the sidebar: not blank, at most <see cref="SavedFilterView.MaxNameLength"/> characters.</summary>
    public static IRuleBuilderOptions<T, string> ViewName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(SavedFilterView.MaxNameLength)
            .Must(name => name is null || name.Trim().Length > 0).WithMessage("Name the view.");
}
