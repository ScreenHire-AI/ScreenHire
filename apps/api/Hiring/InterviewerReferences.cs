using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// The interviewers a ScheduleInterview action names. <see cref="DefinitionValidator"/> checks only their number and that
/// none repeats, because it has no database; a person saving a workflow or publishing a template gets this check too, so
/// an automation never names someone outside the organization. Templates are organization-wide, so this checks
/// membership only. Who sees the job and who has a calendar is checked where the action runs.
/// </summary>
internal static class InterviewerReferences
{
    /// <summary>One error per ScheduleInterview action that names a user who is not a member of the current organization.</summary>
    public static async Task<IReadOnlyList<DefinitionError>> MissingAsync(
        ScreenHireDbContext db, HiringWorkflow? workflow, string prefix, CancellationToken cancellationToken)
    {
        var references = In(workflow, prefix).ToList();
        if (references.Count == 0)
        {
            return [];
        }

        var named = references.SelectMany(r => r.UserIds).Distinct().ToList();
        // Narrowed to the tenant: the members filter also shows the user's own memberships in other organizations.
        var members = (await db.OrganizationMembers.AsNoTracking()
                .Where(m => m.OrganizationId == db.Tenant.OrganizationId && named.Contains(m.UserId))
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        return
        [
            .. references.Where(r => r.UserIds.Any(id => !members.Contains(id)))
                .Select(r => new DefinitionError(r.Path, "Choose members of this organization as interviewers.")),
        ];
    }

    // Every ScheduleInterview action with its interviewers, with its path in the validator's form.
    private static IEnumerable<(string Path, IReadOnlyList<Guid> UserIds)> In(HiringWorkflow? workflow, string prefix)
    {
        var automations = workflow?.Automations ?? [];
        var start = prefix.Length == 0 ? "automations" : $"{prefix}.automations";
        for (var i = 0; i < automations.Count; i++)
        {
            var actions = automations[i]?.Actions ?? [];
            for (var j = 0; j < actions.Count; j++)
            {
                if (actions[j] is { Type: ActionType.ScheduleInterview, InterviewerUserIds: { Count: > 0 } ids })
                {
                    yield return ($"{start}[{i}].actions[{j}].interviewerUserIds", ids);
                }
            }
        }
    }
}
