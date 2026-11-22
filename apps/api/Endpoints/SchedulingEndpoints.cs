using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// What the Schedule interview sheet checks before anything reaches the candidate (docs/architecture/interview-scheduling.md,
/// "Changing course"): the times pick from our times would offer, so the sheet can say when nothing is open in the next 7 days,
/// and who is free at the times the team looks at. Both read the interviewers' calendars and bookings for the whole
/// organization, through <see cref="OrganizationScope"/>, after checking the member sees the job and every interviewer does;
/// they answer only open times and free or busy, never what anyone's busy time is. People only, with the permission that
/// schedules interviews (<c>EmailCandidates</c>).
/// </summary>
internal static class SchedulingEndpoints
{
    private const int MaxPreviewSlots = 50;

    private const int MaxFreeTimes = 10;

    public static IEndpointRouteBuilder MapSchedulingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/scheduling").WithTags("Interviews").RequireAuthorization();
        group.MapPost("/open-times", OpenTimes).WithName("PreviewInterviewOpenTimes")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EmailCandidates)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/free", Free).WithName("ListInterviewersFree")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EmailCandidates)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Results<Ok<OpenTimes>, NotFound, ValidationProblem, ProblemHttpResult>> OpenTimes(
        OpenTimesRequest request,
        ScreenHireDbContext db,
        ITenantContext tenant,
        IServiceScopeFactory scopes,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await CheckAsync(db, tenant, request.JobId, request.InterviewerUserIds, cancellationToken) is { } refused)
        {
            return refused.Match<Results<Ok<OpenTimes>, NotFound, ValidationProblem, ProblemHttpResult>>(notFound => notFound, invalid => invalid);
        }

        var now = time.GetUtcNow();
        var dayStart = Parse(request.DayStart, InterviewRequest.DefaultDayStart);
        var dayEnd = Parse(request.DayEnd, InterviewRequest.DefaultDayEnd);
        var pick = new InterviewPick(InterviewOption.OurTimes, request.Hours, request.UseBlocks);
        if ((InterviewRequest.Problem(request.DurationMinutes, request.WindowStart, request.WindowEnd, request.TimeZone, dayStart, dayEnd, now)
                ?? InterviewRequest.PickProblem(pick, request.DurationMinutes, request.WindowStart, request.WindowEnd, now)) is { } problem)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("windowEnd", problem));
        }

        InterviewRequest.TryFindZone(request.TimeZone, out var zone);
        var hours = request.Hours ?? WeeklyHours.Weekdays(dayStart, dayEnd);
        try
        {
            var found = await OrganizationScope.RunAsync(scopes, tenant.OrganizationId!.Value, scope =>
                scope.GetRequiredService<InterviewAvailability>().OpenSlotsAsync(
                    [.. request.InterviewerUserIds.Distinct()], request.WindowStart, request.WindowEnd, TimeSpan.FromMinutes(request.DurationMinutes),
                    zone, hours, request.UseBlocks, [], own: null, cancellationToken));
            var week = now.AddDays(7);
            return TypedResults.Ok(new OpenTimes([.. found.Slots.Take(MaxPreviewSlots)], found.Slots.Count(s => s < week), found.UnavailableInterviewers));
        }
        catch (CalendarApiException)
        {
            return TypedResults.Problem("The calendars cannot be read just now. Try again in a minute.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<Results<Ok<List<FreeTime>>, NotFound, ValidationProblem>> Free(
        FreeTimesRequest request,
        ScreenHireDbContext db,
        ITenantContext tenant,
        IServiceScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        if (await CheckAsync(db, tenant, request.JobId, request.InterviewerUserIds, cancellationToken) is { } refused)
        {
            return refused.Match<Results<Ok<List<FreeTime>>, NotFound, ValidationProblem>>(notFound => notFound, invalid => invalid);
        }

        if (request.Starts is not { Count: >= 1 and <= MaxFreeTimes })
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("starts", $"Ask about 1 to {MaxFreeTimes} times."));
        }

        if (!InterviewRequest.AllowedDurations.Contains(request.DurationMinutes))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("durationMinutes", $"An interview lasts {string.Join(", ", InterviewRequest.AllowedDurations)} minutes."));
        }

        // The interview being moved must be on this job and one the member sees.
        if (request.InterviewId is { } id && !await db.InterviewRequests.AsNoTracking().AnyAsync(r => r.Id == id && r.JobId == request.JobId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var answers = await OrganizationScope.RunAsync(scopes, tenant.OrganizationId!.Value, async scope =>
        {
            var own = request.InterviewId is { } interviewId
                ? await scope.GetRequiredService<ScreenHireDbContext>().InterviewRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == interviewId, cancellationToken)
                : null;
            return await scope.GetRequiredService<InterviewAvailability>().FreeAtAsync(
                [.. request.InterviewerUserIds.Distinct()], [.. request.Starts.Select(s => s.ToUniversalTime())], TimeSpan.FromMinutes(request.DurationMinutes), own, cancellationToken);
        });
        return TypedResults.Ok(answers
            .GroupBy(a => a.Start)
            .OrderBy(g => g.Key)
            .Select(g => new FreeTime(g.Key, [.. g.Select(a => new InterviewerFree(a.UserId, a.State))]))
            .ToList());
    }

    // A job the member sees, and interviewers who are members who see it: a 404 for the job, a 400 for the people.
    private static async Task<OneOf?> CheckAsync(
        ScreenHireDbContext db, ITenantContext tenant, Guid jobId, IReadOnlyList<Guid>? interviewerUserIds, CancellationToken cancellationToken)
    {
        var team = await db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.HiringTeam).SingleOrDefaultAsync(cancellationToken);
        if (team is null)
        {
            return new OneOf(TypedResults.NotFound(), null);
        }

        var members = await HiringTeams.MembersAsync(db, tenant, cancellationToken);
        var byUser = members.Values.ToDictionary(m => m.UserId);
        if (interviewerUserIds is not { Count: >= 1 and <= 5 } chosen
            || chosen.Any(userId => !byUser.TryGetValue(userId, out var member) || !(member.SeesAllJobs || team.Contains(member.Id))))
        {
            return new OneOf(null, TypedResults.ValidationProblem(ValidationExtensions.Problem("interviewerUserIds", "Choose 1 to 5 members of the team who see this job.")));
        }

        return null;
    }

    private static TimeOnly Parse(string? text, TimeOnly fallback) =>
        text is not null && TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : fallback;

    // One refusal or the other.
    private sealed record OneOf(NotFound? NotFound, ValidationProblem? Invalid)
    {
        public T Match<T>(Func<NotFound, T> notFound, Func<ValidationProblem, T> invalid) => NotFound is { } n ? notFound(n) : invalid(Invalid!);
    }
}
