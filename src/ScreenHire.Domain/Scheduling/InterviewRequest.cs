using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Scheduling;

public enum InterviewStatus
{
    /// <summary>Waiting for the candidate to pick a time inside the window.</summary>
    Open,

    /// <summary>The candidate picked a time; <see cref="InterviewRequest.StartsAt"/> holds it.</summary>
    Booked,

    /// <summary>The team or the candidate cancelled it.</summary>
    Cancelled,

    /// <summary>The window passed with no booking. Stored lazily: <see cref="InterviewRequest.EffectiveStatus"/> reads it without a timer.</summary>
    Expired,
}

/// <summary>
/// One interview offered to one application's candidate (docs/architecture/interview-scheduling.md): who interviews,
/// how long, the window and working hours the candidate may pick from, and once booked, the time. There is at most one
/// Open or Booked request per application (a partial unique index); history stays as Cancelled or Expired rows.
/// </summary>
/// <remarks>
/// Methods take <c>now</c> and change only this row. The caller books under the interviewers' locks
/// (<c>InterviewBooking</c>), writes the attendees and publishes the messages that put events on calendars.
/// </remarks>
public sealed class InterviewRequest : IApplicationScoped
{
    /// <summary>The interview lengths a request may have, in minutes.</summary>
    public static readonly IReadOnlyList<int> AllowedDurations = [15, 30, 45, 60, 90, 120];

    /// <summary>The longest window a candidate may pick from.</summary>
    public const int MaxWindowDays = 90;

    public const int MaxTimeZoneLength = 64;

    public const int MaxMeetingUrlLength = 500;

    public const int MaxSourceKeyLength = 200;

    public static readonly TimeOnly DefaultDayStart = new(9, 0);

    public static readonly TimeOnly DefaultDayEnd = new(17, 0);

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid JobId { get; init; }

    public Guid ApplicationId { get; init; }

    public int DurationMinutes { get; init; }

    /// <summary>No interview starts before this instant. <see cref="OfferNewTimes"/> replaces it.</summary>
    public DateTimeOffset WindowStart { get; private set; }

    /// <summary>No interview ends after this instant. An Open request past it reads as Expired.</summary>
    public DateTimeOffset WindowEnd { get; private set; }

    /// <summary>
    /// The first instant any window of this request has covered. The windows can be replaced (<see cref="OfferNewTimes"/>), and a
    /// calendar event whose id was never saved is looked up across the whole span, so it is found at any time the
    /// interview has had.
    /// </summary>
    public DateTimeOffset LookupStart { get; private set; }

    /// <summary>The last instant any window of this request has covered (see <see cref="LookupStart"/>).</summary>
    public DateTimeOffset LookupEnd { get; private set; }

    /// <summary>
    /// When the team last offered new times for this booked interview, until the candidate picks one (or it is cancelled).
    /// While set, the booked time stays held, the candidate may move or cancel whatever the minimum notice, and the offered
    /// times come from the new window. It is not a change to the booking, so <see cref="Revision"/> does not move.
    /// </summary>
    public DateTimeOffset? NewTimesOfferedAt { get; private set; }

    /// <summary>The IANA time zone the working hours are in, and the candidate page's display default.</summary>
    public required string TimeZone { get; init; }

    /// <summary>Working hours, local time in <see cref="TimeZone"/>, weekdays only.</summary>
    public TimeOnly DayStart { get; init; } = DefaultDayStart;

    public TimeOnly DayEnd { get; init; } = DefaultDayEnd;

    public InterviewStatus Status { get; private set; }

    public DateTimeOffset? StartsAt { get; private set; }

    public DateTimeOffset? EndsAt { get; private set; }

    /// <summary>The Meet or Teams link, once the organizer's event has one.</summary>
    public string? MeetingUrl { get; private set; }

    /// <summary>
    /// Counts bookings, reschedules and cancellations. The idempotency keys of the messages that sync calendars and the
    /// provider event ids derive from it, so a redelivered message is a no-op and a later change is a new intent.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>The person who scheduled it; null when a workflow action did.</summary>
    public Guid? CreatedByUserId { get; init; }

