using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Api.Realtime;
using ScreenHire.Application.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Each job's hiring team, and the default team a hiring template gives the jobs started from it (ADR-0015). A member
/// on Only jobs they're on sees a job, and everything under it, only while they are on its team. Session only, like
/// managing the team: a team decides who sees a job's candidates, which API keys do not change (ADR-0011). Editing a job's
/// team needs Edit jobs, a template's Edit hiring templates. Every person added or removed lands in the membership log.
/// </summary>
internal static class HiringTeamEndpoints
{
    public static IEndpointRouteBuilder MapHiringTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/v1/jobs/{jobId:guid}/hiring-team").WithTags("Hiring teams").RequireAuthorization();
        jobs.MapGet("/", GetJobTeam).WithName("GetJobHiringTeam").Produces(StatusCodes.Status401Unauthorized);
        jobs.MapPut("/", SetJobTeam).WithName("SetJobHiringTeam")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditJobs)
            .Produces(StatusCodes.Status401Unauthorized);

        var templates = app.MapGroup("/v1/templates/{templateId:guid}/hiring-team").WithTags("Hiring teams").RequireAuthorization();
        templates.MapGet("/", GetTemplateTeam).WithName("GetHiringTemplateTeam").Produces(StatusCodes.Status401Unauthorized);
        templates.MapPut("/", SetTemplateTeam).WithName("SetHiringTemplateTeam")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditHiringTemplates)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // A job outside the caller's hiring teams is a 404, like another organization's: the job filter hides it.
    private static async Task<Results<Ok<List<HiringTeamMember>>, NotFound>> GetJobTeam(
        Guid jobId, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var team = await db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.HiringTeam).SingleOrDefaultAsync(cancellationToken);
        if (team is null)
        {
            return TypedResults.NotFound();
        }

        var members = await HiringTeams.MembersAsync(db, tenant, cancellationToken);
        return TypedResults.Ok(await HiringTeams.DescribeAsync(db, members, team, cancellationToken));
    }

    // Under the member lock, so nobody leaves the organization between the check that each id is a member and the save.
    // A member on Only jobs they're on cannot take themselves off: they would lose the job they are changing, and the
    // job_scope policy refuses the row the save would leave them unable to see.
    private static async Task<Results<Ok<List<HiringTeamMember>>, NotFound, ValidationProblem>> SetJobTeam(
        Guid jobId, HiringTeamRequest request, IValidator<HiringTeamRequest> validator, IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant, RealtimeConnections connections, TimeProvider time, CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var db = outbox.DbContext;
        // No CommitAsync here: SaveChangesAndFlushMessagesAsync commits this transaction with the outbox rows.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Invitations.LockMembersAsync(db, tenant.OrganizationId!.Value, cancellationToken);
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        var members = await HiringTeams.MembersAsync(db, tenant, cancellationToken);
        if (request.MemberIds!.Any(id => !members.ContainsKey(id)))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("memberIds", "Choose people on the team."));
        }

        var team = request.MemberIds!.ToList();
        if (request.CopyFromJobId is { } sourceId)
        {
            // Through the job filter: only a job the caller sees can be copied. Someone who left is not brought over.
            var source = await db.Jobs.AsNoTracking().Where(j => j.Id == sourceId).Select(j => j.HiringTeam).SingleOrDefaultAsync(cancellationToken);
            if (source is null)
            {
                return TypedResults.ValidationProblem(ValidationExtensions.Problem("copyFromJobId", "There is no job with this id to copy."));
            }

            team.AddRange(source.Where(members.ContainsKey));
        }

        if (!tenant.SeesAllJobs && tenant.MemberId is { } self && job.HiringTeam.Contains(self) && !team.Contains(self))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "memberIds", "You see only the jobs you're on, so you cannot take yourself off this one. Ask someone who sees all jobs."));
        }

        var now = time.GetUtcNow();
        var change = job.SetHiringTeam(team);
        if (!change.IsEmpty)
        {
            HiringTeams.Log(db, tenant, members, change, now, jobId: job.Id);
            await outbox.PublishAsync(new HiringTeamChanged(job.OrganizationId, job.Id, now));
            await HiringTeams.SaveAsync(
                connections, members, change, job.OrganizationId, job.Id, () => outbox.SaveChangesAndFlushMessagesAsync(cancellationToken), cancellationToken);
        }

        return TypedResults.Ok(await HiringTeams.DescribeAsync(db, members, job.HiringTeam, cancellationToken));
    }

    // A system template has no default team and reads as an empty one.
    private static async Task<Results<Ok<List<HiringTeamMember>>, NotFound>> GetTemplateTeam(
        Guid templateId, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var team = await db.HiringTemplates.AsNoTracking()
            .Where(t => t.Id == templateId).Select(t => t.DefaultHiringTeam).SingleOrDefaultAsync(cancellationToken);
        if (team is null)
        {
            return TypedResults.NotFound();
        }

        var members = await HiringTeams.MembersAsync(db, tenant, cancellationToken);
        return TypedResults.Ok(await HiringTeams.DescribeAsync(db, members, team, cancellationToken));
    }

    // The default team gives nobody access by itself: it becomes a job's team when someone creates a job from the
    // template (JobEndpoints.CreateJob), and that is logged per job too.
    private static async Task<Results<Ok<List<HiringTeamMember>>, NotFound, ValidationProblem, ProblemHttpResult>> SetTemplateTeam(
        Guid templateId, HiringTeamRequest request, IValidator<HiringTeamRequest> validator, ScreenHireDbContext db, ITenantContext tenant,
        TimeProvider time, CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (request.CopyFromJobId is not null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("copyFromJobId", "A template's team is not copied from a job."));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Invitations.LockMembersAsync(db, tenant.OrganizationId!.Value, cancellationToken);
        var template = await db.HiringTemplates.SingleOrDefaultAsync(t => t.Id == templateId, cancellationToken);
        if (template is null)
        {
            return TypedResults.NotFound();
        }

        if (template.IsSystem)
        {
            return TypedResults.Problem(
                "A system template has no default hiring team. Copy it to give the copy one.", statusCode: StatusCodes.Status409Conflict);
        }

        var members = await HiringTeams.MembersAsync(db, tenant, cancellationToken);
        if (request.MemberIds!.Any(id => !members.ContainsKey(id)))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("memberIds", "Choose people on the team."));
        }

        var change = template.SetDefaultHiringTeam(request.MemberIds!);
        if (!change.IsEmpty)
        {
            HiringTeams.Log(db, tenant, members, change, time.GetUtcNow(), templateId: template.Id);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return TypedResults.Ok(await HiringTeams.DescribeAsync(db, members, template.DefaultHiringTeam, cancellationToken));
    }
}
