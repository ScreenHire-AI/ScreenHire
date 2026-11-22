using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Hiring;
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
/// These routes run for a signed-in member, who may see only some jobs, so they use <c>InterviewAvailability</c> and
/// <c>InterviewBooking</c> only through <see cref="OrganizationScope"/>, after checking the member sees the job: those refuse
/// a context limited to some jobs, because a booking check there could miss an interviewer's other interviews. They offer,
/// change how the candidate picks, book one of the candidate's times, and cancel; the candidate books through the public routes.
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
        interviews.MapPost("/{interviewId:guid}/new-times", OfferNewTimes).WithName("OfferNewInterviewTimes")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EmailCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        interviews.MapPost("/{interviewId:guid}/option", ChangeOption).WithName("ChangeInterviewOption")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EmailCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status401Unauthorized);
        interviews.MapPost("/{interviewId:guid}/book", BookTime).WithName("BookInterviewTime")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EmailCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        interviews.MapPost("/{interviewId:guid}/more-times", AskForMoreTimes).WithName("AskForMoreInterviewTimes")
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
        var withHours = (await db.InterviewHours.AsNoTracking()
            .Where(h => userIds.Contains(h.UserId) && h.Weekly != null).Select(h => h.UserId).ToListAsync(cancellationToken)).ToHashSet();

        return TypedResults.Ok(members
            .OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(m =>
            {
                // A row the host cannot use (its provider is not configured here) does not count as connected.
                var usable = byUser.TryGetValue(m.UserId, out var connection) && connection.Problem is null && calendar.Options.IsAvailable(connection.Provider);
                return new Interviewer(m.UserId, m.DisplayName, usable, usable ? connection!.Provider : null, withHours.Contains(m.UserId));
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
        var pick = PickOf(request.Option ?? InterviewOption.OurTimes, request.Hours, request.UseBlocks, request.ProposedTimes, request.ExtraTimes);
        if (problems is null && InterviewRequest.PickProblem(pick, request.DurationMinutes, request.WindowStart, request.WindowEnd, now) is { } pickProblem)
        {
            problems = ValidationExtensions.Problem(PickField(pickProblem), pickProblem);
        }

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

        var (usable, noCalendar) = await CalendarsAsync(db, calendar, chosen, pick, byUser, cancellationToken);
        if (noCalendar is not null)
        {
            return noCalendar;
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
            application, request.DurationMinutes, request.WindowStart, request.WindowEnd, request.TimeZone!, tenant.UserId, now, dayStart, dayEnd,
            meetingName: job.Workflow.FindStage(application.CurrentStageId)?.MeetingName, pick: pick);
        var attendees = chosen
            .Select((userId, index) => InterviewAttendee.Create(
                interview, userId, isOrganizer: index == 0, usable.TryGetValue(userId, out var provider) ? provider : CalendarProvider.ScreenHire))
            .ToList();
        db.InterviewRequests.Add(interview);
        db.InterviewAttendees.AddRange(attendees);
        db.ApplicationEvents.Add(application.InterviewInvited(interview, now, tenant.UserId));
        foreach (var done in await SchedulingTasks.CompleteAsync(db, application, tenant.UserId, now, cancellationToken))
        {
            await outbox.PublishAsync(done);
        }

        await outbox.PublishAsync(new SendInterviewInvitation(application.OrganizationId, application.Id, interview.Id, Guid.CreateVersion7(), tenant.UserId));
        await outbox.PublishAsync(new InterviewChanged(
            application.OrganizationId, application.Id, application.JobId, interview.Id, interview.Status.ToString(), InterviewChanges.Invited, tenant.UserId));
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
            await outbox.PublishAsync(new CancelInterviewEvents(
                application.OrganizationId, application.Id, interview.Id, interview.Revision, ActorUserId: tenant.UserId));
        }

        await outbox.PublishAsync(new InterviewChanged(
            application.OrganizationId, application.Id, application.JobId, interview.Id, interview.Status.ToString(), InterviewChanges.Cancelled, tenant.UserId,
            StartsAt: interview.StartsAt, Revision: interview.Revision, ChangedAt: now));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        var byUser = (await HiringTeams.MembersAsync(db, tenant, cancellationToken)).Values.ToDictionary(m => m.UserId);
        return TypedResults.Ok(interview.ToDto(attendees, byUser, now));
    }

    // The team offers the candidate new times for a booked interview. The booked time stays held (its row and the calendar
    // events are untouched) until the candidate picks one, which is the public reschedule route: that moves the events.
    // The locks come first, as for a cancellation, so an offer and a candidate's move at the same moment cannot both win.
    private static async Task<Results<Ok<ApplicationInterview>, NotFound, ValidationProblem, ProblemHttpResult>> OfferNewTimes(
        Guid interviewId,
        OfferNewInterviewTimesRequest request,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        InterviewLinks links,
        IServiceProvider services,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (services.GetService<IEmailSender>() is null || !links.IsConfigured)
        {
            return TypedResults.NotFound(); // the candidate cannot be emailed a link, so offering new times is hidden
        }

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
        if (interview.Status != InterviewStatus.Booked)
        {
            return TypedResults.Problem("New times can be offered only for a booked interview.", statusCode: StatusCodes.Status409Conflict);
        }

        if (interview.NewTimesProblem(request.WindowStart, request.WindowEnd, now) is { } problem)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(ProblemField(problem), problem));
        }

        var application = await db.Applications.SingleAsync(a => a.Id == interview.ApplicationId, cancellationToken);
        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        if (application.HasEnded(job.Workflow.FindStage(application.CurrentStageId)))
        {
            return TypedResults.Problem("The application has ended, so there is nothing to move.", statusCode: StatusCodes.Status409Conflict);
        }

        interview.OfferNewTimes(request.WindowStart, request.WindowEnd, now);
        db.ApplicationEvents.Add(application.InterviewNewTimesOffered(interview, now, tenant.UserId));
        await outbox.PublishAsync(new SendInterviewNewTimes(application.OrganizationId, application.Id, interview.Id, Guid.CreateVersion7(), tenant.UserId));
        await outbox.PublishAsync(new InterviewChanged(
            application.OrganizationId, application.Id, application.JobId, interview.Id, interview.Status.ToString(), InterviewChanges.NewTimesOffered, tenant.UserId,
            StartsAt: interview.StartsAt));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

        var attendees = await db.InterviewAttendees.AsNoTracking()
            .Where(a => a.InterviewId == interviewId)
            .OrderByDescending(a => a.IsOrganizer).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        var byUser = (await HiringTeams.MembersAsync(db, tenant, cancellationToken)).Values.ToDictionary(m => m.UserId);
        return TypedResults.Ok(interview.ToDto(attendees, byUser, now));
    }

    // Change how they pick on an open interview, or move a booked one with any of the three options. Under the interviewers'
    // locks, as a cancellation is, so a change and a candidate's booking at the same moment cannot both win. The candidate's
    // link stays; they are emailed that their options changed.
    private static async Task<Results<Ok<ApplicationInterview>, NotFound, ValidationProblem, ProblemHttpResult>> ChangeOption(
        Guid interviewId,
        ChangeInterviewOptionRequest request,
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
            return TypedResults.NotFound();
        }

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
        var status = interview.EffectiveStatus(now);
        if (status is not (InterviewStatus.Open or InterviewStatus.Booked))
        {
            return TypedResults.Problem("This interview is cancelled or has expired. Offer a new one instead.", statusCode: StatusCodes.Status409Conflict);
        }

        var pick = PickOf(request.Option, request.Hours, request.UseBlocks, request.ProposedTimes, request.ExtraTimes);
        if (interview.ChangeProblem(pick, request.WindowStart, request.WindowEnd, now) is { } problem)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(PickField(problem), problem));
        }

        var application = await db.Applications.SingleAsync(a => a.Id == interview.ApplicationId, cancellationToken);
        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        if (application.HasEnded(job.Workflow.FindStage(application.CurrentStageId)))
        {
            return TypedResults.Problem("The application has ended, so there is nothing to schedule.", statusCode: StatusCodes.Status409Conflict);
        }

        var byUser = (await HiringTeams.MembersAsync(db, tenant, cancellationToken)).Values.ToDictionary(m => m.UserId);
        var (_, noCalendar) = await CalendarsAsync(db, calendar, attendeeUsers, pick, byUser, cancellationToken);
        if (noCalendar is not null)
        {
            return noCalendar;
        }

        interview.ChangeOption(pick, request.WindowStart, request.WindowEnd, now);
        db.ApplicationEvents.Add(status == InterviewStatus.Booked
            ? application.InterviewNewTimesOffered(interview, now, tenant.UserId)
            : application.InterviewOptionChanged(interview, now, tenant.UserId));
        await outbox.PublishAsync(new SendInterviewOptionsUpdated(application.OrganizationId, application.Id, interview.Id, Guid.CreateVersion7(), tenant.UserId));
        await outbox.PublishAsync(new InterviewChanged(
            application.OrganizationId, application.Id, application.JobId, interview.Id, interview.Status.ToString(),
            status == InterviewStatus.Booked ? InterviewChanges.NewTimesOffered : InterviewChanges.OptionChanged, tenant.UserId, StartsAt: interview.StartsAt));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Ok(interview.ToDto(await AttendeesAsync(db, interviewId, cancellationToken), byUser, now));
    }

    // The team books one of the times the candidate suggested. The booking runs for the whole organization (the booking check
    // must see every interviewer's bookings), in a scope of its own whose outbox commits it with its messages, as the
    // candidate's own booking does.
    private static async Task<Results<Ok<ApplicationInterview>, NotFound, ProblemHttpResult>> BookTime(
        Guid interviewId,
        BookInterviewTimeRequest request,
        ScreenHireDbContext db,
        ITenantContext tenant,
        IServiceScopeFactory scopes,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        // Read through the member's own filters first: an interview on a job they cannot see is a 404, before any wider scope.
        if (await db.InterviewRequests.AsNoTracking().Where(r => r.Id == interviewId).Select(r => new { r.Option, r.Status }).SingleOrDefaultAsync(cancellationToken) is not { } seen)
        {
            return TypedResults.NotFound();
        }

        if (seen.Option != InterviewOption.TheirTimes)
        {
            return TypedResults.Problem("Only times the candidate suggested are booked here.", statusCode: StatusCodes.Status409Conflict);
        }

        var userId = tenant.UserId;
        var outcome = await OrganizationScope.RunAsync(scopes, tenant.OrganizationId!.Value, async scope =>
        {
            var outbox = scope.GetRequiredService<IDbContextOutbox<ScreenHireDbContext>>();
            var booking = scope.GetRequiredService<InterviewBooking>();
            var inner = outbox.DbContext;
            // No CommitAsync here: SaveChangesAndFlushMessagesAsync commits the booking's transaction with the outbox rows.
            await using var transaction = await inner.Database.BeginTransactionAsync(cancellationToken);
            var reschedule = seen.Status == InterviewStatus.Booked;
            var result = reschedule
                ? await booking.RescheduleAsync(interviewId, request.Start, byCandidate: false, cancellationToken)
                : await booking.BookAsync(interviewId, request.Start, cancellationToken);
            if (!result.IsDone)
            {
                return result.Outcome;
            }

            var interview = result.Interview!;
            var application = await inner.Applications.AsNoTracking().SingleAsync(a => a.Id == interview.ApplicationId, cancellationToken);
            var now = time.GetUtcNow();
            if (reschedule)
            {
                inner.ApplicationEvents.Add(application.InterviewRescheduled(interview, now, userId));
                await outbox.PublishAsync(new UpdateInterviewEvents(application.OrganizationId, application.Id, interview.Id, interview.Revision));
            }
            else
            {
                inner.ApplicationEvents.Add(application.InterviewBooked(interview, now, userId));
                await outbox.PublishAsync(new CreateInterviewEvents(application.OrganizationId, application.Id, interview.Id, interview.Revision));
                // Booked by a person for the candidate, it is still the candidate's first booking: the trigger fires once per interview.
                await outbox.PublishAsync(new RunAutomations(
                    application.OrganizationId, application.Id, TriggerType.InterviewScheduled, StageId: null, interview.Id, now));
            }

            await outbox.PublishAsync(new InterviewChanged(
                application.OrganizationId, application.Id, application.JobId, interview.Id, interview.Status.ToString(),
                reschedule ? InterviewChanges.Rescheduled : InterviewChanges.Booked, userId, StartsAt: interview.StartsAt,
                Revision: interview.Revision, ChangedAt: now));
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            return result.Outcome;
        });

        return outcome switch
        {
            InterviewBookingOutcome.Done => TypedResults.Ok(await DtoAsync(db, tenant, interviewId, time.GetUtcNow(), cancellationToken)),
            InterviewBookingOutcome.NotFound => TypedResults.NotFound(),
            InterviewBookingOutcome.SlotTaken => TypedResults.Problem(
                "That time is not one the candidate suggested, or another interview took it. Choose another.", statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Problem("This interview is not waiting for a time to be booked.", statusCode: StatusCodes.Status409Conflict),
        };
    }

    // None of the candidate's times works: the team asks for more, with an optional message, on the same link.
    private static async Task<Results<Ok<ApplicationInterview>, NotFound, ValidationProblem, ProblemHttpResult>> AskForMoreTimes(
        Guid interviewId,
        AskForMoreTimesRequest request,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        InterviewLinks links,
        IServiceProvider services,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (services.GetService<IEmailSender>() is null || !links.IsConfigured)
        {
            return TypedResults.NotFound();
        }

        if (request.Note is { Length: > InterviewRequest.MaxNoteLength })
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("note", $"Keep the message to {InterviewRequest.MaxNoteLength} characters."));
        }

        var db = outbox.DbContext;
        var attendeeUsers = await db.InterviewAttendees.AsNoTracking().Where(a => a.InterviewId == interviewId).Select(a => a.UserId).ToListAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await InterviewLocks.TakeAsync(db, tenant.OrganizationId!.Value, attendeeUsers, cancellationToken);
        if (await db.InterviewRequests.SingleOrDefaultAsync(r => r.Id == interviewId, cancellationToken) is not { } interview)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        if (interview.Option != InterviewOption.TheirTimes || !interview.IsPicking(now) || interview.CandidateTimes.Count == 0)
        {
            return TypedResults.Problem("The candidate has no times waiting to be booked.", statusCode: StatusCodes.Status409Conflict);
        }

        var application = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == interview.ApplicationId, cancellationToken);
        interview.AskForMoreTimes(request.Note, now);
        db.ApplicationEvents.Add(application.InterviewOptionChanged(interview, now, tenant.UserId));
        await outbox.PublishAsync(new SendInterviewMoreTimes(application.OrganizationId, application.Id, interview.Id, Guid.CreateVersion7(), tenant.UserId));
        await outbox.PublishAsync(new InterviewChanged(
            application.OrganizationId, application.Id, application.JobId, interview.Id, interview.Status.ToString(), InterviewChanges.MoreTimesAsked, tenant.UserId,
            StartsAt: interview.StartsAt));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        var byUser = (await HiringTeams.MembersAsync(db, tenant, cancellationToken)).Values.ToDictionary(m => m.UserId);
        return TypedResults.Ok(interview.ToDto(await AttendeesAsync(db, interviewId, cancellationToken), byUser, now));
    }

    /// <summary>
    /// Each interviewer's calendar provider, and the 422 to answer when times are offered from calendars and an interviewer's
    /// time cannot be known: no working calendar and no interview hours. Interview blocks, the candidate's times and the
    /// team's own need no calendar; an interviewer without one is on ScreenHire's calendar.
    /// </summary>
    internal static async Task<(Dictionary<Guid, CalendarProvider> Usable, ProblemHttpResult? NoCalendar)> CalendarsAsync(
        ScreenHireDbContext db, CalendarAccess calendar, IReadOnlyList<Guid> chosen, InterviewPick pick,
        IReadOnlyDictionary<Guid, HiringTeams.Member> byUser, CancellationToken cancellationToken)
    {
        var connections = await db.CalendarConnections.AsNoTracking().Where(c => chosen.Contains(c.UserId)).ToListAsync(cancellationToken);
        var usable = connections
            .Where(c => c.Problem is null && calendar.Options.IsAvailable(c.Provider))
            .ToDictionary(c => c.UserId, c => c.Provider);
        if (pick.Option != InterviewOption.OurTimes || pick.UseBlocks)
        {
            return (usable, null);
        }

        var withHours = await db.InterviewHours.AsNoTracking().Where(h => chosen.Contains(h.UserId) && h.Weekly != null).Select(h => h.UserId).ToListAsync(cancellationToken);
        var missing = chosen.Where(id => !usable.ContainsKey(id) && !withHours.Contains(id)).ToList();
        return missing.Count == 0
            ? (usable, null)
            : (usable, TypedResults.Problem(
                title: "Calendar not connected",
                detail: $"These interviewers have no working calendar and no interview hours: {string.Join(", ", missing.Select(id => byUser.TryGetValue(id, out var m) ? m.DisplayName : "A former member"))}. "
                    + "Ask them to connect a calendar or set their hours in Your account, Calendar.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { ["interviewerUserIds"] = missing }));
    }

    private static InterviewPick PickOf(
        InterviewOption option, WeeklyHours? hours, bool useBlocks, IReadOnlyList<DateTimeOffset>? proposed, IReadOnlyList<DateTimeOffset>? extra) =>
        new(option, hours, useBlocks, proposed ?? [], extra ?? []);

    // The field a pick's problem is about, so a form can show it beside the input.
    private static string PickField(string problem) =>
        problem.StartsWith("Pick one or", StringComparison.Ordinal) || problem.StartsWith("Only We'll", StringComparison.Ordinal) ? "proposedTimes"
        : problem.StartsWith("Add at most", StringComparison.Ordinal) || problem.StartsWith("Only Pick", StringComparison.Ordinal) ? "extraTimes"
        : problem.StartsWith("Each time", StringComparison.Ordinal) || problem.StartsWith("Times are", StringComparison.Ordinal) ? "proposedTimes"
        : problem.StartsWith("Choose how", StringComparison.Ordinal) ? "option"
        : problem.StartsWith("An interview lasts", StringComparison.Ordinal) || problem.StartsWith("Choose a time zone", StringComparison.Ordinal)
            || problem.StartsWith("The window", StringComparison.Ordinal) || problem.StartsWith("Working hours", StringComparison.Ordinal) ? ProblemField(problem)
        : "hours";

    private static Task<List<InterviewAttendee>> AttendeesAsync(ScreenHireDbContext db, Guid interviewId, CancellationToken cancellationToken) =>
        db.InterviewAttendees.AsNoTracking()
            .Where(a => a.InterviewId == interviewId)
            .OrderByDescending(a => a.IsOrganizer).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

    private static async Task<ApplicationInterview> DtoAsync(ScreenHireDbContext db, ITenantContext tenant, Guid interviewId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var interview = await db.InterviewRequests.AsNoTracking().SingleAsync(r => r.Id == interviewId, cancellationToken);
        var byUser = (await HiringTeams.MembersAsync(db, tenant, cancellationToken)).Values.ToDictionary(m => m.UserId);
        return interview.ToDto(await AttendeesAsync(db, interviewId, cancellationToken), byUser, now);
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
            problems["interviewerUserIds"] = [$"Choose 1 to {MaxInterviewers} interviewers."];
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
            interview.CancelledByCandidate,
            interview.HasPendingNewTimes(now) ? interview.NewTimesOfferedAt : null,
            interview.Option,
            interview.Hours,
            interview.UseBlocks,
            interview.ProposedTimes,
            interview.ExtraTimes,
            interview.CandidateTimes,
            interview.CandidateTimesAt,
            interview.AnotherTimeAskedAt,
            interview.CandidateNote,
            interview.TeamNote,
            interview.WaitsForTeam(now));
}
