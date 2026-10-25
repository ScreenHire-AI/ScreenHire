namespace ScreenHire.Domain.Organizations;

/// <summary>
/// One change to who belongs to an organization, with which role, or to what a custom role allows: the audit log of its
/// team. Append-only: the application role cannot update or delete these rows. Ids only, no foreign key to the
/// membership or the role, so removing a member or deleting a role keeps the record of it.
/// </summary>
public sealed class MembershipEvent : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public required MembershipEventKind Kind { get; init; }

    /// <summary>Who made the change. For <see cref="MembershipEventKind.Joined"/>, the person who accepted.</summary>
    public required Guid ActorUserId { get; init; }

    /// <summary>The member the change is about; null for an invitation nobody has accepted yet.</summary>
    public Guid? MemberUserId { get; init; }

    public Guid? InvitationId { get; init; }

    /// <summary>The role before the change, for a role change or a removal.</summary>
    public OrganizationRole? PreviousRole { get; init; }

    /// <summary>The role after the change, for an invitation, a join or a role change; Custom for a change to a custom role.</summary>
    public OrganizationRole? Role { get; init; }

    /// <summary>The custom role before the change, when <see cref="PreviousRole"/> is Custom.</summary>
    public Guid? PreviousCustomRoleId { get; init; }

    /// <summary>That custom role's name at the time, so the log still reads after a rename or a deletion.</summary>
    public string? PreviousCustomRoleName { get; init; }

    /// <summary>
    /// The custom role after the change, when <see cref="Role"/> is Custom, or the role itself for
    /// <see cref="MembershipEventKind.RoleCreated"/>, <see cref="MembershipEventKind.RoleUpdated"/> and
    /// <see cref="MembershipEventKind.RoleDeleted"/>.
    /// </summary>
    public Guid? CustomRoleId { get; init; }

    /// <summary>That custom role's name at the time.</summary>
    public string? CustomRoleName { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}

public enum MembershipEventKind
{
    Invited,
    InvitationRevoked,
    Joined,
    RoleChanged,
    Removed,

    /// <summary>A custom role was made. Its permissions are in the role's change history.</summary>
    RoleCreated,

    /// <summary>A custom role was renamed or given other permissions; a rename sets <see cref="MembershipEvent.PreviousCustomRoleName"/>.</summary>
    RoleUpdated,

    RoleDeleted,
}
