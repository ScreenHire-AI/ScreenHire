using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Auth;

/// <summary>
/// A role as the server judges it: a built-in or a custom role, its name, and what it may do. The one place the
/// permissions of a member, or of a role someone asks to give, are worked out (ADR-0014). Custom roles are read under
/// row-level security, so only the current organization's are ever found.
/// </summary>
internal sealed record RoleAccess(OrganizationRole Role, Guid? CustomRoleId, string Name, IReadOnlySet<Permission> Permissions)
{
    /// <summary>A built-in role.</summary>
    public static RoleAccess BuiltIn(OrganizationRole role) => new(role, null, role.ToString(), RolePermissions.Of(role));

    /// <summary>A custom role, with what it may do as <see cref="RolePermissions.ForCustom"/> allows.</summary>
    public static RoleAccess Custom(CustomRole role) => new(OrganizationRole.Custom, role.Id, role.Name, RolePermissions.ForCustom(role.Permissions));

    /// <summary>The role of the user's membership of the organization, or null when they are not a member.</summary>
    public static async Task<RoleAccess?> ForMemberAsync(
        ScreenHireDbContext db, Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var row = await (
            from member in db.OrganizationMembers.AsNoTracking()
            where member.OrganizationId == organizationId && member.UserId == userId
            join custom in db.CustomRoles.AsNoTracking() on member.CustomRoleId equals (Guid?)custom.Id into customs
            from custom in customs.DefaultIfEmpty()
            select new { member.Role, member.CustomRoleId, Custom = custom })
            .SingleOrDefaultAsync(cancellationToken);
        return row switch
        {
            null => null,
            { Role: OrganizationRole.Custom, Custom: { } custom } => Custom(custom),
            // A custom role the member's row names is always there (a restricting foreign key); refuse everything if not.
            { Role: OrganizationRole.Custom } => new RoleAccess(OrganizationRole.Custom, row.CustomRoleId, "", new HashSet<Permission>()),
            _ => BuiltIn(row.Role),
        };
    }

    /// <summary>What the signed-in member may do in the current organization; nothing when they are not a member.</summary>
    public static async Task<IReadOnlySet<Permission>> PermissionsOfAsync(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        tenant is { OrganizationId: { } organizationId, UserId: { } userId }
            ? (await ForMemberAsync(db, organizationId, userId, cancellationToken))?.Permissions ?? new HashSet<Permission>()
            : new HashSet<Permission>();

    /// <summary>
    /// The role a request asks to give: a built-in, or one of this organization's custom roles by id. Null when the
    /// custom role is not found, which another organization's id never is.
    /// </summary>
    public static async Task<RoleAccess?> ForRoleAsync(
        ScreenHireDbContext db, OrganizationRole role, Guid? customRoleId, CancellationToken cancellationToken)
    {
        if (role != OrganizationRole.Custom)
        {
            return BuiltIn(role);
        }

        var custom = customRoleId is { } id
            ? await db.CustomRoles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            : null;
        return custom is null ? null : Custom(custom);
    }

    /// <summary>The names of the organization's custom roles by id, for lists that name each member's role.</summary>
    public static Task<Dictionary<Guid, string>> CustomNamesAsync(ScreenHireDbContext db, CancellationToken cancellationToken) =>
        db.CustomRoles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

    /// <summary>A role's name: the built-in's, or the custom role's from <paramref name="customNames"/>.</summary>
    public static string NameOf(OrganizationRole role, Guid? customRoleId, IReadOnlyDictionary<Guid, string> customNames) =>
        role == OrganizationRole.Custom ? customNames.GetValueOrDefault(customRoleId ?? Guid.Empty, "") : role.ToString();
}
