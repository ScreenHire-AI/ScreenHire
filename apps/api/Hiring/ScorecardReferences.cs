using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// Where workflows name library scorecards (workflow-engine.md section 8). <see cref="DefinitionValidator"/> checks only
/// that a scorecard id is well formed, because it cannot see the organization's library; saving a hiring template gets
/// this check too, so a stage never names a scorecard that does not exist, and a job copies the questions of the ones
/// its stages name when it takes a workflow.
/// </summary>
internal static class ScorecardReferences
{
    /// <summary>
    /// The organization's own scorecards among <paramref name="keys"/>, by key. Built-ins are not here: the domain copies
    /// them itself (<see cref="HiringWorkflow.WithScorecardCopies"/>). Read through the tenant filter.
    /// </summary>
    public static async Task<Dictionary<string, Scorecard>> OwnAsync(ScreenHireDbContext db, IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        var ids = keys
            .Where(k => BuiltInScorecards.Find(k) is null)
            .Select(k => Guid.TryParseExact(k, "D", out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return new(StringComparer.Ordinal);
        }

        var rows = await db.ScorecardTemplates.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken);
        return rows.ToDictionary(t => t.Key, t => t.ToScorecard(), StringComparer.Ordinal);
    }

    /// <summary>Errors for each stage of <paramref name="workflow"/> that names a scorecard the library does not have.</summary>
    public static async Task<IReadOnlyList<DefinitionError>> MissingAsync(
        ScreenHireDbContext db, HiringWorkflow? workflow, string prefix, CancellationToken cancellationToken)
    {
        if (workflow?.Stages is not { } stages || workflow.ScorecardTemplateIds() is not { Count: > 0 } keys)
        {
            return [];
        }

        var own = await OwnAsync(db, keys, cancellationToken);
        var errors = new List<DefinitionError>();
        for (var i = 0; i < stages.Count; i++)
        {
            if (stages[i]?.ScorecardTemplateId is { } key && BuiltInScorecards.IsWellFormed(key)
                && BuiltInScorecards.Find(key) is null && !own.ContainsKey(key))
            {
                errors.Add(new($"{Path(prefix)}stages[{i}].scorecardTemplateId", "This scorecard is not in the library any more. Choose another."));
            }
        }

        return errors;
    }

    /// <summary>
    /// A job's workflow with a copy of each scorecard its stages name and do not hold yet, or the errors for the stages
    /// whose scorecard the library does not have.
    /// </summary>
    public static async Task<(HiringWorkflow Workflow, IReadOnlyList<DefinitionError> Errors)> CopyAsync(
        ScreenHireDbContext db, HiringWorkflow workflow, string prefix, CancellationToken cancellationToken)
    {
        var own = await OwnAsync(db, workflow.ScorecardTemplateIds(), cancellationToken);
        var copied = workflow.WithScorecardCopies(own, out var missing);
        return (copied, [.. missing.Select(i => new DefinitionError(
            $"{Path(prefix)}stages[{i}].scorecardTemplateId", "This scorecard is not in the library any more. Choose another."))]);
    }

    private static string Path(string prefix) => prefix.Length == 0 ? "" : $"{prefix}.";
}
