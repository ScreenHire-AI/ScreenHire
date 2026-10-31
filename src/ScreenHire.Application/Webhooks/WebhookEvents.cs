using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Verification;
using ScreenHire.Application.Jobs;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Verification;
using ScreenHire.Domain.Webhooks;

namespace ScreenHire.Application.Webhooks;

/// <summary>
/// Send one event to every endpoint of its organization that subscribes to <see cref="EventType"/>. The handler of the
/// domain event it comes from cascades it, so the fan-out retries and fails apart from that handler's own work.
/// <see cref="Body"/> is the whole delivery body, already serialized: ids and facts, never personal data.
/// </summary>
public sealed record FanOutWebhook(Guid OrganizationId, string EventType, Guid EventId, string Body) : ITenantMessage;

/// <summary>
/// The body of every subscription delivery: <c>id</c> (the event, the same on every endpoint, retry and replay),
/// <c>type</c>, <c>occurredAt</c>, <c>organizationId</c> and the event's <c>data</c>. An integrator fetches anything
/// else through the API with its own credentials.
/// </summary>
public sealed record WebhookEventBody<TData>(Guid Id, string Type, DateTimeOffset OccurredAt, Guid OrganizationId, TData Data);

public sealed record ApplicationSubmittedData(Guid ApplicationId, Guid JobId, string StageId);

public sealed record ApplicationMovedData(Guid ApplicationId, Guid JobId, string FromStageId, string ToStageId);

/// <summary>Categorical levels and the ruleset version only (ADR-0004): the evidence stays behind the API.</summary>
public sealed record VerificationAssessedData(
    Guid ApplicationId, Guid JobId, AuthenticityLevel Authenticity, AiAssistanceLevel AiAssistance, string RulesetVersion);

public sealed record AutomationFailedData(Guid ApplicationId, Guid JobId, string AutomationId, Guid RunId, AutomationRunStatus Status);

public sealed record JobPublishedData(Guid JobId, int Version, Guid? JobVersionId);

public sealed record JobClosedData(Guid JobId);

/// <summary>
/// Maps the domain events that already exist to webhook events (docs/architecture/workflow-engine.md §5). Each event's
/// id comes from stable fields of the domain event, never the clock, so a redelivered domain event raises the same
/// webhook event and the fan-out adds nothing.
/// </summary>
public static class WebhookEvents
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static FanOutWebhook From(ApplicationSubmitted e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Raise(e.OrganizationId, WebhookEventTypes.ApplicationSubmitted, e.EventId.ToString(), e.SubmittedAt,
            new ApplicationSubmittedData(e.ApplicationId, e.JobId, e.StageId));
    }

    public static FanOutWebhook From(ApplicationMoved e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Raise(e.OrganizationId, WebhookEventTypes.ApplicationMoved, e.EventId.ToString(), e.MovedAt,
            new ApplicationMovedData(e.ApplicationId, e.JobId, e.FromStageId, e.ToStageId));
    }

    public static FanOutWebhook From(VerificationAssessed e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Raise(e.OrganizationId, WebhookEventTypes.VerificationAssessed, e.EventId.ToString(), e.AssessedAt,
            new VerificationAssessedData(e.ApplicationId, e.JobId, e.Authenticity, e.AiAssistance, e.RulesetVersion));
    }

    /// <summary>A failed or blocked run, the two outcomes the automation issues list shows; null for any other.</summary>
    public static FanOutWebhook? From(AutomationRunFinished e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.Status is AutomationRunStatus.Failed or AutomationRunStatus.Blocked
            ? Raise(e.OrganizationId, WebhookEventTypes.AutomationFailed, e.RunId.ToString(), e.FinishedAt,
                new AutomationFailedData(e.ApplicationId, e.JobId, e.AutomationId, e.RunId, e.Status))
            : null;
    }

    public static FanOutWebhook From(JobPublished e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Raise(e.OrganizationId, WebhookEventTypes.JobPublished, $"{e.JobId}:{e.Version}", e.PublishedAt,
            new JobPublishedData(e.JobId, e.Version, e.JobVersionId));
    }

    public static FanOutWebhook From(JobClosed e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Raise(e.OrganizationId, WebhookEventTypes.JobClosed, $"{e.JobId}:{e.ClosedAt.UtcTicks}", e.ClosedAt, new JobClosedData(e.JobId));
    }

    /// <summary>
    /// The webhook event id for one domain event: an RFC 9562 version 8 UUID from SHA-256 of the type and the domain
    /// event's own key. The same domain event always gives the same id, and two event types never share one.
    /// </summary>
    public static Guid EventId(string eventType, string sourceKey)
    {
        Span<byte> bytes = stackalloc byte[16];
        SHA256.HashData(Encoding.UTF8.GetBytes($"webhook:{eventType}:{sourceKey}"))[..16].CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    private static FanOutWebhook Raise<TData>(Guid organizationId, string eventType, string sourceKey, DateTimeOffset occurredAt, TData data)
    {
        var id = EventId(eventType, sourceKey);
        var body = JsonSerializer.Serialize(new WebhookEventBody<TData>(id, eventType, occurredAt, organizationId, data), Json);
        return new FanOutWebhook(organizationId, eventType, id, body);
    }
}
