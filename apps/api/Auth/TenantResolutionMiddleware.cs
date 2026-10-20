using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Auth;

/// <summary>
/// Makes the signed-in user's organization the request's tenant. The membership is read on every request rather than
/// kept in the cookie, so removing someone from an organization takes effect on their next request.
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenant, ScreenHireDbContext db)
    {
        if (Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            tenant.SetUser(userId);

            // With only the user set, the filter and row-level security show just this user's own memberships.
            // One organization per user in the dashboard for now: the first one joined.
            var organizationId = await db.OrganizationMembers.AsNoTracking()
                .Where(m => m.UserId == userId)
                .OrderBy(m => m.CreatedAt)
                .Select(m => (Guid?)m.OrganizationId)
                .FirstOrDefaultAsync(context.RequestAborted);
            if (organizationId is { } id)
            {
                tenant.SetOrganization(id);
            }
        }

        await next(context);
    }
}
