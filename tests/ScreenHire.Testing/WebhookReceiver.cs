using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ScreenHire.Testing;

/// <summary>
/// A webhook receiver on this machine, so no test needs the internet: a Kestrel server on <c>127.0.0.1</c> and a random
/// port that records every request and answers with the status codes a test queues (then 200). The stack's worker may
/// post to it because the test stack sets <c>ScreenHire:Webhooks:AllowLoopback</c>. Start one per test.
/// </summary>
public sealed class WebhookReceiver : IAsyncDisposable
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(90);

    private readonly WebApplication app;
    private readonly ConcurrentQueue<int> statuses = new();
    private readonly ConcurrentQueue<ReceivedWebhook> received = new();

    private WebhookReceiver(WebApplication app, Uri url)
    {
        this.app = app;
        Url = url;
    }

    /// <summary>Where to point an endpoint: a path of its own, on loopback.</summary>
    public Uri Url { get; }

    public IReadOnlyList<ReceivedWebhook> Received => [.. received];

    public static async Task<WebhookReceiver> StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        // 127.0.0.1, not localhost: the worker resolves the URL's host, and localhost may come back as ::1 first.
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        WebhookReceiver? receiver = null;
        app.MapPost("/hooks/{id}", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            var headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            receiver!.received.Enqueue(new ReceivedWebhook(body, headers, DateTimeOffset.UtcNow));
            return Results.StatusCode(receiver.statuses.TryDequeue(out var status) ? status : StatusCodes.Status200OK);
        });
        await app.StartAsync(cancellationToken);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        receiver = new WebhookReceiver(app, new Uri($"{address}/hooks/{Guid.NewGuid():N}"));
        return receiver;
    }

    /// <summary>The next requests get these status codes, in order; after them, 200.</summary>
    public void RespondWith(params int[] codes)
    {
        foreach (var code in codes)
        {
            statuses.Enqueue(code);
        }
    }

    /// <summary>Waits until <paramref name="count"/> requests match <paramref name="match"/>, and returns them in arrival order.</summary>
    public async Task<IReadOnlyList<ReceivedWebhook>> WaitForAsync(
        Func<ReceivedWebhook, bool> match, int count, string because, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + Deadline;
        while (true)
        {
            var matching = received.Where(match).ToList();
            if (matching.Count >= count)
            {
                return matching;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"Timed out after {Deadline.TotalSeconds:0} s waiting for {because}: {matching.Count} of {count} arrived, {received.Count} requests in all.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    /// <summary>Waits for one request of event type <paramref name="type"/> about <paramref name="dataId"/> (an id in its data).</summary>
    public async Task<ReceivedWebhook> WaitForEventAsync(string type, Guid dataId, CancellationToken cancellationToken) =>
        (await WaitForAsync(r => r.Type == type && r.Body.Contains(dataId.ToString(), StringComparison.Ordinal), 1, $"a {type} webhook about {dataId}",
            cancellationToken))[0];

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}

/// <summary>One request a <see cref="WebhookReceiver"/> got: the raw body and every header.</summary>
public sealed record ReceivedWebhook(string Body, IReadOnlyDictionary<string, string> Headers, DateTimeOffset ReceivedAt)
{
    public JsonElement Json => JsonDocument.Parse(Body).RootElement;

    public string Type => Json.GetProperty("type").GetString()!;

    public Guid EventId => Json.GetProperty("id").GetGuid();

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// Checks <c>ScreenHire-Signature</c> the way an integrator would, from the documented format alone:
    /// <c>t={unix seconds},v1={hex}</c>, the hex HMAC-SHA256 of <c>{t}.{raw body}</c> keyed with the endpoint's secret.
    /// </summary>
    public bool IsSignedWith(string secret)
    {
        if (Header("ScreenHire-Signature") is not { } signature)
        {
            return false;
        }

        var parts = signature.Split(',').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");
        if (!parts.TryGetValue("t", out var timestamp) || !parts.TryGetValue("v1", out var hex)
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{Body}"));
        return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(hex));
    }
}
