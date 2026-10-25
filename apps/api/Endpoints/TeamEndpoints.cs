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
/// log. Every member can see who is on the team; a role with Manage the team manages it, within <see cref="TeamPolicy"/>
/// (nobody gives a role with more access than their own), and nobody can remove or demote the last Owner. Every change
/// writes a <see cref="MembershipEvent"/>. The roles themselves are <see cref="RoleEndpoints"/>. Accepting an invitation
/// is a sign-in, so it lives in <see cref="AuthEndpoints"/>. Each member also has a job access, All jobs or Only jobs
/// they're on (ADR-0015); the jobs they are on change through each job's hiring team (<see cref="HiringTeamEndpoints"/>).
/// </summary>
internal static class TeamEndpoints
{
    // A recent window, not the whole history: the log is for the team screen. Paging comes with a screen that needs it.
    private const int LogEntries = 200;

    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/organization").WithTags("Team").RequireAuthorization();
        group.MapGet("/members", ListMembers).WithName("ListTeamMembers").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/members/{memberId:guid}/jobs", ListMemberJobs).WithName("ListMemberJobs").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/invitations", ListInvitations).WithName("ListInvitations")
            .RequirePermission(Permission.ManageTeam)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/membership-log", ListMembershipLog).WithName("ListMembershipLog")
            .RequirePermission(Permission.ManageTeam)
            .Produces(StatusCodes.Status401Unauthorized);

        var mutations = group.MapGroup("").RequireAntiforgeryToken();
        mutations.MapPut("/members/{memberId:guid}/role", ChangeMemberRole).WithName("ChangeMemberRole")
            .RequirePermission(Permission.ManageTeam)
            .Produces(StatusCodes.Status401Unauthorized);
        mutations.MapPut("/members/{memberId:guid}/job-access", ChangeMemberJobAccess).WithName("ChangeMemberJobAccess")
            .RequirePermission(Permission.ManageTeam)
            .Produces(StatusCodes.Status401Unauthorized);
        mutations.MapDelete("/members/{memberId:guid}", RemoveMember).WithName("RemoveMember")
            .RequirePermission(Permission.ManageTeam)
            .Produces(StatusCodes.Status401Unauthorized);
        // Sends email, so it counts against the same per-IP limit as the sign-in links.
        mutations.MapPost("/invitations", CreateInvitation).WithName("CreateInvitation")
            .RequirePermission(Permission.ManageTeam)
            .RequireRateLimiting(AuthenticationSetup.RateLimitPolicy)
            .Produces(StatusCodes.Status401Unauthorized);
        mutations.MapDelete("/invitations/{invitationId:guid}", RevokeInvitation).WithName("RevokeInvitation")
            .RequirePermission(Permission.ManageTeam)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Ok<List<TeamMember>>> ListMembers(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var members = await (
            from member in InOrganization(db, tenant).AsNoTracking()
            join user in db.Users on member.UserId equals user.Id
            orderby member.CreatedAt
            select new
            {
                member.Id, member.UserId, user.Email, user.DisplayName, member.Role, member.CustomRoleId, member.CreatedAt, member.JobAccess,
            })
            .ToListAsync(cancellationToken);
        var names = await RoleAccess.CustomNamesAsync(db, cancellationToken);
        var jobCounts = await JobCountsAsync(db, cancellationToken);
        return TypedResults.Ok(members
            .Select(m => new TeamMember(
                m.Id, m.UserId, m.Email ?? "", m.DisplayName ?? m.Email ?? "", m.Role, m.CustomRoleId,
                RoleAccess.NameOf(m.Role, m.CustomRoleId, names), m.CreatedAt,
                m.JobAccess, JobAccessRules.SeesAllJobs(m.Role, m.JobAccess), jobCounts.GetValueOrDefault(m.Id)))
            .ToList());
    }

    // The jobs whose hiring team the member is on, among those the reader sees: the job filter applies to the reader.
    private static async Task<Results<Ok<List<MemberJob>>, NotFound>> ListMemberJobs(
        Guid memberId, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (!await InOrganization(db, tenant).AnyAsync(m => m.Id == memberId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => j.HiringTeam.Contains(memberId))
            .OrderBy(j => j.Title)
            .Select(j => new MemberJob(j.Id, j.Title, j.Status))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(jobs);
    }

    /// <summary>
    /// How many of the jobs the reader sees each member is on, by member id. One query of every visible job's team,
    /// counted here: an organization has tens of jobs, not thousands.
    /// </summary>
    private static async Task<Dictionary<Guid, int>> JobCountsAsync(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var teams = await db.Jobs.AsNoTracking().Select(j => j.HiringTeam).ToListAsync(cancellationToken);
        return teams.SelectMany(team => team.Distinct()).GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
    }

    private static async Task<Ok<List<PendingInvitation>>> ListInvitations(
        ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var invitations = await db.OrganizationInvitations.AsNoTracking()
            .Where(i => i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
        var names = await RoleAccess.CustomNamesAsync(db, cancellationToken);
        return TypedResults.Ok(invitations.Select(i => i.ToPending(RoleAccess.NameOf(i.Role, i.CustomRoleId, names))).ToList());
    }

    // Job titles come through the job, under the reader's job access: a reader on Only jobs they're on gets no entry
    // about a job they cannot see, so the log never names one (ADR-0015). The job filter applies before the limit, so
    // entries about hidden jobs never crowd out older ones the reader may see.
    private static async Task<Ok<List<MembershipLogEntry>>> ListMembershipLog(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var seesAllJobs = tenant.SeesAllJobs;
        var entries = await db.MembershipEvents.AsNoTracking()
            .Where(e => seesAllJobs || e.JobId == null || db.Jobs.Any(j => j.Id == e.JobId))
            .OrderByDescending(e => e.OccurredAt)
            .Take(LogEntries)
            .ToListAsync(cancellationToken);
        var jobIds = entries.Where(e => e.JobId is not null).Select(e => e.JobId!.Value).Distinct().ToList();
        var titles = await db.Jobs.AsNoTracking()
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.Title, cancellationToken);
        var templateIds = entries.Where(e => e.HiringTemplateId is not null).Select(e => e.HiringTemplateId!.Value).Distinct().ToList();
        var templates = await db.HiringTemplates.AsNoTracking()
            .Where(t => templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        return TypedResults.Ok(entries
            .Select(e => e.ToLogEntry(
                e.JobId is { } job ? titles.GetValueOrDefault(job) : null,
                e.HiringTemplateId is { } template ? templates.GetValueOrDefault(template) : null))
            .ToList());
    }

    private static async Task<Results<Ok<TeamMember>, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> ChangeMemberRole(
        Guid memberId, ChangeMemberRoleRequest request, IValidator<ChangeMemberRoleRequest> validator, ScreenHireDbContext db,
        ITenantContext tenant, RealtimeConnections connections, TimeProvider time, CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Invitations.LockMembersAsync(db, tenant.OrganizationId!.Value, cancellationToken);
        var members = await InOrganization(db, tenant).ToListAsync(cancellationToken);
        if (members.SingleOrDefault(m => m.Id == memberId) is not { } member)
        {
            return TypedResults.NotFound();
        }

        if (await RoleAccess.ForRoleAsync(db, request.Role!.Value, request.CustomRoleId, cancellationToken) is not { } to)
        {
            return UnknownRole();
        }

        var from = await HeldAsync(db, member.Role, member.CustomRoleId, cancellationToken);
        if (members.All(m => m.UserId != tenant.UserId)
            || !TeamPolicy.MayChange(await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken), from.Permissions, to.Permissions))
        {
            return TeamProblems.Refused(
                new HashSet<Permission>([.. from.Permissions, .. to.Permissions]),
                "Only an owner can make someone an owner or change an owner's role.");
        }

        // Owner and Admin see every job, so giving one is giving All jobs (ADR-0015).
        if (!TeamPolicy.MayGiveJobAccess(tenant.SeesAllJobs, to.Role, member.JobAccess))
        {
            return TeamProblems.AllJobs();
        }

        var sawAllJobs = member.SeesAllJobs;
        if (!member.Holds(to.Role, to.CustomRoleId))
        {
            if (IsLastOwner(members, member))
            {
                return LastOwner("Make someone else an owner first.");
            }

            db.MembershipEvents.Add(new MembershipEvent
            {
                OrganizationId = member.OrganizationId,
                Kind = MembershipEventKind.RoleChanged,
                ActorUserId = tenant.UserId!.Value,
                MemberUserId = member.UserId,
                PreviousRole = from.Role,
                PreviousCustomRoleId = from.CustomRoleId,
                PreviousCustomRoleName = CustomName(from),
                Role = to.Role,
                CustomRoleId = to.CustomRoleId,
                CustomRoleName = CustomName(to),
                OccurredAt = time.GetUtcNow(),
            });
            member.AssignRole(to.Role, to.CustomRoleId);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        CloseWhenJobsChanged(connections, member, sawAllJobs);
        return TypedResults.Ok(await DescribeAsync(db, member, to.Name, cancellationToken));
    }

    // Owners and Admins see all jobs whatever the choice says, so it is refused for them rather than stored unused. A
    // member who sees only their own jobs cannot give All jobs (TeamPolicy.MayGiveJobAccess).
    private static async Task<Results<Ok<TeamMember>, NotFound, ValidationProblem, ProblemHttpResult>> ChangeMemberJobAccess(
        Guid memberId, ChangeMemberJobAccessRequest request, IValidator<ChangeMemberJobAccessRequest> validator, ScreenHireDbContext db,
        ITenantContext tenant, RealtimeConnections connections, TimeProvider time, CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var access = request.JobAccess!.Value;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Invitations.LockMembersAsync(db, tenant.OrganizationId!.Value, cancellationToken);
        var members = await InOrganization(db, tenant).ToListAsync(cancellationToken);
        if (members.SingleOrDefault(m => m.Id == memberId) is not { } member)
        {
            return TypedResults.NotFound();
        }

        var held = await HeldAsync(db, member.Role, member.CustomRoleId, cancellationToken);
        if (members.All(m => m.UserId != tenant.UserId)
            || !TeamPolicy.MayAssign(await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken), held.Permissions))
        {
            return TeamProblems.Refused(held.Permissions, "Only an owner can change an owner's access.");
        }

        if (member.Role is OrganizationRole.Owner or OrganizationRole.Admin)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("jobAccess", "Owners and Admins always see all jobs."));
        }

        if (!TeamPolicy.MayGiveJobAccess(tenant.SeesAllJobs, member.Role, access))
        {
            return TeamProblems.AllJobs();
        }

        var sawAllJobs = member.SeesAllJobs;
        var previous = member.JobAccess;
        if (member.SetJobAccess(access))
        {
            db.MembershipEvents.Add(new MembershipEvent
            {
                OrganizationId = member.OrganizationId,
                Kind = MembershipEventKind.JobAccessChanged,
                ActorUserId = tenant.UserId!.Value,
                MemberUserId = member.UserId,
                PreviousJobAccess = previous,
                JobAccess = access,
                OccurredAt = time.GetUtcNow(),
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        CloseWhenJobsChanged(connections, member, sawAllJobs);
        return TypedResults.Ok(await DescribeAsync(db, member, held.Name, cancellationToken));
    }

    // A connection joins its job groups when it opens (EventsHub), so a member who stops seeing every job reconnects
    // into the groups of their own jobs, and one who starts seeing them into all of them; the client refetches then.
    private static void CloseWhenJobsChanged(RealtimeConnections connections, OrganizationMember member, bool sawAllJobs)
    {
        if (member.SeesAllJobs != sawAllJobs)
        {
            connections.Close(member.UserId, member.OrganizationId);
        }
    }

    private static async Task<TeamMember> DescribeAsync(
        ScreenHireDbContext db, OrganizationMember member, string roleName, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == member.UserId)
            .Select(u => new { u.Email, u.DisplayName })
            .SingleAsync(cancellationToken);
        var jobCount = await db.Jobs.CountAsync(j => j.HiringTeam.Contains(member.Id), cancellationToken);
        return new TeamMember(
            member.Id, member.UserId, user.Email ?? "", user.DisplayName ?? user.Email ?? "", member.Role, member.CustomRoleId, roleName,
            member.CreatedAt, member.JobAccess, member.SeesAllJobs, jobCount);
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

        var held = await HeldAsync(db, member.Role, member.CustomRoleId, cancellationToken);
        if (members.All(m => m.UserId != tenant.UserId)
            || !TeamPolicy.MayRemove(await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken), held.Permissions))
        {
            return TeamProblems.Refused(held.Permissions, "Only an owner can remove an owner.");
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
            ActorUserId = tenant.UserId!.Value,
            MemberUserId = member.UserId,
            PreviousRole = held.Role,
            PreviousCustomRoleId = held.CustomRoleId,
            PreviousCustomRoleName = CustomName(held),
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
        var email = MagicLinks.NormalizeEmail(request.Email);

        // Under the member lock, so two invitations to one address at once cannot both stay open.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Invitations.LockMembersAsync(db, organizationId, cancellationToken);
        if (await RoleAccess.ForRoleAsync(db, request.Role!.Value, request.CustomRoleId, cancellationToken) is not { } role)
        {
            return UnknownRole();
        }

        var actor = await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken);
        if (!TeamPolicy.MayAssign(actor, role.Permissions))
        {
            return TeamProblems.Refused(role.Permissions, "Only an owner can invite another owner.");
        }

        var jobAccess = request.JobAccess ?? JobAccess.All;
        if (!TeamPolicy.MayGiveJobAccess(tenant.SeesAllJobs, role.Role, jobAccess))
        {
            return TeamProblems.AllJobs();
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
            Role = role.Role,
            CustomRoleId = role.CustomRoleId,
            JobAccess = jobAccess,
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
        var replacing = new List<(OrganizationInvitation Invitation, RoleAccess Role)>();
        foreach (var open in earlier)
        {
            var offered = await HeldAsync(db, open.Role, open.CustomRoleId, cancellationToken);
            if (!TeamPolicy.MayAssign(actor, offered.Permissions))
            {
                return TeamProblems.Refused(offered.Permissions, "Only an owner can replace an invitation to become an owner.");
            }

            replacing.Add((open, offered));
        }

        foreach (var (replaced, offered) in replacing.Where(r => r.Invitation.Revoke(now)))
        {
            db.MembershipEvents.Add(InvitationEvent(MembershipEventKind.InvitationRevoked, replaced, offered, userId, now));
        }

        db.OrganizationInvitations.Add(invitation);
        db.MembershipEvents.Add(InvitationEvent(MembershipEventKind.Invited, invitation, role, userId, now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var organizationName = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId).Select(o => o.Name).SingleAsync(cancellationToken);
        var inviterName = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.DisplayName ?? u.Email ?? "A colleague").SingleAsync(cancellationToken);
        var link = domains.Value.AppUrl($"{Invitations.AcceptPath}#invitation={token}");
        await emailSender.SendAsync(
            Invitations.Compose(email, organizationName, inviterName, Invitations.RoleWords(role), link, lifetime), cancellationToken);

        return TypedResults.Created($"/v1/organization/invitations/{invitation.Id}", invitation.ToPending(role.Name));
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

        var offered = await HeldAsync(db, invitation.Role, invitation.CustomRoleId, cancellationToken);
        if (!TeamPolicy.MayAssign(await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken), offered.Permissions))
        {
            return TeamProblems.Refused(offered.Permissions, "Only an owner can revoke an invitation to become an owner.");
        }

        invitation.Revoke(now);
        db.MembershipEvents.Add(InvitationEvent(MembershipEventKind.InvitationRevoked, invitation, offered, userId, now));
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

    /// <summary>
    /// A role somebody has or an invitation offers. Its custom role exists while anyone has it (a restricting foreign
    /// key) or an open invitation offers it (RoleEndpoints refuses the deletion), so a missing one has no permissions.
    /// </summary>
    private static async Task<RoleAccess> HeldAsync(
        ScreenHireDbContext db, OrganizationRole role, Guid? customRoleId, CancellationToken cancellationToken) =>
        await RoleAccess.ForRoleAsync(db, role, customRoleId, cancellationToken)
        ?? new RoleAccess(OrganizationRole.Custom, customRoleId, "", new HashSet<Permission>());

    private static string? CustomName(RoleAccess role) => role.Role == OrganizationRole.Custom ? role.Name : null;

    private static MembershipEvent InvitationEvent(
        MembershipEventKind kind, OrganizationInvitation invitation, RoleAccess role, Guid actorUserId, DateTimeOffset now) =>
        new()
        {
            OrganizationId = invitation.OrganizationId,
            Kind = kind,
            ActorUserId = actorUserId,
            InvitationId = invitation.Id,
            Role = invitation.Role,
            CustomRoleId = invitation.CustomRoleId,
            CustomRoleName = CustomName(role),
            JobAccess = invitation.JobAccess,
            OccurredAt = now,
        };

    // A 400, not a 404: the member or invitation is there, and the role in the body is not one of this organization's.
    private static ValidationProblem UnknownRole() =>
        TypedResults.ValidationProblem(ValidationExtensions.Problem("customRoleId", "Choose one of this organization's roles."));

    private static Conflict<ProblemDetails> LastOwner(string detail) =>
        TypedResults.Conflict(new ProblemDetails { Title = "Last owner", Detail = detail, Status = StatusCodes.Status409Conflict });
}
