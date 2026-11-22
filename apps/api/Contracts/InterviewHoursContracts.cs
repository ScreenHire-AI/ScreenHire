using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The caller's own interview hours (Your account, Calendar): the days and hours they interview (<c>weekly</c>, null when
/// they set none), the minutes kept free around each interview (<c>bufferMinutes</c>, 0 to 120), the most interviews in a
/// day (<c>maxPerDay</c>, 1 to 12, null for no limit) and the IANA <c>timeZone</c> the hours are in. Hours make them
/// someone times can be offered from without a connected calendar (ScreenHire's calendar). <c>feedUrl</c> is their private
/// subscribe link, a calendar feed of their booked interviews, or null while it is off.
/// </summary>
public sealed record InterviewHoursSettings(string TimeZone, WeeklyHours? Weekly, int BufferMinutes, int? MaxPerDay, string? FeedUrl);

/// <summary>Sets the caller's interview hours; see <see cref="InterviewHoursSettings"/>.</summary>
public sealed record UpdateInterviewHoursRequest(string? TimeZone, WeeklyHours? Weekly, int BufferMinutes = 0, int? MaxPerDay = null);
