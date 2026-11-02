using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Interview scheduling (docs/architecture/interview-scheduling.md). People only, like the email routes: no API key reaches
/// it. Scheduling emails the candidate, so it needs the permission that emails them, <c>EmailCandidates</c>.
/// </summary>
/// <remarks>
/// These routes run for a signed-in member, who may see only some jobs, so they never use <c>InterviewAvailability</c> or
/// <c>InterviewBooking</c> (those refuse a context limited to some jobs, because a booking check there could miss an
/// interviewer's other interviews). They offer and cancel; the candidate books through the public routes.
/// </remarks>
internal static class InterviewEndpoints
{
    private const int MaxInterviewers = 5;

    public static IEndpointRouteBuilder MapInterviewEndpoints(this IEndpointRouteBuilder app)
    {
        var interviewers = app.MapGroup("/v1/interviewers").WithTags("Interviews").RequireAuthorization();
        interviewers.MapGet("", ListInterviewers).WithName("ListInterviewers")
            .RequirePermission(Permission.EmailCandidates)
            .Produces(StatusCodes.Status401Unauthorized);

        var applications = app.MapGroup("/v1/applications").WithTags("Interviews").RequireAuthorization();
        applications.MapPost("/{applicationId:guid}/interviews", ScheduleInterview).WithName("ScheduleInterview")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EmailCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapGet("/{applicationId:guid}/interviews", ListApplicationInterviews).WithName("ListApplicationInterviews")
            .Produces(StatusCodes.Status401Unauthorized);

        var interviews = app.MapGroup("/v1/interviews").WithTags("Interviews").RequireAuthorization();
        interviews.MapPost("/{interviewId:guid}/cancel", CancelInterview).WithName("CancelInterview")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EmailCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
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
            .Where(m => SeesJob(m, team))
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

    // Offers the candidate an interview: the request, its interviewers, the timeline entry, and the invitation email the
    // worker sends (with the link, which is made there). Nothing is on a calendar until the candidate picks a time.
    private static async Task<Results<Created<ApplicationInterview>, NotFound, ValidationProblem, ProblemHttpResult>> ScheduleInterview(
        Guid applicationId,
        ScheduleInterviewRequest request,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        CalendarAccess calendar,
        InterviewLinks links,
        IServiceProvider services,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (services.GetService<IEmailSender>() is null || !links.IsConfigured)
        {
            return TypedResults.NotFound(); // the candidate cannot be emailed a link, so scheduling is hidden
        }

        var now = time.GetUtcNow();
        var (dayStart, dayEnd, problems) = Validate(request, now);
        if (problems is not null)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var db = outbox.DbContext;
        if (await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken) is not { } application)
        {
            return TypedResults.NotFound();
        }

        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        if (application.HasEnded(job.Workflow.FindStage(application.CurrentStageId)))
        {
            return TypedResults.Problem("The application has ended, so there is nothing to schedule.", statusCode: StatusCodes.Status409Conflict);
        }

        var byUser = (await HiringTeams.MembersAsync(db, tenant, cancellationToken)).Values.ToDictionary(m => m.UserId);
        var chosen = request.InterviewerUserIds!;
        if (chosen.Any(id => !byUser.TryGetValue(id, out var member) || !SeesJob(member, job.HiringTeam)))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "interviewerUserIds", "Choose members of the team who see this job."));
        }

        var connections = await db.CalendarConnections.AsNoTracking().Where(c => chosen.Contains(c.UserId)).ToListAsync(cancellationToken);
        var usable = connections
            .Where(c => c.Problem is null && calendar.Options.IsAvailable(c.Provider))
            .ToDictionary(c => c.UserId, c => c.Provider);
        var missing = chosen.Where(id => !usable.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            return TypedResults.Problem(
                title: "Calendar not connected",
                detail: $"These interviewers have no working calendar connected: {string.Join(", ", missing.Select(id => byUser[id].DisplayName))}. "
                    + "Ask them to connect a calendar from the account menu.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { ["interviewerUserIds"] = missing });
        }

        // An Open request past its window is stored as Expired first: the partial unique index sees the stored status.
        var open = await db.InterviewRequests.Where(r => r.ApplicationId == applicationId && r.Status == InterviewStatus.Open).ToListAsync(cancellationToken);
        var expired = false;
        foreach (var stale in open)
        {
            expired |= stale.MarkExpired(now);
        }

        if (expired)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        if (await db.InterviewRequests.AnyAsync(
                r => r.ApplicationId == applicationId && (r.Status == InterviewStatus.Open || r.Status == InterviewStatus.Booked), cancellationToken))
        {
            return AlreadyOffered();
        }

        var interview = InterviewRequest.Create(
            application, request.DurationMinutes, request.WindowStart, request.WindowEnd, request.TimeZone!, tenant.UserId, now, dayStart, dayEnd);
        var attendees = chosen
            .Select((userId, index) => InterviewAttendee.Create(interview, userId, isOrganizer: index == 0, usable[userId]))
            .ToList();
        db.InterviewRequests.Add(interview);
        db.InterviewAttendees.AddRange(attendees);
        db.ApplicationEvents.Add(application.InterviewInvited(interview, now, tenant.UserId));
        await outbox.PublishAsync(new SendInterviewInvitation(application.OrganizationId, application.Id, interview.Id, Guid.CreateVersion7(), tenant.UserId));
        await outbox.PublishAsync(new InterviewChanged(application.OrganizationId, application.Id, application.JobId, interview.Id, interview.Status.ToString()));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return AlreadyOffered(); // someone offered an interview at the same moment
        }

        return TypedResults.Created($"/v1/applications/{applicationId}/interviews", interview.ToDto(attendees, byUser, now));
    }

    // Every interview offered to the application, newest first: the current one and the history of cancelled and expired ones.
    private static async Task<Results<Ok<List<ApplicationInterview>>, NotFound>> ListApplicationInterviews(
        Guid applicationId, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!await db.Applications.AsNoTracking().AnyAsync(a => a.Id == applicationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var requests = await db.InterviewRequests.AsNoTracking()
            .Where(r => r.ApplicationId == applicationId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);
        var attendees = (await db.InterviewAttendees.AsNoTracking()
                .Where(a => a.ApplicationId == applicationId)
                .OrderByDescending(a => a.IsOrganizer).ThenBy(a => a.Id)
                .ToListAsync(cancellationToken))
            .ToLookup(a => a.InterviewId);
        var byUser = (await HiringTeams.MembersAsync(db, tenant, cancellationToken)).Values.ToDictionary(m => m.UserId);
        var now = time.GetUtcNow();
        return TypedResults.Ok(requests.Select(r => r.ToDto([.. attendees[r.Id]], byUser, now)).ToList());
    }

    // The team withdraws the offer or cancels the booked time. A booked interview's calendar events are removed by the
    // worker, and the candidate is told. The interviewers' locks are taken first, as booking does, so a cancellation and a
    // candidate's booking at the same moment cannot both win.
    private static async Task<Results<Ok<ApplicationInterview>, NotFound, ProblemHttpResult>> CancelInterview(
        Guid interviewId,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var attendeeUsers = await db.InterviewAttendees.AsNoTracking().Where(a => a.InterviewId == interviewId).Select(a => a.UserId).ToListAsync(cancellationToken);
        // No CommitAsync here: SaveChangesAndFlushMessagesAsync commits this transaction with the outbox rows.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await InterviewLocks.TakeAsync(db, tenant.OrganizationId!.Value, attendeeUsers, cancellationToken);
        if (await db.InterviewRequests.SingleOrDefaultAsync(r => r.Id == interviewId, cancellationToken) is not { } interview)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        if (interview.EffectiveStatus(now) is not (InterviewStatus.Open or InterviewStatus.Booked))
        {
            return TypedResults.Problem("This interview is already cancelled or has expired.", statusCode: StatusCodes.Status409Conflict);
        }

        var application = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == interview.ApplicationId, cancellationToken);
        var attendees = await db.InterviewAttendees
            .Where(a => a.InterviewId == interviewId)
            .OrderByDescending(a => a.IsOrganizer).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        var wasBooked = interview.Status == InterviewStatus.Booked;
        interview.Cancel(now, byCandidate: false);
        db.ApplicationEvents.Add(application.InterviewCancelled(interview, now, tenant.UserId));
        if (wasBooked)
        {
            // The worker removes each event that was made, and marks the attendee Synced once it has.
            attendees.Where(a => a.ProviderEventId is not null).ToList().ForEach(a => a.MarkPending());
            await outbox.PublishAsync(new CancelInterviewEvents(application.OrganizationId, application.Id, interview.Id, interview.Revision));
        }

        await outbox.PublishAsync(new InterviewChanged(application.OrganizationId, application.Id, application.JobId, interview.Id, interview.Status.ToString()));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        var byUser = (await HiringTeams.MembersAsync(db, tenant, cancellationToken)).Values.ToDictionary(m => m.UserId);
        return TypedResults.Ok(interview.ToDto(attendees, byUser, now));
    }

    private static bool SeesJob(HiringTeams.Member member, IReadOnlyCollection<Guid> team) => member.SeesAllJobs || team.Contains(member.Id);

    private static ProblemHttpResult AlreadyOffered() =>
        TypedResults.Problem("This candidate already has an interview that is open or booked. Cancel it first.", statusCode: StatusCodes.Status409Conflict);

    // The working hours parsed, and the first problems in problem-details form (null when the request is sound).
    private static (TimeOnly DayStart, TimeOnly DayEnd, IDictionary<string, string[]>? Problems) Validate(ScheduleInterviewRequest request, DateTimeOffset now)
    {
        var problems = new Dictionary<string, string[]>();
        if (request.InterviewerUserIds is not { Count: >= 1 and <= MaxInterviewers } chosen || chosen.Distinct().Count() != chosen.Count)
        {
            problems["interviewerUserIds"] = [$"Choose one to {MaxInterviewers} different interviewers."];
        }

        var dayStart = ParseTime(request.DayStart, InterviewRequest.DefaultDayStart, "dayStart", problems);
        var dayEnd = ParseTime(request.DayEnd, InterviewRequest.DefaultDayEnd, "dayEnd", problems);
        if (problems.Count == 0 && InterviewRequest.Problem(
                request.DurationMinutes, request.WindowStart, request.WindowEnd, request.TimeZone, dayStart, dayEnd, now) is { } problem)
        {
            problems[ProblemField(problem)] = [problem];
        }

        return (dayStart, dayEnd, problems.Count == 0 ? null : problems);
    }

    // The field InterviewRequest.Problem's message is about, so a form can show it beside the input.
    private static string ProblemField(string problem) =>
        problem.StartsWith("An interview lasts", StringComparison.Ordinal) ? "durationMinutes"
        : problem.StartsWith("Choose a time zone", StringComparison.Ordinal) ? "timeZone"
        : problem.StartsWith("Working hours", StringComparison.Ordinal) ? "dayEnd"
        : "windowEnd";

    private static TimeOnly ParseTime(string? text, TimeOnly fallback, string field, Dictionary<string, string[]> problems)
    {
        if (text is null)
        {
            return fallback;
        }

        if (TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        problems[field] = ["Use a time such as 09:00."];
        return fallback;
    }

    private static ApplicationInterview ToDto(
        this InterviewRequest interview, IReadOnlyList<InterviewAttendee> attendees, Dictionary<Guid, HiringTeams.Member> byUser, DateTimeOffset now) =>
        new(
            interview.Id,
            interview.EffectiveStatus(now),
            interview.StartsAt,
            interview.EndsAt,
            interview.WindowStart,
            interview.WindowEnd,
            interview.TimeZone,
            interview.DurationMinutes,
            interview.DayStart.ToString("HH:mm", CultureInfo.InvariantCulture),
            interview.DayEnd.ToString("HH:mm", CultureInfo.InvariantCulture),
            interview.MeetingUrl,
            [.. attendees.Select(a => new InterviewParticipant(
                a.UserId, byUser.TryGetValue(a.UserId, out var member) ? member.DisplayName : "Former member", a.IsOrganizer, a.SyncStatus, a.SyncProblem))],
            interview.CreatedAt,
            interview.CreatedByUserId,
            interview.CancelledByCandidate);
}
