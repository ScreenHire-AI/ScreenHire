using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ScreenHire.Application.Automations;

/// <summary>
/// What a CallWebhook action posts: ids and the automation, never candidate details. The receiver fetches anything else
/// through the API with its own credentials. <c>Idempotency-Key</c> carries the action's key, so a receiver can drop a
/// retried delivery, and <c>ScreenHire-Signature</c> lets it check the body came from us (<see cref="WebhookSignature"/>).
/// </summary>
public sealed record WebhookPayload(
    string Type, Guid OrganizationId, Guid ApplicationId, Guid JobId, string AutomationId, Guid RunId, Guid EventId, DateTimeOffset SentAt);

/// <summary>Posts one webhook. The HTTP adapter is <see cref="HttpWebhookSender"/>; tests replace it.</summary>
public interface IWebhookSender
{
    /// <summary>False when no signing key is configured: nothing is sent, because a receiver could not trust it.</summary>
    bool IsConfigured { get; }

    /// <summary>Posts <paramref name="payload"/> and returns the status code; throws when the receiver does not accept it.</summary>
    Task<int> PostAsync(Uri url, string idempotencyKey, WebhookPayload payload, CancellationToken cancellationToken);

    /// <summary>
    /// Posts one subscription delivery, signed with <see cref="WebhookRequest.SigningSecret"/>. Never throws for the
    /// receiver's sake (a refusal, a timeout, an address it may not reach): the result says what happened, so the caller
    /// can record the attempt and schedule the next.
    /// </summary>
    Task<WebhookResponse> DeliverAsync(WebhookRequest request, CancellationToken cancellationToken);
}

/// <summary>One subscription delivery: the exact body, the secret it is signed with, and the extra headers.</summary>
public sealed record WebhookRequest(Uri Url, string SigningSecret, string Body, DateTimeOffset SentAt, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// What a receiver did with a delivery. <see cref="Error"/> is in our words (a status code, a timeout, a refused
/// address) and never quotes the receiver's response or an exception message.
/// </summary>
public sealed record WebhookResponse(int? StatusCode, string? Error)
{
    public bool Succeeded => Error is null;
}

/// <summary><c>ScreenHire:Webhooks</c>.</summary>
public sealed class WebhookOptions
{
    public const string Section = "ScreenHire:Webhooks";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The platform key each organization's signing key is derived from (<see cref="WebhookSignature.OrganizationKey"/>).
    /// It never leaves the worker. The AppHost generates one locally; production sets its own secret. Without one,
    /// CallWebhook actions fail with an issue rather than send something unverifiable.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// Lets webhooks reach loopback addresses (127.0.0.0/8, ::1), so a receiver on the same machine works. The local
    /// stack and the tests turn it on; production never does. Private and link-local ranges stay refused either way. It
    /// is checked on the resolved address at connect time, like the public-address rule.
    /// </summary>
    public bool AllowLoopback { get; set; }

    /// <summary>
    /// How long a subscription delivery waits before each retry; one attempt more than there are delays. Null means
    /// <see cref="DefaultRetryDelays"/>: about 21 hours of retries in all.
    /// </summary>
    public TimeSpan[]? RetryDelays { get; set; }

    /// <summary>Deliveries in a row that fail every attempt before the endpoint is disabled.</summary>
    public int DisableAfterFailedDeliveries { get; set; } = 5;

    /// <summary>How long a finished delivery stays in its endpoint's log (the daily PurgeExpiredWebhookDeliveries sweep).</summary>
    public TimeSpan DeliveryRetention { get; set; } = TimeSpan.FromDays(30);

    public static IReadOnlyList<TimeSpan> DefaultRetryDelays { get; } =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
    ];

    public IReadOnlyList<TimeSpan> EffectiveRetryDelays => RetryDelays ?? DefaultRetryDelays;
}

/// <summary>
/// How a receiver checks a webhook: <c>ScreenHire-Signature: t={unix seconds},v1={hex}</c>, where the hex is
/// HMAC-SHA256 of <c>{t}.{raw body}</c> under its organization's key. Including the time lets a receiver refuse old
/// replays.
/// </summary>
public static class WebhookSignature
{
    public const string Header = "ScreenHire-Signature";

    /// <summary>
    /// The key one organization's deliveries are signed with, and the one its receivers are given: hex HMAC-SHA256 of
    /// <c>webhook-signing:{organizationId}</c> under the platform key. A receiver holding one organization's key cannot
    /// sign a delivery that another organization's receiver would accept, and the platform key stays in the worker.
    /// </summary>
    public static string OrganizationKey(string platformKey, Guid organizationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(platformKey);
        var mac = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(platformKey),
            Encoding.UTF8.GetBytes($"webhook-signing:{organizationId.ToString("D", System.Globalization.CultureInfo.InvariantCulture)}"));
        return Convert.ToHexStringLower(mac);
    }

    /// <summary>
    /// One subscription endpoint's secret, as its integrator is given it: <c>whsec_</c> and the hex HMAC-SHA256 of
    /// <c>webhook-endpoint:{endpointId}:{version}</c> under the platform key. Nothing secret is stored: the database keeps
    /// only the version, and rotating bumps it. Changing the platform key changes every endpoint's secret.
    /// </summary>
    public static string EndpointSecret(string platformKey, Guid endpointId, int version)
    {
        ArgumentException.ThrowIfNullOrEmpty(platformKey);
        var mac = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(platformKey),
            Encoding.UTF8.GetBytes(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"webhook-endpoint:{endpointId:D}:{version}")));
        return $"whsec_{Convert.ToHexStringLower(mac)}";
    }

    public static string Create(string key, DateTimeOffset sentAt, string body)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(body);
        var timestamp = sentAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(mac)}";
    }
}

