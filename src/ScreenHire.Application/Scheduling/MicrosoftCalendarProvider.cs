using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// Outlook and Microsoft 365 calendars through Microsoft Graph (https://learn.microsoft.com/graph/api/resources/calendar)
/// and the Entra v2 endpoint. One delegated permission, <c>Calendars.ReadWrite</c>, plus <c>offline_access</c>, which is
/// the refresh-token plumbing and reads no data. No <c>User.Read</c> and no <c>openid</c>; the address the sheet shows is
/// the calendar owner's, and is optional. Only the member's default calendar is used. Nothing here logs a token, an
/// address or an event's text.
/// </summary>
/// <remarks>
/// <para>
/// When the member is busy comes from <c>calendarView</c>, not <c>getSchedule</c>. <c>getSchedule</c> asks for the schedules
/// of addresses, so it needs the member's own address on every call, and the call site holds only the access token.
/// <c>calendarView</c> on <c>/me</c> needs no address, and it also tells <c>showAs</c> and <c>isCancelled</c> per event.
/// </para>
/// <para>
/// Entra rotates the refresh token on every refresh, so a refresh answer always carries the new one and the caller must
/// store it (<c>CalendarAccess</c> does, under a per-connection lock).
/// </para>
/// </remarks>
public sealed class MicrosoftCalendarProvider : ICalendarProvider, IDisposable
{
    /// <summary>The scopes asked for, and the only ones (ADR-0024, decision 2).</summary>
    public static readonly IReadOnlyList<string> Scopes = ["https://graph.microsoft.com/Calendars.ReadWrite", "offline_access"];

    // Graph returns at most this many events per page of calendarView, and nextLink continues; the cap keeps a calendar
    // with thousands of events in one window from becoming an unbounded loop.
    private const int PageSize = 100;

    private const int MaxPages = 20;

    private readonly CalendarOptions options;
    private readonly TimeProvider time;
    private readonly CalendarHttp http;

    public MicrosoftCalendarProvider(IOptions<CalendarOptions> options, TimeProvider time)
        : this(options, time, CalendarHttp.NetworkHandler())
    {
    }

    /// <summary>With <paramref name="handler"/> in place of the network, for tests.</summary>
    public MicrosoftCalendarProvider(IOptions<CalendarOptions> options, TimeProvider time, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        this.options = options.Value;
        this.time = time;
        http = new CalendarHttp(handler, this.options.Timeout);
    }

    public CalendarProvider Id => CalendarProvider.Microsoft;

