using FluentValidation;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence.History;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// A member of the signed-in user's organization. <c>id</c> is the membership's id, which the member routes take.
/// <c>role</c> is a built-in role or Custom, with <c>customRoleId</c> naming which; <c>roleName</c> is its name either way.
/// </summary>
public sealed record TeamMember(
    Guid Id, Guid UserId, string Email, string DisplayName, OrganizationRole Role, Guid? CustomRoleId, string RoleName, DateTimeOffset JoinedAt);

/// <summary>Gives a member another role: a built-in one, or Custom with the custom role's id.</summary>
public sealed record ChangeMemberRoleRequest(OrganizationRole? Role, Guid? CustomRoleId = null);

/// <summary>Invites a person by email to join the organization with a role: a built-in one, or Custom with its id.</summary>
public sealed record InvitationRequest(string Email, OrganizationRole? Role, Guid? CustomRoleId = null);

/// <summary>An invitation nobody has accepted, revoked or let expire yet, and the role it offers.</summary>
public sealed record PendingInvitation(
    Guid Id, string Email, OrganizationRole Role, Guid? CustomRoleId, string RoleName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// One entry in the organization's membership log. <c>actorUserId</c> made the change; <c>memberUserId</c> is who it is
/// about (null for an invitation not accepted yet and for a change to a role itself). <c>previousRole</c> and
/// <c>role</c> are set where the kind has them, with the custom role's id and its name at the time; for RoleCreated,
/// RoleUpdated and RoleDeleted, <c>customRoleId</c> and <c>roleName</c> are the role, and a rename keeps the old name in
/// <c>previousRoleName</c>.
/// </summary>
public sealed record MembershipLogEntry(
    Guid Id,
    MembershipEventKind Kind,
    Guid ActorUserId,
    Guid? MemberUserId,
    Guid? InvitationId,
    OrganizationRole? PreviousRole,
    Guid? PreviousCustomRoleId,
    string? PreviousRoleName,
    OrganizationRole? Role,
    Guid? CustomRoleId,
    string? RoleName,
    DateTimeOffset OccurredAt);

/// <summary>
/// The organization's roles and what each can do: the four built-in ones, most access first, then its custom roles by
/// name, with every permission in the catalogue (<see cref="RolePermissions"/>).
/// </summary>
public sealed record RoleCatalogue(IReadOnlyList<RoleSummary> Roles, IReadOnlyList<PermissionSummary> Permissions);

/// <summary>
/// A role: built-in (<c>customRoleId</c> null), or Custom with its id. <c>description</c> is one line on what it is
/// for, generated from its permissions for a custom role. <c>people</c> counts the members who have it. A built-in role
/// is last updated by ScreenHire.
/// </summary>
public sealed record RoleSummary(
    OrganizationRole Role,
    Guid? CustomRoleId,
    string Name,
    string Description,
    IReadOnlyList<Permission> Permissions,
    int People,
    LastUpdate LastUpdated);

/// <summary>
/// A permission in the order the Roles page lists it: its group, its name and one line on what it covers.
/// <c>ownerOnly</c> ones never go into a custom role; <c>everyRole</c> ones are in every role.
/// </summary>
public sealed record PermissionSummary(Permission Permission, PermissionGroup Group, string Name, string Description, bool OwnerOnly, bool EveryRole);

/// <summary>One of the organization's custom roles, for its page. <c>copiedFrom</c> is the name of the role it started as.</summary>
public sealed record CustomRoleDetail(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyList<Permission> Permissions,
    int People,
    string CopiedFrom,
    DateTimeOffset CreatedAt,
    LastUpdate LastUpdated);

/// <summary>
/// Makes a custom role by copying a role: a built-in one (<c>copyFrom</c>) or a custom one (<c>copyFromRoleId</c>), never
/// both. Owner-only permissions are never copied.
/// </summary>
public sealed record CreateRoleRequest(string Name, OrganizationRole? CopyFrom, Guid? CopyFromRoleId = null);

/// <summary>Renames a custom role and sets what it can do.</summary>
public sealed record UpdateRoleRequest(string Name, IReadOnlyList<Permission>? Permissions);

internal static class TeamMappings
{
    public static RoleSummary ToSummary(this RoleDefinition role, int people) =>
        new(role.Role, null, role.Role.ToString(), role.Description, InOrder(role.Permissions), people, LastUpdate.ByScreenHire(null));

    public static RoleSummary ToSummary(this CustomRole role, int people, ChangeStamp? lastChange) =>
        new(OrganizationRole.Custom, role.Id, role.Name, RolePermissions.Summarize(role.Permissions), InOrder(RolePermissions.ForCustom(role.Permissions)),
            people, LastUpdate.From(lastChange, role.UpdatedAt));

    public static CustomRoleDetail ToDetail(this CustomRole role, int people, ChangeStamp? lastChange) =>
        new(role.Id, role.Name, RolePermissions.Summarize(role.Permissions), InOrder(RolePermissions.ForCustom(role.Permissions)), people,
            role.CopiedFrom, role.CreatedAt, LastUpdate.From(lastChange, role.UpdatedAt));

    public static IReadOnlyList<PermissionSummary> PermissionSummaries() =>
        [.. RolePermissions.Permissions.Select(p => new PermissionSummary(p.Permission, p.Group, p.Name, p.Description, p.OwnerOnly, p.EveryRole))];

    public static PendingInvitation ToPending(this OrganizationInvitation invitation, string roleName) =>
        new(invitation.Id, invitation.Email, invitation.Role, invitation.CustomRoleId, roleName, invitation.CreatedAt, invitation.ExpiresAt);

    public static MembershipLogEntry ToLogEntry(this MembershipEvent entry) =>
        new(entry.Id, entry.Kind, entry.ActorUserId, entry.MemberUserId, entry.InvitationId,
            entry.PreviousRole, entry.PreviousCustomRoleId, NameOf(entry.PreviousRole, entry.PreviousCustomRoleName),
            entry.Role, entry.CustomRoleId, NameOf(entry.Role, entry.CustomRoleName), entry.OccurredAt);

    private static string? NameOf(OrganizationRole? role, string? customName) =>
        role == OrganizationRole.Custom ? customName : customName ?? role?.ToString();

    private static List<Permission> InOrder(IReadOnlySet<Permission> permissions) =>
        [.. RolePermissions.Permissions.Select(p => p.Permission).Where(permissions.Contains)];
}

internal sealed class ChangeMemberRoleRequestValidator : AbstractValidator<ChangeMemberRoleRequest>
{
    // Nullable in the contract so a body without a role is a 400, not the enum's default, which is Owner.
    public ChangeMemberRoleRequestValidator()
    {
        RuleFor(r => r.Role).NotNull().WithMessage("Choose a role.").IsInEnum();
        RuleFor(r => r.CustomRoleId).RoleIdMatches(r => r.Role);
    }
}

internal sealed class InvitationRequestValidator : AbstractValidator<InvitationRequest>
{
    public InvitationRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256)
            .Must(MagicLinkRequestValidator.IsBareAddress).WithMessage("Enter an email address, like name@example.com.");
        RuleFor(r => r.Role).NotNull().WithMessage("Choose a role.").IsInEnum();
        RuleFor(r => r.CustomRoleId).RoleIdMatches(r => r.Role);
    }
}

