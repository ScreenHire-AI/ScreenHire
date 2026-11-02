using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Application.Scheduling;

// Phase 5 of the interview scheduling work implements these two against the free Google Calendar and Microsoft Graph
// APIs. Until then they exist so CalendarAccess has its constructor parameters (Wolverine forbids service location) and
// the registration is final. CalendarAccess never selects one unless its client id and secret are both set, so these
// methods are not reachable in a host that has not configured the provider.

/// <summary>Google Calendar through its REST API. Not implemented yet.</summary>
public sealed class GoogleCalendarProvider : ICalendarProvider
{
    public CalendarProvider Id => CalendarProvider.Google;

    public Uri AuthorizeUrl(string state, Uri redirectUri) => throw NotYet();

    public Task<CalendarGrant> ExchangeCodeAsync(string code, Uri redirectUri, CancellationToken cancellationToken) => throw NotYet();

    public Task<CalendarGrant> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw NotYet();

    public Task RevokeAsync(string token, CancellationToken cancellationToken) => throw NotYet();

    public Task<IReadOnlyList<BusyInterval>> FreeBusyAsync(string accessToken, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken) =>
        throw NotYet();

    public Task<CalendarEventResult> CreateEventAsync(string accessToken, EventSpec spec, CancellationToken cancellationToken) => throw NotYet();

    public Task<CalendarEventResult> MoveEventAsync(string accessToken, string eventId, DateTimeOffset newStart, DateTimeOffset newEnd, CancellationToken cancellationToken) =>
        throw NotYet();

    public Task CancelEventAsync(string accessToken, string eventId, CancellationToken cancellationToken) => throw NotYet();

    private static NotImplementedException NotYet() => new("The Google Calendar provider is implemented in phase 5 of the interview scheduling work.");
}

/// <summary>Outlook calendars through Microsoft Graph. Not implemented yet.</summary>
public sealed class MicrosoftCalendarProvider : ICalendarProvider
{
    public CalendarProvider Id => CalendarProvider.Microsoft;

    public Uri AuthorizeUrl(string state, Uri redirectUri) => throw NotYet();

    public Task<CalendarGrant> ExchangeCodeAsync(string code, Uri redirectUri, CancellationToken cancellationToken) => throw NotYet();

    public Task<CalendarGrant> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw NotYet();

    public Task RevokeAsync(string token, CancellationToken cancellationToken) => throw NotYet();

    public Task<IReadOnlyList<BusyInterval>> FreeBusyAsync(string accessToken, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken) =>
        throw NotYet();

    public Task<CalendarEventResult> CreateEventAsync(string accessToken, EventSpec spec, CancellationToken cancellationToken) => throw NotYet();

    public Task<CalendarEventResult> MoveEventAsync(string accessToken, string eventId, DateTimeOffset newStart, DateTimeOffset newEnd, CancellationToken cancellationToken) =>
        throw NotYet();

    public Task CancelEventAsync(string accessToken, string eventId, CancellationToken cancellationToken) => throw NotYet();

    private static NotImplementedException NotYet() => new("The Microsoft calendar provider is implemented in phase 5 of the interview scheduling work.");
}
