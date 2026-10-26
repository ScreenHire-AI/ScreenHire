namespace ScreenHire.Domain.Organizations;

/// <summary>
/// A user's membership of an organization. The model allows many organizations per user; the dashboard shows one.
/// </summary>
public sealed class OrganizationMember : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The signed-in user (an identity in the persistence layer; the domain only knows the id).</summary>
    public Guid UserId { get; init; }

    /// <summary>A built-in role, or <see cref="OrganizationRole.Custom"/> with <see cref="CustomRoleId"/> naming which.</summary>
    public OrganizationRole Role { get; private set; }

    /// <summary>The organization's own role this member has; set exactly when <see cref="Role"/> is Custom.</summary>
    public Guid? CustomRoleId { get; private set; }

    /// <summary>
    /// All jobs, or only the jobs whose hiring team they are on (ADR-0015). Owners and Admins see all jobs whatever this
    /// says; it applies to every other role. Existing members and new invitations start at All.
    /// </summary>
    public JobAccess JobAccess { get; private set; } = JobAccess.All;

    /// <summary>Whether the member sees every job: their role is Owner or Admin, or their job access is All.</summary>
    public bool SeesAllJobs => JobAccessRules.SeesAllJobs(Role, JobAccess);

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Sets which jobs the member sees. Returns whether it changed.</summary>
    public bool SetJobAccess(JobAccess access)
    {
        if (JobAccess == access)
        {
            return false;
        }

        JobAccess = access;
        return true;
    }

    /// <summary>Gives the member one role: a built-in, or a custom role by its id. A member never holds two.</summary>
    public void AssignRole(OrganizationRole role, Guid? customRoleId = null)
    {
        if ((role == OrganizationRole.Custom) != customRoleId.HasValue)
        {
            throw new ArgumentException("A custom role needs its id, and a built-in role takes none.", nameof(customRoleId));
        }

        Role = role;
        CustomRoleId = customRoleId;
    }

    /// <summary>Whether the member has exactly this role.</summary>
    public bool Holds(OrganizationRole role, Guid? customRoleId) => Role == role && CustomRoleId == customRoleId;

    /// <summary>A new membership with <paramref name="role"/>.</summary>
    public static OrganizationMember Create(
        Guid organizationId, Guid userId, OrganizationRole role, DateTimeOffset createdAt, Guid? customRoleId = null,
        JobAccess jobAccess = JobAccess.All)
    {
        var member = new OrganizationMember { OrganizationId = organizationId, UserId = userId, CreatedAt = createdAt, JobAccess = jobAccess };
        member.AssignRole(role, customRoleId);
        return member;
    }
}

/// <summary>
/// The four roles ScreenHire supplies, most access first, and <see cref="Custom"/> for a role the organization made
/// (<see cref="CustomRole"/>). Stored as text.
/// </summary>
public enum OrganizationRole
{
    Owner,
    Admin,
    Recruiter,
    Viewer,
    Custom,
}
