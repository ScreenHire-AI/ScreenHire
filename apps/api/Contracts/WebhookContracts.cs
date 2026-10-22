using System.Net;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Webhooks;

namespace ScreenHire.Api.Contracts;

/// <summary>A new webhook endpoint: where to post, and which events (<c>GET /v1/webhooks/event-types</c> lists them).</summary>
public sealed record CreateWebhookEndpointRequest(string Url, IReadOnlyList<string> Events, string? Description = null);

/// <summary>
/// Replaces an endpoint's settings. A body without <c>enabled</c> keeps the endpoint on or off as it is. Turning it
/// back on clears an automatic disable and its failure count; deliveries skipped while it was off stay skipped and can
/// be replayed.
/// </summary>
public sealed record UpdateWebhookEndpointRequest(string Url, IReadOnlyList<string> Events, bool? Enabled = null, string? Description = null);

/// <summary>
/// One webhook endpoint. The signing secret is never part of it: it appears once, in the create and rotate responses.
/// <see cref="DisabledReason"/> says why the worker turned the endpoint off, when it did.
/// </summary>
public sealed record WebhookEndpointEntry(
    Guid Id,
    string Url,
    string? Description,
    IReadOnlyList<string> Events,
    bool Enabled,
    int ConsecutiveFailures,
    DateTimeOffset? DisabledAt,
    string? DisabledReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset SecretRotatedAt);

/// <summary>
/// An endpoint with its signing secret, returned only when the secret is new (create, rotate). Store it now: the API
/// cannot show it again. Check each delivery's <c>ScreenHire-Signature: t={unix seconds},v1={hex}</c>, the hex
/// HMAC-SHA256 of <c>{t}.{raw body}</c> keyed with this secret.
/// </summary>
public sealed record WebhookEndpointWithSecret(WebhookEndpointEntry Endpoint, string SigningSecret);

/// <summary>
/// One delivery in an endpoint's log. <see cref="Payload"/> is the exact body sent; <see cref="ReplayOfId"/> is set on
/// a replay. <see cref="LastError"/> is our description of the last failed attempt, never the receiver's response.
/// </summary>
public sealed record WebhookDeliveryEntry(
    Guid Id,
    Guid EventId,
    string EventType,
    WebhookDeliveryStatus Status,
    int Attempts,
    int? LastStatusCode,
    string? LastError,
    Guid? ReplayOfId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? CompletedAt,
    string Payload);

internal static class WebhookMappings
{
    public static WebhookEndpointEntry ToEntry(this WebhookEndpoint e) => new(
        e.Id, e.Url, e.Description, e.Events, e.Enabled, e.ConsecutiveFailures, e.DisabledAt, e.DisabledReason, e.CreatedAt, e.UpdatedAt,
        e.SecretRotatedAt);

    public static WebhookDeliveryEntry ToEntry(this WebhookDelivery d) => new(
        d.Id, d.EventId, d.EventType, d.Status, d.Attempts, d.LastStatusCode, d.LastError, d.ReplayOfId, d.CreatedAt, d.LastAttemptAt,
        d.NextAttemptAt, d.CompletedAt, d.Payload);
}

/// <summary>
/// Checks an endpoint's URL and events before it is saved. The address rule holds again when the worker connects
/// (<see cref="WebhookAddresses"/>, on the resolved address); this only refuses what can never work, early.
/// </summary>
internal static class WebhookValidation
{
    public static Dictionary<string, string[]>? Problems(string? url, IReadOnlyList<string>? events, string? description, bool allowLoopback)
    {
        var problems = new Dictionary<string, string[]>();
        if (UrlProblem(url, allowLoopback) is { } urlProblem)
        {
            problems["url"] = [urlProblem];
        }

        if (events is not { Count: > 0 })
        {
            problems["events"] = ["Choose at least one event."];
        }
        else if (events.FirstOrDefault(e => !WebhookEventTypes.IsKnown(e)) is { } unknown)
        {
            problems["events"] = [$"'{unknown}' is not an event. The events are {string.Join(", ", WebhookEventTypes.All)}."];
        }

        if (description is { Length: > WebhookEndpoint.MaxDescriptionLength })
        {
            problems["description"] = [$"A description is at most {WebhookEndpoint.MaxDescriptionLength} characters."];
        }

        return problems.Count == 0 ? null : problems;
    }

    private static string? UrlProblem(string? url, bool allowLoopback)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > WebhookEndpoint.MaxUrlLength
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            return $"A webhook URL is an absolute https URL of at most {WebhookEndpoint.MaxUrlLength} characters.";
        }

        if (uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
        {
            return "A webhook URL has no user name, password or #fragment.";
        }

        var loopback = uri.IsLoopback;
        if (IPAddress.TryParse(uri.DnsSafeHost, out var address) && !WebhookAddresses.MayReach(address, allowLoopback))
        {
            return "Webhooks reach public addresses only.";
        }

        if (loopback && !allowLoopback)
        {
            return "Webhooks reach public addresses only.";
        }

        // Plain http only to this machine, which the local stack allows; anything that crosses a network is https.
        return uri.Scheme == "http" && !(allowLoopback && loopback) ? "A webhook URL uses https." : null;
    }
}
