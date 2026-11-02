using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// A member who can interview for a job: one who sees it (everyone who sees all jobs, and the members on its hiring team).
/// <c>calendarConnected</c> is true when their calendar is connected and works here, so ScreenHire can read when they are
/// busy and put the interview in it; an interview cannot be offered with someone who has none. <c>calendarProvider</c>
/// says which calendar it is, and is null when <c>calendarConnected</c> is false.
/// </summary>
public sealed record Interviewer(Guid UserId, string DisplayName, bool CalendarConnected, CalendarProvider? CalendarProvider);
