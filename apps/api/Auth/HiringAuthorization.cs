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

    public static RouteHandlerBuilder RequireHiringRole(this RouteHandlerBuilder builder) =>
        builder.RequireRole(Writers, "Read-only role", "Your role can see hiring data but not change it. Ask an owner or admin.");

    /// <summary>
    /// For routes that change the organization itself, such as its careers-page branding: Owners and Admins only, with the
    /// same 403 problem bodies as <see cref="RequireHiringRole"/>.
    /// </summary>
    public static RouteHandlerBuilder RequireOrganizationAdmin(this RouteHandlerBuilder builder) =>
        builder.RequireRole(Administrators, "Owners and admins only", "Only an owner or admin can change the organization's settings.");

    private static RouteHandlerBuilder RequireRole(this RouteHandlerBuilder builder, OrganizationRole[] allowed, string title, string detail) =>
        builder
            .AddEndpointFilter(async (invocation, next) =>
            {
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