    public Uri AuthorizeUrl(string state, Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        var query = CalendarJson.Query(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["response_mode"] = "query",
            ["scope"] = string.Join(' ', Scopes),
            ["state"] = state,
        });
        return new Uri($"{new Uri(options.Endpoints.MicrosoftAuthority, "authorize").AbsoluteUri}?{query}");
    }

    public async Task<CalendarGrant> ExchangeCodeAsync(string code, Uri redirectUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        CalendarGrant grant;
        using (var reply = await SendTokenAsync(new()
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["grant_type"] = "authorization_code",
            ["scope"] = string.Join(' ', Scopes),
        }, cancellationToken))
        {
            // The member can allow the sign-in and still refuse the calendar permission: the scope list then lacks it.
            grant = CalendarOAuth.ReadGrant(reply, time.GetUtcNow(), needsRefreshToken: true, [Scopes[0]]);
        }

        return grant with { AccountEmail = await AccountEmailAsync(grant.AccessToken, cancellationToken) };
    }

    public async Task<CalendarGrant> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var reply = await SendTokenAsync(new()
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
            ["scope"] = string.Join(' ', Scopes),
        }, cancellationToken);

        // Entra rotates the refresh token: the answer's is the one to keep, and the old one is spent. An answer without one
        // leaves the grant's refresh token null, and the caller keeps the old one.
        return CalendarOAuth.ReadGrant(reply, time.GetUtcNow(), needsRefreshToken: false, []);
    }

    // Graph has no per-token revoke for delegated tokens: signing the user out everywhere is a different, wider
    // action than disconnecting one app. Disconnecting deletes our copy of the tokens, and the member can remove the
    // app's access at https://myapps.microsoft.com. So this is a documented no-op.
    public Task RevokeAsync(string token, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<IReadOnlyList<BusyInterval>> FreeBusyAsync(
        string accessToken, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken)
    {
        // free is free. busy, tentative, oof and workingElsewhere block the time, and so does anything Graph labels
        // "unknown": a time we cannot read is not offered. A cancelled event blocks nothing.
        var busy = await ViewAsync(
            accessToken,
            windowStart,
            windowEnd,
            "start,end,showAs,isCancelled",
            e => CalendarJson.Text(e, "showAs") != "free" && !Flag(e, "isCancelled")
                ? new BusyInterval(
                    CalendarJson.Instant(CalendarJson.Text(CalendarJson.Child(e, "start"), "dateTime")),
                    CalendarJson.Instant(CalendarJson.Text(CalendarJson.Child(e, "end"), "dateTime")))
                : null,
            cancellationToken);
        return [.. busy.OrderBy(b => b.Start).ThenBy(b => b.End)];
    }

    public async Task<CalendarEventResult> CreateEventAsync(string accessToken, EventSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var body = new JsonObject
        {
            ["subject"] = spec.Title,
            ["body"] = new JsonObject { ["contentType"] = "text", ["content"] = spec.Description },
            ["start"] = Moment(spec.Start),
            ["end"] = Moment(spec.End),
            ["transactionId"] = spec.IdempotencyId,
        };
        if (spec.InviteEmails.Count > 0)
        {
            // Graph emails the invitation itself when an event with attendees is created.
            body["attendees"] = new JsonArray([.. spec.InviteEmails.Select(email => (JsonNode)new JsonObject
            {
                ["emailAddress"] = new JsonObject { ["address"] = email },
                ["type"] = "required",
            })]);
        }

        if (spec.CreateMeetingLink)
        {
            body["isOnlineMeeting"] = true;
            body["onlineMeetingProvider"] = "teamsForBusiness";
        }

        using var reply = await SendAsync(HttpMethod.Post, "me/events", accessToken, body, cancellationToken);
        if (reply.IsSuccess)
        {
            return Result(reply.RequireRoot());
        }

        // The transactionId makes Graph recognise a repeat of this create. How it answers a repeat is not spelled out in
        // its documentation, so both likely answers are handled: the existing event comes back as a success above, or a
        // refusal (409, or an error naming the transaction) is answered by finding the event with that transactionId.
        if (reply.Status == HttpStatusCode.Conflict || NamesTheTransaction(reply.Root))
        {
            var found = await FindByTransactionAsync(accessToken, spec, cancellationToken);
            return found ?? throw new CalendarApiException("duplicate_not_found", permanent: true);
        }

        throw CalendarFailures.Failure(reply);
    }

    public async Task<CalendarEventResult> MoveEventAsync(
        string accessToken, string eventId, DateTimeOffset newStart, DateTimeOffset newEnd, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["start"] = Moment(newStart), ["end"] = Moment(newEnd) };

        // Graph sends the attendees an updated invitation by itself.
        using var reply = await SendAsync(HttpMethod.Patch, EventPath(eventId), accessToken, body, cancellationToken);
        if (reply.Status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw new CalendarApiException("event_not_found");
        }

        if (!reply.IsSuccess)
        {
            throw CalendarFailures.Failure(reply);
        }

        var root = reply.RequireRoot();
        if (Flag(root, "isCancelled"))
        {
            throw new CalendarApiException("event_not_found");
        }

        return Result(root);
    }

    public async Task CancelEventAsync(string accessToken, string eventId, CancellationToken cancellationToken)
    {
        // The organizer's cancel action tells the attendees; it is the polite route for an event with guests.
        using (var cancel = await SendAsync(HttpMethod.Post, $"{EventPath(eventId)}/cancel", accessToken, new JsonObject(), cancellationToken))
        {
            if (cancel.IsSuccess || cancel.Status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                return;
            }

            // An event with no attendees, or one this member does not organize, is not "cancelled" but deleted. Anything
            // else is a real failure, and deleting would hide it.
            if (cancel.Status is not (HttpStatusCode.BadRequest or HttpStatusCode.MethodNotAllowed))
            {
                throw CalendarFailures.Failure(cancel);
            }
        }

        using var delete = await SendAsync(HttpMethod.Delete, EventPath(eventId), accessToken, null, cancellationToken);
        if (!delete.IsSuccess && delete.Status is not (HttpStatusCode.NotFound or HttpStatusCode.Gone))
        {
            throw CalendarFailures.Failure(delete);
        }
    }

    // Graph has no switch to delete a meeting quietly: deleting it as the organizer tells its attendees.
    public async Task RemoveEventAsync(string accessToken, string eventId, CancellationToken cancellationToken)
    {
        using var delete = await SendAsync(HttpMethod.Delete, EventPath(eventId), accessToken, null, cancellationToken);
        if (!delete.IsSuccess && delete.Status is not (HttpStatusCode.NotFound or HttpStatusCode.Gone))
        {
            throw CalendarFailures.Failure(delete);
        }
    }

    public async Task<string?> FindEventIdAsync(string accessToken, string idempotencyId, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken)
    {
        var spec = new EventSpec(idempotencyId, "", "", windowStart, windowEnd, [], CreateMeetingLink: false);
        return (await FindByTransactionAsync(accessToken, spec, cancellationToken))?.EventId;
    }

    public void Dispose() => http.Dispose();

    private string ClientId => options.Microsoft.ClientId ?? throw new InvalidOperationException("No Microsoft client id is configured.");

    private string ClientSecret => options.Microsoft.ClientSecret ?? throw new InvalidOperationException("No Microsoft client secret is configured.");

    private static string EventPath(string eventId) => $"me/events/{Uri.EscapeDataString(eventId)}";

    private static bool Flag(JsonElement element, string name) =>
        CalendarJson.Child(element, name).ValueKind == JsonValueKind.True;

    // Graph takes a local time and a zone name; ours is UTC.
    private static JsonObject Moment(DateTimeOffset instant) => new() { ["dateTime"] = CalendarJson.Utc(instant, zone: false), ["timeZone"] = "UTC" };

    private static CalendarEventResult Result(JsonElement eventResource) =>
        new(
            CalendarJson.Required(eventResource, "id"),
            CalendarJson.Text(CalendarJson.Child(eventResource, "onlineMeeting"), "joinUrl") is { Length: > 0 } joinUrl ? joinUrl : null);

    private static bool NamesTheTransaction(JsonElement root) =>
        CalendarJson.ErrorCode(root) is { } code && code.Contains("Transaction", StringComparison.OrdinalIgnoreCase);

    // The owner of the default calendar is the member. This needs only Calendars.ReadWrite.
    private async Task<string?> AccountEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var reply = await SendAsync(HttpMethod.Get, "me/calendar?$select=owner", accessToken, null, cancellationToken);
            return reply.IsSuccess ? CalendarJson.AsEmail(CalendarJson.Text(CalendarJson.Child(reply.Root, "owner"), "address")) : null;
        }
        catch (CalendarApiException)
        {
            // Only a label for the sheet. A calendar that cannot be read fails the first real call, not the connect.
            return null;
        }
    }

    // Looks at the window the event is in for the one carrying this create's transactionId.
    private async Task<CalendarEventResult?> FindByTransactionAsync(string accessToken, EventSpec spec, CancellationToken cancellationToken)
    {
        var matches = await ViewAsync(
            accessToken,
            spec.Start,
            spec.End,
            "id,transactionId,isCancelled,onlineMeeting",
            e => CalendarJson.Text(e, "transactionId") == spec.IdempotencyId && !Flag(e, "isCancelled") ? Result(e) : null,
            cancellationToken);
        return matches.FirstOrDefault();
    }

    /// <summary>
    /// Reads <c>/me/calendarView</c> for the window, page by page, in UTC. <paramref name="pick"/> turns one event into a
    /// value or null to skip it. The next page's link is followed only when it is on the Graph host, so a token is never sent
    /// anywhere else.
    /// </summary>
    private async Task<List<T>> ViewAsync<T>(
        string accessToken, DateTimeOffset start, DateTimeOffset end, string select, Func<JsonElement, T?> pick, CancellationToken cancellationToken)
        where T : class
    {
        var graph = options.Endpoints.MicrosoftGraph;
        var query = CalendarJson.Query(new Dictionary<string, string> { ["startDateTime"] = CalendarJson.Utc(start), ["endDateTime"] = CalendarJson.Utc(end) });
        var next = new Uri(graph, $"me/calendarView?{query}&$select={select}&$top={PageSize}");
        var picked = new List<T>();
        for (var page = 0; next is not null && page < MaxPages; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // Times come back in UTC rather than the mailbox's zone.
            request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");
            using var reply = await http.SendAsync(request, cancellationToken);
            if (!reply.IsSuccess)
            {
                throw CalendarFailures.Failure(reply);
            }

            var root = reply.RequireRoot();
            picked.AddRange(CalendarJson.Items(CalendarJson.Child(root, "value")).Select(pick).OfType<T>());
            next = null;
            if (CalendarJson.Text(root, "@odata.nextLink") is { Length: > 0 } link)
            {
                next = Uri.TryCreate(link, UriKind.Absolute, out var parsed) && parsed.Scheme == graph.Scheme && parsed.Authority == graph.Authority
                    ? parsed
                    : throw new CalendarApiException("unreadable_answer");
            }
        }

        // A window with more events than the cap is not a free one: the times we did not read could be busy.
        return next is null ? picked : throw new CalendarApiException("too_many_events", permanent: false);
    }

    private async Task<CalendarReply> SendTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.Endpoints.MicrosoftAuthority, "token")) { Content = new FormUrlEncodedContent(form) };
        return await http.SendAsync(request, cancellationToken);
    }

    private async Task<CalendarReply> SendAsync(HttpMethod method, string relative, string accessToken, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(options.Endpoints.MicrosoftGraph, relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = CalendarJson.Json(body);
        }

        return await http.SendAsync(request, cancellationToken);
    }
}
