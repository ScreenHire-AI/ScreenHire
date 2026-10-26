using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// A hiring template definition's errors: the validator's, plus every email template (<see cref="EmailTemplateReferences"/>)
/// and library scorecard (<see cref="ScorecardReferences"/>) it names that does not exist. The template routes run it
/// before each save and before publishing.
/// </summary>
internal static class TemplateReferences
{
    public static async Task<IReadOnlyList<DefinitionError>> ValidateAsync(
        ScreenHireDbContext db, HiringTemplateDefinition? definition, CancellationToken cancellationToken) =>
    [
        .. await EmailTemplateReferences.ValidateAsync(db, definition, cancellationToken),
        .. await ScorecardReferences.MissingAsync(db, definition?.Workflow, "workflow", cancellationToken),
    ];
}
