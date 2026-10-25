namespace ScreenHire.Domain.Organizations;

/// <summary>
/// Who may give whom which role, and who may make, change or delete a custom role, by permissions rather than role
/// names (ADR-0014). An actor needs Manage the team, and the role they give, take away or edit may hold nothing their
/// own role does not: a role never grants more than it has. For the built-in roles that is the rule they always had.
/// Owners manage everyone; Admins manage Admins, Recruiters, Viewers and every custom role, but cannot make anyone an
/// Owner or change or remove an Owner, because Manage Owners is the Owner's alone, so an Admin can never take an
/// organization over; Recruiters and Viewers manage nobody. The last-Owner guard is separate: it needs the
/// organization's other members.
/// </summary>
public static class TeamPolicy
{
    /// <summary>Whether an actor with <paramref name="actor"/> may invite someone as, or move someone into, a role with <paramref name="role"/>.</summary>
    public static bool MayAssign(IReadOnlySet<Permission> actor, IReadOnlySet<Permission> role) =>
        actor.Contains(Permission.ManageTeam) && role.IsSubsetOf(actor);

    /// <summary>Whether the actor may change a member's role from one with <paramref name="from"/> to one with <paramref name="to"/>.</summary>
    public static bool MayChange(IReadOnlySet<Permission> actor, IReadOnlySet<Permission> from, IReadOnlySet<Permission> to) =>
        MayAssign(actor, from) && MayAssign(actor, to);

    /// <summary>Whether the actor may remove a member whose role has <paramref name="role"/>.</summary>
    public static bool MayRemove(IReadOnlySet<Permission> actor, IReadOnlySet<Permission> role) => MayAssign(actor, role);

    /// <summary>
    /// Whether the actor may make (<paramref name="before"/> null), change, or delete (<paramref name="after"/> null) a
    /// custom role with these permissions.
    /// </summary>
    public static bool MayManageRole(IReadOnlySet<Permission> actor, IReadOnlySet<Permission>? before, IReadOnlySet<Permission>? after) =>
        actor.Contains(Permission.ManageTeam) && (before?.IsSubsetOf(actor) ?? true) && (after?.IsSubsetOf(actor) ?? true);

    /// <summary>Whether a role with <paramref name="role"/> holds an Owner-only permission: whether refusing it is "Owners only".</summary>
    public static bool IsOwnerLevel(IReadOnlySet<Permission> role) =>
        role.Any(permission => RolePermissions.Definition(permission).OwnerOnly);
}
