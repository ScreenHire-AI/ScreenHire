using Microsoft.AspNetCore.Http.HttpResults;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Where each template is used, for the Templates page (docs/design/design-system.md section 22). One read route for
/// every kind, kept apart from the template lists, which many screens read only to name templates.
/// </summary>
internal static class TemplateUsageEndpoints
{
    public static IEndpointRouteBuilder MapTemplateUsageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/template-usage", GetTemplateUsage).WithName("GetTemplateUsage").WithTags("Templates")
            .RequireAuthorization()
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Ok<TemplateUsageList>> GetTemplateUsage(
        ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken) =>
        TypedResults.Ok(await TemplateUsage.ListAsync(db, tenant.OrganizationId!.Value, time.GetUtcNow(), cancellationToken));
}
