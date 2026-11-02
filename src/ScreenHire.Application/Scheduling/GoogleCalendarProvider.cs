using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// Google Calendar through its REST API (https://developers.google.com/workspace/calendar/api/v3/reference) and Google's
/// OAuth 2.0 web server flow. Two scopes only, both sensitive: <c>calendar.freebusy</c> to read when a member is busy and
/// <c>calendar.events</c> to write the interview's event. No <c>openid</c>, <c>email</c> or <c>profile</c>; the address
/// the sheet shows comes from the primary calendar's title, and is optional. Only the member's primary calendar is used.
/// Nothing here logs a token, an address or an event's text.
/// </summary>
/// <remarks>
/// The event id is chosen by ScreenHire, from the idempotency id, so creating the same event twice cannot make two: Google
/// answers 409 for an id it has, and the existing event is returned. A deleted event keeps its id on Google's side, so a
/// 409 for a cancelled event is answered by bringing that event back.
/// </remarks>
public sealed partial class GoogleCalendarProvider : ICalendarProvider, IDisposable
{
    /// <summary>The scopes asked for, and the only ones (Decision 12 of the interview scheduling plan).</summary>
    public static readonly IReadOnlyList<string> Scopes =
    [
        "https://www.googleapis.com/auth/calendar.freebusy",
        "https://www.googleapis.com/auth/calendar.events",
    ];

    private readonly CalendarOptions options;
    private readonly TimeProvider time;
    private readonly CalendarHttp http;

    public GoogleCalendarProvider(IOptions<CalendarOptions> options, TimeProvider time)
        : this(options, time, CalendarHttp.NetworkHandler())
    {
    }

    /// <summary>With <paramref name="handler"/> in place of the network, for tests.</summary>
    public GoogleCalendarProvider(IOptions<CalendarOptions> options, TimeProvider time, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        this.options = options.Value;
        this.time = time;
        http = new CalendarHttp(handler, this.options.Timeout);
    }

    public CalendarProvider Id => CalendarProvider.Google;

    public Uri AuthorizeUrl(string state, Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);

