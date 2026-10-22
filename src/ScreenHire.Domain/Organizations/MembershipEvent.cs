namespace ScreenHire.Domain.Organizations;

/// <summary>
/// One change to who belongs to an organization, or with which role: the audit log of its team. Append-only: the
/// application role cannot update or delete these rows. Ids only, no foreign key to the membership, so removing a
/// member keeps the record of their removal.
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

    /// <summary>The role after the change, for an invitation, a join or a role change.</summary>
    public OrganizationRole? Role { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}

public enum MembershipEventKind
{
    Invited,
    InvitationRevoked,
    Joined,
    RoleChanged,
    Removed,
}
