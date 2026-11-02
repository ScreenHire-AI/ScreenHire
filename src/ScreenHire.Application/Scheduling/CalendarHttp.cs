using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// The one HTTP client a real calendar provider sends its calls through, and the answers it hands back. A long-lived
/// <see cref="HttpClient"/> of its own rather than one from the HTTP client factory, as <c>SlackApiClient</c> does: the
/// service defaults add the standard resilience handler to every factory client, and its retries would repeat a call.
/// Wolverine's retry policy is the one place a failed call is tried again, and creating an event is idempotent on the ids.
/// </summary>
internal sealed class CalendarHttp(HttpMessageHandler handler, TimeSpan timeout) : IDisposable
{
    private readonly HttpClient client = new(handler) { Timeout = timeout };

    public static HttpMessageHandler NetworkHandler() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };

    /// <summary>
    /// Sends one request and reads the whole answer, whatever its status; the caller decides what a status means. Only a
    /// call that got no answer throws: <c>unreachable</c> and <c>timeout</c> are never permanent.
    /// </summary>
    public async Task<CalendarReply> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonDocument? body = null;
            if (text.Length > 0)
            {
                try
                {
                    body = JsonDocument.Parse(text);
                }
                catch (JsonException)
                {
                    // A page that is not JSON (a proxy's error page): the status still says what happened.
                }
            }

            return new CalendarReply(response.StatusCode, body, response.Headers.RetryAfter?.Delta);
        }
        catch (HttpRequestException e)
        {
            throw new CalendarApiException("unreachable", permanent: false, e);
        }
        catch (IOException e)
        {
            throw new CalendarApiException("unreachable", permanent: false, e);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CalendarApiException("timeout", permanent: false, e);
        }
    }

    public void Dispose() => client.Dispose();
}

/// <summary>A provider's answer: its status and, when it sent JSON, the body. Dispose it when the values are copied out.</summary>
internal sealed class CalendarReply(HttpStatusCode status, JsonDocument? body, TimeSpan? retryAfter) : IDisposable
{
    public HttpStatusCode Status { get; } = status;

    public bool IsSuccess => (int)Status is >= 200 and < 300;

    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>The body's root, or an undefined element when there was none or it was not JSON.</summary>
    public JsonElement Root => body?.RootElement ?? default;

    /// <summary>The root of a successful answer that must have a body.</summary>
    public JsonElement RequireRoot() => body is { RootElement.ValueKind: JsonValueKind.Object } ? body.RootElement : throw new CalendarApiException("unreadable_answer");

    public void Dispose() => body?.Dispose();
}

/// <summary>Turns a refusal into a <see cref="CalendarApiException"/>, the same way for both providers.</summary>
internal static class CalendarFailures
{
    // Codes that mean "try again later" whatever the status says. Google answers a rate limit with 403 or 429.
    private static readonly HashSet<string> TransientCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "rateLimitExceeded",
        "userRateLimitExceeded",
        "quotaExceeded",
        "backendError",
        "internalError",
        "activityLimitReached",
        "ErrorServerBusy",
        "ErrorTimeoutExceeded",
        "temporarily_unavailable",
    };

    /// <summary>
    /// 429, 408 and every 5xx are transient, as are the rate and quota codes. Everything else is permanent for this call
    /// (401, <c>invalid_grant</c>, a 403 for a missing permission, a 400): a retry would be refused the same way. The code
    /// is the provider's own reason, kept only when it is a short token; the body's text never reaches the exception.
    /// </summary>
    public static CalendarApiException Failure(CalendarReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var status = (int)reply.Status;
        var code = CalendarJson.SafeCode(CalendarJson.ErrorCode(reply.Root));
        if (CalendarJson.ErrorDescription(reply.Root) is { } description
            && (description.Contains("AADSTS70008", StringComparison.Ordinal) || description.Contains("AADSTS700082", StringComparison.Ordinal)))
        {
            // Microsoft: the refresh token expired from not being used. The member has to connect again.
            code = "invalid_grant";
        }

        code ??= status == 429 ? "rate_limited" : string.Create(CultureInfo.InvariantCulture, $"http_{status}");
        var transient = status is 408 or 429 or >= 500 || TransientCodes.Contains(code);
        return new CalendarApiException(code, permanent: !transient) { RetryAfter = reply.RetryAfter };
    }
}

