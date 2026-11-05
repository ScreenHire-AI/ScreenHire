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

    /// <summary>
    /// Who made the change. For <see cref="MembershipEventKind.Joined"/>, the person who accepted. Null when an
    /// organization API key made it (<see cref="ActorApiKeyId"/>): a job a key creates starts with its template's
    /// default hiring team. Null with no key too when nobody on the team acted: a candidate's own data request
    /// (<see cref="MembershipEventKind.DataRequestReceived"/>), the retention schedule
    /// (<see cref="MembershipEventKind.RetentionApplied"/>) and an erasure it caused.
    /// </summary>
    public required Guid? ActorUserId { get; init; }

    /// <summary>The API key that made the change, when no person did.</summary>
    public Guid? ActorApiKeyId { get; init; }

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

    /// <summary>The job access before the change, for <see cref="MembershipEventKind.JobAccessChanged"/>.</summary>
    public JobAccess? PreviousJobAccess { get; init; }

    /// <summary>
    /// The job access after the change, for <see cref="MembershipEventKind.JobAccessChanged"/>, or the one an invitation
    /// offers or a join starts with.
    /// </summary>
    public JobAccess? JobAccess { get; init; }

    /// <summary>The job whose hiring team changed, for the hiring-team kinds. Its title is read through the job, under the reader's job access.</summary>
    public Guid? JobId { get; init; }

    /// <summary>The hiring template whose default hiring team changed, for the hiring-team kinds.</summary>
    public Guid? HiringTemplateId { get; init; }

    /// <summary>
    /// The data request the entry is about, for the data request kinds, and for <see cref="MembershipEventKind.CandidateErased"/>
    /// when an erase request caused it. An id only: it names no one once the candidate is gone.
    /// </summary>
    public Guid? DataRequestId { get; init; }

    /// <summary>How many applications a <see cref="MembershipEventKind.RetentionApplied"/> run deleted.</summary>
    public int? ItemCount { get; init; }

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

    /// <summary>A member's job access changed between All and Only jobs they're on (ADR-0015).</summary>
    JobAccessChanged,

    /// <summary>A member joined a job's hiring team (<see cref="MembershipEvent.JobId"/>) or a template's default one (<see cref="MembershipEvent.HiringTemplateId"/>).</summary>
    AddedToHiringTeam,

    /// <summary>A member left a job's hiring team or a template's default one.</summary>
    RemovedFromHiringTeam,

    /// <summary>
    /// A person erased a candidate. The tombstone: who and when, with nothing about the candidate, because the point of
    /// erasing is that nothing about them is kept.
    /// </summary>
    CandidateErased,

    /// <summary>A candidate asked for a copy of their data or for it to be erased (<see cref="MembershipEvent.DataRequestId"/>). No actor: the candidate asked.</summary>
    DataRequestReceived,

    /// <summary>A member sent a candidate the copy they asked for. The request is <see cref="MembershipEvent.DataRequestId"/>.</summary>
    DataRequestCopySent,

    /// <summary>A member declined a data request and told the candidate why. The reason stays on the request, not in the log.</summary>
    DataRequestDeclined,

    /// <summary>
    /// The record retention schedule deleted ended applications: how many is <see cref="MembershipEvent.ItemCount"/>. No actor,
    /// and nothing about the people.
    /// </summary>
    RetentionApplied,
}
