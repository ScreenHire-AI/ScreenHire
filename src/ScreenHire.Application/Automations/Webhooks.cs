using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace ScreenHire.Application.Automations;

/// <summary>
/// What a CallWebhook action posts: ids and the automation, never candidate details. The receiver fetches anything else
/// through the API with its own credentials. <c>Idempotency-Key</c> carries the action's key, so a receiver can drop a
/// retried delivery.
/// </summary>
public sealed record WebhookPayload(
    string Type, Guid OrganizationId, Guid ApplicationId, Guid JobId, string AutomationId, Guid RunId, Guid EventId, DateTimeOffset SentAt);

/// <summary>Posts one webhook. The HTTP adapter is <see cref="HttpWebhookSender"/>; tests replace it.</summary>
public interface IWebhookSender
{
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
    private readonly HttpClient client = new(CreateHandler(options)) { Timeout = options.Value.Timeout };

    public async Task<int> PostAsync(Uri url, string idempotencyKey, WebhookPayload payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

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
