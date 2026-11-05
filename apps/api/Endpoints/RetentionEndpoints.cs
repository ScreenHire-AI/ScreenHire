using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Privacy;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Privacy;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The organization's record retention (docs/architecture/data-requests.md, "Retention"): the period after which an ended
/// application is deleted, off by default, a dry-run count of what a period would delete now, and the setting's history.
/// Saving a period deletes data on the next daily run, so every route needs the Erase candidates permission and a person who
/// sees every job (the count and the deletion reach every application); anyone else gets a 404. People only: no API keys.
/// </summary>
internal static class RetentionEndpoints
{
    public static IEndpointRouteBuilder MapRetentionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/organization/data-retention").WithTags("Data retention").RequireAuthorization();
        group.MapGet("", GetRetention).WithName("GetOrganizationRetention")
            .RequirePermission(Permission.EraseCandidates)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/history", GetRetentionHistory).WithName("GetOrganizationRetentionHistory")
            .RequirePermission(Permission.EraseCandidates)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/preview", PreviewRetention).WithName("PreviewOrganizationRetention")
            .RequirePermission(Permission.EraseCandidates)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("", UpdateRetention).WithName("UpdateOrganizationRetention")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EraseCandidates)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Results<Ok<OrganizationRetention>, NotFound>> GetRetention(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (!tenant.SeesAllJobs)
        {
            return TypedResults.NotFound();
        }

        var settings = await db.RetentionSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return TypedResults.Ok(await ToRetentionAsync(db, settings, cancellationToken));
    }

    private static Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetRetentionHistory(
        Guid? before, int? limit, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        tenant.SeesAllJobs
            ? ChangeHistoryRoutes.OrganizationPageAsync(ChangeSubjectKind.RetentionSettings, before, limit, db, tenant, cancellationToken)
            : Task.FromResult<Results<Ok<HistoryPage>, NotFound, ValidationProblem>>(TypedResults.NotFound());

    // The count the daily run would act on now if this period were saved, from the same query the run uses.
    private static async Task<Results<Ok<RetentionPreview>, NotFound, ValidationProblem>> PreviewRetention(
        int? months, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!tenant.SeesAllJobs)
        {
            return TypedResults.NotFound();
        }

        if (months is not { } value || !RetentionSettings.IsValidMonths(value))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "months", $"Choose from {RetentionSettings.MinMonths} to {RetentionSettings.MaxMonths} months."));
        }

        var cutoff = RetentionSettings.CutoffFor(value, time.GetUtcNow())!.Value;
        var selection = await RetentionSelection.FindAsync(db, cutoff, cancellationToken);
        return TypedResults.Ok(new RetentionPreview(selection.ApplicationIds.Count, selection.CandidateCount));
    }

    // Upserts the organization's row, as the verification settings do. The next daily run applies it.
    private static async Task<Results<Ok<OrganizationRetention>, NotFound, ValidationProblem>> UpdateRetention(
        UpdateRetentionRequest request,
        IValidator<UpdateRetentionRequest> validator,
        ScreenHireDbContext db,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!tenant.SeesAllJobs || tenant.OrganizationId is not { } organizationId)
        {
            return TypedResults.NotFound();
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var now = time.GetUtcNow();
        var settings = await db.RetentionSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new RetentionSettings { OrganizationId = organizationId };
            db.RetentionSettings.Add(settings);
        }

        settings.Set(request.Months, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Two first saves at once: the other created the row. A PUT replaces the setting, so this one is applied on top
            // of it (last write wins) instead of failing.
            db.Entry(settings).State = EntityState.Detached;
            settings = await db.RetentionSettings.SingleAsync(cancellationToken);
            settings.Set(request.Months, now);
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await ToRetentionAsync(db, settings, cancellationToken));
    }

    private static async Task<OrganizationRetention> ToRetentionAsync(
        ScreenHireDbContext db, RetentionSettings? settings, CancellationToken cancellationToken)
    {
        var last = settings is null
            ? null
            : await ChangeHistoryQueries.LastAsync(
                db, ChangeSubjectKind.RetentionSettings, ChangeHistoryRoutes.SubjectId(settings.OrganizationId), cancellationToken);

        // The newest run that deleted anything: the team log only gets an entry when the count is above zero.
        var run = await db.MembershipEvents.AsNoTracking()
            .Where(e => e.Kind == MembershipEventKind.RetentionApplied)
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Select(e => new { e.OccurredAt, e.ItemCount })
            .FirstOrDefaultAsync(cancellationToken);

        // No row is ScreenHire's default (off), which nobody in the organization has changed.
        var lastUpdated = settings is null ? LastUpdate.ByScreenHire(null) : LastUpdate.FromChange(last, settings.UpdatedAt);
        return new OrganizationRetention(
            settings?.EndedApplicationsMonths, lastUpdated, run is null ? null : new RetentionLastRun(run.OccurredAt, run.ItemCount ?? 0));
    }
}
