using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Realtime;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The signed-in user's organization's team: its members and their roles, invitations by email, and the membership
/// log. Every member can see who is on the team; Owners and Admins manage it, within <see cref="TeamPolicy"/>, and
/// nobody can remove or demote the last Owner. Every change writes a <see cref="MembershipEvent"/>. Accepting an
/// invitation is a sign-in, so it lives in <see cref="AuthEndpoints"/>.
/// </summary>
internal static class TeamEndpoints
{
    // A recent window, not the whole history: the log is for the team screen. Paging comes with a screen that needs it.
    private const int LogEntries = 200;

    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/organization").WithTags("Team").RequireAuthorization();
        group.MapGet("/members", ListMembers).WithName("ListTeamMembers").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/invitations", ListInvitations).WithName("ListInvitations")
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/membership-log", ListMembershipLog).WithName("ListMembershipLog")
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);

        var mutations = group.MapGroup("").RequireAntiforgeryToken();
        mutations.MapPut("/members/{memberId:guid}/role", ChangeMemberRole).WithName("ChangeMemberRole")
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        mutations.MapDelete("/members/{memberId:guid}", RemoveMember).WithName("RemoveMember")
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        // Sends email, so it counts against the same per-IP limit as the sign-in links.
        mutations.MapPost("/invitations", CreateInvitation).WithName("CreateInvitation")
            .RequireOrganizationAdmin()
            .RequireRateLimiting(AuthenticationSetup.RateLimitPolicy)
            .Produces(StatusCodes.Status401Unauthorized);
        mutations.MapDelete("/invitations/{invitationId:guid}", RevokeInvitation).WithName("RevokeInvitation")
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Ok<List<TeamMember>>> ListMembers(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        TypedResults.Ok(await (
            from member in InOrganization(db, tenant).AsNoTracking()
            join user in db.Users on member.UserId equals user.Id
            orderby member.CreatedAt
            select new TeamMember(member.Id, user.Id, user.Email ?? "", user.DisplayName ?? user.Email ?? "", member.Role, member.CreatedAt))
            .ToListAsync(cancellationToken));

    private static async Task<Ok<List<PendingInvitation>>> ListInvitations(
        ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var invitations = await db.OrganizationInvitations.AsNoTracking()
            .Where(i => i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(invitations.Select(i => i.ToPending()).ToList());
    }

    private static async Task<Ok<List<MembershipLogEntry>>> ListMembershipLog(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var entries = await db.MembershipEvents.AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .Take(LogEntries)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(entries.Select(e => e.ToLogEntry()).ToList());
    }

    private static async Task<Results<Ok<TeamMember>, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> ChangeMemberRole(
        Guid memberId, ChangeMemberRoleRequest request, IValidator<ChangeMemberRoleRequest> validator, ScreenHireDbContext db,
        ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var role = request.Role!.Value;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Invitations.LockMembersAsync(db, tenant.OrganizationId!.Value, cancellationToken);
        var members = await InOrganization(db, tenant).ToListAsync(cancellationToken);
        if (members.SingleOrDefault(m => m.Id == memberId) is not { } member)
        {
            return TypedResults.NotFound();
        }

        if (members.SingleOrDefault(m => m.UserId == tenant.UserId) is not { } actor
            || !TeamPolicy.MayChange(actor.Role, member.Role, role))
        {
            return OwnersOnly("Only an owner can make someone an owner or change an owner's role.");
        }

        if (member.Role != role)
        {
            if (IsLastOwner(members, member))
            {
                return LastOwner("Make someone else an owner first.");
            }

            db.MembershipEvents.Add(new MembershipEvent
            {
                OrganizationId = member.OrganizationId,
                Kind = MembershipEventKind.RoleChanged,
                ActorUserId = actor.UserId,
                MemberUserId = member.UserId,
                PreviousRole = member.Role,
                Role = role,
                OccurredAt = time.GetUtcNow(),
            });
            member.Role = role;
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == member.UserId)
            .Select(u => new { u.Email, u.DisplayName })
            .SingleAsync(cancellationToken);
        return TypedResults.Ok(new TeamMember(
            member.Id, member.UserId, user.Email ?? "", user.DisplayName ?? user.Email ?? "", member.Role, member.CreatedAt));
    }

    // Removing a member ends their access on their next request (Memberships.ResolveTenantAsync reads the membership
    // every time) and closes their open live-update connections into this organization at once.
    private static async Task<Results<NoContent, NotFound, Conflict<ProblemDetails>, ProblemHttpResult>> RemoveMember(
        Guid memberId, ScreenHireDbContext db, ITenantContext tenant, RealtimeConnections connections, TimeProvider time,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Invitations.LockMembersAsync(db, tenant.OrganizationId!.Value, cancellationToken);
        var members = await InOrganization(db, tenant).ToListAsync(cancellationToken);
        if (members.SingleOrDefault(m => m.Id == memberId) is not { } member)
        {
            return TypedResults.NotFound();
        }

        if (members.SingleOrDefault(m => m.UserId == tenant.UserId) is not { } actor || !TeamPolicy.MayRemove(actor.Role, member.Role))
        {
            return OwnersOnly("Only an owner can remove an owner.");
        }

        if (IsLastOwner(members, member))
        {
            return LastOwner("An organization needs an owner. Make someone else an owner first.");
        }

        db.OrganizationMembers.Remove(member);
        db.MembershipEvents.Add(new MembershipEvent
        {
            OrganizationId = member.OrganizationId,
            Kind = MembershipEventKind.Removed,
            ActorUserId = actor.UserId,
            MemberUserId = member.UserId,
            PreviousRole = member.Role,
            OccurredAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        connections.Close(member.UserId, member.OrganizationId);
        return TypedResults.NoContent();
    }

    // The email goes out inside the request, after the invitation commits, as sign-in links do (MagicLinks): the token
    // must never sit in the outbox's message table in the clear, and the inviter sees a failed send as an error.
    private static async Task<Results<Created<PendingInvitation>, ValidationProblem, NotFound, Conflict<ProblemDetails>, ProblemHttpResult>> CreateInvitation(
        InvitationRequest request, IValidator<InvitationRequest> validator, IServiceProvider services, ScreenHireDbContext db,
        ITenantContext tenant, TimeProvider time, IOptions<AuthOptions> auth, IOptions<DomainOptions> domains,
        CancellationToken cancellationToken)
    {
        if (services.GetService<IEmailSender>() is not { } emailSender)
        {
            return TypedResults.NotFound(); // no email sender is configured, so there is no way to deliver an invitation
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var organizationId = tenant.OrganizationId!.Value;
        var userId = tenant.UserId!.Value;
        var role = request.Role!.Value;
        var email = MagicLinks.NormalizeEmail(request.Email);

        // Under the member lock, so two invitations to one address at once cannot both stay open.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Invitations.LockMembersAsync(db, organizationId, cancellationToken);
        var actorRole = await InOrganization(db, tenant).AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.Role)
            .SingleAsync(cancellationToken);
        if (!TeamPolicy.MayAssign(actorRole, role))
        {
            return OwnersOnly("Only an owner can invite another owner.");
        }

        // Identity normalizes emails to upper case (UpperInvariantLookupNormalizer).
        var normalizedEmail = email.ToUpperInvariant();
        var alreadyMember = await (
            from member in InOrganization(db, tenant)
            join user in db.Users on member.UserId equals user.Id
            where user.NormalizedEmail == normalizedEmail
            select member.Id).AnyAsync(cancellationToken);
        if (alreadyMember)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Already a member",
                Detail = "That person is already in this organization. Change their role instead.",
                Status = StatusCodes.Status409Conflict,
            });
        }

        var now = time.GetUtcNow();
        var lifetime = auth.Value.Invitation.Lifetime;
        var token = Invitations.NewToken(organizationId);
        var invitation = new OrganizationInvitation
        {
            OrganizationId = organizationId,
            Email = email,
            Role = role,
            TokenHash = MagicLinks.Hash(token),
            InvitedByUserId = userId,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };

        // One open invitation per address: a new one replaces the earlier ones, whose links stop working. Replacing is
        // revoking, so an Admin cannot replace an invitation to become an Owner, as RevokeInvitation refuses it too.
        var earlier = await db.OrganizationInvitations
            .Where(i => i.Email == email && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        if (earlier.Any(i => !TeamPolicy.MayAssign(actorRole, i.Role)))
        {
            return OwnersOnly("Only an owner can replace an invitation to become an owner.");
        }

        foreach (var replaced in earlier.Where(i => i.Revoke(now)))
        {
            db.MembershipEvents.Add(InvitationEvent(MembershipEventKind.InvitationRevoked, replaced, userId, now));
        }

        db.OrganizationInvitations.Add(invitation);
        db.MembershipEvents.Add(InvitationEvent(MembershipEventKind.Invited, invitation, userId, now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var organizationName = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId).Select(o => o.Name).SingleAsync(cancellationToken);
        var inviterName = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.DisplayName ?? u.Email ?? "A colleague").SingleAsync(cancellationToken);
        var link = domains.Value.AppUrl($"{Invitations.AcceptPath}#invitation={token}");
        await emailSender.SendAsync(Invitations.Compose(email, organizationName, inviterName, role, link, lifetime), cancellationToken);

        return TypedResults.Created($"/v1/organization/invitations/{invitation.Id}", invitation.ToPending());
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RevokeInvitation(
        Guid invitationId, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var userId = tenant.UserId!.Value;
        var invitation = await db.OrganizationInvitations.SingleOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        if (invitation is null || !invitation.IsOpen(now))
        {
            return TypedResults.NotFound(); // accepted, revoked and expired invitations are not in the pending list either
        }

        var actorRole = await InOrganization(db, tenant).AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.Role)
            .SingleAsync(cancellationToken);
        if (!TeamPolicy.MayAssign(actorRole, invitation.Role))
        {
            return OwnersOnly("Only an owner can revoke an invitation to become an owner.");
        }

        invitation.Revoke(now);
        db.MembershipEvents.Add(InvitationEvent(MembershipEventKind.InvitationRevoked, invitation, userId, now));
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The current organization's memberships. The <c>organization_members</c> filter and row-level security also show
    /// the signed-in user's own memberships of other organizations (that is how sign-in finds them), so a team route
    /// narrows to the tenant here. Without it, a member of two organizations would see, and could aim a change at,
    /// their membership of the other one.
    /// </summary>
    private static IQueryable<OrganizationMember> InOrganization(ScreenHireDbContext db, ITenantContext tenant) =>
        db.OrganizationMembers.Where(m => m.OrganizationId == tenant.OrganizationId);

    private static bool IsLastOwner(List<OrganizationMember> members, OrganizationMember member) =>
        member.Role == OrganizationRole.Owner && members.Count(m => m.Role == OrganizationRole.Owner) == 1;

    private static MembershipEvent InvitationEvent(
        MembershipEventKind kind, OrganizationInvitation invitation, Guid actorUserId, DateTimeOffset now) =>
        new()
        {
            OrganizationId = invitation.OrganizationId,
            Kind = kind,
            ActorUserId = actorUserId,
            InvitationId = invitation.Id,
            Role = invitation.Role,
            OccurredAt = now,
        };

    private static ProblemHttpResult OwnersOnly(string detail) =>
        TypedResults.Problem(title: "Owners only", detail: detail, statusCode: StatusCodes.Status403Forbidden);

    private static Conflict<ProblemDetails> LastOwner(string detail) =>
        TypedResults.Conflict(new ProblemDetails { Title = "Last owner", Detail = detail, Status = StatusCodes.Status409Conflict });
}
