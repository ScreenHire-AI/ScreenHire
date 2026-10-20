using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The signed-in user's organization: today its careers-page branding. Saving a brand colour derives and checks the
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
        return app;
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