/// <summary>
/// The CallWebhook adapter: one POST whose connections only open to public addresses (<see cref="WebhookAddresses"/>),
/// checked on the resolved address at connect time so DNS rebinding cannot slip past it.
/// </summary>
/// <remarks>
/// One long-lived <see cref="HttpClient"/> over a pooled <see cref="SocketsHttpHandler"/>, not IHttpClientFactory: the
/// service defaults give every factory client a retrying resilience handler, and Wolverine already retries the action,
/// so two layers would post the same webhook several times. The opt-out (RemoveAllResilienceHandlers) is still marked
/// experimental.
/// </remarks>
public sealed class HttpWebhookSender(IOptions<WebhookOptions> options) : IWebhookSender, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient client = new(CreateHandler(options)) { Timeout = options.Value.Timeout };

    public bool IsConfigured => !string.IsNullOrEmpty(options.Value.SigningKey);

    public async Task<int> PostAsync(Uri url, string idempotencyKey, WebhookPayload payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(payload);
        var key = options.Value.SigningKey ?? throw new InvalidOperationException("No webhook signing key is configured.");

        // Serialized once, so the signature covers exactly the bytes sent.
        var body = JsonSerializer.Serialize(payload, Json);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add(WebhookSignature.Header,
            WebhookSignature.Create(WebhookSignature.OrganizationKey(key, payload.OrganizationId), payload.SentAt, body));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (int)response.StatusCode;
    }

    public async Task<WebhookResponse> DeliverAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, request.Url)
        {
            Content = new StringContent(request.Body, Encoding.UTF8, "application/json"),
        };
        message.Headers.Add(WebhookSignature.Header, WebhookSignature.Create(request.SigningSecret, request.SentAt, request.Body));
        foreach (var (name, value) in request.Headers)
        {
            message.Headers.Add(name, value);
        }

        try
        {
            // Headers only: the receiver's body is never read, stored or logged.
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;
            return new WebhookResponse(
                status, response.IsSuccessStatusCode ? null : string.Create(CultureInfo.InvariantCulture, $"The receiver answered HTTP {status}."));
        }
        catch (HttpRequestException exception) when (IsRefusedAddress(exception))
        {
            return new WebhookResponse(null, "The URL does not resolve to an address webhooks may reach.");
        }
        catch (HttpRequestException exception)
        {
            return new WebhookResponse(null, $"Could not reach the receiver ({exception.HttpRequestError}).");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebhookResponse(
                null, string.Create(CultureInfo.InvariantCulture, $"The receiver did not answer within {client.Timeout.TotalSeconds:0} seconds."));
        }
    }

    private static bool IsRefusedAddress(Exception exception)
    {
        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is WebhookAddressRefusedException)
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose() => client.Dispose();

    /// <summary>The client's handler: refuses to connect to a non-public address.</summary>
    private static SocketsHttpHandler CreateHandler(IOptions<WebhookOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5), // picks up DNS changes
            AllowAutoRedirect = false, // a redirect could point anywhere; the receiver gets one try at its own URL
            // Through a proxy, ConnectCallback would see the proxy's address and the proxy would resolve the target.
            UseProxy = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var allowLoopback = options.Value.AllowLoopback;
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                var address = addresses.FirstOrDefault(a => WebhookAddresses.MayReach(a, allowLoopback))
                    ?? throw new WebhookAddressRefusedException(context.DnsEndPoint.Host);
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
    }
}

/// <summary>A webhook host resolved only to addresses webhooks may not reach (<see cref="WebhookAddresses"/>).</summary>
public sealed class WebhookAddressRefusedException(string host)
    : HttpRequestException($"Webhook host {host} does not resolve to a public address.");

/// <summary>Which addresses a webhook may reach: public unicast only, plus loopback when the local stack allows it.</summary>
public static class WebhookAddresses
{
    public static bool MayReach(IPAddress address, bool allowLoopback)
    {
        ArgumentNullException.ThrowIfNull(address);
        return IsPublic(address) || (allowLoopback && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address));
    }

    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6 && EmbeddedIPv4(address.GetAddressBytes()) is { } embedded)
        {
            // NAT64 and 6to4 carry an IPv4 address that a gateway on the path can reach: judge that one.
            address = embedded;
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast) || address.Equals(IPAddress.None))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0 // "this" network
                || b[0] == 10
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // carrier-grade NAT
                || (b[0] == 169 && b[1] == 254) // link-local, including cloud metadata endpoints
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) // benchmarking
                || b[0] >= 224); // multicast and reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6Teredo
                || (b[0] & 0xfe) == 0xfc // unique local fc00::/7
                || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b) // any other NAT64 prefix, such as local-use 64:ff9b:1::/48
                || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8)); // documentation 2001:db8::/32
        }

        return false;
    }

    // The IPv4 address inside a well-known NAT64 address (64:ff9b::/96, last four bytes) or a 6to4 address (2002::/16,
    // bytes two to five); null for any other IPv6 address.
    private static IPAddress? EmbeddedIPv4(byte[] b)
    {
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0)
        {
            return new IPAddress(b.AsSpan(12, 4));
        }

        return b[0] == 0x20 && b[1] == 0x02 ? new IPAddress(b.AsSpan(2, 4)) : null;
    }
}
