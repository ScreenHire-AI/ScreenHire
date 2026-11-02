using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// A hiring template definition's errors: the validator's, plus every email template (<see cref="EmailTemplateReferences"/>),
/// library scorecard (<see cref="ScorecardReferences"/>) and interviewer (<see cref="InterviewerReferences"/>) it names that
/// does not exist. The template routes run it before each save and before publishing.
/// </summary>
internal static class TemplateReferences
{
    public static async Task<IReadOnlyList<DefinitionError>> ValidateAsync(
        ScreenHireDbContext db, HiringTemplateDefinition? definition, CancellationToken cancellationToken) =>
    [
        .. await EmailTemplateReferences.ValidateAsync(db, definition, cancellationToken),
        .. await ScorecardReferences.MissingAsync(db, definition?.Workflow, "workflow", cancellationToken),
        .. await InterviewerReferences.MissingAsync(db, definition?.Workflow, "workflow", cancellationToken),
    ];

    /// <summary>
    /// A job's workflow: the validator's errors, plus the email templates and interviewers it names that do not exist.
    /// The job's stages hold their own scorecard copies, so there are no library scorecards to check.
    /// </summary>
    public static async Task<IReadOnlyList<DefinitionError>> ValidateAsync(
        ScreenHireDbContext db, HiringWorkflow? workflow, CancellationToken cancellationToken) =>
    [
        .. await EmailTemplateReferences.ValidateAsync(db, workflow, cancellationToken),
        .. await InterviewerReferences.MissingAsync(db, workflow, "", cancellationToken),
    ];
}
