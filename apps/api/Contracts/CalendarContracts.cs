using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The signed-in member's calendar (docs/architecture/interview-scheduling.md). <c>providers</c> are the calendars they
/// can connect here, empty when this ScreenHire has none configured. <c>connection</c> is their own, null until they
/// connect one. No token is ever in an answer.
/// </summary>
/// <remarks><c>sandbox</c>: true when the local sandbox calendar is one of the providers.</remarks>
public sealed record CalendarStatus(bool Sandbox, IReadOnlyList<CalendarProviderOption> Providers, CalendarAccount? Connection);

/// <summary>A calendar the member can connect: its id, and the name the sheet shows.</summary>
public sealed record CalendarProviderOption(CalendarProvider Id, string Name);

/// <summary>
/// The member's connected calendar. <c>accountEmail</c> is the calendar owner's address when the provider says it.
/// <c>problem</c> is <c>NeedsReconnect</c> once the provider stopped accepting the connection.
/// </summary>
public sealed record CalendarAccount(
    CalendarProvider Provider,
    string ProviderName,
    string? AccountEmail,
    DateTimeOffset ConnectedAt,
    CalendarProblem? Problem);

/// <summary>Connects the member's calendar. <c>returnUrl</c>: the dashboard path to come back to, a local path only.</summary>
public sealed record StartCalendarConnectRequest(CalendarProvider Provider, string? ReturnUrl);

/// <summary>Where to send the browser next: the provider's consent page (or the sandbox's).</summary>
public sealed record CalendarAuthorization(string AuthorizeUrl);

/// <summary>A block of time the member is busy in the sandbox calendar, as UTC instants. Development and tests only.</summary>
public sealed record AddSandboxBusyRequest(DateTimeOffset Start, DateTimeOffset End);
