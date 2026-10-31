using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Applications;

/// <summary>Job access (ADR-0015) as a worker handler checks it: a handler has no member, so its own queries see every job.</summary>
public static class JobAccessQueries
{
    /// <summary>
    /// Whether <paramref name="userId"/> is a member of the organization who sees the job: one who sees all jobs, or one
    /// on its hiring team. Narrowed to this organization: the members filter also shows a user's memberships elsewhere.
    /// </summary>
    public static async Task<bool> SeesJobAsync(
        ScreenHireDbContext db, Guid organizationId, Guid userId, Guid jobId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var member = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
            .Select(m => new { m.Id, m.Role, m.JobAccess })
            .SingleOrDefaultAsync(cancellationToken);
        if (member is null)
        {
            return false;
        }

        var team = await db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.HiringTeam).SingleOrDefaultAsync(cancellationToken);
        return team is not null && (JobAccessRules.SeesAllJobs(member.Role, member.JobAccess) || team.Contains(member.Id));
    }
}
