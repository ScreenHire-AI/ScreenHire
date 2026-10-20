using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Integrity.Llm;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The signed-in user's organization: its careers-page branding and its integrity settings. Saving a brand colour derives and checks the
/// palette on the server (design-system §7); a colour that fails a contrast minimum is darkened, never refused, so only a
/// malformed colour is a 400.
/// </summary>
internal static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/organization").WithTags("Organization").RequireAuthorization();
        group.MapGet("/branding", GetBranding).WithName("GetOrganizationBranding").Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/branding", UpdateBranding).WithName("UpdateOrganizationBranding")
            .RequireAntiforgeryToken()
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/integrity-settings", GetIntegritySettings).WithName("GetOrganizationIntegritySettings")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/integrity-settings", UpdateIntegritySettings).WithName("UpdateOrganizationIntegritySettings")
            .RequireAntiforgeryToken()
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
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
        return new OrganizationIntegritySettings(settings?.LlmConsistencyCheckEnabled ?? false, settings?.MonthlyTokenBudget, used);
    }

    private static async Task<Results<Ok<OrganizationBranding>, NotFound>> GetBranding(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == tenant.OrganizationId, cancellationToken);
        return organization is null ? TypedResults.NotFound() : TypedResults.Ok(ToBranding(organization));
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
        return TypedResults.Ok(ToBranding(organization));
    }

    private static OrganizationBranding ToBranding(Organization organization) =>
        new(organization.BrandColor, organization.ToPublic().Branding, organization.GetBrandPalette()?.BrandAdjusted ?? false);
}