    /// <summary>The workflow action's key (run, action index), so running the action again finds this request.</summary>
    public string? SourceKey { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public bool CancelledByCandidate { get; private set; }

    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);

    /// <summary>The zone the working hours are in; UTC if this host no longer knows the id.</summary>
    public TimeZoneInfo Zone => TryFindZone(TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>
    /// The first thing wrong with these inputs in plain words, or null when a request can be made from them. The API turns
    /// it into a validation problem; <see cref="Create"/> throws it.
    /// </summary>
    public static string? Problem(
        int durationMinutes, DateTimeOffset windowStart, DateTimeOffset windowEnd, string? timeZone, TimeOnly dayStart, TimeOnly dayEnd, DateTimeOffset now)
    {
        if (!AllowedDurations.Contains(durationMinutes))
        {
            return $"An interview lasts {string.Join(", ", AllowedDurations)} minutes.";
        }

        if (windowEnd <= windowStart)
        {
            return "The window must end after it starts.";
        }

        if (windowEnd - windowStart > TimeSpan.FromDays(MaxWindowDays))
        {
            return $"The window can be at most {MaxWindowDays} days.";
        }

        if (windowEnd <= now)
        {
            return "The window must end in the future.";
        }

        if (!TryFindZone(timeZone, out _))
        {
            return "Choose a time zone.";
        }

        if (dayEnd <= dayStart)
        {
            return "Working hours must end after they start.";
        }

        return dayEnd - dayStart < TimeSpan.FromMinutes(durationMinutes) ? "Working hours are shorter than the interview." : null;
    }

    /// <summary>The zone for an IANA id, or false when this host does not know it.</summary>
    public static bool TryFindZone(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaxTimeZoneLength)
        {
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>A new Open request for <paramref name="application"/>. Working hours default to 09:00 to 17:00.</summary>
    public static InterviewRequest Create(
        JobApplication application,
        int durationMinutes,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string timeZone,
        Guid? createdByUserId,
        DateTimeOffset now,
        TimeOnly? dayStart = null,
        TimeOnly? dayEnd = null,
        string? sourceKey = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        var start = dayStart ?? DefaultDayStart;
        var end = dayEnd ?? DefaultDayEnd;
        if (Problem(durationMinutes, windowStart, windowEnd, timeZone, start, end, now) is { } problem)
        {
            throw new ArgumentException(problem);
        }

        if (sourceKey is { Length: > MaxSourceKeyLength })
        {
            throw new ArgumentException("The source key is too long.", nameof(sourceKey));
        }

        return new InterviewRequest
        {
            OrganizationId = application.OrganizationId,
            JobId = application.JobId,
            ApplicationId = application.Id,
            DurationMinutes = durationMinutes,
            WindowStart = windowStart.ToUniversalTime(),
            WindowEnd = windowEnd.ToUniversalTime(),
            LookupStart = windowStart.ToUniversalTime(),
            LookupEnd = windowEnd.ToUniversalTime(),
            TimeZone = timeZone,
            DayStart = start,
            DayEnd = end,
            Status = InterviewStatus.Open,
            CreatedByUserId = createdByUserId,
            SourceKey = sourceKey,
            CreatedAt = now,
        };
    }

    /// <summary>The status as it reads at <paramref name="now"/>: an Open request past its window is Expired, with no timer to say so.</summary>
    public InterviewStatus EffectiveStatus(DateTimeOffset now) =>
        Status == InterviewStatus.Open && now >= WindowEnd ? InterviewStatus.Expired : Status;

    /// <summary>Stores Expired for an Open request past its window (the partial unique index sees the stored status). False when nothing changed.</summary>
    public bool MarkExpired(DateTimeOffset now)
    {
        if (Status != InterviewStatus.Open || EffectiveStatus(now) != InterviewStatus.Expired)
        {
            return false;
        }

        Status = InterviewStatus.Expired;
        return true;
    }

    /// <summary>The candidate (or a person for them) picks <paramref name="start"/>. Only an Open request inside its window books.</summary>
    public void Book(DateTimeOffset start, DateTimeOffset now)
    {
        if (EffectiveStatus(now) != InterviewStatus.Open)
        {
            throw new InvalidOperationException("Only an open interview can be booked.");
        }

        Place(start);
        Status = InterviewStatus.Booked;
    }

    /// <summary>Moves a booked interview to <paramref name="start"/>.</summary>
    public void Reschedule(DateTimeOffset start, DateTimeOffset now)
    {
        if (Status != InterviewStatus.Booked)
        {
            throw new InvalidOperationException("Only a booked interview can be rescheduled.");
        }

        if (start <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "An interview cannot be moved into the past.");
        }

        Place(start);
        NewTimesOfferedAt = null;
    }

    /// <summary>
    /// The first thing wrong with offering the new window <paramref name="windowStart"/> to <paramref name="windowEnd"/> for this
    /// interview, in plain words, or null. The window follows the same rules as a new request's, in this request's length,
    /// zone and working hours.
    /// </summary>
    public string? NewTimesProblem(DateTimeOffset windowStart, DateTimeOffset windowEnd, DateTimeOffset now) =>
        Problem(DurationMinutes, windowStart, windowEnd, TimeZone, DayStart, DayEnd, now);

    /// <summary>
    /// The team offers the candidate new times for a booked interview: the window the candidate picks from is replaced, and
    /// the booked time stays as it is, so it stays held until the candidate picks one (<see cref="Reschedule"/>). Offering again
    /// replaces the window again. Not a booking change, so <see cref="Revision"/> stays.
    /// </summary>
    public void OfferNewTimes(DateTimeOffset windowStart, DateTimeOffset windowEnd, DateTimeOffset now)
    {
        if (Status != InterviewStatus.Booked)
        {
            throw new InvalidOperationException("Only a booked interview can be offered new times.");
        }

        if (NewTimesProblem(windowStart, windowEnd, now) is { } problem)
        {
            throw new ArgumentException(problem);
        }

        WindowStart = windowStart.ToUniversalTime();
        WindowEnd = windowEnd.ToUniversalTime();
        LookupStart = DateTimeOffset.Compare(LookupStart, WindowStart) <= 0 ? LookupStart : WindowStart;
        LookupEnd = DateTimeOffset.Compare(LookupEnd, WindowEnd) >= 0 ? LookupEnd : WindowEnd;
        NewTimesOfferedAt = now;
    }

    /// <summary>Cancels an Open or Booked interview. A booked one keeps its time as history.</summary>
    public void Cancel(DateTimeOffset now, bool byCandidate)
    {
        if (EffectiveStatus(now) is not (InterviewStatus.Open or InterviewStatus.Booked))
        {
            throw new InvalidOperationException("Only an open or booked interview can be cancelled.");
        }

        Status = InterviewStatus.Cancelled;
        CancelledAt = now;
        CancelledByCandidate = byCandidate;
        NewTimesOfferedAt = null;
        Revision++;
    }

    /// <summary>
    /// Keeps the meeting link the organizer's event got. It comes from the provider and ends up as a link in the dashboard and
    /// the candidate's email, so anything but an absolute https address is dropped, as is an overlong one.
    /// </summary>
    public void SetMeetingUrl(string? url)
    {
        var trimmed = url?.Trim();
        MeetingUrl = trimmed is { Length: <= MaxMeetingUrlLength }
            && Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
                ? trimmed
                : null;
    }

    /// <summary>
    /// Whether the candidate may reschedule or cancel at <paramref name="now"/>: booked, and at least
    /// <paramref name="minNotice"/> before the start. When the team has offered new times the candidate may always, because
    /// the team asked for the move, even inside the notice or after the old time has passed.
    /// </summary>
    public bool CanBeChangedByCandidate(DateTimeOffset now, TimeSpan minNotice) =>
        Status == InterviewStatus.Booked && (NewTimesOfferedAt is not null || (StartsAt is { } starts && now <= starts - minNotice));

    /// <summary>Whether an interview starting at <paramref name="start"/> fits inside the window.</summary>
    public bool FitsWindow(DateTimeOffset start) => start >= WindowStart && start + Duration <= WindowEnd;

    private void Place(DateTimeOffset start)
    {
        if (!FitsWindow(start))
        {
            throw new ArgumentOutOfRangeException(nameof(start), "The interview must fit inside the window.");
        }

        StartsAt = start.ToUniversalTime();
        EndsAt = StartsAt + Duration;
        Revision++;
    }
}

/// <summary>Where one interviewer's calendar event stands.</summary>
public enum InterviewSyncStatus
{
    /// <summary>The event is not on the calendar yet, or must be changed to match the booking.</summary>
    Pending,

