namespace ScreenHire.Domain.Organizations;

/// <summary>
/// A role an organization made by copying another and changing what it may do, such as an Interviewer who sees
/// candidates, adds notes and fills in scorecards (docs/adr/0014-permissions-and-custom-roles.md). It never holds an
/// Owner-only permission and always sees hiring data (<see cref="RolePermissions.ForCustom"/>). A member who has it has
/// <see cref="OrganizationRole.Custom"/> and its id.
/// </summary>
public sealed class CustomRole : ITenantOwned
{
    /// <summary>The longest name a role may have.</summary>
    public const int MaxNameLength = 50;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public string Name { get; private set; } = "";

    /// <summary><see cref="Name"/> trimmed and upper-cased: names are unique in an organization, ignoring case.</summary>
    public string NormalizedName { get; private set; } = "";

    /// <summary>What it may do, in catalogue order.</summary>
    public List<Permission> Permissions { get; private set; } = [];

    /// <summary>The name of the role it was copied from, as it was then.</summary>
    public required string CopiedFrom { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>A new role named <paramref name="name"/> with the permissions of the role it copies.</summary>
    public static CustomRole Copy(Guid organizationId, string name, string copiedFrom, IEnumerable<Permission> permissions, DateTimeOffset now)
    {
        var role = new CustomRole { OrganizationId = organizationId, CopiedFrom = copiedFrom, CreatedAt = now };
        role.Rename(name, now);
        role.SetPermissions(permissions, now);
        return role;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        var trimmed = name.Trim();
        if (trimmed == Name)
        {
            return;
        }

        Name = trimmed;
        NormalizedName = Normalize(trimmed);
        UpdatedAt = now;
    }

    /// <summary>Sets what the role may do. Owner-only permissions are dropped and seeing hiring data is kept.</summary>
    public void SetPermissions(IEnumerable<Permission> permissions, DateTimeOffset now)
    {
        var allowed = RolePermissions.ForCustom(permissions);
        var ordered = RolePermissions.Permissions.Select(p => p.Permission).Where(allowed.Contains).ToList();
        if (ordered.SequenceEqual(Permissions))
        {
            return;
        }

        Permissions = ordered;
        UpdatedAt = now;
    }

    /// <summary>The form names are compared in: trimmed and upper-cased.</summary>
    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
}
