using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.History;

/// <summary>
/// A custom role's changes in words: "created the role from <b>Viewer</b>", "renamed the role from Reviewer to
/// <b>Interviewer</b>", "allowed <b>Move candidates</b>", "took away <b>Email candidates</b>", "deleted the role".
/// Permissions are named as the Roles page names them.
/// </summary>
public static class RoleChanges
{
    /// <param name="before">The role before the save; null when the save added it.</param>
    /// <param name="after">The role after the save; null when the save deleted it.</param>
    public static IReadOnlyList<ChangeLine> Describe(CustomRole? before, CustomRole? after)
    {
        if (after is null)
        {
            return before is null ? [] : [ChangeLine.Plain("deleted the role")];
        }

        if (before is null)
        {
            return [ChangeLine.Of("created the role from ", after.CopiedFrom)];
        }

        var lines = new List<ChangeLine>();
        if (before.Name != after.Name)
        {
            lines.Add(ChangeLine.Of($"renamed the role from {before.Name} to ", after.Name));
        }

        var had = before.Permissions.ToHashSet();
        var has = after.Permissions.ToHashSet();
        foreach (var permission in RolePermissions.Permissions)
        {
            if (has.Contains(permission.Permission) && !had.Contains(permission.Permission))
            {
                lines.Add(ChangeLine.Of("allowed ", permission.Name));
            }
            else if (had.Contains(permission.Permission) && !has.Contains(permission.Permission))
            {
                lines.Add(ChangeLine.Of("took away ", permission.Name));
            }
        }

        return lines;
    }
}
