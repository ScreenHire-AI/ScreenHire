using FluentValidation;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Contracts;

/// <summary>A member of the signed-in user's organization. <c>id</c> is the membership's id, which the member routes take.</summary>
public sealed record TeamMember(Guid Id, Guid UserId, string Email, string DisplayName, OrganizationRole Role, DateTimeOffset JoinedAt);

/// <summary>Gives a member another role.</summary>
public sealed record ChangeMemberRoleRequest(OrganizationRole? Role);

/// <summary>Invites a person by email to join the organization with a role.</summary>
public sealed record InvitationRequest(string Email, OrganizationRole? Role);

/// <summary>An invitation nobody has accepted, revoked or let expire yet.</summary>
public sealed record PendingInvitation(Guid Id, string Email, OrganizationRole Role, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// One entry in the organization's membership log. <c>actorUserId</c> made the change; <c>memberUserId</c> is who it is
/// about (null for an invitation not accepted yet). <c>previousRole</c> and <c>role</c> are set where the kind has them.
/// </summary>
public sealed record MembershipLogEntry(
    Guid Id,
    MembershipEventKind Kind,
    Guid ActorUserId,
    Guid? MemberUserId,
    Guid? InvitationId,
    OrganizationRole? PreviousRole,
    OrganizationRole? Role,
    DateTimeOffset OccurredAt);

/// <summary>
/// The built-in roles and what each can do: the permission catalogue (<see cref="RolePermissions"/>). It describes what the
/// server allows; it holds no organization's data.
/// </summary>
public sealed record RoleCatalogue(IReadOnlyList<RoleSummary> Roles, IReadOnlyList<PermissionSummary> Permissions);

/// <summary>A built-in role, most access first: one line on what it is for, and its permissions in catalogue order.</summary>
public sealed record RoleSummary(OrganizationRole Role, string Description, IReadOnlyList<Permission> Permissions);

/// <summary>A permission in the order the Roles page lists it: its group, its name and one line on what it covers.</summary>
public sealed record PermissionSummary(Permission Permission, PermissionGroup Group, string Name, string Description);

internal static class TeamMappings
{
    public static RoleCatalogue ToCatalogue() =>
        new(
            [.. RolePermissions.Roles.Select(role => new RoleSummary(
                role.Role,
                role.Description,
                [.. RolePermissions.Permissions.Select(p => p.Permission).Where(role.Permissions.Contains)]))],
            [.. RolePermissions.Permissions.Select(p => new PermissionSummary(p.Permission, p.Group, p.Name, p.Description))]);

    public static PendingInvitation ToPending(this OrganizationInvitation invitation) =>
        new(invitation.Id, invitation.Email, invitation.Role, invitation.CreatedAt, invitation.ExpiresAt);

    public static MembershipLogEntry ToLogEntry(this MembershipEvent entry) =>
        new(entry.Id, entry.Kind, entry.ActorUserId, entry.MemberUserId, entry.InvitationId, entry.PreviousRole, entry.Role, entry.OccurredAt);
}

internal sealed class ChangeMemberRoleRequestValidator : AbstractValidator<ChangeMemberRoleRequest>
{
    // Nullable in the contract so a body without a role is a 400, not the enum's default, which is Owner.
    public ChangeMemberRoleRequestValidator() => RuleFor(r => r.Role).NotNull().WithMessage("Choose a role.").IsInEnum();
}

internal sealed class InvitationRequestValidator : AbstractValidator<InvitationRequest>
{
    public InvitationRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256)
            .Must(MagicLinkRequestValidator.IsBareAddress).WithMessage("Enter an email address, like name@example.com.");
        RuleFor(r => r.Role).NotNull().WithMessage("Choose a role.").IsInEnum();
    }
}
