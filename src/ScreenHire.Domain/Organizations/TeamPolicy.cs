namespace ScreenHire.Domain.Organizations;

/// <summary>
/// Who may give whom which role. Owners manage everyone. Admins manage Admins, Recruiters and Viewers, but cannot make
/// anyone an Owner or change or remove an Owner, so an Admin can never take an organization over. Recruiters and
/// Viewers manage nobody. The last-Owner guard is separate: it needs the organization's other members.
/// </summary>
public static class TeamPolicy
{
    /// <summary>Whether <paramref name="actor"/> may invite someone as, or move someone into, <paramref name="role"/>.</summary>
    public static bool MayAssign(OrganizationRole actor, OrganizationRole role) =>
        actor == OrganizationRole.Owner || (actor == OrganizationRole.Admin && role != OrganizationRole.Owner);

    /// <summary>Whether <paramref name="actor"/> may change a member's role from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static bool MayChange(OrganizationRole actor, OrganizationRole from, OrganizationRole to) =>
        MayAssign(actor, from) && MayAssign(actor, to);

    /// <summary>Whether <paramref name="actor"/> may remove a member who has <paramref name="role"/>.</summary>
    public static bool MayRemove(OrganizationRole actor, OrganizationRole role) => MayAssign(actor, role);
}
