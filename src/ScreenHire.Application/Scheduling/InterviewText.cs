using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// The words and ids interview scheduling writes: the time in an email, the title and body of a calendar event, and the
/// stable ids that make a redelivered message a no-op (docs/architecture/interview-scheduling.md). Pure, so the worker's
/// handlers and the tests share one reading of each rule.
/// </summary>
/// <remarks>
/// A calendar event holds the candidate's name, the job title, and a link to the application. Nothing from verification
/// goes into an event, an email or a message (ADR-0004).
/// </remarks>
public static class InterviewText
{
    /// <summary>
    /// The interview's time for a reader, in <paramref name="timeZoneId"/>: <c>Tuesday 3 November 2026, 10:00 to 10:30
    /// (Europe/London)</c>. English names whatever the host's culture; a zone this host does not know reads as UTC.
    /// </summary>
    public static string Time(DateTimeOffset start, DateTimeOffset end, string timeZoneId)
    {
        var known = InterviewRequest.TryFindZone(timeZoneId, out var zone);
        var label = known ? timeZoneId : "UTC";
        var from = TimeZoneInfo.ConvertTime(start, zone);
        var to = TimeZoneInfo.ConvertTime(end, zone);
        var culture = CultureInfo.GetCultureInfo("en-GB");
        var day = from.ToString("dddd d MMMM yyyy", culture);
        var range = from.Date == to.Date
            ? $"{from.ToString("HH:mm", culture)} to {to.ToString("HH:mm", culture)}"
            : $"{from.ToString("HH:mm", culture)} to {to.ToString("dddd d MMMM HH:mm", culture)}";
        return $"{day}, {range} ({label})";
    }

    /// <summary>The length in words: <c>30 minutes</c>, <c>1 hour</c>, <c>1 hour 30 minutes</c>.</summary>
    public static string Length(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;
        var parts = new List<string>();
        if (hours > 0)
        {
            parts.Add(hours == 1 ? "1 hour" : $"{hours} hours");
        }

        if (rest > 0 || hours == 0)
        {
            parts.Add(rest == 1 ? "1 minute" : $"{rest} minutes");
        }

        return string.Join(' ', parts);
    }

    /// <summary>The title every interviewer's event has.</summary>
    public static string EventTitle(string candidateName, string jobTitle) => $"Interview: {candidateName} - {jobTitle}";

    /// <summary>
    /// The body of an interviewer's event: the job, the candidate, a link to the application in the dashboard when this
    /// host knows its address, and the meeting link on the copies that do not carry it themselves (everyone but the
    /// organizer, whose event makes it).
    /// </summary>
    public static string EventDescription(string candidateName, string jobTitle, Uri? applicationLink, string? meetingUrl)
    {
        var lines = new List<string> { $"Job: {jobTitle}", $"Candidate: {candidateName}" };
        if (applicationLink is not null)
        {
            lines.Add($"Application: {applicationLink}");
        }

        if (!string.IsNullOrEmpty(meetingUrl))
        {
            lines.Add($"Meeting link: {meetingUrl}");
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// The provider's event id for one interviewer's copy of an interview. Derived from the interview and the member only,
    /// so the same copy is the same id however often the message runs. Lowercase hex, which both Google (event ids use
    /// a to v and 0 to 9) and Microsoft's transaction ids accept.
    /// </summary>
    public static string EventIdempotencyId(Guid interviewId, Guid userId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"interview-event:{interviewId:N}:{userId:N}")))[..32];

    /// <summary>
    /// The id of an email the worker sends about a booking: the same for the same kind, interview and revision, so a
    /// redelivered message finds the timeline entry the first send left and sends nothing twice.
    /// </summary>
    public static Guid SendId(string kind, Guid interviewId, int revision)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"interview-email:{kind}:{interviewId:N}:{revision}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// Whether a calendar message still describes the request. It does when the request has not changed since the
    /// message was queued (same <paramref name="revision"/>) and is in the state the message is about; otherwise a later
    /// change superseded it and the message does nothing.
    /// </summary>
    public static bool IsCurrent(InterviewRequest interview, int revision, InterviewStatus expected)
    {
        ArgumentNullException.ThrowIfNull(interview);
        return interview.Revision == revision && interview.Status == expected;
    }
}
