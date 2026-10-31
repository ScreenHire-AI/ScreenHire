using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Content;
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
/// Hiring templates (docs/architecture/workflow-engine.md §1, §5). An organization sees its own templates and the system
/// templates; the tenant filter and row-level security decide which, so no handler filters by organization. System
/// templates are read-only (409); another organization's template is a 404. Every definition is validated before it is
/// saved, guardrails included, and a published version never changes.
/// </summary>
internal static class TemplateEndpoints
{
    private const string SystemReadOnly = "System templates are read-only; copy one to customise it.";

    private const string ArchivedReadOnly = "The template is archived; restore it to change it.";

    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/templates").WithTags("Hiring templates").AllowApiKeys();

        group.MapGet("/", ListTemplates).WithName("ListHiringTemplates").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/", CreateTemplate).WithName("CreateHiringTemplate")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{templateId:guid}", GetTemplate).WithName("GetHiringTemplate").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{templateId:guid}/history", GetHistory).WithName("GetHiringTemplateHistory").Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/{templateId:guid}", UpdateTemplate).WithName("UpdateHiringTemplate")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{templateId:guid}/versions", CreateVersion).WithName("CreateHiringTemplateVersion")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{templateId:guid}/versions/{version:int}", GetVersion).WithName("GetHiringTemplateVersion")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/{templateId:guid}/versions/{version:int}", UpdateVersion).WithName("UpdateHiringTemplateVersion")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{templateId:guid}/versions/{version:int}/publish", PublishVersion).WithName("PublishHiringTemplateVersion")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{templateId:guid}/archive", ArchiveTemplate).WithName("ArchiveHiringTemplate")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{templateId:guid}/unarchive", UnarchiveTemplate).WithName("UnarchiveHiringTemplate")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    // System templates first, then the organization's own, by name.
    private static async Task<List<HiringTemplateSummary>> ListTemplates(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var templates = await db.HiringTemplates.AsNoTracking()
            .Include(t => t.Versions)
            .OrderBy(t => t.OrganizationId != null)
            .ThenBy(t => t.Name)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var changes = await ChangeHistoryQueries.LastAsync(
            db, ChangeSubjectKind.HiringTemplate, [.. templates.Where(t => !t.IsSystem).Select(t => SubjectId(t.Id))], cancellationToken);
        return [.. templates.Select(t => t.ToSummary(changes.GetValueOrDefault(SubjectId(t.Id))))];
    }

    private static async Task<Results<Ok<HiringTemplateDetail>, NotFound>> GetTemplate(
        Guid templateId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var template = await LoadAsync(db, templateId, tracking: false, cancellationToken);
        return template is null ? TypedResults.NotFound() : TypedResults.Ok(await DetailAsync(db, template, cancellationToken));
    }

    // Every change to the template and its versions, newest first.
    private static async Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetHistory(
        Guid templateId, Guid? before, int? limit, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (ChangeHistoryRoutes.LimitProblem(limit) is { } problem)
        {
            return problem;
        }

        if (!await db.HiringTemplates.AnyAsync(t => t.Id == templateId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return await ChangeHistoryRoutes.PageAsync(db, ChangeSubjectKind.HiringTemplate, SubjectId(templateId), before, limit, cancellationToken) is { } page
            ? TypedResults.Ok(page)
            : ChangeHistoryRoutes.CursorProblem();
    }

    private static async Task<Results<Created<HiringTemplateDetail>, ValidationProblem>> CreateTemplate(
        CreateHiringTemplateRequest request,
        IValidator<CreateHiringTemplateRequest> validator,
        ScreenHireDbContext db,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var definition = request.Definition;
        Guid? copiedFrom = null;
        if (request.CopyFromVersionId is { } sourceId)
        {
            // Through the tenant filter: an own version or a system one.
            var source = await db.HiringTemplateVersions.AsNoTracking()
                .Where(v => v.Id == sourceId)
                .Select(v => new { v.Definition, v.TemplateId })
                .SingleOrDefaultAsync(cancellationToken);
            if (source is null)
            {
                return TypedResults.ValidationProblem(ValidationExtensions.Problem("copyFromVersionId", "No template version has this id."));
            }

            definition = source.Definition;
            copiedFrom = source.TemplateId;
        }

        definition ??= HiringTemplateDefinition.Default;
        if ((await TemplateReferences.ValidateAsync(db, definition, cancellationToken)).ToProblems("definition") is { } definitionProblems)
        {
            return TypedResults.ValidationProblem(definitionProblems);
        }

        var template = HiringTemplate.Create(
            tenant.OrganizationId!.Value, request.Name.Trim(), Text.NullIfBlank(request.Description), Sanitize(definition), time.GetUtcNow(), tenant.UserId,
            copiedFrom);
        db.HiringTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/v1/templates/{template.Id}", await DetailAsync(db, template, cancellationToken));
    }

    private static async Task<Results<Ok<HiringTemplateDetail>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateTemplate(
        Guid templateId,
        UpdateHiringTemplateRequest request,
        IValidator<UpdateHiringTemplateRequest> validator,
        ScreenHireDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var template = await LoadAsync(db, templateId, tracking: true, cancellationToken);
        if (template is null)
        {
            return TypedResults.NotFound();
        }

        if (template.IsSystem)
        {
            return TypedResults.Problem(SystemReadOnly, statusCode: StatusCodes.Status409Conflict);
        }

        if (template.ArchivedAt is not null)
        {
            return TypedResults.Problem(ArchivedReadOnly, statusCode: StatusCodes.Status409Conflict);
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        template.Rename(request.Name.Trim(), Text.NullIfBlank(request.Description), time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await DetailAsync(db, template, cancellationToken));
    }

    // Starts the next version as a draft: the given definition, or a copy of the newest version's.
    private static async Task<Results<Created<HiringTemplateVersionDetail>, NotFound, ValidationProblem, ProblemHttpResult>> CreateVersion(
        Guid templateId,
        CreateHiringTemplateVersionRequest request,
        ScreenHireDbContext db,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var template = await LoadAsync(db, templateId, tracking: true, cancellationToken);
        if (template is null)
        {
            return TypedResults.NotFound();
        }

        if (template.IsSystem)
        {
            return TypedResults.Problem(SystemReadOnly, statusCode: StatusCodes.Status409Conflict);
        }

        if (template.ArchivedAt is not null)
        {
            return TypedResults.Problem(ArchivedReadOnly, statusCode: StatusCodes.Status409Conflict);
        }

        if (template.Draft is { } existing)
        {
            return TypedResults.Problem($"Version {existing.Version} is still a draft; edit or publish it first.", statusCode: StatusCodes.Status409Conflict);
        }

        var definition = request.Definition ?? template.Versions.MaxBy(v => v.Version)?.Definition ?? HiringTemplateDefinition.Default;
        if ((await TemplateReferences.ValidateAsync(db, definition, cancellationToken)).ToProblems("definition") is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var draft = template.CreateDraft(Sanitize(definition), time.GetUtcNow(), tenant.UserId);
        // Added explicitly: found through the tracked template's collection, a new row with a client-generated key
        // would be taken for an existing one and saved as an UPDATE.
        db.HiringTemplateVersions.Add(draft);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return TypedResults.Problem("Someone else started a draft of this template at the same moment.", statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Created($"/v1/templates/{template.Id}/versions/{draft.Version}", draft.ToDetail());
    }

    private static async Task<Results<Ok<HiringTemplateVersionDetail>, NotFound>> GetVersion(
        Guid templateId, int version, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var found = await db.HiringTemplateVersions.AsNoTracking()
            .SingleOrDefaultAsync(v => v.TemplateId == templateId && v.Version == version, cancellationToken);
        return found is null ? TypedResults.NotFound() : TypedResults.Ok(found.ToDetail());
    }

    private static async Task<Results<Ok<HiringTemplateVersionDetail>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateVersion(
        Guid templateId,
        int version,
        UpdateHiringTemplateVersionRequest request,
        ScreenHireDbContext db,
        CancellationToken cancellationToken)
    {
        var found = await db.HiringTemplateVersions
            .SingleOrDefaultAsync(v => v.TemplateId == templateId && v.Version == version, cancellationToken);
        if (found is null)
        {
            return TypedResults.NotFound();
        }

        if (found.OrganizationId is null)
        {
            return TypedResults.Problem(SystemReadOnly, statusCode: StatusCodes.Status409Conflict);
        }

        if (found.Status != TemplateVersionStatus.Draft)
        {
            return TypedResults.Problem($"Version {version} is published and cannot change; create a new version.", statusCode: StatusCodes.Status409Conflict);
        }

        if (await db.HiringTemplates.AnyAsync(t => t.Id == templateId && t.ArchivedAt != null, cancellationToken))
        {
            return TypedResults.Problem(ArchivedReadOnly, statusCode: StatusCodes.Status409Conflict);
        }

        if ((await TemplateReferences.ValidateAsync(db, request.Definition, cancellationToken)).ToProblems("definition") is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        found.Revise(Sanitize(request.Definition));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Row-level security updates drafts only, so an edit that lost a race with a publish matched no row.
            return PublishedMeanwhile(version);
        }

        return TypedResults.Ok(found.ToDetail());
    }

    // Publishing a published version changes nothing and raises no second event. The draft is validated again first: it
    // was valid when saved, but the rules (the guardrails above all) may have tightened since.
    private static async Task<Results<Ok<HiringTemplateVersionDetail>, NotFound, ValidationProblem, ProblemHttpResult>> PublishVersion(
        Guid templateId,
        int version,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var template = await LoadAsync(outbox.DbContext, templateId, tracking: true, cancellationToken);
        var found = template?.Versions.SingleOrDefault(v => v.Version == version);
        if (template is null || found is null)
        {
            return TypedResults.NotFound();
        }

        if (template.IsSystem)
        {
            return TypedResults.Problem(SystemReadOnly, statusCode: StatusCodes.Status409Conflict);
        }

        if (template.ArchivedAt is not null)
        {
            return TypedResults.Problem(ArchivedReadOnly, statusCode: StatusCodes.Status409Conflict);
        }

        if (found.Status == TemplateVersionStatus.Draft)
        {
            if ((await TemplateReferences.ValidateAsync(outbox.DbContext, found.Definition, cancellationToken)).ToProblems("definition") is { } problems)
            {
                return TypedResults.ValidationProblem(problems);
            }

            // Read before the save writes this publish's own change: the member who changed the template last, when that
            // is someone other than the publisher. A later change by the publisher, a key or ScreenHire means nobody else
            // was the last to change it.
            var lastChange = await ChangeHistoryQueries.LastAsync(
                outbox.DbContext, ChangeSubjectKind.HiringTemplate, SubjectId(template.Id), cancellationToken);
            var previousEditor = lastChange?.UserId is { } lastEditor && lastEditor != tenant.UserId ? lastEditor : (Guid?)null;
            template.Publish(found, time.GetUtcNow(), tenant.UserId);
            await outbox.PublishAsync(new HiringTemplateVersionPublished(
                template.OrganizationId!.Value, template.Id, found.Id, found.Version, found.PublishedAt!.Value, tenant.UserId, previousEditor));
            try
            {
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Two publishes of one draft: row-level security updates drafts only, so the second matched no row and
                // committed nothing. The first published it, which is what this caller asked for.
                var published = await outbox.DbContext.HiringTemplateVersions.AsNoTracking()
                    .SingleAsync(v => v.Id == found.Id, cancellationToken);
                return published.Status == TemplateVersionStatus.Published
                    ? TypedResults.Ok(published.ToDetail())
                    : PublishedMeanwhile(version);
            }
        }

        return TypedResults.Ok(found.ToDetail());
    }

    // Archiving takes the template out of the pickers: no new job starts from it, and the jobs that did keep their copy.
    // Both directions are idempotent, and each records a line in the template's History.
    private static Task<Results<Ok<HiringTemplateDetail>, NotFound, ProblemHttpResult>> ArchiveTemplate(
        Guid templateId, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken) =>
        SetArchivedAsync(templateId, archived: true, db, time, cancellationToken);

    private static Task<Results<Ok<HiringTemplateDetail>, NotFound, ProblemHttpResult>> UnarchiveTemplate(
        Guid templateId, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken) =>
        SetArchivedAsync(templateId, archived: false, db, time, cancellationToken);

    private static async Task<Results<Ok<HiringTemplateDetail>, NotFound, ProblemHttpResult>> SetArchivedAsync(
        Guid templateId, bool archived, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var template = await LoadAsync(db, templateId, tracking: true, cancellationToken);
        if (template is null)
        {
            return TypedResults.NotFound();
        }

        if (template.IsSystem)
        {
            return TypedResults.Problem("System templates cannot be archived.", statusCode: StatusCodes.Status409Conflict);
        }

        if (archived)
        {
            template.Archive(time.GetUtcNow());
        }
        else
        {
            template.Unarchive(time.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await DetailAsync(db, template, cancellationToken));
    }

    private static async Task<HiringTemplateDetail> DetailAsync(ScreenHireDbContext db, HiringTemplate template, CancellationToken cancellationToken) =>
        template.ToDetail(template.IsSystem
            ? null
            : await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.HiringTemplate, SubjectId(template.Id), cancellationToken));

    private static string SubjectId(Guid templateId) => templateId.ToString("D");

    private static Task<HiringTemplate?> LoadAsync(ScreenHireDbContext db, Guid templateId, bool tracking, CancellationToken cancellationToken)
    {
        var query = db.HiringTemplates.Include(t => t.Versions).Where(t => t.Id == templateId);
        return (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    // Rich text is sanitized on the way in, wherever it arrives: a job created from the template copies it, and careers
    // pages and feeds render it as HTML.
    private static HiringTemplateDefinition Sanitize(HiringTemplateDefinition definition) =>
        definition.PostingDefaults.Description is { } description
            ? definition with { PostingDefaults = definition.PostingDefaults with { Description = RichText.Sanitize(description) } }
            : definition;

    private static ProblemHttpResult PublishedMeanwhile(int version) =>
        TypedResults.Problem($"Version {version} was published at the same moment and cannot change; create a new version.", statusCode: StatusCodes.Status409Conflict);
}
