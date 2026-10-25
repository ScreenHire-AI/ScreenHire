using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Realtime;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// The pieces a hiring team change shares, on a job or a hiring template (ADR-0015): who the organization's members
/// are, how a team reads, the membership log entry per person, and the live-update groups of the people it touches.
/// </summary>
internal static class HiringTeams
{
    /// <summary>One current member of the organization, as a team needs them.</summary>
    internal sealed record Member(Guid Id, Guid UserId, string Email, string DisplayName, OrganizationRole Role, Guid? CustomRoleId, JobAccess JobAccess)
    {
        public bool SeesAllJobs => JobAccessRules.SeesAllJobs(Role, JobAccess);
    }

    /// <summary>
    /// The organization's members by id. Narrowed to the tenant, because the membership filter also shows the user's own
    /// memberships elsewhere (TeamEndpoints.InOrganization).
    /// </summary>
    public static async Task<Dictionary<Guid, Member>> MembersAsync(ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var members = await (
            from member in db.OrganizationMembers.AsNoTracking()
            where member.OrganizationId == tenant.OrganizationId
            join user in db.Users on member.UserId equals user.Id
            select new Member(
                member.Id, member.UserId, user.Email ?? "", user.DisplayName ?? user.Email ?? "", member.Role, member.CustomRoleId, member.JobAccess))
            .ToListAsync(cancellationToken);
        return members.ToDictionary(m => m.Id);
    }

    /// <summary>
    /// A team as the API returns it: the people in <paramref name="team"/> who are still members, by name. Ids of people
    /// who left are dropped here; they never match a member again, because a rejoin is a new membership.
    /// </summary>
    public static async Task<List<HiringTeamMember>> DescribeAsync(
        ScreenHireDbContext db, IReadOnlyDictionary<Guid, Member> members, IEnumerable<Guid> team, CancellationToken cancellationToken)
    {
        var names = await RoleAccess.CustomNamesAsync(db, cancellationToken);
        return [.. team.Distinct()
            .Where(members.ContainsKey)
            .Select(id => members[id])
            .OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => new HiringTeamMember(m.Id, m.UserId, m.Email, m.DisplayName, RoleAccess.NameOf(m.Role, m.CustomRoleId, names), m.SeesAllJobs))];
    }

    /// <summary>
    /// One membership log entry per person who joined or left: a job's team (<paramref name="jobId"/>) or a template's
    /// default team (<paramref name="templateId"/>). The actor is the signed-in user, or the API key.
    /// </summary>
    public static void Log(
        ScreenHireDbContext db, ITenantContext tenant, IReadOnlyDictionary<Guid, Member> members, HiringTeamChange change,
        DateTimeOffset now, Guid? jobId = null, Guid? templateId = null)
    {
        foreach (var (memberId, kind) in change.Added.Select(id => (id, MembershipEventKind.AddedToHiringTeam))
                     .Concat(change.Removed.Select(id => (id, MembershipEventKind.RemovedFromHiringTeam))))
        {
            if (!members.TryGetValue(memberId, out var member))
            {
                continue; // someone who left the organization: their removal is logged already
            }

            db.MembershipEvents.Add(new MembershipEvent
            {
                OrganizationId = tenant.OrganizationId!.Value,
                Kind = kind,
                ActorUserId = tenant.UserId,
                ActorApiKeyId = tenant.UserId is null ? tenant.ApiKeyId : null,
                MemberUserId = member.UserId,
                JobId = jobId,
                HiringTemplateId = templateId,
                OccurredAt = now,
            });
        }
    }

    /// <summary>
    /// Saves a job's team change with <paramref name="save"/> and moves open live-update connections to match. A member on
    /// Only jobs they're on who joins gets the job's group before the save, because the save's own HiringTeamChanged can
    /// reach the hub before this request runs on; if the save fails, their connections close, so they reconnect without
    /// it. One who left has their connections closed after the save, so they reconnect without the job and refetch.
    /// People who see all jobs are in every job's events already.
    /// </summary>
    public static async Task SaveAsync(
        RealtimeConnections connections, IReadOnlyDictionary<Guid, Member> members, HiringTeamChange change, Guid organizationId, Guid jobId,
        Func<Task> save, CancellationToken cancellationToken)
    {
        var joined = change.Added.Where(members.ContainsKey).Select(id => members[id]).Where(m => !m.SeesAllJobs).ToList();
        foreach (var member in joined)
        {
            await connections.JoinTeamAsync(member.UserId, organizationId, jobId, cancellationToken);
        }

        try
        {
            await save();
        }
        catch
        {
            foreach (var member in joined)
            {
                connections.Close(member.UserId, organizationId);
            }

            throw;
        }

        foreach (var member in change.Removed.Where(members.ContainsKey).Select(id => members[id]).Where(m => !m.SeesAllJobs))
        {
            connections.Close(member.UserId, organizationId);
        }
    }
}
