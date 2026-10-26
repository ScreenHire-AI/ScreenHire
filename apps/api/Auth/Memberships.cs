using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Organizations;
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
    /// Sets the user from <paramref name="user"/>'s claims, then their active organization and their job access in it
    /// (ADR-0015), and returns the organization; null
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

        // With only the user set, the filter and row-level security show just this user's own memberships. The
        // organization they last switched to comes first while they still belong to it; otherwise the first one joined.
        var membership = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => db.Users.Any(u => u.Id == userId && u.ActiveOrganizationId == m.OrganizationId))
            .ThenBy(m => m.CreatedAt)
            .Select(m => new { m.Id, m.OrganizationId, m.Role, m.JobAccess })
            .FirstOrDefaultAsync(cancellationToken);
        if (membership is null)
        {
            return null;
        }

        tenant.SetOrganization(membership.OrganizationId);
        tenant.SetMemberScope(membership.Id, JobAccessRules.SeesAllJobs(membership.Role, membership.JobAccess));
        return membership.OrganizationId;
    }

    /// <summary>
    /// Sets the job access of the user's membership of the tenant organization (ADR-0015), for a path that sets the user
    /// and organization itself: switching organization, accepting an invitation, the local demo. Until it runs, such a
    /// path sees no jobs. Returns false when the user is not a member.
    /// </summary>
    public static async Task<bool> ResolveMemberScopeAsync(TenantContext tenant, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (tenant is not { OrganizationId: { } organizationId, UserId: { } userId })
        {
            return false;
        }

        var membership = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
            .Select(m => new { m.Id, m.Role, m.JobAccess })
            .SingleOrDefaultAsync(cancellationToken);
        if (membership is null)
        {
            return false;
        }

        tenant.SetMemberScope(membership.Id, JobAccessRules.SeesAllJobs(membership.Role, membership.JobAccess));
        return true;
    }
}
