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

    public required DateTimeOffset CreatedAt { get; init; }

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
        Guid organizationId, Guid userId, OrganizationRole role, DateTimeOffset createdAt, Guid? customRoleId = null)
    {
        var member = new OrganizationMember { OrganizationId = organizationId, UserId = userId, CreatedAt = createdAt };
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
