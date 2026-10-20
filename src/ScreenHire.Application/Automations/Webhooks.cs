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
}

/// <summary><c>ScreenHire:Webhooks</c>.</summary>
public sealed class WebhookOptions
{
    public const string Section = "ScreenHire:Webhooks";

    /// <summary>
    /// Lets webhooks reach loopback and private addresses. Off by default, because a recruiter-supplied URL must never
    /// reach the hosts' own network (server-side request forgery). Only for local development against a local receiver.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The HMAC-SHA256 key every delivery is signed with. The AppHost generates one locally; production sets its own
    /// secret. Without one, CallWebhook actions fail with an issue rather than send something unverifiable.
    /// </summary>
    public string? SigningKey { get; set; }
}

/// <summary>
/// How a receiver checks a webhook: <c>ScreenHire-Signature: t={unix seconds},v1={hex}</c>, where the hex is
/// HMAC-SHA256 of <c>{t}.{raw body}</c> under the shared key. Including the time lets a receiver refuse old replays.
/// </summary>
public static class WebhookSignature
{
    public const string Header = "ScreenHire-Signature";

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
        request.Headers.Add(WebhookSignature.Header, WebhookSignature.Create(key, payload.SentAt, body));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (int)response.StatusCode;
    }

    public void Dispose() => client.Dispose();

    /// <summary>The client's handler: refuses to connect to a non-public address unless the options allow it.</summary>
    private static SocketsHttpHandler CreateHandler(IOptions<WebhookOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var allowPrivate = options.Value.AllowPrivateNetworks;
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5), // picks up DNS changes
            AllowAutoRedirect = false, // a redirect could point anywhere; the receiver gets one try at its own URL
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                var address = addresses.FirstOrDefault(a => allowPrivate || WebhookAddresses.IsPublic(a))
                    ?? throw new HttpRequestException($"Webhook host {context.DnsEndPoint.Host} does not resolve to a public address.");
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

/// <summary>Which addresses a webhook may reach: public unicast only.</summary>
public static class WebhookAddresses
{
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
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
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                || (b[0] & 0xfe) == 0xfc); // unique local fc00::/7
        }

        return false;
    }
}
