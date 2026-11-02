using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Scheduling;

public enum InterviewBookingOutcome
{
    /// <summary>The interview now has a time (booked, or moved). The request and its attendees are changed and saved.</summary>
    Done,

    /// <summary>The time is not offered (not a slot, or an interviewer is busy then), or another booking got it first.</summary>
    SlotTaken,

    /// <summary>The request is not in a state that allows this: not Open to book, not Booked to move, or an Open one whose window passed.</summary>
    NotOpen,

    /// <summary>The candidate may no longer change a booked interview: it starts within the minimum notice.</summary>
    TooLate,

    /// <summary>An interviewer's calendar cannot be read (<see cref="InterviewBookingResult.UnavailableInterviewers"/>), so nothing is offered or booked.</summary>
    NoCalendar,

    /// <summary>No such interview request (or it is outside the caller's tenant).</summary>
    NotFound,
}

/// <summary>What <see cref="InterviewBooking"/> did. <see cref="Interview"/> and <see cref="Attendees"/> are set unless the outcome is <see cref="InterviewBookingOutcome.NotFound"/>.</summary>
public sealed record InterviewBookingResult(
    InterviewBookingOutcome Outcome,
    InterviewRequest? Interview = null,
    IReadOnlyList<InterviewAttendee>? Attendees = null,
    IReadOnlyList<Guid>? UnavailableInterviewers = null)
{
    public bool IsDone => Outcome == InterviewBookingOutcome.Done;
}

/// <summary>
/// Books or moves an interview without double-booking an interviewer (docs/architecture/interview-scheduling.md). It
/// recomputes the offered times, checks the chosen start is one of them, takes a Postgres advisory lock per interviewer
/// (sorted, so two bookings never wait on each other in a circle), checks again against the interviews ScreenHire has
/// already confirmed, then changes the request and marks every attendee's calendar event Pending.
/// </summary>
/// <remarks>
/// <para>
/// It does not publish messages or send email: the caller does that with the outcome, in the same transaction. When the
/// caller has a transaction open (to publish through the outbox, then
/// <c>SaveChangesAndFlushMessagesAsync</c>), the locks and changes join it and the caller commits; the provider calls then
/// run inside it. Without one the service opens its own and commits.
/// </para>
/// <para>
/// A database constraint cannot do this check without an extension (a <c>btree_gist</c> exclusion), so the lock plus the
/// overlap query is the guard. Like <see cref="InterviewAvailability"/>, it must run for the whole organization.
/// </para>
/// </remarks>
public sealed class InterviewBooking(ScreenHireDbContext db, InterviewAvailability availability, CalendarAccess calendar, TimeProvider time)
{
    /// <summary>The candidate picks the first time for an Open interview.</summary>
    public Task<InterviewBookingResult> BookAsync(Guid interviewId, DateTimeOffset start, CancellationToken cancellationToken) =>
        ChangeAsync(interviewId, start, reschedule: false, byCandidate: false, cancellationToken);

    /// <summary>
    /// Moves a Booked interview. <paramref name="byCandidate"/> applies the minimum notice: the candidate cannot move an
    /// interview that starts sooner than that (<see cref="InterviewRequest.CanBeChangedByCandidate"/>).
    /// </summary>
    public Task<InterviewBookingResult> RescheduleAsync(Guid interviewId, DateTimeOffset start, bool byCandidate, CancellationToken cancellationToken) =>
        ChangeAsync(interviewId, start, reschedule: true, byCandidate, cancellationToken);

    private async Task<InterviewBookingResult> ChangeAsync(
        Guid interviewId, DateTimeOffset start, bool reschedule, bool byCandidate, CancellationToken cancellationToken)
    {
        InterviewAvailability.RequireWholeOrganization(db);
        start = start.ToUniversalTime();
        var interview = await db.InterviewRequests.SingleOrDefaultAsync(r => r.Id == interviewId, cancellationToken);
        if (interview is null)
        {
            return new InterviewBookingResult(InterviewBookingOutcome.NotFound);
        }

        var attendees = await LoadAttendeesAsync(interviewId, cancellationToken);
        var now = time.GetUtcNow();
        if (StateProblem(interview, reschedule, byCandidate, now, calendar.Options.Interviews) is { } problem)
        {
            if (interview.MarkExpired(now))
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return new InterviewBookingResult(problem, interview, attendees);
        }

        if (attendees.Count == 0)
        {
            return new InterviewBookingResult(InterviewBookingOutcome.NoCalendar, interview, attendees, []);
        }

        // Before the locks: these are the slow calls to the providers.
        var offered = await availability.FindSlotsAsync(interview, attendees, cancellationToken);
        if (!offered.HasAllCalendars)
        {
            return new InterviewBookingResult(InterviewBookingOutcome.NoCalendar, interview, attendees, offered.UnavailableInterviewers);
        }

        if (!offered.Slots.Contains(start))
        {
            return new InterviewBookingResult(InterviewBookingOutcome.SlotTaken, interview, attendees);
        }

        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var userIds = attendees.Select(a => a.UserId).Distinct().Order().ToList();
        await InterviewLocks.TakeAsync(db, interview.OrganizationId, userIds, cancellationToken);

        // Another request may have booked, moved or cancelled this one while this waited for the locks.
        await db.Entry(interview).ReloadAsync(cancellationToken);
        if (StateProblem(interview, reschedule, byCandidate, now, calendar.Options.Interviews) is { } changed)
        {
            return new InterviewBookingResult(changed, interview, attendees);
        }

        var end = start + interview.Duration;
        var taken = await availability.ConfirmedBusyAsync(userIds, start, end, interview.Id, cancellationToken);
        if (taken.Count > 0)
        {
            return new InterviewBookingResult(InterviewBookingOutcome.SlotTaken, interview, attendees);
        }

        if (reschedule)
        {
            interview.Reschedule(start, now);
        }
        else
        {
            interview.Book(start, now);
        }

        foreach (var attendee in attendees)
        {
            attendee.MarkPending();
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new InterviewBookingResult(InterviewBookingOutcome.Done, interview, attendees);
    }

    // Null when the request is in the state the change needs.
    private static InterviewBookingOutcome? StateProblem(
        InterviewRequest interview, bool reschedule, bool byCandidate, DateTimeOffset now, CalendarInterviewOptions options)
    {
        if (reschedule)
        {
            if (interview.Status != InterviewStatus.Booked)
            {
                return InterviewBookingOutcome.NotOpen;
            }

            return byCandidate && !interview.CanBeChangedByCandidate(now, TimeSpan.FromHours(options.MinNoticeHours))
                ? InterviewBookingOutcome.TooLate
                : null;
        }

        return interview.EffectiveStatus(now) == InterviewStatus.Open ? null : InterviewBookingOutcome.NotOpen;
    }

    private async Task<List<InterviewAttendee>> LoadAttendeesAsync(Guid interviewId, CancellationToken cancellationToken) =>
        await db.InterviewAttendees.Where(a => a.InterviewId == interviewId)
            .OrderByDescending(a => a.IsOrganizer).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
}
