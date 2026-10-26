namespace ScreenHire.Domain.Organizations;

/// <summary>
/// Which jobs a member sees (ADR-0015): every job, or only the jobs whose hiring team they are on
/// (<see cref="Jobs.Job.HiringTeam"/>). Stored as text on the membership and on an invitation.
/// </summary>
public enum JobAccess
{
    /// <summary>Every job in the organization, including jobs added later.</summary>
    All,

    /// <summary>Only the jobs whose hiring team the member is on.</summary>
    HiringTeamOnly,
}

/// <summary>The one rule for who sees all jobs. The server applies it when it sets the tenant; the dashboard mirrors it.</summary>
public static class JobAccessRules
{
    /// <summary>
    /// Whether a member with <paramref name="role"/> and <paramref name="access"/> sees every job. Owners and Admins
    /// always do, whatever their stored choice; every other role, custom roles included, follows it.
    /// </summary>
    public static bool SeesAllJobs(OrganizationRole role, JobAccess access) =>
        role is OrganizationRole.Owner or OrganizationRole.Admin || access == JobAccess.All;
}
