using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The organization's roles (ADR-0014): the four built-in ones, which never change, and the custom roles an Owner or
/// Admin makes by copying a role and changing what it may do. Every member can see them. Making, changing and deleting
/// one needs Manage the team, and the role may hold nothing the actor's own role does not (<see cref="TeamPolicy"/>).
/// Each change runs under the member lock, so a deletion's count cannot race a member being given the role, and lands
/// in the membership log; the role's change history records what changed.
/// </summary>
internal static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/organization/roles").WithTags("Team").RequireAuthorization();
        group.MapGet("", ListRoles).WithName("ListRoles").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{roleId:guid}", GetRole).WithName("GetRole").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{roleId:guid}/history", GetRoleHistory).WithName("GetRoleHistory").Produces(StatusCodes.Status401Unauthorized);

        var mutations = group.MapGroup("").RequireAntiforgeryToken();
        mutations.MapPost("", CreateRole).WithName("CreateRole")
            .RequirePermission(Permission.ManageTeam)
            .Produces(StatusCodes.Status401Unauthorized);
        mutations.MapPut("/{roleId:guid}", UpdateRole).WithName("UpdateRole")
            .RequirePermission(Permission.ManageTeam)
            .Produces(StatusCodes.Status401Unauthorized);
        mutations.MapDelete("/{roleId:guid}", DeleteRole).WithName("DeleteRole")
            .RequirePermission(Permission.ManageTeam)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Ok<RoleCatalogue>> ListRoles(ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var people = await PeopleAsync(db, tenant, cancellationToken);
        var customs = await db.CustomRoles.AsNoTracking().OrderBy(r => r.NormalizedName).ToListAsync(cancellationToken);
        var changes = await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.Role, [.. customs.Select(SubjectId)], cancellationToken);
        return TypedResults.Ok(new RoleCatalogue(
            [
                .. RolePermissions.Roles.Select(role => role.ToSummary(people.GetValueOrDefault((role.Role, null)))),
                .. customs.Select(role => role.ToSummary(people.GetValueOrDefault((OrganizationRole.Custom, role.Id)), changes.GetValueOrDefault(SubjectId(role)))),
            ],
            TeamMappings.PermissionSummaries()));
    }

    private static async Task<Results<Ok<CustomRoleDetail>, NotFound>> GetRole(
        Guid roleId, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        await db.CustomRoles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken) is { } role
            ? TypedResults.Ok(await DetailAsync(db, tenant, role, cancellationToken))
            : TypedResults.NotFound();

    private static async Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetRoleHistory(
        Guid roleId, Guid? before, int? limit, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (ChangeHistoryRoutes.LimitProblem(limit) is { } problem)
        {
            return problem;
        }

        if (!await db.CustomRoles.AnyAsync(r => r.Id == roleId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return await ChangeHistoryRoutes.PageAsync(db, ChangeSubjectKind.Role, roleId.ToString("D"), before, limit, cancellationToken) is { } page
            ? TypedResults.Ok(page)
            : ChangeHistoryRoutes.CursorProblem();
    }

    private static async Task<Results<Created<CustomRoleDetail>, ValidationProblem, ProblemHttpResult>> CreateRole(
        CreateRoleRequest request, IValidator<CreateRoleRequest> validator, ScreenHireDbContext db, ITenantContext tenant,
        TimeProvider time, CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var organizationId = tenant.OrganizationId!.Value;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Invitations.LockMembersAsync(db, organizationId, cancellationToken);
        var source = request.CopyFrom is { } builtIn
            ? RoleAccess.BuiltIn(builtIn)
            : await RoleAccess.ForRoleAsync(db, OrganizationRole.Custom, request.CopyFromRoleId, cancellationToken);
        if (source is null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("copyFromRoleId", "Choose a role to copy."));
        }

        var now = time.GetUtcNow();
        var role = CustomRole.Copy(organizationId, request.Name, source.Name, source.Permissions, now);
        var actor = await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken);
        if (!TeamPolicy.MayManageRole(actor, null, RolePermissions.ForCustom(role.Permissions)))
        {
            return TeamProblems.MoreAccess();
        }

        if (await NameTakenAsync(db, role.NormalizedName, null, cancellationToken))
        {
            return NameTaken();
        }

        db.CustomRoles.Add(role);
        db.MembershipEvents.Add(RoleEvent(MembershipEventKind.RoleCreated, role, tenant.UserId!.Value, now));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return NameTaken(); // the name check runs under the member lock; this is the index behind it
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/v1/organization/roles/{role.Id}", await DetailAsync(db, tenant, role, cancellationToken));
    }

    private static async Task<Results<Ok<CustomRoleDetail>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateRole(
        Guid roleId, UpdateRoleRequest request, IValidator<UpdateRoleRequest> validator, ScreenHireDbContext db, ITenantContext tenant,
        TimeProvider time, CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Invitations.LockMembersAsync(db, tenant.OrganizationId!.Value, cancellationToken);
        if (await db.CustomRoles.SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken) is not { } role)
        {
            return TypedResults.NotFound();
        }

        var actor = await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken);
        var before = RolePermissions.ForCustom(role.Permissions);
        var after = RolePermissions.ForCustom(request.Permissions!);
        if (!TeamPolicy.MayManageRole(actor, before, after))
        {
            return TeamProblems.MoreAccess();
        }

        var now = time.GetUtcNow();
        var previousName = role.Name;
        role.Rename(request.Name, now);
        if (role.Name != previousName && await NameTakenAsync(db, role.NormalizedName, role.Id, cancellationToken))
        {
            return NameTaken();
        }

        role.SetPermissions(request.Permissions!, now);
        if (db.ChangeTracker.HasChanges())
        {
            var entry = RoleEvent(MembershipEventKind.RoleUpdated, role, tenant.UserId!.Value, now);
            db.MembershipEvents.Add(role.Name == previousName
                ? entry
                : new MembershipEvent
                {
                    OrganizationId = entry.OrganizationId,
                    Kind = entry.Kind,
                    ActorUserId = entry.ActorUserId,
                    PreviousRole = OrganizationRole.Custom,
                    PreviousCustomRoleId = role.Id,
                    PreviousCustomRoleName = previousName,
                    Role = entry.Role,
                    CustomRoleId = entry.CustomRoleId,
                    CustomRoleName = entry.CustomRoleName,
                    OccurredAt = now,
                });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.IsUniqueViolation())
            {
                return NameTaken();
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(await DetailAsync(db, tenant, role, cancellationToken));
    }

    // Refused while anyone has the role or an open invitation offers it, saying how many: a member is never left without
    // a role, and an invitation never offers one that is gone. The restricting foreign key on members stands behind it.
    private static async Task<Results<NoContent, NotFound, Conflict<ProblemDetails>, ProblemHttpResult>> DeleteRole(
        Guid roleId, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Invitations.LockMembersAsync(db, tenant.OrganizationId!.Value, cancellationToken);
        if (await db.CustomRoles.SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken) is not { } role)
        {
            return TypedResults.NotFound();
        }

        var actor = await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken);
        if (!TeamPolicy.MayManageRole(actor, RolePermissions.ForCustom(role.Permissions), null))
        {
            return TeamProblems.MoreAccess();
        }

        var now = time.GetUtcNow();
        var people = await db.OrganizationMembers.CountAsync(
            m => m.OrganizationId == tenant.OrganizationId && m.CustomRoleId == role.Id, cancellationToken);
        var invitations = await db.OrganizationInvitations.CountAsync(
            i => i.CustomRoleId == role.Id && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now, cancellationToken);
        if (people + invitations > 0)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Role in use",
                Detail = InUse(people, invitations),
                Status = StatusCodes.Status409Conflict,
            });
        }

        // Through the change tracker, so the role's history records the deletion.
        db.CustomRoles.Remove(role);
        db.MembershipEvents.Add(RoleEvent(MembershipEventKind.RoleDeleted, role, tenant.UserId!.Value, now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>"2 people have this role. Give them another role first.", and the open invitations that offer it.</summary>
    internal static string InUse(int people, int invitations)
    {
        var holders = people == 1 ? "1 person has this role" : $"{people} people have this role";
        var offers = invitations == 1 ? "1 open invitation offers it" : $"{invitations} open invitations offer it";
        return (people, invitations) switch
        {
            (> 0, 0) => $"{holders}. Give them another role first.",
            (0, 1) => "1 open invitation offers this role. Revoke it first.",
            (0, _) => $"{invitations} open invitations offer this role. Revoke them first.",
            _ => $"{holders} and {offers}. Give the people another role and revoke the invitations first.",
        };
    }

    private static async Task<CustomRoleDetail> DetailAsync(
        ScreenHireDbContext db, ITenantContext tenant, CustomRole role, CancellationToken cancellationToken)
    {
        var people = await db.OrganizationMembers.AsNoTracking()
            .CountAsync(m => m.OrganizationId == tenant.OrganizationId && m.CustomRoleId == role.Id, cancellationToken);
        return role.ToDetail(people, await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.Role, SubjectId(role), cancellationToken));
    }

    // Narrowed to the tenant: the members filter also shows the user's own memberships elsewhere (TeamEndpoints.InOrganization).
    private static async Task<Dictionary<(OrganizationRole, Guid?), int>> PeopleAsync(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        (await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == tenant.OrganizationId)
            .GroupBy(m => new { m.Role, m.CustomRoleId })
            .Select(g => new { g.Key.Role, g.Key.CustomRoleId, Count = g.Count() })
            .ToListAsync(cancellationToken))
        .ToDictionary(g => (g.Role, g.CustomRoleId), g => g.Count);

    private static Task<bool> NameTakenAsync(ScreenHireDbContext db, string normalizedName, Guid? except, CancellationToken cancellationToken) =>
        db.CustomRoles.AnyAsync(r => r.NormalizedName == normalizedName && r.Id != except, cancellationToken);

    private static ValidationProblem NameTaken() =>
        TypedResults.ValidationProblem(ValidationExtensions.Problem("name", "Another role has this name. Choose another."));

    private static MembershipEvent RoleEvent(MembershipEventKind kind, CustomRole role, Guid actorUserId, DateTimeOffset now) =>
        new()
        {
            OrganizationId = role.OrganizationId,
            Kind = kind,
            ActorUserId = actorUserId,
            Role = OrganizationRole.Custom,
            CustomRoleId = role.Id,
            CustomRoleName = role.Name,
            OccurredAt = now,
        };

    private static string SubjectId(CustomRole role) => role.Id.ToString("D");
}

/// <summary>The 403s for giving or editing a role beyond the actor's own (<see cref="TeamPolicy"/>).</summary>
internal static class TeamProblems
{
    public static ProblemHttpResult OwnersOnly(string detail) =>
        TypedResults.Problem(title: "Owners only", detail: detail, statusCode: StatusCodes.Status403Forbidden);

    public static ProblemHttpResult MoreAccess() =>
        TypedResults.Problem(
            title: "More access than your role",
            detail: "You can only give, change or make a role that has nothing your own role does not.",
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>"Owners only" when the role is Owner-level, otherwise "More access than your role".</summary>
    public static ProblemHttpResult Refused(IReadOnlySet<Permission> role, string ownersDetail) =>
        TeamPolicy.IsOwnerLevel(role) ? OwnersOnly(ownersDetail) : MoreAccess();
}
