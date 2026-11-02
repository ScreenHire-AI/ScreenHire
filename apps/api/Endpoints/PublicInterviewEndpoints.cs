using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Branding;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// A candidate's interview page reads, books, moves and cancels the interview through these routes
/// (docs/architecture/interview-scheduling.md). The token in the path is the signed link the invitation emailed
/// (<see cref="InterviewLinks"/>): it names the organization and the interview request, and one token serves the request's
/// whole life, so the request's status is what closes it. Anonymous, rate limited like the apply flow, and called by the
/// careers server.
/// </summary>
/// <remarks>
/// <para>
/// The slug's organization becomes the tenant, as on every public route, and the token must name that organization: a
/// token for another organization, or one that is forged, damaged or long past its expiry, is a 404. The page shows only
/// what the candidate needs: the employer, the job, the length, the offered times and their own booked time. It never
/// names the interviewers and never holds verification output. An interview whose application has ended (rejected,
/// withdrawn, hired or kept in mind) reads as cancelled, whatever its row says, so nobody books a time on the team's
/// calendars for a job they have left.
/// </para>
/// <para>
/// A change answers with a problem whose <c>type</c> (also its <c>code</c> member) says why, so the page can word it: 410 for an interview that is
/// <c>cancelled</c> or <c>expired</c>; 409 for <c>already_booked</c>, <c>not_booked</c>, <c>slot_taken</c> (the time is not
/// offered, or someone else took it) and <c>too_late</c> (inside the minimum notice); 503 for <c>calendar_unavailable</c>
/// (the team's calendars cannot be read now, try again). Booking and moving run in one transaction with the interviewers'
/// locks and the outbox, so a time is never booked twice and the messages that write the calendars leave only if the booking
/// commits.
/// </para>
/// </remarks>
internal static class PublicInterviewEndpoints
{
    public static IEndpointRouteBuilder MapPublicInterviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/public/organizations/{organizationSlug}/interviews/{token}")
            .WithTags("Public careers")
            .AllowAnonymous()
            .RequireRateLimiting(ApplyOptions.RateLimitPolicy);