        // access_type=offline with prompt=consent is what makes Google issue a refresh token on every connect.
        var query = CalendarJson.Query(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', Scopes),
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
        });
        return new Uri($"{options.Endpoints.GoogleAuthorize.AbsoluteUri}?{query}");
    }

    public async Task<CalendarGrant> ExchangeCodeAsync(string code, Uri redirectUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        CalendarGrant grant;
        using (var reply = await SendTokenAsync(new()
        {
            ["code"] = code,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["grant_type"] = "authorization_code",
        }, cancellationToken))
        {
            grant = CalendarOAuth.ReadGrant(reply, time.GetUtcNow(), needsRefreshToken: true, Scopes);
        }

        return grant with { AccountEmail = await AccountEmailAsync(grant.AccessToken, cancellationToken) };
    }

    public async Task<CalendarGrant> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var reply = await SendTokenAsync(new()
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["grant_type"] = "refresh_token",
        }, cancellationToken);

        // Google keeps the refresh token the same, so an answer without one means "keep the old one".
        return CalendarOAuth.ReadGrant(reply, time.GetUtcNow(), needsRefreshToken: false, []);
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoints.GoogleRevoke)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }),
        };
        using var reply = await http.SendAsync(request, cancellationToken);

        // invalid_token: Google already dropped it, which is what the caller wanted.
        if (!reply.IsSuccess && !(reply.Status == HttpStatusCode.BadRequest && CalendarJson.ErrorCode(reply.Root) == "invalid_token"))
        {
            throw CalendarFailures.Failure(reply);
        }
    }

    public async Task<IReadOnlyList<BusyInterval>> FreeBusyAsync(
        string accessToken, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["timeMin"] = CalendarJson.Utc(windowStart),
            ["timeMax"] = CalendarJson.Utc(windowEnd),
            ["timeZone"] = "UTC",
            ["items"] = new JsonArray(new JsonObject { ["id"] = "primary" }),
        };
        using var reply = await SendAsync(HttpMethod.Post, "freeBusy", accessToken, body, cancellationToken);
        if (!reply.IsSuccess)
        {
            throw CalendarFailures.Failure(reply);
        }

        var primary = CalendarJson.Child(CalendarJson.Child(reply.RequireRoot(), "calendars"), "primary");
        if (primary.ValueKind != JsonValueKind.Object)
        {
            throw new CalendarApiException("unreadable_answer");
        }

        // A calendar Google could not compute is not a free one: offering times from it could double-book the member.
        if (CalendarJson.Items(CalendarJson.Child(primary, "errors")).FirstOrDefault() is { ValueKind: JsonValueKind.Object } error)
        {
            var reason = CalendarJson.SafeCode(CalendarJson.Text(error, "reason")) ?? "freebusy_error";
            throw new CalendarApiException(reason, permanent: reason is not ("internalError" or "backendError" or "rateLimitExceeded"));
        }

        return
        [
            .. CalendarJson.Items(CalendarJson.Child(primary, "busy"))
                .Select(b => new BusyInterval(CalendarJson.Instant(CalendarJson.Text(b, "start")), CalendarJson.Instant(CalendarJson.Text(b, "end"))))
                .OrderBy(b => b.Start)
                .ThenBy(b => b.End),
        ];
    }

    public async Task<CalendarEventResult> CreateEventAsync(string accessToken, EventSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var eventId = EventId(spec.IdempotencyId);

        // The meeting and the invitation are opt-in on Google: conferenceDataVersion=1 for Meet, sendUpdates=all to email
        // the guests. Without guests there is nobody to email.
        var query = EventQuery(spec.CreateMeetingLink, notify: spec.InviteEmails.Count > 0);
        var body = new JsonObject
        {
            ["id"] = eventId,
            ["summary"] = spec.Title,
            ["description"] = spec.Description,
            ["start"] = Moment(spec.Start),
            ["end"] = Moment(spec.End),
        };
        if (spec.InviteEmails.Count > 0)
        {
            body["attendees"] = new JsonArray([.. spec.InviteEmails.Select(email => (JsonNode)new JsonObject { ["email"] = email })]);
        }

        if (spec.CreateMeetingLink)
        {
            body["conferenceData"] = new JsonObject
            {
                ["createRequest"] = new JsonObject
                {
                    ["requestId"] = spec.IdempotencyId,
                    ["conferenceSolutionKey"] = new JsonObject { ["type"] = "hangoutsMeet" },
                },
            };
        }

        using (var created = await SendAsync(HttpMethod.Post, $"calendars/primary/events{query}", accessToken, body, cancellationToken))
        {
            if (created.IsSuccess)
            {
                return await ResultAsync(accessToken, created.RequireRoot(), spec.CreateMeetingLink, cancellationToken);
            }

            if (created.Status != HttpStatusCode.Conflict)
            {
                throw CalendarFailures.Failure(created);
            }
        }

        // 409: the id exists, so an earlier attempt made this event (or it was made and then deleted).
        using var existing = await SendAsync(HttpMethod.Get, EventPath(eventId), accessToken, null, cancellationToken);
        if (!existing.IsSuccess)
        {
            throw CalendarFailures.Failure(existing);
        }

        if (CalendarJson.Text(existing.Root, "status") != "cancelled")
        {
            return await ResultAsync(accessToken, existing.RequireRoot(), spec.CreateMeetingLink, cancellationToken);
        }

        // An event deleted by hand stays on Google as "cancelled" and keeps its id. Patch it back to life.
        body["status"] = "confirmed";
        using var revived = await SendAsync(HttpMethod.Patch, $"{EventPath(eventId)}{query}", accessToken, body, cancellationToken);
        return revived.IsSuccess
            ? await ResultAsync(accessToken, revived.RequireRoot(), spec.CreateMeetingLink, cancellationToken)
            : throw CalendarFailures.Failure(revived);
    }

    public async Task<CalendarEventResult> MoveEventAsync(
        string accessToken, string eventId, DateTimeOffset newStart, DateTimeOffset newEnd, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["start"] = Moment(newStart), ["end"] = Moment(newEnd) };

        // sendUpdates=all always: it emails the guests when there are any and does nothing when there are none.
        using var reply = await SendAsync(HttpMethod.Patch, $"{EventPath(eventId)}?sendUpdates=all", accessToken, body, cancellationToken);
        if (reply.Status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw new CalendarApiException("event_not_found");
        }

        if (!reply.IsSuccess)
        {
            throw CalendarFailures.Failure(reply);
        }

        // Google still answers for an event someone deleted, as "cancelled". It is gone for our purposes.
        if (CalendarJson.Text(reply.Root, "status") == "cancelled")
        {
            throw new CalendarApiException("event_not_found");
        }

        return new CalendarEventResult(CalendarJson.Required(reply.Root, "id"), MeetingLink(reply.Root));
    }

    public async Task CancelEventAsync(string accessToken, string eventId, CancellationToken cancellationToken)
    {
        using var reply = await SendAsync(HttpMethod.Delete, $"{EventPath(eventId)}?sendUpdates=all", accessToken, null, cancellationToken);

        // 404 and 410 (already deleted): the event is gone, which is what was asked for.
        if (!reply.IsSuccess && reply.Status is not (HttpStatusCode.NotFound or HttpStatusCode.Gone))
        {
            throw CalendarFailures.Failure(reply);
        }
    }

    public void Dispose() => http.Dispose();

    /// <summary>
    /// The event id Google gets for an idempotency id. An id that is already valid (5 to 1024 characters of a to v and 0 to 9,
    /// which the lowercase hex ids of <c>InterviewText.EventIdempotencyId</c> are) is used as it is; any other becomes the
    /// SHA-256 of it in lowercase hex, which is valid too. Stable for the same input, different for different inputs.
    /// </summary>
    public static string EventId(string idempotencyId)
    {
        ArgumentException.ThrowIfNullOrEmpty(idempotencyId);
        return ValidEventId().IsMatch(idempotencyId)
            ? idempotencyId
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyId)));
    }

    [GeneratedRegex("^[a-v0-9]{5,1024}$")]
    private static partial Regex ValidEventId();

    private string ClientId => options.Google.ClientId ?? throw new InvalidOperationException("No Google client id is configured.");

    private string ClientSecret => options.Google.ClientSecret ?? throw new InvalidOperationException("No Google client secret is configured.");

    private static string EventPath(string eventId) => $"calendars/primary/events/{Uri.EscapeDataString(eventId)}";

    private static string EventQuery(bool meeting, bool notify) => (meeting, notify) switch
    {
        (true, true) => "?conferenceDataVersion=1&sendUpdates=all",
        (true, false) => "?conferenceDataVersion=1",
        (false, true) => "?sendUpdates=all",
        _ => "",
    };

    private static JsonObject Moment(DateTimeOffset instant) => new() { ["dateTime"] = CalendarJson.Utc(instant), ["timeZone"] = "UTC" };

    private static string? MeetingLink(JsonElement eventResource)
    {
        if (CalendarJson.Text(eventResource, "hangoutLink") is { Length: > 0 } link)
        {
            return link;
        }

        return CalendarJson.Items(CalendarJson.Child(CalendarJson.Child(eventResource, "conferenceData"), "entryPoints"))
            .Where(e => CalendarJson.Text(e, "entryPointType") == "video")
            .Select(e => CalendarJson.Text(e, "uri"))
            .FirstOrDefault(uri => !string.IsNullOrEmpty(uri));
    }

    // Meet is made after the insert returns and the link can lag by a moment, so one look-again before giving up on it.
    private async Task<CalendarEventResult> ResultAsync(string accessToken, JsonElement eventResource, bool wantsLink, CancellationToken cancellationToken)
    {
        var eventId = CalendarJson.Required(eventResource, "id");
        var link = MeetingLink(eventResource);
        if (link is null && wantsLink)
        {
            using var again = await SendAsync(HttpMethod.Get, EventPath(eventId), accessToken, null, cancellationToken);
            if (again.IsSuccess)
            {
                link = MeetingLink(again.Root);
            }
        }

        return new CalendarEventResult(eventId, link);
    }

    // The primary calendar's title is the member's address on Google. Anything else (a renamed calendar) is not shown.
    private async Task<string?> AccountEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var reply = await SendAsync(HttpMethod.Get, "calendars/primary/events?maxResults=1&fields=summary", accessToken, null, cancellationToken);
            return reply.IsSuccess ? CalendarJson.AsEmail(CalendarJson.Text(reply.Root, "summary")) : null;
        }
        catch (CalendarApiException)
        {
            // Only a label for the sheet. A calendar that cannot be read fails the first real call, not the connect.
            return null;
        }
    }

    private Task<CalendarReply> SendTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken) =>
        SendFormAsync(options.Endpoints.GoogleToken, form, cancellationToken);

    private async Task<CalendarReply> SendFormAsync(Uri endpoint, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(form) };
        return await http.SendAsync(request, cancellationToken);
    }

    private async Task<CalendarReply> SendAsync(HttpMethod method, string relative, string accessToken, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(options.Endpoints.GoogleApi, relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = CalendarJson.Json(body);
        }

        return await http.SendAsync(request, cancellationToken);
    }
}
