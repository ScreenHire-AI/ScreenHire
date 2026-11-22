using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// The times an interview can be offered at. <see cref="Slots"/> are UTC start instants at which every interviewer is
/// free for the whole interview. <see cref="UnavailableInterviewers"/> lists the interviewers (user ids) whose time cannot
/// be known now, because they have no calendar connected (or it needs reconnecting, or its provider is not configured here)
/// and no interview hours of their own; while any are listed there are no slots, because offering times without knowing
/// when they are free could double-book them. Interview blocks need no calendar, so they never list anyone.
/// </summary>
public sealed record InterviewAvailabilityResult(IReadOnlyList<DateTimeOffset> Slots, IReadOnlyList<Guid> UnavailableInterviewers)
{
    public bool HasAllCalendars => UnavailableInterviewers.Count == 0;
}

/// <summary>Whether one interviewer is free at one time, as the team sees it beside the candidate's or its own times.</summary>
public enum InterviewerFreeness
{
    Free,

    /// <summary>Their calendar or an interview booked through ScreenHire holds the time (or the buffer around one, or a full day).</summary>
    Busy,

    /// <summary>The time is outside their own interview hours.</summary>
    OutsideHours,

    /// <summary>Nothing says: no calendar to read and no hours, or their calendar cannot be read just now.</summary>
    Unknown,
}

/// <summary>Whether <see cref="UserId"/> is free for an interview starting at <see cref="Start"/>.</summary>
public sealed record InterviewerFreeAt(DateTimeOffset Start, Guid UserId, InterviewerFreeness State);

/// <summary>
/// Works out when interviewers are free (docs/architecture/interview-scheduling.md): each one's busy time from their own
/// calendar, the interviews ScreenHire has booked for them with their buffer and daily limit, and the time outside their own
/// interview hours; then the times an interview offers, put through <see cref="SlotFinder"/> in the interview's hours and zone.
/// </summary>
/// <remarks>
/// The bookings of other interviews are read through the job-scope filter, so the caller must act for the whole
/// organization (the public careers endpoints and the worker do: no signed-in user; a staff route opens a scope for the
/// organization). A signed-in member on Only jobs they're on would not see an interviewer's bookings on other jobs. A provider
/// that is down throws <see cref="CalendarApiException"/> from <see cref="FindSlotsAsync"/>, which is not the same as having no
/// calendar; the caller lets it propagate. <see cref="FreeAtAsync"/> reads it as unknown instead, because a person decides.
/// </remarks>
public sealed class InterviewAvailability(ScreenHireDbContext db, CalendarAccess calendar, TimeProvider time)
{
    /// <summary>The most start times one call returns (a 90 day window at a 15 minute step would otherwise list thousands).</summary>
    public const int MaxSlots = 2000;

