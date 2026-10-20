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
internal static class HiringAuthorization
{
    private static readonly OrganizationRole[] Writers = [OrganizationRole.Owner, OrganizationRole.Admin, OrganizationRole.Recruiter];

    public static RouteHandlerBuilder RequireHiringRole(this RouteHandlerBuilder builder) =>
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
                return role is { } found && Writers.Contains(found)
                    ? await next(invocation)
                    : TypedResults.Problem(
                        title: "Read-only role",
                        detail: "Your role can see hiring data but not change it. Ask an owner or admin.",
                        statusCode: StatusCodes.Status403Forbidden);
            })
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