    Synced,

    /// <summary>The calendar refused or could not be reached for good; <see cref="InterviewAttendee.SyncProblem"/> says so in plain words.</summary>
    Failed,
}

/// <summary>
/// One interviewer on an interview: their own event on their own calendar. The first one is the organizer, and the
/// candidate is invited on that event only. Relational, not JSON, so the double-booking check can query by interviewer.
/// </summary>
public sealed class InterviewAttendee : IApplicationScoped
{
    public const int MaxEventIdLength = 200;

    public const int MaxProblemLength = 200;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    public Guid InterviewId { get; init; }

    /// <summary>The interviewer's user id (the calendar connection's owner).</summary>
    public Guid UserId { get; init; }

    /// <summary>The one whose event invites the candidate and makes the meeting link.</summary>
    public bool IsOrganizer { get; private set; }

    /// <summary>The provider of the interviewer's calendar when the event was last written.</summary>
    public CalendarProvider Provider { get; private set; }

    public string? ProviderEventId { get; private set; }

    public InterviewSyncStatus SyncStatus { get; private set; }

    public string? SyncProblem { get; private set; }

    /// <summary>
    /// The interview start this interviewer was last reminded about (the day-before email). The reminder is for one booked time, so
    /// a move starts again, and a redelivered reminder for the same time finds this and sends nothing.
    /// </summary>
    public DateTimeOffset? RemindedForStart { get; private set; }

