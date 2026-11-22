using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// A member who can interview for a job: one who sees it (everyone who sees all jobs, and the members on its hiring team).
/// <c>calendarConnected</c> is true when their calendar is connected and works here, so ScreenHire can read when they are
/// busy and put the interview in it; an interview cannot be offered with someone who has none. <c>calendarProvider</c>
/// says which calendar it is, and is null when <c>calendarConnected</c> is false. <c>hasHours</c> is true when they set their
/// own interview hours, which is ScreenHire's calendar for someone with no connected one: either makes them someone times can
/// be offered from.
/// </summary>
public sealed record Interviewer(Guid UserId, string DisplayName, bool CalendarConnected, CalendarProvider? CalendarProvider, bool HasHours = false);

/// <summary>
/// Offers an application's candidate an interview. <c>interviewerUserIds</c> are one to five members who see the job; the
/// first is the organizer, whose calendar event invites the candidate and makes the meeting link. <c>durationMinutes</c>
/// is 15, 30, 45, 60, 90 or 120. The candidate picks a start that fits inside <c>windowStart</c> and <c>windowEnd</c>
/// (at most 90 days) within the working hours <c>dayStart</c> and <c>dayEnd</c> (<c>HH:mm</c>, default 09:00 and 17:00,
/// weekdays only) in <c>timeZone</c> (an IANA id such as <c>Europe/London</c>), which is also the zone the candidate's page
/// first shows.
/// </summary>
/// <remarks>
/// How the candidate picks (docs/architecture/interview-scheduling.md, "Three ways to pick a time"): <c>option</c> is
/// <c>OurTimes</c> (the default: open slots, booked at once), <c>TheirTimes</c> (the candidate suggests 3 to 5 times within
/// the window and hours) or <c>OurSuggestion</c> (the team's one or two <c>proposedTimes</c>, accepted by the candidate).
/// <c>hours</c> are the hours times fall in, any day of the week, in place of <c>dayStart</c> and <c>dayEnd</c>; with
/// <c>useBlocks</c> (pick from our times only) they are interview blocks, and the interviewers need no calendar.
/// <c>extraTimes</c> are up to three one-off times added to pick from our times.
/// </remarks>
public sealed record ScheduleInterviewRequest(
    IReadOnlyList<Guid>? InterviewerUserIds,
    int DurationMinutes,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string? TimeZone,
    string? DayStart = null,
    string? DayEnd = null,
    InterviewOption? Option = null,
    WeeklyHours? Hours = null,
    bool UseBlocks = false,
    IReadOnlyList<DateTimeOffset>? ProposedTimes = null,
    IReadOnlyList<DateTimeOffset>? ExtraTimes = null);

/// <summary>
/// Changes how the candidate picks a time: on an Open interview (Change how they pick), or to move a Booked one, whose time
/// stays held until a new one is booked. The fields are <see cref="ScheduleInterviewRequest"/>'s, over the new days
/// <c>windowStart</c> to <c>windowEnd</c>; the interview keeps its interviewers, length and zone. The candidate's link stays
/// the same, and they are emailed that their options changed.
/// </summary>
public sealed record ChangeInterviewOptionRequest(
    InterviewOption Option,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    WeeklyHours? Hours = null,
    bool UseBlocks = false,
    IReadOnlyList<DateTimeOffset>? ProposedTimes = null,
    IReadOnlyList<DateTimeOffset>? ExtraTimes = null);

/// <summary>The team books one of the times the candidate suggested.</summary>
public sealed record BookInterviewTimeRequest(DateTimeOffset Start);

/// <summary>None of the candidate's times works: the team asks for more, with an optional short message (500 characters at most).</summary>
public sealed record AskForMoreTimesRequest(string? Note);

/// <summary>
/// The times an interview would offer, before it is sent: for the sheet's check that pick from our times has something to
/// offer. The fields are the interview's; <c>jobId</c> is the job the interviewers must see.
/// </summary>
public sealed record OpenTimesRequest(
    Guid JobId,
    IReadOnlyList<Guid> InterviewerUserIds,
    int DurationMinutes,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string TimeZone,
    string? DayStart = null,
    string? DayEnd = null,
    WeeklyHours? Hours = null,
    bool UseBlocks = false);

/// <summary>
/// What pick from our times would offer: <c>slots</c>, the first open times (at most 50) in order, and
/// <c>nextSevenDays</c>, how many open times there are in the 7 days from now. <c>unavailableInterviewerUserIds</c> are the
/// interviewers whose time cannot be known (no calendar and no hours), while any are named nothing is offered.
/// </summary>
public sealed record OpenTimes(IReadOnlyList<DateTimeOffset> Slots, int NextSevenDays, IReadOnlyList<Guid> UnavailableInterviewerUserIds);