internal sealed class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public CreateRoleRequestValidator()
    {
        RuleFor(r => r.Name).RoleName();
        RuleFor(r => r.CopyFrom).IsInEnum().NotEqual(OrganizationRole.Custom).WithMessage("Copy a custom role by its id.");
        RuleFor(r => r.CopyFrom).NotNull().When(r => r.CopyFromRoleId is null).WithMessage("Choose a role to copy.");
        RuleFor(r => r.CopyFromRoleId).Null().When(r => r.CopyFrom is not null).WithMessage("Copy one role.");
    }
}

internal sealed class UpdateRoleRequestValidator : AbstractValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator()
    {
        RuleFor(r => r.Name).RoleName();
        RuleFor(r => r.Permissions).NotNull().WithMessage("Say what the role can do.");
        RuleForEach(r => r.Permissions).IsInEnum();
        RuleFor(r => r.Permissions)
            .Must(permissions => permissions is null || !permissions.Any(p => Enum.IsDefined(p) && RolePermissions.Definition(p).OwnerOnly))
            .WithMessage("Manage Owners is for Owners only. A custom role cannot have it.");
        RuleFor(r => r.Permissions)
            .Must(permissions => permissions is null
                || RolePermissions.Permissions.Where(p => p.EveryRole).All(p => permissions.Contains(p.Permission)))
            .WithMessage("Every role sees jobs and candidates.");
        // The delete route asks for Add notes and tags first (NotePolicy then allows anyone's note), so one without the
        // other would do nothing.
        RuleFor(r => r.Permissions)
            .Must(permissions => permissions is null
                || !permissions.Contains(Permission.DeleteAnyNote) || permissions.Contains(Permission.AddNotesAndTags))
            .WithMessage("Delete anyone's notes comes with Add notes and tags. Allow both, or neither.");
    }
}

internal static class RoleValidation
{
    /// <summary>
    /// A custom role's name: 1 to 50 characters on one line, and not one of the built-in roles' names. One line because
    /// the name goes into the plain-text invitation email and the membership log as it was typed.
    /// </summary>
    public static IRuleBuilderOptions<T, string> RoleName<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Enter a name.")
            .Must(name => name is null || name.Trim().Length <= CustomRole.MaxNameLength)
            .WithMessage($"Use at most {CustomRole.MaxNameLength} characters.")
            .Must(name => name is null || !name.Any(char.IsControl))
            .WithMessage("Keep the name on one line, with no tabs.")
            .Must(name => name is null || !RolePermissions.IsBuiltInName(name))
            .WithMessage("Owner, Admin, Recruiter and Viewer are ScreenHire's roles. Choose another name.");

    /// <summary>A custom role's id is there exactly when the role is Custom.</summary>
    public static void RoleIdMatches<T>(this IRuleBuilder<T, Guid?> rule, Func<T, OrganizationRole?> role) =>
        rule.Must((request, id) => (role(request) == OrganizationRole.Custom) == id.HasValue)
            .WithMessage((request, _) => role(request) == OrganizationRole.Custom ? "Choose a role." : "Only a custom role takes an id.");
}
