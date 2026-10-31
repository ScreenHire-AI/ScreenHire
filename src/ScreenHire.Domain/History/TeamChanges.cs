using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.History;

/// <summary>
/// What the history may see of an invitation: who it went to, what it offers and whether it was revoked. Never its
/// token's hash (docs/architecture/change-history.md, "Secrets").
/// </summary>
public sealed record InvitationFacts(string Email, OrganizationRole Role, Guid? CustomRoleId, JobAccess JobAccess, DateTimeOffset? RevokedAt)
{
    public static InvitationFacts Of(OrganizationInvitation invitation)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        return new(invitation.Email, invitation.Role, invitation.CustomRoleId, invitation.JobAccess, invitation.RevokedAt);
    }
}

/// <summary>
/// The team's changes in words, one history for Settings, Team: "created the team", "joined the team as
/// <b>Recruiter</b>", "changed <b>Sam Lee</b>'s role from Viewer to Admin", "changed <b>Sam Lee</b>'s jobs from All jobs
/// to Only jobs they're on", "removed <b>Sam Lee</b> from the team", "invited <b>sam@example.com</b> as Viewer" and
/// "revoked the invitation to <b>sam@example.com</b>". A member is named by id (<see cref="ChangeText.Person"/>); a role
/// by the name it had then. Who is on a job's hiring team is the membership log's to record (ADR-0015).
/// </summary>
public static class TeamChanges
{
    /// <param name="before">The membership before the save; null when the save added it.</param>
    /// <param name="after">The membership after the save; null when the save removed it.</param>
    /// <param name="roleBefore">The name of the role <paramref name="before"/> holds.</param>
    /// <param name="roleAfter">The name of the role <paramref name="after"/> holds.</param>
    /// <param name="withOrganization">Whether the save also adds the organization: its first member creates the team.</param>
    public static IReadOnlyList<ChangeLine> Member(
        OrganizationMember? before, OrganizationMember? after, string? roleBefore, string? roleAfter, bool withOrganization = false)
    {
        if (after is null)
        {
            return before is null
                ? []
                : [ChangeLine.Of([new("removed "), ChangeText.Person(before.UserId), new(" from the team")], [new("Role", roleBefore, null)])];
        }

        if (before is null)
        {
            return withOrganization
                ? [ChangeLine.Plain("created the team", [new("Role", null, roleAfter)])]
                : [ChangeLine.Of("joined the team as ", roleAfter ?? "", details: [new("Jobs", null, ChangeLabels.JobAccess(after.JobAccess))])];
        }

        var lines = new List<ChangeLine>();
        if (!after.Holds(before.Role, before.CustomRoleId))
        {
            lines.Add(ChangeLine.Of(
                [new("changed "), ChangeText.Person(after.UserId), new($"'s role from {roleBefore} to {roleAfter}")],
                [new("Role", roleBefore, roleAfter)]));
        }

        if (before.JobAccess != after.JobAccess)
        {
            var was = ChangeLabels.JobAccess(before.JobAccess);
            var now = ChangeLabels.JobAccess(after.JobAccess);
            lines.Add(ChangeLine.Of([new("changed "), ChangeText.Person(after.UserId), new($"'s jobs from {was} to {now}")], [new("Jobs", was, now)]));
        }

        return lines;
    }

    /// <param name="before">The invitation before the save; null when the save sent it.</param>
    /// <param name="after">The invitation after the save.</param>
    /// <param name="role">The name of the role it offers.</param>
    public static IReadOnlyList<ChangeLine> Invitation(InvitationFacts? before, InvitationFacts? after, string? role)
    {
        if (after is null)
        {
            return [];
        }

        if (before is null)
        {
            return [ChangeLine.Of("invited ", after.Email, $" as {role}", [new("Role", null, role), new("Jobs", null, ChangeLabels.JobAccess(after.JobAccess))])];
        }

        return before.RevokedAt is null && after.RevokedAt is not null ? [ChangeLine.Of("revoked the invitation to ", after.Email)] : [];
    }
}
