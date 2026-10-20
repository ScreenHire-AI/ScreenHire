using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Auth;

/// <summary>
/// Makes a signed-in user's organization the tenant of a unit of work: an HTTP request
/// (<see cref="TenantResolutionMiddleware"/>) or a call on the real-time hub, which runs in a scope of its own.
/// </summary>
internal static class Memberships
{
    /// <summary>
    /// Sets the user from <paramref name="user"/>'s claims, then their organization, and returns that organization; null
    /// when nobody is signed in or the user has not joined one yet. The membership is read every time rather than kept in
    /// the cookie, so removing someone from an organization takes effect on their next request or connection.
    /// </summary>
    public static async Task<Guid?> ResolveTenantAsync(
        ClaimsPrincipal? user, TenantContext tenant, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return null;
        }

        tenant.SetUser(userId);

        // With only the user set, the filter and row-level security show just this user's own memberships.
        // One organization per user in the dashboard for now: the first one joined.
        var organizationId = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => (Guid?)m.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);
        if (organizationId is { } id)
        {
            tenant.SetOrganization(id);
        }

        return organizationId;
    }
}
