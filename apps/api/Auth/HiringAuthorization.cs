using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Auth;

/// <summary>
/// For every route that changes hiring data (jobs, templates, applications): the signed-in user must belong to an
/// organization with a role that may change it. Viewers read; Owners, Admins and Recruiters also write. Anyone else
/// gets 403 with a problem body. The role is read per request, like the membership itself
/// (<see cref="TenantResolutionMiddleware"/>), so a role change takes effect on the next request.
/// </summary>
/// <remarks>
/// An endpoint filter rather than an authorization policy: a failed policy ends in the cookie handler's bare 403
/// (<c>OnRedirectToAccessDenied</c>), and the dashboard needs the problem body that says why ("No organization" or
/// "Read-only role"). A policy would need a custom <c>IAuthorizationMiddlewareResultHandler</c> to say that, which is
/// a bigger layer than this filter. <c>HiringRolesTests</c> sends every write route as a Viewer, so a route that
/// forgets the filter fails a test.
/// </remarks>
internal static class HiringAuthorization
{
    private static readonly OrganizationRole[] Writers = [OrganizationRole.Owner, OrganizationRole.Admin, OrganizationRole.Recruiter];

    private static readonly OrganizationRole[] Administrators = [OrganizationRole.Owner, OrganizationRole.Admin];

    /// <remarks>An API key needs the <see cref="ApiKeyScope.Write"/> scope instead of a role.</remarks>
    public static RouteHandlerBuilder RequireHiringRole(this RouteHandlerBuilder builder) =>
        builder.RequireRole(Writers, ApiKeyScope.Write, "Read-only role", "Your role can see hiring data but not change it. Ask an owner or admin.");

    /// <summary>
    /// For routes that change the organization itself, such as its careers-page branding, or manage its API keys: Owners
    /// and Admins only, with the same 403 problem bodies as <see cref="RequireHiringRole"/>. API keys never pass.
    /// </summary>
    public static RouteHandlerBuilder RequireOrganizationAdmin(this RouteHandlerBuilder builder) =>
        builder.RequireRole(Administrators, keyScope: null, "Owners and admins only", "Only an owner or admin can change the organization's settings.");

    private static RouteHandlerBuilder RequireRole(
        this RouteHandlerBuilder builder, OrganizationRole[] allowed, ApiKeyScope? keyScope, string title, string detail) =>
        builder
            .AddEndpointFilter(async (invocation, next) =>
            {
                // An API key has an organization but no user, so it has a scope where a person has a role.
                var user = invocation.HttpContext.User;
                if (user.IsApiKey())
                {
                    if (keyScope is not { } scope)
                    {
                        return TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status403Forbidden);
                    }

                    return user.HasApiKeyScope(scope)
                        ? await next(invocation)
                        : TypedResults.Problem(
                            title: "Read-only key",
                            detail: "This API key can read hiring data but not change it. Create a key with the Write scope.",
                            statusCode: StatusCodes.Status403Forbidden);
                }

                var services = invocation.HttpContext.RequestServices;
                var tenant = services.GetRequiredService<ITenantContext>();
                if (tenant is not { OrganizationId: { } organizationId, UserId: { } userId })
                {
                    return TypedResults.Problem(
                        title: "No organization",
                        detail: "Create or join an organization first.",
                        statusCode: StatusCodes.Status403Forbidden);
                }

                var db = services.GetRequiredService<ScreenHireDbContext>();
                var role = await db.OrganizationMembers.AsNoTracking()
                    .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
                    .Select(m => (OrganizationRole?)m.Role)
                    .SingleOrDefaultAsync(invocation.HttpContext.RequestAborted);
                return role is { } found && allowed.Contains(found)
                    ? await next(invocation)
                    : TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status403Forbidden);
            })
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