    /// <summary>
    /// The offered start times for <paramref name="interview"/> with <paramref name="attendees"/>: for pick from our times, the
    /// free times in its hours plus its one-off times that are still free. For a booked interview that is being moved, the
    /// interview's own event on each calendar is not counted as busy: an event that fills exactly its current time is dropped
    /// (best effort: a provider that merges it with an adjacent meeting hides it).
    /// </summary>
    public Task<InterviewAvailabilityResult> FindSlotsAsync(
        InterviewRequest interview, IReadOnlyList<InterviewAttendee> attendees, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interview);
        ArgumentNullException.ThrowIfNull(attendees);
        return OpenSlotsAsync(
            [.. attendees.Select(a => a.UserId).Distinct()], interview.WindowStart, interview.WindowEnd, interview.Duration, interview.Zone,
            interview.EffectiveHours, interview.UseBlocks, interview.ExtraTimes, interview, cancellationToken);
    }

    /// <summary>
    /// The times pick from our times would offer for these interviewers, before or after an interview exists: the free times
    /// in <paramref name="hours"/> on the step, past the minimum notice, plus <paramref name="extraTimes"/> that are free.
    /// <paramref name="own"/> is the interview itself, if it exists, whose own time does not count as busy.
    /// </summary>
    public async Task<InterviewAvailabilityResult> OpenSlotsAsync(
        IReadOnlyList<Guid> userIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        TimeSpan duration,
        TimeZoneInfo zone,
        WeeklyHours hours,
        bool useBlocks,
        IReadOnlyList<DateTimeOffset> extraTimes,
        InterviewRequest? own,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        ArgumentNullException.ThrowIfNull(extraTimes);
        RequireWholeOrganization(db);
        var now = time.GetUtcNow();
        var options = calendar.Options.Interviews;
        var busy = await BusyAsync(userIds, windowStart, windowEnd, own, softCalendars: false, cancellationToken);

        // Interview blocks are the business's word for when it interviews, so nobody needs a calendar for them.
        var unavailable = useBlocks ? [] : busy.Where(b => !b.Value.Known).Select(b => b.Key).ToList();
        if (unavailable.Count > 0)
        {
            return new InterviewAvailabilityResult([], unavailable);
        }

        var perInterviewer = userIds.Select(u => busy[u].All).ToList();
        var slots = SlotFinder.Find(
            windowStart, windowEnd, duration, options.StepMinutes, zone, hours, now + TimeSpan.FromHours(options.MinNoticeHours), perInterviewer, MaxSlots).ToList();

        // A one-off time is outside the usual hours by design, so only busy time and the window hold it back.
        var merged = perInterviewer.Select(SlotFinder.Merge).ToList();
        foreach (var extra in extraTimes)
        {
            var end = extra + duration;
            if (extra > now && extra >= windowStart && end <= windowEnd && merged.All(m => SlotFinder.IsFree(m, extra, end)) && !slots.Contains(extra))
            {
                slots.Add(extra);
            }
        }

        slots.Sort();
        return new InterviewAvailabilityResult(slots, []);
    }

    /// <summary>
    /// Whether each of <paramref name="userIds"/> is free for an interview of <paramref name="duration"/> at each of
    /// <paramref name="starts"/>, for the team to choose a time by: the candidate's suggestions, or the times the team is picking.
    /// <paramref name="interview"/> is the interview being moved, if any, whose own time does not count as busy.
    /// </summary>
    public async Task<IReadOnlyList<InterviewerFreeAt>> FreeAtAsync(
        IReadOnlyList<Guid> userIds, IReadOnlyList<DateTimeOffset> starts, TimeSpan duration, InterviewRequest? interview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        ArgumentNullException.ThrowIfNull(starts);
        RequireWholeOrganization(db);
        if (userIds.Count == 0 || starts.Count == 0)
        {
            return [];
        }

        var from = starts.Min();
        var to = starts.Max() + duration;
        var busy = await BusyAsync([.. userIds.Distinct()], from, to, interview, softCalendars: true, cancellationToken);
        var answers = new List<InterviewerFreeAt>();
        foreach (var start in starts.Distinct().Order())
        {
            var end = start + duration;
            foreach (var userId in userIds.Distinct())
            {
                var person = busy[userId];
                var state = !SlotFinder.IsFree(person.Booked, start, end) ? InterviewerFreeness.Busy
                    : person.Outside is { } outside && !SlotFinder.IsFree(outside, start, end) ? InterviewerFreeness.OutsideHours
                    : person.Calendar is { } read && !SlotFinder.IsFree(read, start, end) ? InterviewerFreeness.Busy
                    : person.Calendar is null && person.Outside is null ? InterviewerFreeness.Unknown
                    : InterviewerFreeness.Free;
                answers.Add(new InterviewerFreeAt(start, userId, state));
            }
        }

        return answers;
    }

    /// <summary>
    /// The interviews ScreenHire has booked for these interviewers between <paramref name="windowStart"/> and <paramref name="windowEnd"/>,
    /// as busy time per interviewer (user id), leaving out the interview <paramref name="exceptInterviewId"/>. Read from the
    /// database each call, never from the change tracker, because the booking check runs it after taking the locks.
    /// </summary>
    private async Task<Dictionary<Guid, List<BusyInterval>>> ConfirmedBusyAsync(
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
    /// Whether ScreenHire's own bookings keep any of these interviewers from <paramref name="start"/> to <paramref name="end"/>:
    /// an overlap, and with <paramref name="withHours"/> also what their own hours make of those bookings (the buffer
    /// around each, a day at its maximum, time outside their hours). Read from the database, so the booking check runs it
    /// after taking the locks, when another booking may have landed since the times were offered.
    /// </summary>
    public async Task<bool> BookingsKeepAsync(
        IReadOnlyList<Guid> userIds, DateTimeOffset start, DateTimeOffset end, Guid exceptInterviewId, bool withHours, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        // A day either side, so a booking just outside the span still counts toward its day's limit and its buffer.
        var bookings = await ConfirmedBusyAsync(userIds, start.AddDays(-1), end.AddDays(1), exceptInterviewId, cancellationToken);
        var hours = withHours
            ? await db.InterviewHours.AsNoTracking().Where(h => userIds.Contains(h.UserId)).ToDictionaryAsync(h => h.UserId, cancellationToken)
            : new Dictionary<Guid, InterviewHours>();
        foreach (var userId in userIds)
        {
            IReadOnlyList<BusyInterval> booked = bookings.GetValueOrDefault(userId) ?? [];
            IEnumerable<BusyInterval> busy = hours.GetValueOrDefault(userId) is { } own ? [.. booked, .. own.BusyTime(booked, start, end)] : booked;
            if (busy.Any(b => b.Start < end && b.End > start))
            {
                return true;
            }
        }

        return false;
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

    // Each interviewer's busy time from `from` to `to`, by its source. One interviewer at a time: the sandbox provider shares
    // this context, which runs one query at a time. With softCalendars a calendar that is down reads as not read.
    private async Task<Dictionary<Guid, InterviewerBusy>> BusyAsync(
        IReadOnlyList<Guid> userIds, DateTimeOffset from, DateTimeOffset to, InterviewRequest? interview, bool softCalendars, CancellationToken cancellationToken)
    {
        var hours = await db.InterviewHours.AsNoTracking().Where(h => userIds.Contains(h.UserId)).ToDictionaryAsync(h => h.UserId, cancellationToken);

        // A day either side, so a booking just outside the span still counts toward its day's limit and its buffer.
        var bookings = await ConfirmedBusyAsync(userIds, from.AddDays(-1), to.AddDays(1), interview?.Id ?? Guid.Empty, cancellationToken);
        var result = new Dictionary<Guid, InterviewerBusy>();
        foreach (var userId in userIds)
        {
            IReadOnlyList<BusyInterval>? read = null;
            if (await calendar.GetSessionAsync(userId, cancellationToken) is { } session)
            {
                try
                {
                    var intervals = await session.Provider.FreeBusyAsync(session.AccessToken, from, to, cancellationToken);
                    read = [.. intervals.Where(i => interview is null || !IsTheInterviewsOwnEvent(interview, i))];
                }
                catch (CalendarApiException) when (softCalendars)
                {
                    read = null;
                }
            }

            var booked = bookings.GetValueOrDefault(userId) ?? [];
            var own = hours.GetValueOrDefault(userId);
            IReadOnlyList<BusyInterval> fromHours = own?.BusyTime(booked, from, to) ?? [];
            IReadOnlyList<BusyInterval>? outside = own?.Weekly?.Outside(own.Zone, from, to);
            var bookedAll = SlotFinder.Merge([.. booked, .. fromHours.Except(outside ?? [])]);
            result[userId] = new InterviewerBusy(read is null ? null : SlotFinder.Merge(read), bookedAll, outside is null ? null : SlotFinder.Merge(outside), own?.HasHours == true);
        }

        return result;
    }

    private static bool IsTheInterviewsOwnEvent(InterviewRequest interview, BusyInterval interval) =>
        interview.Status == InterviewStatus.Booked && interview.StartsAt == interval.Start && interview.EndsAt == interval.End;

    // One interviewer's busy time: what their calendar says (null when it was not read), what ScreenHire's bookings make
    // (the bookings, their buffer and full days), and the time outside their own hours (null without hours).
    private sealed record InterviewerBusy(
        IReadOnlyList<BusyInterval>? Calendar, IReadOnlyList<BusyInterval> Booked, IReadOnlyList<BusyInterval>? Outside, bool HasHours)
    {
        /// <summary>Whether their time can be known: a calendar was read, or they set their own hours.</summary>
        public bool Known => Calendar is not null || HasHours;

        public IReadOnlyList<BusyInterval> All => [.. Calendar ?? [], .. Booked, .. Outside ?? []];
    }
}
