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
    /// The interview's time for a reader, in <paramref name="timeZoneId"/>: <c>Tuesday, November 3, 2026, 10:00 AM to 10:30 AM
    /// (Europe/London)</c>. US English whatever the host's culture, as the careers page writes it (design system section 13);
    /// a zone this host does not know reads as UTC.
    /// </summary>
    public static string Time(DateTimeOffset start, DateTimeOffset end, string timeZoneId)
    {
        var known = InterviewRequest.TryFindZone(timeZoneId, out var zone);
        var label = known ? timeZoneId : "UTC";
        var from = TimeZoneInfo.ConvertTime(start, zone);
        var to = TimeZoneInfo.ConvertTime(end, zone);
        var culture = CultureInfo.GetCultureInfo("en-US");
        var day = from.ToString("dddd, MMMM d, yyyy", culture);
        var range = from.Date == to.Date
            ? $"{from.ToString("h:mm tt", culture)} to {to.ToString("h:mm tt", culture)}"
            : $"{from.ToString("h:mm tt", culture)} to {to.ToString("dddd, MMMM d, h:mm tt", culture)}";
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

    /// <summary>The text without angle brackets and control characters, so it cannot be read as markup or start a new line.</summary>
    public static string Plain(string text) => new([.. text.Where(c => c is not ('<' or '>') && !char.IsControl(c))]);

    /// <summary>The title every interviewer's event has.</summary>
    public static string EventTitle(string candidateName, string jobTitle) => $"Interview: {Plain(candidateName)} - {Plain(jobTitle)}";

    /// <summary>
    /// The body of an interviewer's event: the job, the candidate, a link to the application in the dashboard when this
    /// host knows its address, and the meeting link on the copies that do not carry it themselves (everyone but the
    /// organizer, whose event makes it). A candidate chooses their own name, and Google renders HTML in an event's
    /// description, so the name and the title go in as <see cref="Plain"/> text: a link in a name must not become a link in
    /// an interviewer's calendar.
    /// </summary>
    public static string EventDescription(string candidateName, string jobTitle, Uri? applicationLink, string? meetingUrl)
    {
        var lines = new List<string> { $"Job: {Plain(jobTitle)}", $"Candidate: {Plain(candidateName)}" };
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
    /// The id of a reminder email to the candidate: the same for the same kind, interview and start, so a redelivered
    /// reminder finds the timeline entry the first send left. A move to another time is another reminder.
    /// </summary>
    public static Guid ReminderSendId(InterviewReminderKind kind, Guid interviewId, DateTimeOffset start)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"interview-email:reminder-{kind}:{interviewId:N}:{start.UtcTicks}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>How long before the start each reminder goes.</summary>
    public static TimeSpan ReminderLead(InterviewReminderKind kind) => kind == InterviewReminderKind.DayBefore ? TimeSpan.FromHours(24) : TimeSpan.FromHours(1);

    /// <summary>
    /// The reminders still to send for an interview starting at <paramref name="start"/>, and when each goes. One whose time is
    /// not after <paramref name="now"/> is left out: it would say "tomorrow" about an interview that is sooner than that, and the
    /// confirmation the candidate just got says the time.
    /// </summary>
    public static IReadOnlyList<(InterviewReminderKind Kind, DateTimeOffset At)> Reminders(DateTimeOffset start, DateTimeOffset now) =>
        [.. Enum.GetValues<InterviewReminderKind>().Select(kind => (Kind: kind, At: start - ReminderLead(kind))).Where(r => r.At > now)];

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
