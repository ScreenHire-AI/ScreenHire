using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Content;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Templates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;
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

    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/templates").WithTags("Hiring templates").RequireAuthorization();

        group.MapGet("/", ListTemplates).WithName("ListHiringTemplates").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/", CreateTemplate).WithName("CreateHiringTemplate")
            .RequireAntiforgeryToken()
            .RequireOrganization()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{templateId:guid}", GetTemplate).WithName("GetHiringTemplate").Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/{templateId:guid}", UpdateTemplate).WithName("UpdateHiringTemplate")
            .RequireAntiforgeryToken()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{templateId:guid}/versions", CreateVersion).WithName("CreateHiringTemplateVersion")
            .RequireAntiforgeryToken()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{templateId:guid}/versions/{version:int}", GetVersion).WithName("GetHiringTemplateVersion")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/{templateId:guid}/versions/{version:int}", UpdateVersion).WithName("UpdateHiringTemplateVersion")
            .RequireAntiforgeryToken()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{templateId:guid}/versions/{version:int}/publish", PublishVersion).WithName("PublishHiringTemplateVersion")
            .RequireAntiforgeryToken()
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
        return [.. templates.Select(t => t.ToSummary())];
    }

    private static async Task<Results<Ok<HiringTemplateDetail>, NotFound>> GetTemplate(
        Guid templateId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var template = await LoadAsync(db, templateId, tracking: false, cancellationToken);
        return template is null ? TypedResults.NotFound() : TypedResults.Ok(template.ToDetail());
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
        if (request.CopyFromVersionId is { } sourceId)
        {
            // Through the tenant filter: an own version or a system one.
            definition = await db.HiringTemplateVersions.AsNoTracking()
                .Where(v => v.Id == sourceId)
                .Select(v => v.Definition)
                .SingleOrDefaultAsync(cancellationToken);
            if (definition is null)
            {
                return TypedResults.ValidationProblem(ValidationExtensions.Problem("copyFromVersionId", "No template version has this id."));
            }
        }

        definition ??= HiringTemplateDefinition.Default;
        if (DefinitionValidator.Validate(definition).ToProblems("definition") is { } definitionProblems)
        {
            return TypedResults.ValidationProblem(definitionProblems);
        }

        var template = HiringTemplate.Create(
            tenant.OrganizationId!.Value, request.Name.Trim(), Clean(request.Description), Sanitize(definition), time.GetUtcNow(), tenant.UserId);
        db.HiringTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/v1/templates/{template.Id}", template.ToDetail());
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

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        template.Rename(request.Name.Trim(), Clean(request.Description), time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(template.ToDetail());
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

        if (template.Draft is { } existing)
        {
            return TypedResults.Problem($"Version {existing.Version} is still a draft; edit or publish it first.", statusCode: StatusCodes.Status409Conflict);
        }

        var definition = request.Definition ?? template.Versions.MaxBy(v => v.Version)?.Definition ?? HiringTemplateDefinition.Default;
        if (DefinitionValidator.Validate(definition).ToProblems("definition") is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var draft = template.CreateDraft(Sanitize(definition), time.GetUtcNow(), tenant.UserId);
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

        if (DefinitionValidator.Validate(request.Definition).ToProblems("definition") is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        found.Revise(Sanitize(request.Definition));
        await db.SaveChangesAsync(cancellationToken);
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

        if (found.Status == TemplateVersionStatus.Draft)
        {
            if (DefinitionValidator.Validate(found.Definition).ToProblems("definition") is { } problems)
            {
                return TypedResults.ValidationProblem(problems);
            }

            template.Publish(found, time.GetUtcNow(), tenant.UserId);
            await outbox.PublishAsync(new HiringTemplateVersionPublished(
                template.OrganizationId!.Value, template.Id, found.Id, found.Version, found.PublishedAt!.Value));
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }

        return TypedResults.Ok(found.ToDetail());
    }

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

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
