using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// The library forms a workflow's follow-up questionnaires copy (workflow-engine.md section 7, Forms). A hiring template's
/// copy is the library version's, always: every save rewrites it from the version it names, so no client can make a
/// template's form differ from the library. A job's copy is never rewritten, because a live job's questions change only when
/// someone updates the job; only its kind comes from the library, so a form's answers keep the protection its kind gives.
/// </summary>
internal static class FormReferences
{
    /// <summary>A published library form version: the form's kind and the version's content.</summary>
    private sealed record LibraryVersion(Guid FormId, int Version, FormKind Kind, FormContent Content);

    /// <summary>
    /// <paramref name="workflow"/> with each linked follow-up rewritten from the library version it names. A follow-up whose
    /// form or version is not in the library, or not published, is left as it is; <see cref="MissingAsync"/> reports it.
    /// </summary>
    public static async Task<HiringWorkflow> RefreshAsync(ScreenHireDbContext db, HiringWorkflow workflow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var library = await VersionsAsync(db, workflow, cancellationToken);
        if (library.Count == 0)
        {
            return workflow;
        }

        return workflow with
        {
            FollowUpQuestionnaires = [.. workflow.FollowUpQuestionnaires!.Select(f =>
                f is { FormTemplateId: { } formId, FormVersion: { } version } && library.TryGetValue((formId, version), out var found)
                    ? Copy(found, f.Id)
                    : f)],
        };
    }

    /// <summary><paramref name="definition"/> with its workflow's linked follow-ups rewritten from the library.</summary>
    public static async Task<HiringTemplateDefinition> RefreshAsync(
        ScreenHireDbContext db, HiringTemplateDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Workflow is null ? definition : definition with { Workflow = await RefreshAsync(db, definition.Workflow, cancellationToken) };
    }

    /// <summary>A job's <paramref name="workflow"/> with each linked follow-up's kind taken from its library form.</summary>
    public static async Task<HiringWorkflow> WithLibraryKindsAsync(ScreenHireDbContext db, HiringWorkflow workflow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var ids = LinkedIds(workflow);
        if (ids.Count == 0)
        {
            return workflow;
        }

        var kinds = await db.FormTemplates.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Kind, cancellationToken);
        return workflow with
        {
            FollowUpQuestionnaires = [.. workflow.FollowUpQuestionnaires!.Select(f =>
                f?.FormTemplateId is { } formId && kinds.TryGetValue(formId, out var kind) ? f with { Kind = KindOf(kind) } : f!)],
        };
    }

    /// <summary>
    /// An error for each linked follow-up whose library form is not this organization's, or whose version is not published.
    /// <paramref name="path"/> is the workflow's own path ("workflow" in a template definition, empty in a job).
    /// </summary>
    public static async Task<IReadOnlyList<DefinitionError>> MissingAsync(
        ScreenHireDbContext db, HiringWorkflow? workflow, string path, CancellationToken cancellationToken)
    {
        if (workflow is null)
        {
            return [];
        }

        var library = await VersionsAsync(db, workflow, cancellationToken);
        var followUps = workflow.FollowUpQuestionnaires ?? [];
        var errors = new List<DefinitionError>();
        for (var i = 0; i < followUps.Count; i++)
        {
            if (followUps[i] is { FormTemplateId: { } formId, FormVersion: { } version } followUp && !library.ContainsKey((formId, version)))
            {
                var at = $"{(path.Length == 0 ? "" : path + ".")}followUpQuestionnaires[{i}].formTemplateId";
                errors.Add(new(at, $"The form \"{followUp.Name}\" names a library form, or a version of one, that is not published in your library. Choose the form again."));
            }
        }

        return errors;
    }

    /// <summary>
    /// <paramref name="definition"/> with each follow-up that copies no library form made into one: a published version-1
    /// form of the organization, which the follow-up then names. A new hiring template's forms come from the library this
    /// way, copied from a ScreenHire template above all, whose forms belong to no organization. The forms are added to
    /// <paramref name="db"/> and saved with the template.
    /// </summary>
    public static HiringTemplateDefinition Adopt(
        ScreenHireDbContext db, HiringTemplateDefinition definition, Guid organizationId, DateTimeOffset now, Guid? userId)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Workflow?.FollowUpQuestionnaires is not { Count: > 0 } followUps || followUps.All(f => f?.FormTemplateId is not null))
        {
            return definition;
        }

        var adopted = followUps.Select(followUp =>
        {
            if (followUp is null || followUp.FormTemplateId is not null)
            {
                return followUp;
            }

            // A draft may hold a form that is not finished yet; that one stays the template's own until someone fixes it.
            var kind = followUp.Kind ?? FormKind.Standard;
            var content = new FormContent(followUp.Name?.Trim() ?? "", followUp.Questions, followUp.Sections);
            if (DefinitionValidator.ValidateForm(content, kind).Count > 0)
            {
                return followUp;
            }

            var form = FormTemplate.Create(organizationId, kind, content, now, userId);
            var version = form.Draft!;
            form.Publish(version, now, userId);
            db.FormTemplates.Add(form);
            return form.ToFollowUp(version, followUp.Id);
        });
        return definition with { Workflow = definition.Workflow with { FollowUpQuestionnaires = [.. adopted.Select(f => f!)] } };
    }

    /// <summary>A hiring template's copy of a library version, under the follow-up id <paramref name="followUpId"/>.</summary>
    private static FollowUpQuestionnaire Copy(LibraryVersion version, string followUpId) =>
        new(followUpId, version.Content.Name, version.Content.Questions, version.Content.Sections, version.FormId, version.Version, KindOf(version.Kind));

    // Absent means Standard, so a standard form's stored JSON stays as it was.
    private static FormKind? KindOf(FormKind kind) => kind == FormKind.Standard ? null : kind;

    private static List<Guid> LinkedIds(HiringWorkflow workflow) =>
        [.. (workflow.FollowUpQuestionnaires ?? []).Select(f => f?.FormTemplateId).OfType<Guid>().Distinct()];

    // Through the tenant filter: another organization's form is not found, so naming it is the same as naming none.
    private static async Task<Dictionary<(Guid, int), LibraryVersion>> VersionsAsync(
        ScreenHireDbContext db, HiringWorkflow workflow, CancellationToken cancellationToken)
    {
        var ids = LinkedIds(workflow);
        if (ids.Count == 0)
        {
            return [];
        }

        var versions = await db.FormTemplateVersions.AsNoTracking()
            .Where(v => ids.Contains(v.FormTemplateId) && v.Status == TemplateVersionStatus.Published)
            .Join(db.FormTemplates, v => v.FormTemplateId, t => t.Id, (v, t) => new LibraryVersion(t.Id, v.Version, t.Kind, v.Content))
            .ToListAsync(cancellationToken);
        return versions.ToDictionary(v => (v.FormId, v.Version));
    }
}