/// <summary>
/// Whether each interviewer is free at each of <c>starts</c> (one to ten times) for an interview of <c>durationMinutes</c>:
/// for the team choosing among the candidate's times or picking its own. <c>interviewId</c> is the interview being moved, whose
/// own time does not count as busy.
/// </summary>
public sealed record FreeTimesRequest(Guid JobId, IReadOnlyList<Guid> InterviewerUserIds, IReadOnlyList<DateTimeOffset> Starts, int DurationMinutes, Guid? InterviewId = null);

/// <summary>One interviewer at one time: <c>Free</c>, <c>Busy</c>, <c>OutsideHours</c> (outside their own interview hours) or <c>Unknown</c>.</summary>
public sealed record InterviewerFree(Guid UserId, Application.Scheduling.InterviewerFreeness State);

/// <summary>Who is free at one time.</summary>
public sealed record FreeTime(DateTimeOffset Start, IReadOnlyList<InterviewerFree> Interviewers);

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
/// booked time (<c>startsAt</c>) stays held, and <c>windowStart</c> and <c>windowEnd</c> are the new window. The end of that
/// window ends the offer.
/// </summary>
/// <remarks>
/// <c>option</c> is how the candidate picks, with <c>hours</c> and <c>useBlocks</c> as offered. <c>proposedTimes</c> are the
/// team's times (we'll suggest a time) and <c>extraTimes</c> the one-off times added to pick from our times.
/// <c>candidateTimes</c> are the times the candidate suggested (tell us when you're free), sent at <c>candidateTimesAt</c>.
/// <c>anotherTimeAskedAt</c> and <c>candidateNote</c> say the candidate asked for another time than the team's, and
/// <c>teamNote</c> is the team's message when it asked for more times. <c>waitsForTeam</c> is true when the next step is the
/// team's: book one of the candidate's times, or answer their request for another.
/// </remarks>
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
    DateTimeOffset? NewTimesOfferedAt,
    InterviewOption Option = InterviewOption.OurTimes,
    WeeklyHours? Hours = null,
    bool UseBlocks = false,
    IReadOnlyList<DateTimeOffset>? ProposedTimes = null,
    IReadOnlyList<DateTimeOffset>? ExtraTimes = null,
    IReadOnlyList<DateTimeOffset>? CandidateTimes = null,
    DateTimeOffset? CandidateTimesAt = null,
    DateTimeOffset? AnotherTimeAskedAt = null,
    string? CandidateNote = null,
    string? TeamNote = null,
    bool WaitsForTeam = false);

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
/// booked time stays until they pick, and they may move or cancel it whatever the notice, until the offered days end.
/// <c>meetingName</c> is what the team calls the meeting (the stage's meeting name), in lower case, such as "trial shift"
/// or "interview"; the page uses it for its title, headings and buttons.
/// Nothing about the interviewers, and nothing from verification.
/// </summary>
/// <remarks>
/// <c>option</c> is how the candidate picks. Pick from our times uses <c>slots</c>. Tell us when you're free uses
/// <c>suggestable</c>, every time within the team's limits (the window, the hours and the notice, on the step), of which the
/// candidate sends 3 to 5; <c>suggestedTimes</c> are the ones they sent. We'll suggest a time uses <c>proposedTimes</c>, of
/// which the candidate accepts one or asks for another (<c>anotherTimeAsked</c>). <c>waitingForTeam</c> is true when the next
/// step is the team's, and <c>teamNote</c> is the team's message when it asked for more times.
/// </remarks>
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
    bool NewTimesOffered,
    string MeetingName,
    InterviewOption Option = InterviewOption.OurTimes,
    IReadOnlyList<DateTimeOffset>? Suggestable = null,
    IReadOnlyList<DateTimeOffset>? SuggestedTimes = null,
    IReadOnlyList<DateTimeOffset>? ProposedTimes = null,
    bool AnotherTimeAsked = false,
    bool WaitingForTeam = false,
    string? TeamNote = null);

/// <summary>The start time the candidate picked, one of the offered <c>slots</c>.</summary>
public sealed record PublicInterviewChoice(DateTimeOffset Start);

/// <summary>The times the candidate is free: 3 to 5 of <c>suggestable</c>.</summary>
public sealed record PublicInterviewSuggestion(IReadOnlyList<DateTimeOffset> Times);

/// <summary>The candidate cannot make the team's times and asks for another, with an optional note (500 characters at most).</summary>
public sealed record PublicInterviewAnotherTime(string? Note);
