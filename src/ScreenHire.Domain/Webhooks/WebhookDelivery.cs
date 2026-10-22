using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Webhooks;

/// <summary>
/// One event sent to one endpoint: the endpoint's delivery log. <see cref="Payload"/> is the exact body, written once
/// when the event fans out, so every retry and replay sends the same bytes. It holds ids and facts, never personal data.
/// A replay is a delivery of its own, with <see cref="ReplayOfId"/> set and the same event id and body.
/// </summary>
public sealed class WebhookDelivery : ITenantOwned
{
    public const int MaxErrorLength = 200;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid EndpointId { get; init; }

    /// <summary>The event's id, the same for every endpoint and every replay. Receivers deduplicate on it.</summary>
    public Guid EventId { get; init; }

    public required string EventType { get; init; }

    public required string Payload { get; init; }

    public Guid? ReplayOfId { get; init; }

    public WebhookDeliveryStatus Status { get; private set; } = WebhookDeliveryStatus.Pending;

    public int Attempts { get; private set; }

    /// <summary>The receiver's status code on the last attempt; null when it never answered.</summary>
    public int? LastStatusCode { get; private set; }

    /// <summary>What went wrong on the last attempt, in our words: never the receiver's response body.</summary>
    public string? LastError { get; private set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastAttemptAt { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static WebhookDelivery For(Guid organizationId, Guid endpointId, Guid eventId, string eventType, string payload, DateTimeOffset now) =>
        new()
        {
            OrganizationId = organizationId,
            EndpointId = endpointId,
            EventId = eventId,
            EventType = eventType,
            Payload = payload,
            CreatedAt = now,
        };

    /// <summary>A new delivery of the same event and body to the same endpoint.</summary>
    public WebhookDelivery Replay(DateTimeOffset now) =>
        new()
        {
            OrganizationId = OrganizationId,
            EndpointId = EndpointId,
            EventId = EventId,
            EventType = EventType,
            Payload = Payload,
            ReplayOfId = Id,
            CreatedAt = now,
        };

    /// <summary>Records one attempt's outcome. <paramref name="nextAttemptAt"/> null with a failure means no attempts are left.</summary>
    public void RecordAttempt(DateTimeOffset now, int? statusCode, string? error, DateTimeOffset? nextAttemptAt)
    {
        Attempts++;
        LastAttemptAt = now;
        LastStatusCode = statusCode;
        LastError = error is { Length: > MaxErrorLength } ? error[..MaxErrorLength] : error;
        NextAttemptAt = error is null ? null : nextAttemptAt;
        if (error is null)
        {
            Status = WebhookDeliveryStatus.Succeeded;
            CompletedAt = now;
        }
        else if (nextAttemptAt is null)
        {
            Status = WebhookDeliveryStatus.Failed;
            CompletedAt = now;
        }
    }

    /// <summary>Ends the delivery without sending, for a reason that is not the receiver's: the endpoint is off.</summary>
    public void Skip(string reason, DateTimeOffset now)
    {
        Status = WebhookDeliveryStatus.Skipped;
        LastError = reason;
        NextAttemptAt = null;
        CompletedAt = now;
    }
}

public enum WebhookDeliveryStatus
{
    Pending,
    Succeeded,
    Failed,
    Skipped,
}
