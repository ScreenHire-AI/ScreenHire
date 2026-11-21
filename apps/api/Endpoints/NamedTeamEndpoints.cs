using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Content;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The organization's named teams, Settings, Team, Teams (sign-in-and-tenancy.md, "Job access"). A team is a name and some
/// of the organization's people. Adding it to a job copies its people into the job's hiring team (ADR-0015), and the job
/// keeps that copy, so changing or deleting a team changes no job. A team gives nobody access by itself, so every member
/// may read the teams to pick one; changing them needs Manage the team, as changing the people does. Session only, like
/// the hiring teams they fill: a key does not choose who works on a job (ADR-0011). Every change is a line in the Team
/// page's history (TrackedKinds).
/// </summary>
internal static class NamedTeamEndpoints
{
    public static IEndpointRouteBuilder MapNamedTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/organization/teams").WithTags("Teams").RequireAuthorization();
        group.MapGet("/", ListTeams).WithName("ListTeams").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/suggested", GetSuggestedTeam).WithName("GetSuggestedTeam").Produces(StatusCodes.Status401Unauthorized);

        var mutations = group.MapGroup("").RequireAntiforgeryToken();
        mutations.MapPost("/", CreateTeam).WithName("CreateTeam")
            .RequirePermission(Permission.ManageTeam)
            .Produces(StatusCodes.Status401Unauthorized);
        mutations.MapPut("/{teamId:guid}", UpdateTeam).WithName("UpdateTeam")
            .RequirePermission(Permission.ManageTeam)
            .Produces(StatusCodes.Status401Unauthorized);
        mutations.MapDelete("/{teamId:guid}", DeleteTeam).WithName("DeleteTeam")
            .RequirePermission(Permission.ManageTeam)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // By name. A person who left the organization is not listed, and is never copied to a job.
    private static async Task<Ok<List<TeamDetail>>> ListTeams(ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var teams = await db.NamedTeams.AsNoTracking().OrderBy(t => t.NormalizedName).ToListAsync(cancellationToken);
        var members = await HiringTeams.MembersAsync(db, tenant, cancellationToken);
        var names = await RoleAccess.CustomNamesAsync(db, cancellationToken);
        return TypedResults.Ok(teams.Select(team => Detail(team, HiringTeams.Describe(members, names, team.MemberIds))).ToList());
    }

    // Through the job filter, so a member on Only jobs they're on learns nothing about a job they cannot see. A template's
    // department is its posting default, which is what a new job from it starts with. No template, no suggestion.
    private static async Task<Ok<TeamSuggestion>> GetSuggestedTeam(Guid? templateVersionId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var version = templateVersionId is { } versionId
            ? await db.HiringTemplateVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == versionId, cancellationToken)
            : null;
        if (version is null)
        {
            return TypedResults.Ok(new TeamSuggestion(null));
        }

        var templateId = version.TemplateId;
        var department = Text.NullIfBlank(version.Definition.PostingDefaults.Department) is { } named ? Like.Exactly(named) : null;
        var teamId = await db.Jobs.AsNoTracking()
            .Where(j => j.StartedWithTeamId != null
                && (j.SourceTemplateId == templateId || (department != null && EF.Functions.ILike(j.Department!, department, Like.Escape))))
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => j.StartedWithTeamId)
            .FirstOrDefaultAsync(cancellationToken);
        return TypedResults.Ok(new TeamSuggestion(teamId));
    }

    private static async Task<Results<Created<TeamDetail>, ValidationProblem>> CreateTeam(
        TeamRequest request, IValidator<TeamRequest> validator, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var members = await HiringTeams.MembersAsync(db, tenant, cancellationToken);
        if (request.MemberIds!.Any(id => !members.ContainsKey(id)))
        {
            return TypedResults.ValidationProblem(NotMembers());
        }

        var team = NamedTeam.Create(tenant.OrganizationId!.Value, request.Name!, request.MemberIds!, time.GetUtcNow());
        if (await NameTakenAsync(db, team.NormalizedName, null, cancellationToken))
        {
            return TypedResults.ValidationProblem(NameTaken());
        }

        db.NamedTeams.Add(team);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return TypedResults.ValidationProblem(NameTaken()); // two saves of one name at once; the index decides
        }

        return TypedResults.Created($"/v1/organization/teams/{team.Id}", await DetailAsync(db, members, team, cancellationToken));
    }

    // People who left are dropped on save: the list sent back is what the dashboard showed, members only.
    private static async Task<Results<Ok<TeamDetail>, NotFound, ValidationProblem>> UpdateTeam(
        Guid teamId, TeamRequest request, IValidator<TeamRequest> validator, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var team = await db.NamedTeams.SingleOrDefaultAsync(t => t.Id == teamId, cancellationToken);
        if (team is null)
        {
            return TypedResults.NotFound();
        }

        var members = await HiringTeams.MembersAsync(db, tenant, cancellationToken);
        if (request.MemberIds!.Any(id => !members.ContainsKey(id)))
        {
            return TypedResults.ValidationProblem(NotMembers());
        }

        if (await NameTakenAsync(db, NamedTeam.Normalize(request.Name!), team.Id, cancellationToken))
        {
            return TypedResults.ValidationProblem(NameTaken());
        }

        team.Update(request.Name!, request.MemberIds!, time.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return TypedResults.ValidationProblem(NameTaken());
        }

        return TypedResults.Ok(await DetailAsync(db, members, team, cancellationToken));
    }

    // The jobs it was added to keep their people; the ones that started with it stop suggesting it (the key sets null).
    private static async Task<Results<NoContent, NotFound>> DeleteTeam(Guid teamId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var team = await db.NamedTeams.SingleOrDefaultAsync(t => t.Id == teamId, cancellationToken);
        if (team is null)
        {
            return TypedResults.NotFound();
        }

        db.NamedTeams.Remove(team);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<TeamDetail> DetailAsync(
        ScreenHireDbContext db, IReadOnlyDictionary<Guid, HiringTeams.Member> members, NamedTeam team, CancellationToken cancellationToken) =>
        Detail(team, await HiringTeams.DescribeAsync(db, members, team.MemberIds, cancellationToken));

    private static TeamDetail Detail(NamedTeam team, List<HiringTeamMember> people) => new(team.Id, team.Name, people, team.CreatedAt, team.UpdatedAt);

    private static Task<bool> NameTakenAsync(ScreenHireDbContext db, string normalizedName, Guid? except, CancellationToken cancellationToken) =>
        db.NamedTeams.AnyAsync(t => t.NormalizedName == normalizedName && t.Id != except, cancellationToken);

    private static Dictionary<string, string[]> NameTaken() => ValidationExtensions.Problem("name", "Another team has this name.");

    private static Dictionary<string, string[]> NotMembers() => ValidationExtensions.Problem("memberIds", "Choose people on the team.");
}