    public static InterviewAttendee Create(InterviewRequest interview, Guid userId, bool isOrganizer, CalendarProvider provider)
    {
        ArgumentNullException.ThrowIfNull(interview);
        return new InterviewAttendee
        {
            OrganizationId = interview.OrganizationId,
            ApplicationId = interview.ApplicationId,
            InterviewId = interview.Id,
            UserId = userId,
            IsOrganizer = isOrganizer,
            Provider = provider,
        };
    }

    /// <summary>The organizer left before the interview was booked, so this interviewer's event will invite the candidate instead.</summary>
    public void MakeOrganizer() => IsOrganizer = true;

    /// <summary>The booking changed: the event must be written again.</summary>
    public void MarkPending()
    {
        SyncStatus = InterviewSyncStatus.Pending;
        SyncProblem = null;
    }

    public void MarkSynced(CalendarProvider provider, string eventId)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventId);
        Provider = provider;
        ProviderEventId = eventId.Length > MaxEventIdLength ? eventId[..MaxEventIdLength] : eventId;
        SyncStatus = InterviewSyncStatus.Synced;
        SyncProblem = null;
    }

    /// <summary>Records that the reminder for the interview starting at <paramref name="start"/> was sent.</summary>
    public void MarkReminded(DateTimeOffset start) => RemindedForStart = start;

    public void MarkFailed(string problem)
    {
        ArgumentException.ThrowIfNullOrEmpty(problem);
        SyncStatus = InterviewSyncStatus.Failed;
        SyncProblem = problem.Length > MaxProblemLength ? problem[..MaxProblemLength] : problem;
    }
}
