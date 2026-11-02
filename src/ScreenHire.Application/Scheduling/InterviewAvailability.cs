using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// The times an interview can be offered at. <see cref="Slots"/> are UTC start instants at which every interviewer is
/// free for the whole interview. <see cref="UnavailableInterviewers"/> lists the interviewers (user ids) whose calendar
/// cannot be read now, because they have none connected, it needs reconnecting, or its provider is not configured here;
/// while any are listed there are no slots, because offering times without their calendar could double-book them.
/// </summary>
public sealed record InterviewAvailabilityResult(IReadOnlyList<DateTimeOffset> Slots, IReadOnlyList<Guid> UnavailableInterviewers)
{
    public bool HasAllCalendars => UnavailableInterviewers.Count == 0;
}

/// <summary>
/// Works out the start times a candidate may pick for an interview request (docs/architecture/interview-scheduling.md):
/// each interviewer's busy time from their own calendar, plus the interviews ScreenHire has booked for them, put through
/// <see cref="SlotFinder"/> in the request's time zone.
/// </summary>
/// <remarks>
/// The bookings of other interviews are read through the job-scope filter, so the caller must act for the whole
/// organization (the public careers endpoints and the worker do: no signed-in user). A signed-in member on Only jobs they're
/// on would not see an interviewer's bookings on other jobs. A provider that is down throws
/// <see cref="CalendarApiException"/>, which is not the same as having no calendar; the caller lets it propagate.
/// </remarks>
public sealed class InterviewAvailability(ScreenHireDbContext db, CalendarAccess calendar, TimeProvider time)
{
    /// <summary>The most start times one call returns (a 90 day window at a 15 minute step would otherwise list thousands).</summary>
    public const int MaxSlots = 2000;

    /// <summary>
    /// The offered start times for <paramref name="interview"/> with <paramref name="attendees"/>. For a booked interview
    /// that is being moved, the interview's own event on each calendar is not counted as busy: an event that fills exactly
    /// its current time is dropped (best effort: a provider that merges it with an adjacent meeting hides it).
    /// </summary>
    public async Task<InterviewAvailabilityResult> FindSlotsAsync(
        InterviewRequest interview, IReadOnlyList<InterviewAttendee> attendees, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interview);
        ArgumentNullException.ThrowIfNull(attendees);
        RequireWholeOrganization(db);
        var now = time.GetUtcNow();
        var options = calendar.Options.Interviews;
        var userIds = attendees.Select(a => a.UserId).Distinct().ToList();

        // One interviewer at a time: the sandbox provider shares this context, which runs one query at a time.
        var busy = new Dictionary<Guid, List<BusyInterval>>();
        var unavailable = new List<Guid>();
        foreach (var userId in userIds)
        {
            if (await calendar.GetSessionAsync(userId, cancellationToken) is not { } session)
            {
                unavailable.Add(userId);
                continue;
            }

            var intervals = await session.Provider.FreeBusyAsync(session.AccessToken, interview.WindowStart, interview.WindowEnd, cancellationToken);
            busy[userId] = [.. intervals.Where(i => !IsTheInterviewsOwnEvent(interview, i))];
        }

        if (unavailable.Count > 0)
        {
            return new InterviewAvailabilityResult([], unavailable);
        }

        var confirmed = await ConfirmedBusyAsync(userIds, interview.WindowStart, interview.WindowEnd, interview.Id, cancellationToken);
        var perInterviewer = userIds
            .Select(u => (IReadOnlyList<BusyInterval>)[.. busy[u], .. confirmed.GetValueOrDefault(u) ?? []])
            .ToList();
        var slots = SlotFinder.Find(
            interview.WindowStart,
            interview.WindowEnd,
            interview.Duration,
            options.StepMinutes,
            interview.Zone,
            interview.DayStart,
            interview.DayEnd,
            now + TimeSpan.FromHours(options.MinNoticeHours),
            perInterviewer,
            MaxSlots);
        return new InterviewAvailabilityResult(slots, []);
    }

    /// <summary>
    /// The interviews ScreenHire has booked for these interviewers between <paramref name="windowStart"/> and <paramref name="windowEnd"/>,
    /// as busy time per interviewer (user id), leaving out the interview <paramref name="exceptInterviewId"/>. Read from the
    /// database each call, never from the change tracker, because the booking check runs it after taking the locks.
    /// </summary>
    public async Task<Dictionary<Guid, List<BusyInterval>>> ConfirmedBusyAsync(
        IReadOnlyCollection<Guid> userIds, DateTimeOffset windowStart, DateTimeOffset windowEnd, Guid exceptInterviewId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        RequireWholeOrganization(db);
        var rows = await (
            from attendee in db.InterviewAttendees.AsNoTracking()
            where userIds.Contains(attendee.UserId) && attendee.InterviewId != exceptInterviewId
            join interview in db.InterviewRequests.AsNoTracking() on new { attendee.OrganizationId, attendee.InterviewId } equals new { interview.OrganizationId, InterviewId = interview.Id }
            where interview.Status == InterviewStatus.Booked && interview.StartsAt < windowEnd && interview.EndsAt > windowStart
            select new { attendee.UserId, Start = interview.StartsAt!.Value, End = interview.EndsAt!.Value })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.Select(r => new BusyInterval(r.Start, r.End)).ToList());
    }

    /// <summary>
    /// Refuses a context limited to some jobs: it would not see an interviewer's bookings on the others, and a double-booking
    /// could pass. The public careers endpoints and the worker act for the whole organization.
    /// </summary>
    internal static void RequireWholeOrganization(ScreenHireDbContext db)
    {
        if (!db.Tenant.SeesAllJobs)
        {
            throw new InvalidOperationException("Interview availability and booking run for the whole organization, not for a member limited to some jobs.");
        }
    }

    private static bool IsTheInterviewsOwnEvent(InterviewRequest interview, BusyInterval interval) =>
        interview.Status == InterviewStatus.Booked && interview.StartsAt == interval.Start && interview.EndsAt == interval.End;
}
