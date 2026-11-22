using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Templates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The organization's form library (workflow-engine.md section 7, Forms): follow-up questionnaires built once and used
/// by any number of hiring templates, versioned like hiring templates. A template copies a published version and names it;
/// publishing a newer version changes no template until someone updates it (<c>apply-to-templates</c>). Tenant-scoped like
/// every hiring route: another organization's form is a 404. Edits are recorded in the form's history (change-history.md).
/// </summary>
internal static class FormTemplateEndpoints
{
    /// <summary>Forms in one organization's library, archived ones included.</summary>
    public const int MaxForms = 200;

    private const string ArchivedReadOnly = "The form is archived; restore it to change it.";

    public static IEndpointRouteBuilder MapFormTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/form-templates").WithTags("Form templates").AllowApiKeys();
        group.MapGet("/", ListFormTemplates).WithName("ListFormTemplates").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/", CreateFormTemplate).WithName("CreateFormTemplate")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{formId:guid}", GetFormTemplate).WithName("GetFormTemplate").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{formId:guid}/history", GetFormTemplateHistory).WithName("GetFormTemplateHistory").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{formId:guid}/versions", CreateFormTemplateVersion).WithName("CreateFormTemplateVersion")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{formId:guid}/versions/{version:int}", GetFormTemplateVersion).WithName("GetFormTemplateVersion")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/{formId:guid}/versions/{version:int}", UpdateFormTemplateVersion).WithName("UpdateFormTemplateVersion")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{formId:guid}/versions/{version:int}/publish", PublishFormTemplateVersion).WithName("PublishFormTemplateVersion")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{formId:guid}/versions/{version:int}/apply-to-templates", ApplyFormToTemplates).WithName("ApplyFormToTemplates")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{formId:guid}/archive", ArchiveFormTemplate).WithName("ArchiveFormTemplate")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{formId:guid}/unarchive", UnarchiveFormTemplate).WithName("UnarchiveFormTemplate")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Ok<FormTemplateList>> ListFormTemplates(
        ScreenHireDbContext db, IOptions<FormsOptions> options, CancellationToken cancellationToken)
    {
        var forms = await db.FormTemplates.AsNoTracking().Include(t => t.Versions).OrderBy(t => t.Name).ThenBy(t => t.Id)
            .AsSplitQuery().ToListAsync(cancellationToken);
        var changes = await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.FormTemplate, [.. forms.Select(f => SubjectId(f.Id))], cancellationToken);
        return TypedResults.Ok(new FormTemplateList(
            [.. forms.Select(f => f.ToSummary(changes.GetValueOrDefault(SubjectId(f.Id))))], options.Value.EqualityMonitoring.Enabled));
    }

    private static async Task<Results<Ok<FormTemplateDetail>, NotFound>> GetFormTemplate(
        Guid formId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var form = await LoadAsync(db, formId, tracking: false, cancellationToken);
        return form is null ? TypedResults.NotFound() : TypedResults.Ok(await DetailAsync(db, form, cancellationToken));
    }

    // Every change to the form and its versions, newest first.
    private static async Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetFormTemplateHistory(
        Guid formId, Guid? before, int? limit, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (ChangeHistoryRoutes.LimitProblem(limit) is { } problem)
        {
            return problem;
        }

        if (!await db.FormTemplates.AnyAsync(t => t.Id == formId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return await ChangeHistoryRoutes.PageAsync(db, ChangeSubjectKind.FormTemplate, SubjectId(formId), before, limit, cancellationToken) is { } page
            ? TypedResults.Ok(page)
            : ChangeHistoryRoutes.CursorProblem();
    }

    private static async Task<Results<Created<FormTemplateDetail>, ValidationProblem, ProblemHttpResult>> CreateFormTemplate(
        CreateFormTemplateRequest request,
        ScreenHireDbContext db,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        IOptions<FormsOptions> options,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var content = request.Content;
        var kind = request.Kind ?? FormKind.Standard;
        if (request.CopyFromFormId is { } sourceId)
        {
            var source = await LoadAsync(db, sourceId, tracking: false, cancellationToken);
            if (source?.Versions.MaxBy(v => v.Version) is not { } newest)
            {
                return TypedResults.ValidationProblem(ValidationExtensions.Problem("copyFromFormId", "No form in the library has this id."));
            }

            content ??= newest.Content with { Name = CopyName(newest.Content.Name) };
            kind = source.Kind;
        }

        if (kind == FormKind.EqualityMonitoring && !options.Value.EqualityMonitoring.Enabled)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "kind", "Equality monitoring forms are not offered yet. They need a legal review before they are switched on."));
        }

        content = Trimmed(content);
        if (DefinitionValidator.ValidateForm(content, kind).ToProblems("content") is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (await db.FormTemplates.CountAsync(cancellationToken) >= MaxForms)
        {
            return TypedResults.Problem(
                $"The library holds at most {MaxForms} forms. Change one you have instead.", statusCode: StatusCodes.Status409Conflict);
        }

        var form = FormTemplate.Create(tenant.OrganizationId!.Value, kind, content!, time.GetUtcNow(), tenant.UserId, request.CopyFromFormId);
        db.FormTemplates.Add(form);
        await outbox.SaveTemplateChangeAsync(TemplateKind.FormTemplate, SubjectId(form.Id), tenant, time, cancellationToken);
        return TypedResults.Created($"/v1/form-templates/{form.Id}", await DetailAsync(db, form, cancellationToken));
    }

    // Starts the next version as a draft: the given content, or a copy of the newest version's.
    private static async Task<Results<Created<FormTemplateVersionDetail>, NotFound, ValidationProblem, ProblemHttpResult>> CreateFormTemplateVersion(
        Guid formId,
        CreateFormTemplateVersionRequest request,
        ScreenHireDbContext db,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var form = await LoadAsync(db, formId, tracking: true, cancellationToken);
        if (form is null)
        {
            return TypedResults.NotFound();
        }

        if (form.ArchivedAt is not null)
        {
            return TypedResults.Problem(ArchivedReadOnly, statusCode: StatusCodes.Status409Conflict);
        }

        if (form.Draft is { } existing)
        {
            return TypedResults.Problem($"Version {existing.Version} is still a draft; edit or publish it first.", statusCode: StatusCodes.Status409Conflict);
        }

        var content = Trimmed(request.Content ?? form.Versions.MaxBy(v => v.Version)!.Content);
        if (DefinitionValidator.ValidateForm(content, form.Kind).ToProblems("content") is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var draft = form.CreateDraft(content!, time.GetUtcNow(), tenant.UserId);
        // Added explicitly: found through the tracked form's collection, a new row with a client-generated key would be
        // taken for an existing one and saved as an UPDATE.
        db.FormTemplateVersions.Add(draft);
        try
        {
            await outbox.SaveTemplateChangeAsync(TemplateKind.FormTemplate, SubjectId(form.Id), tenant, time, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return TypedResults.Problem("Someone else started a draft of this form at the same moment.", statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Created($"/v1/form-templates/{form.Id}/versions/{draft.Version}", draft.ToDetail(form.Kind));
    }

    private static async Task<Results<Ok<FormTemplateVersionDetail>, NotFound>> GetFormTemplateVersion(
        Guid formId, int version, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var found = await db.FormTemplateVersions.AsNoTracking()
            .Where(v => v.FormTemplateId == formId && v.Version == version)
            .Join(db.FormTemplates, v => v.FormTemplateId, t => t.Id, (v, t) => new { Version = v, t.Kind })
            .SingleOrDefaultAsync(cancellationToken);
        return found is null ? TypedResults.NotFound() : TypedResults.Ok(found.Version.ToDetail(found.Kind));
    }

    // A library draft is saved only when it is a whole form: unlike a hiring template's, nothing needs a draft with problems.
    private static async Task<Results<Ok<FormTemplateVersionDetail>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateFormTemplateVersion(
        Guid formId,
        int version,
        UpdateFormTemplateVersionRequest request,
        ScreenHireDbContext db,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var form = await LoadAsync(db, formId, tracking: true, cancellationToken);
        var found = form?.Versions.SingleOrDefault(v => v.Version == version);
        if (form is null || found is null)
        {
            return TypedResults.NotFound();
        }

        if (form.ArchivedAt is not null)
        {
            return TypedResults.Problem(ArchivedReadOnly, statusCode: StatusCodes.Status409Conflict);
        }

        if (found.Status != TemplateVersionStatus.Draft)
        {
            return TypedResults.Problem($"Version {version} is published and cannot change; create a new version.", statusCode: StatusCodes.Status409Conflict);
        }

        var content = Trimmed(request.Content);
        if (DefinitionValidator.ValidateForm(content, form.Kind).ToProblems("content") is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        form.Revise(found, content!, time.GetUtcNow());
        try
        {
            await outbox.SaveTemplateChangeAsync(TemplateKind.FormTemplate, SubjectId(form.Id), tenant, time, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Row-level security updates drafts only, so an edit that lost a race with a publish matched no row.
            return PublishedMeanwhile(version);
        }

        return TypedResults.Ok(found.ToDetail(form.Kind));
    }

    // Publishing a published version changes nothing. The draft is checked again, as the rules may have tightened.
    private static async Task<Results<Ok<FormTemplateVersionDetail>, NotFound, ValidationProblem, ProblemHttpResult>> PublishFormTemplateVersion(
        Guid formId,
        int version,
        ScreenHireDbContext db,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var form = await LoadAsync(db, formId, tracking: true, cancellationToken);
        var found = form?.Versions.SingleOrDefault(v => v.Version == version);
        if (form is null || found is null)
        {
            return TypedResults.NotFound();
        }

        if (form.ArchivedAt is not null)
        {
            return TypedResults.Problem(ArchivedReadOnly, statusCode: StatusCodes.Status409Conflict);
        }

        if (found.Status == TemplateVersionStatus.Draft)
        {
            if (DefinitionValidator.ValidateForm(found.Content, form.Kind).ToProblems("content") is { } problems)
            {
                return TypedResults.ValidationProblem(problems);
            }

            form.Publish(found, time.GetUtcNow(), tenant.UserId);
            try
            {
                await outbox.SaveTemplateChangeAsync(TemplateKind.FormTemplate, SubjectId(form.Id), tenant, time, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return PublishedMeanwhile(version);
            }
        }

        return TypedResults.Ok(found.ToDetail(form.Kind));
    }

    // Each named hiring template that uses an older version of this form takes this one, under the follow-up id it already
    // has, so its automations keep sending it. A template with a draft has the draft changed; one without gets a new
    // version that is published through the template's own publish path, so it is checked and announced as a Publish is.
    // Every template is checked before anything is saved. The drafts are saved together first, then the new versions are
    // published, so a teammate's change in between can leave a draft saved and unpublished; the 409 says so.
    private static async Task<Results<Ok<ApplyFormToTemplatesResult>, NotFound, ValidationProblem, ProblemHttpResult>> ApplyFormToTemplates(
        Guid formId,
        int version,
        ApplyFormToTemplatesRequest request,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var form = await LoadAsync(db, formId, tracking: false, cancellationToken);
        var published = form?.Versions.SingleOrDefault(v => v.Version == version);
        if (form is null || published is null)
        {
            return TypedResults.NotFound();
        }

        if (published.Status != TemplateVersionStatus.Published)
        {
            return TypedResults.Problem($"Version {version} is a draft. Publish it before updating templates.", statusCode: StatusCodes.Status409Conflict);
        }

        if (request.TemplateIds is not { Count: > 0 and <= MaxForms } ids)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("templateIds", $"Name between 1 and {MaxForms} hiring templates to update."));
        }

        var now = time.GetUtcNow();
        var results = new List<FormTemplateUpdate>();
        var drafts = new List<(HiringTemplate Template, HiringTemplateVersion Draft, bool Publish)>();
        var distinct = ids.Distinct().ToList();
        var templates = await db.HiringTemplates.Include(t => t.Versions)
            .Where(t => distinct.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        foreach (var templateId in distinct)
        {
            var template = templates.GetValueOrDefault(templateId);
            if (template is null || template.IsSystem)
            {
                results.Add(new(templateId, "", FormUpdateOutcome.NotUpdated, Problem: "No hiring template of yours has this id."));
                continue;
            }

            if (template.ArchivedAt is not null)
            {
                results.Add(new(templateId, template.Name, FormUpdateOutcome.NotUpdated, Problem: "The template is archived. Restore it to update it."));
                continue;
            }

            var existing = template.Draft;
            var basis = existing ?? template.Versions.Where(v => v.Status == TemplateVersionStatus.Published).MaxBy(v => v.Version);
            var updated = basis is null ? null : Updated(basis.Definition, form, published);
            if (updated is null)
            {
                results.Add(new(templateId, template.Name, FormUpdateOutcome.NotUpdated, Problem: $"It does not use an older version of {published.Content.Name}."));
                continue;
            }

            var problems = await TemplateReferences.ValidateAsync(db, updated, cancellationToken);
            if (existing is null && problems.Count > 0)
            {
                results.Add(new(templateId, template.Name, FormUpdateOutcome.NotUpdated, Problem: problems[0].Message));
                continue;
            }

            if (existing is not null)
            {
                existing.Revise(TemplateEndpoints.Sanitize(updated));
                drafts.Add((template, existing, false));
                results.Add(new(templateId, template.Name, FormUpdateOutcome.UpdatedDraft, existing.Version));
            }
            else
            {
                var draft = template.CreateDraft(TemplateEndpoints.Sanitize(updated), now, tenant.UserId);
                db.HiringTemplateVersions.Add(draft);
                drafts.Add((template, draft, true));
                results.Add(new(templateId, template.Name, FormUpdateOutcome.Published, draft.Version));
            }
        }

        if (drafts.Count == 0)
        {
            return TypedResults.Ok(new ApplyFormToTemplatesResult(results));
        }

        // A new version is saved as a draft first, so its history says it started and then that it was published.
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            foreach (var (template, draft, publish) in drafts)
            {
                if (publish && (await TemplateEndpoints.PublishDraftAsync(outbox, template, draft, tenant, time, cancellationToken)).Count > 0)
                {
                    // Checked above with the same rules; only a member who left in between could make it fail here.
                    var index = results.FindIndex(r => r.TemplateId == template.Id);
                    results[index] = results[index] with { Outcome = FormUpdateOutcome.UpdatedDraft };
                }
                else if (!publish)
                {
                    await outbox.PublishAsync(new TemplateChanged(tenant.OrganizationId!.Value, TemplateKind.HiringTemplate, SubjectId(template.Id), now, tenant.UserId));
                }
            }

            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || exception.IsUniqueViolation())
        {
            return TypedResults.Problem(
                "Someone changed one of these templates at the same moment, so some may have a draft with this form and others none. Look again, then update the templates that still need it.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok(new ApplyFormToTemplatesResult(results));
    }

    private static Task<Results<Ok<FormTemplateDetail>, NotFound>> ArchiveFormTemplate(
        Guid formId, ScreenHireDbContext db, IDbContextOutbox<ScreenHireDbContext> outbox, ITenantContext tenant, TimeProvider time,
        CancellationToken cancellationToken) =>
        SetArchivedAsync(formId, archived: true, db, outbox, tenant, time, cancellationToken);

    private static Task<Results<Ok<FormTemplateDetail>, NotFound>> UnarchiveFormTemplate(
        Guid formId, ScreenHireDbContext db, IDbContextOutbox<ScreenHireDbContext> outbox, ITenantContext tenant, TimeProvider time,
        CancellationToken cancellationToken) =>
        SetArchivedAsync(formId, archived: false, db, outbox, tenant, time, cancellationToken);

    // Archiving takes the form out of the pickers; templates and jobs that copy it keep their copies. Both directions are
    // idempotent and recorded in the form's History.
    private static async Task<Results<Ok<FormTemplateDetail>, NotFound>> SetArchivedAsync(
        Guid formId, bool archived, ScreenHireDbContext db, IDbContextOutbox<ScreenHireDbContext> outbox, ITenantContext tenant,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var form = await LoadAsync(db, formId, tracking: true, cancellationToken);
        if (form is null)
        {
            return TypedResults.NotFound();
        }

        if (archived)
        {
            form.Archive(time.GetUtcNow());
        }
        else
        {
            form.Unarchive(time.GetUtcNow());
        }

        await outbox.SaveTemplateChangeAsync(TemplateKind.FormTemplate, SubjectId(form.Id), tenant, time, cancellationToken);
        return TypedResults.Ok(await DetailAsync(db, form, cancellationToken));
    }

    /// <summary>
    /// <paramref name="definition"/> with every follow-up that copies an older version of <paramref name="form"/> holding
    /// <paramref name="version"/> instead, or null when no follow-up does.
    /// </summary>
    private static HiringTemplateDefinition? Updated(HiringTemplateDefinition definition, FormTemplate form, FormTemplateVersion version)
    {
        var followUps = definition.Workflow?.FollowUpQuestionnaires;
        if (followUps is null || !followUps.Any(f => f?.FormTemplateId == form.Id && f.FormVersion < version.Version))
        {
            return null;
        }

        return definition with
        {
            Workflow = definition.Workflow! with
            {
                FollowUpQuestionnaires = [.. followUps.Select(f =>
                    f?.FormTemplateId == form.Id && f.FormVersion < version.Version ? form.ToFollowUp(version, f.Id) : f!)],
            },
        };
    }

    // Names and labels are kept as people typed them, without the spaces around them.
    private static FormContent? Trimmed(FormContent? content) => content is null ? null : content with { Name = content.Name?.Trim() ?? "" };

    private static string CopyName(string name)
    {
        const string suffix = " (copy)";
        var room = DefinitionValidator.MaxFormNameLength - suffix.Length;
        return (name.Length > room ? name[..room] : name) + suffix;
    }

    // Through the tenant filter: another organization's form is not found.
    private static Task<FormTemplate?> LoadAsync(ScreenHireDbContext db, Guid formId, bool tracking, CancellationToken cancellationToken)
    {
        var query = db.FormTemplates.Include(t => t.Versions).Where(t => t.Id == formId);
        return (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<FormTemplateDetail> DetailAsync(ScreenHireDbContext db, FormTemplate form, CancellationToken cancellationToken) =>
        form.ToDetail(await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.FormTemplate, SubjectId(form.Id), cancellationToken));

    private static string SubjectId(Guid formId) => formId.ToString("D");

    private static ProblemHttpResult PublishedMeanwhile(int version) =>
        TypedResults.Problem($"Version {version} was published at the same moment and cannot change; create a new version.", statusCode: StatusCodes.Status409Conflict);
}
