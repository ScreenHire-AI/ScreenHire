using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// What ScreenHire asks of a calendar provider (docs/architecture/interview-scheduling.md): the OAuth round trip, when a
/// member is busy, and one event on a member's own calendar. <see cref="SandboxCalendarProvider"/> is the local
/// stand-in; the Google and Microsoft providers call their free APIs. A call a provider refuses throws
/// <see cref="CalendarApiException"/>.
/// </summary>
public interface ICalendarProvider
{
    CalendarProvider Id { get; }

    /// <summary>Where to send the browser for the member to allow ScreenHire to use their calendar.</summary>
    Uri AuthorizeUrl(string state, Uri redirectUri);

    /// <summary>Exchanges the consent's code for tokens, and the account address when the provider says it.</summary>
    Task<CalendarGrant> ExchangeCodeAsync(string code, Uri redirectUri, CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a refresh token for a new access token. The grant's refresh token is null when the provider did not
    /// rotate it (Google), and then the old one stays.
    /// </summary>
    Task<CalendarGrant> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Gives the token back, so it stops working.</summary>
    Task RevokeAsync(string token, CancellationToken cancellationToken);

    /// <summary>When the member is busy between <paramref name="windowStart"/> and <paramref name="windowEnd"/>, on their calendar.</summary>
    Task<IReadOnlyList<BusyInterval>> FreeBusyAsync(string accessToken, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken);

    /// <summary>
    /// Puts an event on the member's calendar. Creating the same <see cref="EventSpec.IdempotencyId"/> twice gives the
    /// first event back instead of a second one.
    /// </summary>
    Task<CalendarEventResult> CreateEventAsync(string accessToken, EventSpec spec, CancellationToken cancellationToken);

    /// <summary>Moves an event to a new time. The meeting link stays.</summary>
    Task<CalendarEventResult> MoveEventAsync(string accessToken, string eventId, DateTimeOffset newStart, DateTimeOffset newEnd, CancellationToken cancellationToken);

    /// <summary>Removes an event. An event that is already gone is not an error.</summary>
    Task CancelEventAsync(string accessToken, string eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an event without telling its guests, so nothing of it is left on the calendar. For an erased candidate, whose
    /// name and address the event holds. An event that is already gone is not an error. Google is told not to send updates;
    /// Microsoft Graph has no such switch, and the organizer deleting a meeting there tells its attendees.
    /// </summary>
    Task RemoveEventAsync(string accessToken, string eventId, CancellationToken cancellationToken);

    /// <summary>
    /// The provider's id for the event made with <paramref name="idempotencyId"/> in the window, or null when there is none.
    /// For an event a create made before its id was saved (the transaction rolled back after the provider answered), which
    /// would otherwise stay on the calendar after a cancellation or an erasure.
    /// </summary>
    Task<string?> FindEventIdAsync(string accessToken, string idempotencyId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken);
}

/// <summary>What a code or refresh token exchange gave back. Times are UTC instants.</summary>
public sealed record CalendarGrant(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, string? AccountEmail);

/// <summary>
/// An event to create. <see cref="IdempotencyId"/> is derived from stable ids (interview, member and revision), never the
/// clock or a new <see cref="Guid"/>, so a redelivered message creates the event once. <see cref="InviteEmails"/> are
/// the people the provider emails an invitation to; <see cref="CreateMeetingLink"/> asks for a Meet or Teams link.
/// </summary>
public sealed record EventSpec(
    string IdempotencyId,
    string Title,
    string Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    IReadOnlyList<string> InviteEmails,
    bool CreateMeetingLink);

/// <summary>The event's id on the member's calendar and its meeting link, if it has one.</summary>
public sealed record CalendarEventResult(string EventId, string? MeetingUrl);

/// <summary>
/// A provider refused a call, or did not answer properly. <see cref="Permanent"/> means a retry cannot fix it: the
/// tokens no longer work (<c>invalid_grant</c>, <c>invalid_auth</c>), so the member must connect again.
/// </summary>
public sealed class CalendarApiException : Exception
{
    public CalendarApiException()
        : this("unknown", permanent: false)
    {
    }

    public CalendarApiException(string code)
        : this(code, permanent: false)
    {
    }

    public CalendarApiException(string code, bool permanent)
        : base($"The calendar provider answered {code}.")
    {
        Code = code;
        Permanent = permanent;
    }

    public CalendarApiException(string code, bool permanent, Exception innerException)
        : base($"The calendar provider answered {code}.", innerException)
    {
        Code = code;
        Permanent = permanent;
    }

    public string Code { get; } = "unknown";

    public bool Permanent { get; }

    /// <summary>How long the provider asked callers to wait (<c>Retry-After</c>), when it said so. Only a transient refusal has one.</summary>
    public TimeSpan? RetryAfter { get; init; }
}