        group.MapGet("", GetPublicInterview).WithName("GetPublicInterview")
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPost("/book", BookPublicInterview).WithName("BookPublicInterview")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPost("/reschedule", ReschedulePublicInterview).WithName("ReschedulePublicInterview")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPost("/cancel", CancelPublicInterview).WithName("CancelPublicInterview")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .Produces(StatusCodes.Status429TooManyRequests);
        return app;
    }

    private static async Task<Results<Ok<PublicInterview>, NotFound>> GetPublicInterview(
        string organizationSlug,
        string token,
        [FromQuery] string? tz,
        ScreenHireDbContext db,
        TenantContext tenant,
        InterviewLinks links,
        InterviewAvailability availability,
        CalendarAccess calendar,
        IOptions<DomainOptions> domains,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(organizationSlug, token, db, tenant, links, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        var interview = found.Interview;
        var status = found.StatusAt(now);
        var minNotice = TimeSpan.FromHours(calendar.Options.Interviews.MinNoticeHours);
        var canChange = status == InterviewStatus.Booked && interview.CanBeChangedByCandidate(now, minNotice);
        IReadOnlyList<DateTimeOffset> slots = [];
        var unavailable = false;
        if (status == InterviewStatus.Open || (status == InterviewStatus.Booked && canChange))
        {
            var attendees = await db.InterviewAttendees.AsNoTracking()
                .Where(a => a.InterviewId == interview.Id)
                .OrderByDescending(a => a.IsOrganizer).ThenBy(a => a.Id)
                .ToListAsync(cancellationToken);
            try
            {
                var offered = await availability.FindSlotsAsync(interview, attendees, cancellationToken);
                slots = offered.Slots;
                unavailable = !offered.HasAllCalendars;
            }
            catch (CalendarApiException)
            {
                unavailable = true; // a provider that is only down: the page asks the candidate to try again later
            }
        }

        var zone = InterviewRequest.TryFindZone(tz, out _) ? tz! : interview.TimeZone;
        return TypedResults.Ok(new PublicInterview(
            await found.Organization.ToPublicAsync(db, domains.Value, cancellationToken),
            found.JobTitle,
            status switch
            {
                InterviewStatus.Open => PublicInterviewState.Open,
                InterviewStatus.Booked => PublicInterviewState.Booked,
                InterviewStatus.Cancelled => PublicInterviewState.Cancelled,
                _ => PublicInterviewState.Expired,
            },
            status == InterviewStatus.Booked ? interview.StartsAt : null,
            status == InterviewStatus.Booked ? interview.EndsAt : null,
            zone,
            interview.TimeZone,
            interview.DurationMinutes,
            slots,
            unavailable,
            canChange,
            calendar.Options.Interviews.MinNoticeHours));
    }

    private static Task<Results<NoContent, NotFound, ProblemHttpResult>> BookPublicInterview(
        string organizationSlug,
        string token,
        PublicInterviewChoice choice,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        InterviewLinks links,
        InterviewBooking booking,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ChangeAsync(organizationSlug, token, choice.Start, reschedule: false, outbox, tenant, links, booking, time, cancellationToken);

    private static Task<Results<NoContent, NotFound, ProblemHttpResult>> ReschedulePublicInterview(
        string organizationSlug,
        string token,
        PublicInterviewChoice choice,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        InterviewLinks links,
        InterviewBooking booking,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ChangeAsync(organizationSlug, token, choice.Start, reschedule: true, outbox, tenant, links, booking, time, cancellationToken);

    // One transaction: the interviewers' locks, the request and attendees, the timeline entry, the messages that write the
    // calendars and tell open dashboards. SaveChangesAndFlushMessagesAsync commits it, and only a booking that is Done gets
    // there; anything else leaves the transaction to roll back.
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> ChangeAsync(
        string organizationSlug,
        string token,
        DateTimeOffset start,
        bool reschedule,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        InterviewLinks links,
        InterviewBooking booking,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        if (await FindAsync(organizationSlug, token, db, tenant, links, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        if (NotChangeable(found.StatusAt(now), reschedule) is { } refused)
        {
            return refused;
        }

        InterviewBookingResult result;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            result = reschedule
                ? await booking.RescheduleAsync(found.Interview.Id, start, byCandidate: true, cancellationToken)
                : await booking.BookAsync(found.Interview.Id, start, cancellationToken);
        }
        catch (CalendarApiException)
        {
            return CalendarUnavailable();
        }

        if (!result.IsDone)
        {
            return Refused(result, reschedule, now);
        }

        var interview = result.Interview!;
        var application = found.Application;
        if (reschedule)
        {
            db.ApplicationEvents.Add(application.InterviewRescheduled(interview, now));
            await outbox.PublishAsync(new UpdateInterviewEvents(application.OrganizationId, application.Id, interview.Id, interview.Revision));
        }
        else
        {
            db.ApplicationEvents.Add(application.InterviewBooked(interview, now));
            await outbox.PublishAsync(new CreateInterviewEvents(application.OrganizationId, application.Id, interview.Id, interview.Revision));
            // The first booking raises the InterviewScheduled trigger, once per interview whatever the redelivery.
            await outbox.PublishAsync(new RunAutomations(
                application.OrganizationId, application.Id, TriggerType.InterviewScheduled, StageId: null, interview.Id, now));
        }

        await outbox.PublishAsync(new InterviewChanged(application.OrganizationId, application.Id, application.JobId, interview.Id, interview.Status.ToString()));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // The candidate cancels: an offer they decline, or a booked time at least the minimum notice ahead. The locks come first,
    // as for a booking, so a cancel and a move at the same moment cannot both win.
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> CancelPublicInterview(
        string organizationSlug,
        string token,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        InterviewLinks links,
        CalendarAccess calendar,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        if (await FindAsync(organizationSlug, token, db, tenant, links, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        var interview = found.Interview;
        var now = time.GetUtcNow();
        if (ClosedProblem(found.StatusAt(now)) is { } closed)
        {
            return closed;
        }

        var attendeeUsers = await db.InterviewAttendees.AsNoTracking().Where(a => a.InterviewId == interview.Id).Select(a => a.UserId).ToListAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await InterviewLocks.TakeAsync(db, interview.OrganizationId, attendeeUsers, cancellationToken);
        await db.Entry(interview).ReloadAsync(cancellationToken);
        if (ClosedProblem(found.StatusAt(now)) is { } closedNow)
        {
            return closedNow;
        }

        var wasBooked = interview.Status == InterviewStatus.Booked;
        if (wasBooked && !interview.CanBeChangedByCandidate(now, TimeSpan.FromHours(calendar.Options.Interviews.MinNoticeHours)))
        {
            return TooLate();
        }

        var application = found.Application;
        interview.Cancel(now, byCandidate: true);
        db.ApplicationEvents.Add(application.InterviewCancelled(interview, now));
        if (wasBooked)
        {
            var attendees = await db.InterviewAttendees.Where(a => a.InterviewId == interview.Id).ToListAsync(cancellationToken);
            attendees.Where(a => a.ProviderEventId is not null).ToList().ForEach(a => a.MarkPending());
            await outbox.PublishAsync(new CancelInterviewEvents(application.OrganizationId, application.Id, interview.Id, interview.Revision));
        }

        await outbox.PublishAsync(new InterviewChanged(application.OrganizationId, application.Id, application.JobId, interview.Id, interview.Status.ToString()));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // A request that cannot take this change at all, before any calendar is read: closed for good (410), or not in the state
    // the change needs (409).
    private static ProblemHttpResult? NotChangeable(InterviewStatus status, bool reschedule)
    {
        if (ClosedProblem(status) is { } closed)
        {
            return closed;
        }

        return reschedule
            ? status == InterviewStatus.Open ? NotBooked() : null
            : status == InterviewStatus.Booked ? AlreadyBooked() : null;
    }

    private static ProblemHttpResult Refused(InterviewBookingResult result, bool reschedule, DateTimeOffset now) => result.Outcome switch
    {
        InterviewBookingOutcome.SlotTaken => Problem(
            StatusCodes.Status409Conflict, "slot_taken", "That time is no longer available", "Someone else took that time, or it is not offered. Choose another."),
        InterviewBookingOutcome.TooLate => TooLate(),
        InterviewBookingOutcome.NoCalendar => CalendarUnavailable(),
        // The request changed while this waited: say what it is now.
        InterviewBookingOutcome.NotOpen when result.Interview is { } interview => NotChangeable(interview.EffectiveStatus(now), reschedule) ?? NotBooked(),
        _ => Problem(StatusCodes.Status409Conflict, "slot_taken", "That time is no longer available", "Choose another time."),
    };

    private static ProblemHttpResult? ClosedProblem(InterviewStatus status) => status switch
    {
        InterviewStatus.Cancelled => Problem(
            StatusCodes.Status410Gone, "cancelled", "Interview cancelled", "This interview was cancelled. Contact the hiring team if you would like another time."),
        InterviewStatus.Expired => Problem(
            StatusCodes.Status410Gone, "expired", "Link expired", "The time to pick an interview has passed. Contact the hiring team if you would like another time."),
        _ => null,
    };

    private static ProblemHttpResult AlreadyBooked() => Problem(
        StatusCodes.Status409Conflict, "already_booked", "Already booked", "A time is already booked. Use Pick another time to move it.");

    private static ProblemHttpResult NotBooked() => Problem(
        StatusCodes.Status409Conflict, "not_booked", "Nothing to move", "No time is booked yet. Pick a time first.");

    private static ProblemHttpResult TooLate() => Problem(
        StatusCodes.Status409Conflict, "too_late", "Too close to the start", "The interview is too close to change here. Contact the hiring team.");

    private static ProblemHttpResult CalendarUnavailable() => Problem(
        StatusCodes.Status503ServiceUnavailable, "calendar_unavailable", "Try again soon", "The times cannot be checked right now. Try again in a few minutes.");

    private static ProblemHttpResult Problem(int status, string code, string title, string detail) =>
        TypedResults.Problem(title: title, detail: detail, statusCode: status, type: code, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static async Task<Found?> FindAsync(
        string organizationSlug, string token, ScreenHireDbContext db, TenantContext tenant, InterviewLinks links, CancellationToken cancellationToken)
    {
        if (await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is not { } organization
            || links.Read(token) is not { } link
            || link.OrganizationId != organization.Id)
        {
            return null;
        }

        var interview = await db.InterviewRequests.SingleOrDefaultAsync(r => r.Id == link.InterviewId && r.OrganizationId == organization.Id, cancellationToken);
        if (interview is null)
        {
            return null;
        }

        var row = await db.Applications.AsNoTracking()
            .Where(a => a.Id == interview.ApplicationId)
            .Join(db.Jobs.AsNoTracking(), a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new Found(organization, interview, row.Application, row.Job.Title, row.Application.HasEnded(row.Job.Workflow.FindStage(row.Application.CurrentStageId)));
    }

    private sealed record Found(
        Domain.Organizations.Organization Organization, InterviewRequest Interview, JobApplication Application, string JobTitle, bool ApplicationEnded)
    {
        // What the candidate is told: the request's own status, or cancelled once the application it belongs to has ended.
        public InterviewStatus StatusAt(DateTimeOffset now)
        {
            var status = Interview.EffectiveStatus(now);
            return ApplicationEnded && status is InterviewStatus.Open or InterviewStatus.Booked ? InterviewStatus.Cancelled : status;
        }
    }
}
