using System.Globalization;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Integrity.Llm;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The signed-in user's organization: its name, its careers-page branding and its integrity settings. Saving a brand colour derives and checks the
/// palette on the server (design-system §7); a colour that fails a contrast minimum is darkened, never refused, so only a
/// malformed colour is a 400. Each settings page has its history (design system T6): the name, the brand colour and the
/// integrity settings, readable by every member like the settings themselves.
/// </summary>
internal static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/organization").WithTags("Organization").RequireAuthorization();
        group.MapGet("", GetOrganization).WithName("GetOrganization").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/history", GetOrganizationHistory).WithName("GetOrganizationHistory").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/branding/history", GetBrandingHistory).WithName("GetOrganizationBrandingHistory").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/integrity-settings/history", GetIntegritySettingsHistory).WithName("GetOrganizationIntegritySettingsHistory")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("", UpdateOrganization).WithName("UpdateOrganization")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/branding", GetBranding).WithName("GetOrganizationBranding").Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/branding", UpdateBranding).WithName("UpdateOrganizationBranding")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/integrity-settings", GetIntegritySettings).WithName("GetOrganizationIntegritySettings")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/integrity-settings", UpdateIntegritySettings).WithName("UpdateOrganizationIntegritySettings")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Results<Ok<OrganizationProfile>, NotFound>> GetOrganization(
        ScreenHireDbContext db, ITenantContext tenant, IOptions<DomainOptions> domains, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == tenant.OrganizationId, cancellationToken);
        return organization is null ? TypedResults.NotFound() : TypedResults.Ok(await ToProfileAsync(db, organization, domains.Value, cancellationToken));
    }

    // Renames only. The slug is the careers address boards and candidates already link to, so it stays.
    private static async Task<Results<Ok<OrganizationProfile>, NotFound, ValidationProblem>> UpdateOrganization(
        UpdateOrganizationRequest request, IValidator<UpdateOrganizationRequest> validator, ScreenHireDbContext db, ITenantContext tenant,
        IOptions<DomainOptions> domains, CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == tenant.OrganizationId, cancellationToken);
        if (organization is null)
        {
            return TypedResults.NotFound();
        }

        organization.Name = request.Name.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ToProfileAsync(db, organization, domains.Value, cancellationToken));
    }

    private static async Task<OrganizationProfile> ToProfileAsync(
        ScreenHireDbContext db, Organization organization, DomainOptions domains, CancellationToken cancellationToken)
    {
        var last = await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.Organization, SubjectId(organization.Id), cancellationToken);
        return new(organization.Id, organization.Name, organization.Slug, organization.HomeRegion, domains.CareersBoardUrl(organization.Slug),
            organization.CreatedAt, LastUpdate.From(last, organization.CreatedAt));
    }

    // Every settings history's subject is the organization itself.
    private static string SubjectId(Guid organizationId) => organizationId.ToString("D");

    private static Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetOrganizationHistory(
        Guid? before, int? limit, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        HistoryAsync(ChangeSubjectKind.Organization, before, limit, db, tenant, cancellationToken);

    private static Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetBrandingHistory(
        Guid? before, int? limit, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        HistoryAsync(ChangeSubjectKind.Branding, before, limit, db, tenant, cancellationToken);

    private static Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetIntegritySettingsHistory(
        Guid? before, int? limit, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        HistoryAsync(ChangeSubjectKind.IntegritySettings, before, limit, db, tenant, cancellationToken);

    private static async Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> HistoryAsync(
        ChangeSubjectKind kind, Guid? before, int? limit, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (ChangeHistoryRoutes.LimitProblem(limit) is { } problem)
        {
            return problem;
        }

        if (tenant.OrganizationId is not { } organizationId)
        {
            return TypedResults.NotFound();
        }

        return await ChangeHistoryRoutes.PageAsync(db, kind, SubjectId(organizationId), before, limit, cancellationToken) is { } page
            ? TypedResults.Ok(page)
            : ChangeHistoryRoutes.CursorProblem();
    }

    private static async Task<Ok<OrganizationIntegritySettings>> GetIntegritySettings(
        ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var settings = await db.IntegritySettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return TypedResults.Ok(await ToIntegritySettingsAsync(db, settings, time.GetUtcNow(), cancellationToken));
    }

    // Upserts the organization's row. The check runs per application when it is next triggered; turning it on does not
    // reach back to applications already checked or skipped.
    private static async Task<Results<Ok<OrganizationIntegritySettings>, ValidationProblem>> UpdateIntegritySettings(
        UpdateIntegritySettingsRequest request, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (request.MonthlyTokenBudget is { } budget && budget is < 1 or > IntegritySettings.MaxMonthlyTokenBudget)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "monthlyTokenBudget", $"A monthly budget is between 1 and {IntegritySettings.MaxMonthlyTokenBudget.ToString("N0", CultureInfo.InvariantCulture)} tokens."));
        }

        var now = time.GetUtcNow();
        var settings = await db.IntegritySettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new IntegritySettings { OrganizationId = tenant.OrganizationId!.Value };
            db.IntegritySettings.Add(settings);
        }

        Apply(settings, request, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Two first saves at once: the other created the row. A PUT replaces the settings, so this one is applied
            // on top of it (last write wins) instead of failing.
            db.Entry(settings).State = EntityState.Detached;
            settings = await db.IntegritySettings.SingleAsync(cancellationToken);
            Apply(settings, request, now);
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await ToIntegritySettingsAsync(db, settings, now, cancellationToken));
    }

    private static void Apply(IntegritySettings settings, UpdateIntegritySettingsRequest request, DateTimeOffset now)
    {
        settings.LlmConsistencyCheckEnabled = request.LlmConsistencyCheck;
        settings.MonthlyTokenBudget = request.MonthlyTokenBudget;
        settings.UpdatedAt = now;
    }

    private static async Task<OrganizationIntegritySettings> ToIntegritySettingsAsync(
        ScreenHireDbContext db, IntegritySettings? settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var used = await db.TokensUsedThisMonthAsync(now, cancellationToken);
        var last = settings is null
            ? null
            : await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.IntegritySettings, SubjectId(settings.OrganizationId), cancellationToken);
        // No row is ScreenHire's defaults, which nobody in the organization has changed.
        var lastUpdated = settings is null ? LastUpdate.ByScreenHire(null) : LastUpdate.From(last, settings.UpdatedAt);
        return new OrganizationIntegritySettings(settings?.LlmConsistencyCheckEnabled ?? false, settings?.MonthlyTokenBudget, used, lastUpdated);
    }

    private static async Task<Results<Ok<OrganizationBranding>, NotFound>> GetBranding(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == tenant.OrganizationId, cancellationToken);
        return organization is null ? TypedResults.NotFound() : TypedResults.Ok(await ToBrandingAsync(db, organization, cancellationToken));
    }

    private static async Task<Results<Ok<OrganizationBranding>, NotFound, ValidationProblem>> UpdateBranding(
        UpdateBrandingRequest request, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (request.BrandColor is not null && !BrandPalette.IsValid(request.BrandColor))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "brandColor", "A brand colour is a hex colour such as #1f4e5a."));
        }

        var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == tenant.OrganizationId, cancellationToken);
        if (organization is null)
        {
            return TypedResults.NotFound();
        }

        organization.SetBrandColor(request.BrandColor);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ToBrandingAsync(db, organization, cancellationToken));
    }

    private static async Task<OrganizationBranding> ToBrandingAsync(ScreenHireDbContext db, Organization organization, CancellationToken cancellationToken)
    {
        var last = await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.Branding, SubjectId(organization.Id), cancellationToken);
        // ScreenHire's default colour, never changed, is by ScreenHire; a colour set before the history began has only a time.
        var lastUpdated = last is null && organization.BrandColor is null ? LastUpdate.ByScreenHire(null) : LastUpdate.From(last, organization.CreatedAt);
        return new(organization.BrandColor, organization.ToPublic().Branding, organization.GetBrandPalette()?.BrandAdjusted ?? false, lastUpdated);
    }
}
