using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The organization's scorecard library (workflow-engine.md section 8): ScreenHire's built-ins, which are read only, and
/// its own. A hiring template's stage names one; a job copies its questions when it takes the workflow, so an edit here
/// reaches jobs created afterwards and never a job that has its copy. Tenant-scoped like every hiring route: another
/// organization's scorecard is a 404. Edits are recorded in the scorecard's history (change-history.md).
/// </summary>
internal static class ScorecardTemplateEndpoints
{
    /// <summary>Scorecards of an organization's own, beside the built-ins.</summary>
    public const int MaxTemplates = 200;

    private const string BuiltInReadOnly = "Built-in scorecards are read only; copy one to change it.";

    public static IEndpointRouteBuilder MapScorecardTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/scorecard-templates").WithTags("Scorecard templates").AllowApiKeys();
        group.MapGet("/", ListScorecardTemplates).WithName("ListScorecardTemplates").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/", CreateScorecardTemplate).WithName("CreateScorecardTemplate")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{templateId}", GetScorecardTemplate).WithName("GetScorecardTemplate").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{templateId}/history", GetScorecardTemplateHistory).WithName("GetScorecardTemplateHistory")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/{templateId}", UpdateScorecardTemplate).WithName("UpdateScorecardTemplate")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // The built-ins first, in their own order, then the organization's own by name.
    private static async Task<Ok<List<ScorecardTemplateSummary>>> ListScorecardTemplates(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var own = await db.ScorecardTemplates.AsNoTracking().OrderBy(t => t.Name).ThenBy(t => t.Id).ToListAsync(cancellationToken);
        var changes = await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.ScorecardTemplate, [.. own.Select(t => t.Key)], cancellationToken);
        return TypedResults.Ok<List<ScorecardTemplateSummary>>(
            [.. BuiltInScorecards.All.Select(b => b.ToSummary()), .. own.Select(t => t.ToSummary(changes.GetValueOrDefault(t.Key)))]);
    }

    private static async Task<Results<Ok<ScorecardTemplateDetail>, NotFound>> GetScorecardTemplate(
        string templateId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (BuiltInScorecards.Find(templateId) is { } builtIn)
        {
            return TypedResults.Ok(builtIn.ToDetail());
        }

        return await FindAsync(db, templateId, tracking: false, cancellationToken) is { } own
            ? TypedResults.Ok(await DetailAsync(db, own, cancellationToken))
            : TypedResults.NotFound();
    }

    // Every change to the scorecard, newest first. A built-in has none: nobody in an organization changes it.
    private static async Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetScorecardTemplateHistory(
        string templateId, Guid? before, int? limit, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (ChangeHistoryRoutes.LimitProblem(limit) is { } problem)
        {
            return problem;
        }

        if (BuiltInScorecards.Find(templateId) is null && await FindAsync(db, templateId, tracking: false, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        return await ChangeHistoryRoutes.PageAsync(db, ChangeSubjectKind.ScorecardTemplate, templateId, before, limit, cancellationToken) is { } page
            ? TypedResults.Ok(page)
            : ChangeHistoryRoutes.CursorProblem();
    }

    private static async Task<Results<Created<ScorecardTemplateDetail>, ValidationProblem, ProblemHttpResult>> CreateScorecardTemplate(
        CreateScorecardTemplateRequest request,
        IValidator<CreateScorecardTemplateRequest> validator,
        ScreenHireDbContext db,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (request.CopiedFrom is { } source && BuiltInScorecards.Find(source) is null
            && await FindAsync(db, source, tracking: false, cancellationToken) is null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("copiedFrom", "No scorecard in the library has this id."));
        }

        if (await db.ScorecardTemplates.CountAsync(cancellationToken) >= MaxTemplates)
        {
            return TypedResults.Problem(
                $"The library holds at most {MaxTemplates} scorecards of your own. Change one you have instead.", statusCode: StatusCodes.Status409Conflict);
        }

        var template = ScorecardTemplate.Create(tenant.OrganizationId!.Value, request.Name.Trim(), request.Questions, time.GetUtcNow(), request.CopiedFrom);
        db.ScorecardTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/v1/scorecard-templates/{template.Key}", await DetailAsync(db, template, cancellationToken));
    }

    private static async Task<Results<Ok<ScorecardTemplateDetail>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateScorecardTemplate(
        string templateId,
        UpdateScorecardTemplateRequest request,
        IValidator<UpdateScorecardTemplateRequest> validator,
        ScreenHireDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (BuiltInScorecards.Find(templateId) is not null)
        {
            return TypedResults.Problem(BuiltInReadOnly, statusCode: StatusCodes.Status409Conflict);
        }

        var template = await FindAsync(db, templateId, tracking: true, cancellationToken);
        if (template is null)
        {
            return TypedResults.NotFound();
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var name = request.Name.Trim();
        if (template.Name != name || !SameQuestions(template.Questions, request.Questions))
        {
            template.Update(name, request.Questions, time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await DetailAsync(db, template, cancellationToken));
    }

    // Through the tenant filter: another organization's scorecard is not found.
    private static async Task<ScorecardTemplate?> FindAsync(ScreenHireDbContext db, string key, bool tracking, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(key, "D", out var id))
        {
            return null;
        }

        var query = tracking ? db.ScorecardTemplates : db.ScorecardTemplates.AsNoTracking();
        return await query.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    private static async Task<ScorecardTemplateDetail> DetailAsync(ScreenHireDbContext db, ScorecardTemplate template, CancellationToken cancellationToken) =>
        template.ToDetail(await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.ScorecardTemplate, template.Key, cancellationToken));

    // Records compare their lists by reference, so the options are compared item by item.
    private static bool SameQuestions(IReadOnlyList<ScorecardQuestion> left, IReadOnlyList<ScorecardQuestion> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First with { Options = null } == pair.Second with { Options = null }
            && (pair.First.Options ?? []).SequenceEqual(pair.Second.Options ?? []));
}
