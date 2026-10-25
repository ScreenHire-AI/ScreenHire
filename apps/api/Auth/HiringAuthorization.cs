using Microsoft.AspNetCore.Http.HttpResults;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Auth;

/// <summary>
/// For every route that changes data (hiring, templates, the organization, its team, keys and webhooks): the
/// signed-in user must belong to an organization with a role that has the route's permission (ADR-0014). The server
/// checks permissions from the catalogue (<see cref="RolePermissions"/>), never role names, so a custom role works
/// everywhere a built-in one does. Anyone else gets 403 with a problem body that names the permission. The role is read
/// per request, like the membership itself (<see cref="TenantResolutionMiddleware"/>), so a role change, or a change to
/// what a custom role allows, takes effect on the next request. An API key has no role: it is judged by its scope
/// instead (ADR-0011).
/// </summary>
/// <remarks>
/// An endpoint filter rather than an authorization policy: a failed policy ends in the cookie handler's bare 403
/// (<c>OnRedirectToAccessDenied</c>), and the dashboard needs the problem body that says why ("No organization" or
/// "Not in your role"). A policy would need a custom <c>IAuthorizationMiddlewareResultHandler</c> to say that, which is
/// a bigger layer than this filter. <c>RolePermissionsTests</c> reads every route that calls it and holds each to its
/// permission, and <c>HiringRolesTests</c> and <c>CustomRolesTests</c> send writes as roles without them.
/// </remarks>
internal static class HiringAuthorization
{
    /// <summary>The 403 title when the role lacks the permission.</summary>
    public const string NotInYourRole = "Not in your role";

    /// <summary>
    /// Requires <paramref name="permission"/>. An API key passes only for a hiring write (<see cref="RolePermissions.HiringWrites"/>)
    /// and only with the <see cref="ApiKeyScope.Write"/> scope; every other permission refuses keys.
    /// </summary>
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, Permission permission) =>
        builder
            .AddEndpointFilter(async (invocation, next) =>
            {
                // An API key has an organization but no user, so it has a scope where a person has a role.
                var user = invocation.HttpContext.User;
                if (user.IsApiKey())
                {
                    if (!RolePermissions.HiringWrites.Contains(permission))
                    {
                        return TypedResults.Problem(
                            title: "Not for API keys",
                            detail: "An API key cannot do this. Sign in to the dashboard instead.",
                            statusCode: StatusCodes.Status403Forbidden);
                    }

                    return user.HasApiKeyScope(ApiKeyScope.Write)
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
                var access = await RoleAccess.ForMemberAsync(db, organizationId, userId, invocation.HttpContext.RequestAborted);
                return access is not null && access.Permissions.Contains(permission)
                    ? await next(invocation)
                    : Refused(permission);
            })
            .ProducesProblem(StatusCodes.Status403Forbidden);

    /// <summary>The 403 for a role without <paramref name="permission"/>.</summary>
    public static ProblemHttpResult Refused(Permission permission) =>
        TypedResults.Problem(
            title: NotInYourRole,
            detail: $"Your role does not include {RolePermissions.Definition(permission).Name}. Ask an owner or admin.",
            statusCode: StatusCodes.Status403Forbidden);
}
