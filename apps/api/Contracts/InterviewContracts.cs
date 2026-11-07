using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// A member who can interview for a job: one who sees it (everyone who sees all jobs, and the members on its hiring team).
/// <c>calendarConnected</c> is true when their calendar is connected and works here, so ScreenHire can read when they are
/// busy and put the interview in it; an interview cannot be offered with someone who has none. <c>calendarProvider</c>
/// says which calendar it is, and is null when <c>calendarConnected</c> is false.
/// </summary>
public sealed record Interviewer(Guid UserId, string DisplayName, bool CalendarConnected, CalendarProvider? CalendarProvider);

/// <summary>
/// Offers an application's candidate an interview. <c>interviewerUserIds</c> are one to five members who see the job; the
/// first is the organizer, whose calendar event invites the candidate and makes the meeting link. <c>durationMinutes</c>
/// is 15, 30, 45, 60, 90 or 120. The candidate picks a start that fits inside <c>windowStart</c> and <c>windowEnd</c>
/// (at most 90 days) within the working hours <c>dayStart</c> and <c>dayEnd</c> (<c>HH:mm</c>, default 09:00 and 17:00,
/// weekdays only) in <c>timeZone</c> (an IANA id such as <c>Europe/London</c>), which is also the zone the candidate's page
/// first shows.
/// </summary>
public sealed record ScheduleInterviewRequest(
    IReadOnlyList<Guid>? InterviewerUserIds,
    int DurationMinutes,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string? TimeZone,
    string? DayStart = null,
    string? DayEnd = null);

/// <summary>
/// One interviewer on an interview and the state of their calendar event. <c>syncStatus</c> is <c>Pending</c> (not on the
/// calendar yet, or being changed), <c>Synced</c> or <c>Failed</c>; <c>syncProblem</c> says why in plain words when it
/// failed. <c>displayName</c> is "Former member" for someone who left the organization.
/// </summary>
public sealed record InterviewParticipant(Guid UserId, string DisplayName, bool IsOrganizer, InterviewSyncStatus SyncStatus, string? SyncProblem);

/// <summary>
/// An interview offered to the candidate. <c>status</c> is <c>Open</c> (waiting for the candidate to pick a time),
/// <c>Booked</c>, <c>Cancelled</c> or <c>Expired</c> (the window passed with no booking; an Open request past its window
/// reads as Expired). <c>startsAt</c> and <c>endsAt</c> are set once booked, and stay on a cancelled interview that had
/// been booked. <c>meetingUrl</c> is the organizer's Meet or Teams link, once their event made it. <c>cancelledByCandidate</c>
/// is true when the candidate cancelled it. <c>createdByUserId</c> is null when a workflow action offered it.
/// <c>newTimesOfferedAt</c> is set while the team's offer of new times for a booked interview waits for the candidate: the
/// booked time (<c>startsAt</c>) stays held, and <c>windowStart</c> and <c>windowEnd</c> are the new window.
/// </summary>
public sealed record ApplicationInterview(
    Guid Id,
    InterviewStatus Status,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string TimeZone,
    int DurationMinutes,
    string DayStart,
    string DayEnd,
    string? MeetingUrl,
    IReadOnlyList<InterviewParticipant> Interviewers,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    bool CancelledByCandidate,
    DateTimeOffset? NewTimesOfferedAt);

/// <summary>
/// Offers the candidate new times for a booked interview: they pick from <c>windowStart</c> to <c>windowEnd</c> (at most 90
/// days), in the interview's own length, working hours and zone. The booked time stays held until they pick, and the
/// events in every calendar then move. Offering again replaces the window.
/// </summary>
public sealed record OfferNewInterviewTimesRequest(DateTimeOffset WindowStart, DateTimeOffset WindowEnd);

/// <summary>Where a candidate's interview stands on the careers page.</summary>
public enum PublicInterviewState
{
    /// <summary>The candidate may pick a time from <c>slots</c>.</summary>
    Open,

    /// <summary>A time is booked (<c>startsAt</c>); <c>canChange</c> says whether the candidate may still move or cancel it.</summary>
    Booked,

    /// <summary>The team or the candidate cancelled it.</summary>
    Cancelled,

    /// <summary>The window passed with no booking.</summary>
    Expired,
}

/// <summary>
/// The interview as the candidate's page shows it. <c>slots</c> are the start times offered, as UTC instants in order, each
/// <c>durationMinutes</c> long: for an Open interview, and for a Booked one the candidate may still change. The page writes
/// them in <c>timeZone</c> (the <c>tz</c> asked for when it is a zone the server knows, otherwise <c>defaultTimeZone</c>,
/// the zone the team chose). <c>slotsUnavailable</c> is true when the team's calendars could not be read just now, so no
/// times are offered; trying again later may work. <c>minNoticeHours</c> is how long before the start the candidate can
/// still move or cancel. <c>newTimesOffered</c> is true while the team's offer of new times waits for the candidate: the
/// booked time stays until they pick, and they may move or cancel it whatever the notice. Nothing about the interviewers,
/// and nothing from verification.
/// </summary>
public sealed record PublicInterview(
    PublicOrganization Organization,
    string JobTitle,
    PublicInterviewState State,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string TimeZone,
    string DefaultTimeZone,
    int DurationMinutes,
    IReadOnlyList<DateTimeOffset> Slots,
    bool SlotsUnavailable,
    bool CanChange,
    int MinNoticeHours,
    bool NewTimesOffered);

/// <summary>The start time the candidate picked, one of the offered <c>slots</c>.</summary>
public sealed record PublicInterviewChoice(DateTimeOffset Start);