/// <summary>Reading provider JSON without throwing on a shape it did not expect, and writing the request forms.</summary>
internal static class CalendarJson
{
    public static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string Required(JsonElement element, string name) =>
        Text(element, name) is { Length: > 0 } value ? value : throw new CalendarApiException("unreadable_answer");

    /// <summary>The named child, or an undefined element when there is none.</summary>
    public static JsonElement Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    public static IEnumerable<JsonElement> Items(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];

    /// <summary>
    /// The provider's error code from either body shape: OAuth's <c>{"error": "invalid_grant"}</c>, Google's
    /// <c>{"error": {"errors": [{"reason": "..."}]}}</c> and Graph's <c>{"error": {"code": "..."}}</c>.
    /// </summary>
    public static string? ErrorCode(JsonElement root)
    {
        var error = Child(root, "error");
        if (error.ValueKind == JsonValueKind.String)
        {
            return error.GetString();
        }

        if (Items(Child(error, "errors")).FirstOrDefault() is { ValueKind: JsonValueKind.Object } first && Text(first, "reason") is { } reason)
        {
            return reason;
        }

        return Text(error, "code") ?? Text(error, "status");
    }

    public static string? ErrorDescription(JsonElement root) => Text(root, "error_description");

    /// <summary>A code that is safe to put in an exception message and a log line: letters, digits and <c>_ - .</c>, at most 64.</summary>
    public static string? SafeCode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64)
        {
            return null;
        }

        return value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? value : null;
    }

    /// <summary>
    /// A time the provider sent. Graph sends no offset (the request asked for UTC), Google sends RFC 3339; both read as UTC
    /// instants with offset zero.
    /// </summary>
    public static DateTimeOffset Instant(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : throw new CalendarApiException("unreadable_answer");

    /// <summary>The wire form of an instant for query strings and request bodies: UTC, seven decimals, a <c>Z</c>.</summary>
    public static string Utc(DateTimeOffset instant, bool zone = true) =>
        instant.UtcDateTime.ToString(zone ? "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'" : "yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    public static string Query(IEnumerable<KeyValuePair<string, string>> parameters) =>
        string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

    /// <summary>The address, when the text is one. The sheet shows it as a label, so a calendar title that is not an address is dropped.</summary>
    public static string? AsEmail(string? value) =>
        value is { Length: > 0 } && MailAddress.TryCreate(value, out var address) && address.Address == value && address.Host.Contains('.', StringComparison.Ordinal)
            ? value
            : null;

    public static StringContent Json(JsonNode body) => new(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
}

/// <summary>Reading the token endpoint's answer, the same for both providers.</summary>
internal static class CalendarOAuth
{
    /// <summary>
    /// A grant from a token answer. The scopes the member actually allowed are checked against <paramref name="requiredScopes"/>
    /// when the provider lists them (Google lets a member untick one on the consent screen): a missing one is
    /// <c>consent_required</c>. A first connection needs a refresh token (<paramref name="needsRefreshToken"/>), or the
    /// connection would die within the hour.
    /// </summary>
    public static CalendarGrant ReadGrant(CalendarReply reply, DateTimeOffset now, bool needsRefreshToken, IReadOnlyList<string> requiredScopes)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(requiredScopes);
        if (!reply.IsSuccess)
        {
            throw CalendarFailures.Failure(reply);
        }

        var root = reply.RequireRoot();
        var access = CalendarJson.Required(root, "access_token");
        if (CalendarJson.Text(root, "scope") is { } scope)
        {
            var granted = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (requiredScopes.Any(required => !granted.Contains(Name(required))))
            {
                throw new CalendarApiException("consent_required", permanent: true);
            }
        }

        var refresh = CalendarJson.Text(root, "refresh_token") is { Length: > 0 } token ? token : null;
        if (needsRefreshToken && refresh is null)
        {
            throw new CalendarApiException("no_refresh_token", permanent: true);
        }

        return new CalendarGrant(access, refresh, now + TimeSpan.FromSeconds(Lifetime(root)), null);
    }

    // Scopes come back as full addresses from Google and as bare names or addresses from Microsoft.
    private static string Name(string scope) => scope[(scope.LastIndexOf('/') + 1)..];

    private static double Lifetime(JsonElement root)
    {
        var value = CalendarJson.Child(root, "expires_in");
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var seconds) => seconds,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) => seconds,
            _ => 3600,
        };
    }
}
