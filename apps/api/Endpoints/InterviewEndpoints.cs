using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Interview scheduling (docs/architecture/interview-scheduling.md). People only, like the email routes: no API key reaches
/// it. Scheduling emails the candidate, so it needs the permission that emails them, <c>EmailCandidates</c>.
/// </summary>
internal static class InterviewEndpoints
{
    public static IEndpointRouteBuilder MapInterviewEndpoints(this IEndpointRouteBuilder app)
    {
        var interviewers = app.MapGroup("/v1/interviewers").WithTags("Interviews").RequireAuthorization();
        interviewers.MapGet("", ListInterviewers).WithName("ListInterviewers")
            .RequirePermission(Permission.EmailCandidates)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // The members who see the job, by the one rule the API applies elsewhere (ApplicationChanges.AudienceAsync): everyone
    // who sees all jobs, and the members on its hiring team. A job outside the caller's teams is a 404, like another
    // organization's: the job filter hides it.
    private static async Task<Results<Ok<List<Interviewer>>, NotFound>> ListInterviewers(
        [FromQuery] Guid jobId, ScreenHireDbContext db, ITenantContext tenant, CalendarAccess calendar, CancellationToken cancellationToken)
    {
        var team = await db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.HiringTeam).SingleOrDefaultAsync(cancellationToken);
        if (team is null)
        {
            return TypedResults.NotFound();
        }

        var members = (await HiringTeams.MembersAsync(db, tenant, cancellationToken)).Values
            .Where(m => m.SeesAllJobs || team.Contains(m.Id))
            .ToList();
        var userIds = members.Select(m => m.UserId).ToList();
        var connections = await db.CalendarConnections.AsNoTracking()
            .Where(c => userIds.Contains(c.UserId))
            .Select(c => new { c.UserId, c.Provider, c.Problem })
            .ToListAsync(cancellationToken);
        var byUser = connections.ToDictionary(c => c.UserId);

        return TypedResults.Ok(members
            .OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(m =>
            {
                // A row the host cannot use (its provider is not configured here) does not count as connected.
                var usable = byUser.TryGetValue(m.UserId, out var connection) && connection.Problem is null && calendar.Options.IsAvailable(connection.Provider);
                return new Interviewer(m.UserId, m.DisplayName, usable, usable ? connection!.Provider : null);
            })
            .ToList());
    }
}
