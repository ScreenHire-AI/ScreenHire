using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// Where workflows name the organization's own email templates. <see cref="DefinitionValidator"/> checks only that an
/// id is well formed, because it also runs in the worker, where it cannot know the organization's templates; a person
/// saving a workflow gets this check too, so an automation never names a template that does not exist. Deleting a
/// template that an automation names is refused (<see cref="UsesAsync"/>).
/// </summary>
internal static class EmailTemplateReferences
{
    /// <summary>The definition's structural errors plus any organization template it names that does not exist.</summary>
    public static async Task<IReadOnlyList<DefinitionError>> ValidateAsync(
        ScreenHireDbContext db, HiringTemplateDefinition? definition, CancellationToken cancellationToken) =>
        [.. DefinitionValidator.Validate(definition), .. await MissingAsync(db, definition?.Workflow, "workflow", cancellationToken)];

    /// <summary>The workflow's structural errors plus any organization template it names that does not exist.</summary>
    public static async Task<IReadOnlyList<DefinitionError>> ValidateAsync(
        ScreenHireDbContext db, HiringWorkflow? workflow, CancellationToken cancellationToken) =>
        [.. DefinitionValidator.Validate(workflow), .. await MissingAsync(db, workflow, "", cancellationToken)];

    /// <summary>
    /// The open or draft jobs and the hiring templates whose automations name <paramref name="templateId"/>, by name.
    /// A closed job runs no automations, so it does not count.
    /// </summary>
    public static async Task<IReadOnlyList<string>> UsesAsync(ScreenHireDbContext db, string templateId, CancellationToken cancellationToken)
    {
        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => j.Status != JobStatus.Closed)
            .Select(j => new { j.Title, j.Workflow })
            .ToListAsync(cancellationToken);
        var templates = await db.HiringTemplateVersions.AsNoTracking()
            .Where(v => v.OrganizationId != null)
            .Join(db.HiringTemplates, v => v.TemplateId, t => t.Id, (v, t) => new { t.Name, v.Definition })
            .ToListAsync(cancellationToken);
        return
        [
            .. jobs.Where(j => Names(j.Workflow, templateId)).Select(j => j.Title),
            .. templates.Where(t => Names(t.Definition.Workflow, templateId)).Select(t => t.Name),
        ];
    }

    private static async Task<IReadOnlyList<DefinitionError>> MissingAsync(
        ScreenHireDbContext db, HiringWorkflow? workflow, string prefix, CancellationToken cancellationToken)
    {
        var references = In(workflow, prefix).ToList();
        if (references.Count == 0)
        {
            return [];
        }

        var missing = await EmailTemplateStore.MissingAsync(db, references.Select(r => r.TemplateId), cancellationToken);
        return
        [
            .. references.Where(r => missing.Contains(r.TemplateId))
                .Select(r => new DefinitionError(r.Path, "This email template does not exist any more. Choose another.")),
        ];
    }

    private static bool Names(HiringWorkflow? workflow, string templateId) =>
        In(workflow, "").Any(r => r.TemplateId == templateId);

    // Every action that names an organization template, with its path in the validator's form.
    private static IEnumerable<(string Path, string TemplateId)> In(HiringWorkflow? workflow, string prefix)
    {
        var automations = workflow?.Automations ?? [];
        var start = prefix.Length == 0 ? "automations" : $"{prefix}.automations";
        for (var i = 0; i < automations.Count; i++)
        {
            var actions = automations[i]?.Actions ?? [];
            for (var j = 0; j < actions.Count; j++)
            {
                if (actions[j]?.EmailTemplate is { } id && EmailTemplateIds.IsOrganizationTemplate(id))
                {
                    yield return ($"{start}[{i}].actions[{j}].emailTemplate", id);
                }
            }
        }
    }
}
