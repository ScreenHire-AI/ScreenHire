using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Integrity;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Webhooks;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Webhooks;

/// <summary>
/// Make attempt <see cref="Attempt"/> of one subscription delivery. The fan-out sends attempt 1; each failed attempt
/// schedules the next. The delivery row's attempt count makes a duplicate or stale message a no-op.
/// </summary>
public sealed record DeliverWebhook(Guid OrganizationId, Guid DeliveryId, int Attempt) : ITenantMessage
{
    public string IdempotencyKey => $"{DeliveryId}:{Attempt}";
}

/// <summary>
/// The fan-out: one delivery row per enabled endpoint that subscribes to the event, and its first attempt. An endpoint
/// that already has the event (a redelivered domain event) gets nothing new; the unique index on (endpoint, event)
/// backs that check up.
/// </summary>
public static class FanOutWebhookHandler
{
    public static async Task<OutgoingMessages> Handle(
        FanOutWebhook message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var endpoints = await db.WebhookEndpoints.AsNoTracking()
            .Where(e => e.Enabled && e.Events.Contains(message.EventType))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        if (endpoints.Count == 0)
        {
            return outgoing;
        }

        var delivered = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => endpoints.Contains(d.EndpointId) && d.EventId == message.EventId && d.ReplayOfId == null)
            .Select(d => d.EndpointId)
            .ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        foreach (var endpointId in endpoints.Except(delivered))
        {
            var delivery = WebhookDelivery.For(message.OrganizationId, endpointId, message.EventId, message.EventType, message.Body, now);
            db.WebhookDeliveries.Add(delivery);
            outgoing.Add(new DeliverWebhook(message.OrganizationId, delivery.Id, 1));
        }

        return outgoing;
    }
}

/// <summary>
/// Sends one attempt of a delivery and records it on the delivery row, the endpoint's delivery log. A receiver that
/// fails gets the next attempt after the next of <see cref="WebhookOptions.EffectiveRetryDelays"/>, as a durable
/// scheduled message; after the last, the delivery fails and counts toward disabling the endpoint.
/// </summary>
/// <remarks>
/// The handler schedules its own retries instead of throwing into Wolverine's error policy: a throw rolls back the
/// transaction, so the attempt could not be logged or counted, and the policy's delays are the same for every message.
/// Anything other than the receiver's answer (the database, a bug) still throws and goes through the policy.
/// </remarks>
public static partial class DeliverWebhookHandler
{
    /// <summary>Who we say we are to receivers.</summary>
    public const string UserAgent = "ScreenHire-Webhooks/1";

    public const string EventIdHeader = "ScreenHire-Event-Id";

    public const string EventTypeHeader = "ScreenHire-Event-Type";

    public const string DeliveryIdHeader = "ScreenHire-Delivery-Id";

    [Deduplicated(ValueSource.InputMember, nameof(DeliverWebhook.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        DeliverWebhook message,
        ScreenHireDbContext db,
        IWebhookSender sender,
        IOptions<WebhookOptions> options,
        TimeProvider time,
        ILogger<DeliverWebhook> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        // A delivery goes with its endpoint, so a missing one was deleted with it: nothing to send.
        var delivery = await db.WebhookDeliveries.SingleOrDefaultAsync(d => d.Id == message.DeliveryId, cancellationToken);
        if (delivery is null || delivery.Status != WebhookDeliveryStatus.Pending || delivery.Attempts != message.Attempt - 1)
        {
            return outgoing; // done already, deleted, or a duplicate of an attempt that ran
        }

        // Null when the endpoint was deleted since: its deliveries go with it, so there is nothing to record either.
        var endpoint = await db.WebhookEndpoints.AsNoTracking().SingleOrDefaultAsync(e => e.Id == delivery.EndpointId, cancellationToken);
        if (endpoint is null)
        {
            return outgoing;
        }

        var now = time.GetUtcNow();
        if (!endpoint.Enabled)
        {
            delivery.Skip("The endpoint is disabled, so nothing was sent.", now);
            return outgoing;
        }

        var settings = options.Value;
        if (string.IsNullOrEmpty(settings.SigningKey))
        {
            delivery.Skip("Webhook signing is not set up (ScreenHire:Webhooks:SigningKey), so nothing was sent.", now);
            return outgoing;
        }

        var response = await sender.DeliverAsync(
            new WebhookRequest(
                new Uri(endpoint.Url),
                WebhookSignature.EndpointSecret(settings.SigningKey, endpoint.Id, endpoint.SecretVersion),
                delivery.Payload,
                now,
                new Dictionary<string, string>
                {
                    ["User-Agent"] = UserAgent,
                    ["Idempotency-Key"] = delivery.EventId.ToString(),
                    [EventIdHeader] = delivery.EventId.ToString(),
                    [EventTypeHeader] = delivery.EventType,
                    [DeliveryIdHeader] = delivery.Id.ToString(),
                }),
            cancellationToken);

        var delays = settings.EffectiveRetryDelays;
        DateTimeOffset? nextAt = response.Succeeded || message.Attempt > delays.Count ? null : now + delays[message.Attempt - 1];
        delivery.RecordAttempt(now, response.StatusCode, response.Error, nextAt);
        LogAttempt(logger, delivery.Id, message.Attempt, endpoint.Id, delivery.Status, response.StatusCode);

        if (response.Succeeded)
        {
            if (endpoint.ConsecutiveFailures > 0)
            {
                await db.WebhookEndpoints.Where(e => e.Id == endpoint.Id && e.ConsecutiveFailures > 0)
                    .ExecuteUpdateAsync(set => set.SetProperty(e => e.ConsecutiveFailures, 0), cancellationToken);
            }
        }
        else if (nextAt is { } retryAt)
        {
            outgoing.Schedule(new DeliverWebhook(message.OrganizationId, delivery.Id, message.Attempt + 1), retryAt);
        }
        else
        {
            await CountFailedDeliveryAsync(db, endpoint, settings.DisableAfterFailedDeliveries, response.Error!, now, cancellationToken);
        }

        return outgoing;
    }

    // One statement, so parallel deliveries to the same endpoint never lose a count: Postgres evaluates every SET against
    // the row as it was, under the row lock.
    private static async Task CountFailedDeliveryAsync(
        ScreenHireDbContext db, WebhookEndpoint endpoint, int limit, string lastError, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reason = string.Create(CultureInfo.InvariantCulture,
            $"Disabled automatically: {limit} deliveries in a row failed every attempt. The last: {lastError}");
        if (reason.Length > WebhookEndpoint.MaxDisabledReasonLength)
        {
            reason = reason[..WebhookEndpoint.MaxDisabledReasonLength];
        }

        await db.WebhookEndpoints.Where(e => e.Id == endpoint.Id && e.Enabled)
            .ExecuteUpdateAsync(set => set
                .SetProperty(e => e.ConsecutiveFailures, e => e.ConsecutiveFailures + 1)
                .SetProperty(e => e.Enabled, e => e.ConsecutiveFailures + 1 < limit)
                .SetProperty(e => e.DisabledAt, e => e.ConsecutiveFailures + 1 < limit ? e.DisabledAt : now)
                .SetProperty(e => e.DisabledReason, e => e.ConsecutiveFailures + 1 < limit ? e.DisabledReason : reason)
                .SetProperty(e => e.UpdatedAt, e => e.ConsecutiveFailures + 1 < limit ? e.UpdatedAt : now),
                cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Webhook delivery {DeliveryId} attempt {Attempt} to endpoint {EndpointId}: {Status} ({StatusCode})")]
    private static partial void LogAttempt(
        ILogger logger, Guid deliveryId, int attempt, Guid endpointId, WebhookDeliveryStatus status, int? statusCode);
}

/// <summary>
/// The two webhook sources with no worker handler of their own: the API pushes them to dashboards, and the worker
/// raises their webhook events here.
/// </summary>
public static class IntegrityAssessedHandler
{
    public static FanOutWebhook Handle(IntegrityAssessed message) => WebhookEvents.From(message);
}

/// <inheritdoc cref="IntegrityAssessedHandler"/>
public static class AutomationRunFinishedHandler
{
    public static OutgoingMessages Handle(AutomationRunFinished message)
    {
        var outgoing = new OutgoingMessages();
        if (WebhookEvents.From(message) is { } raised)
        {
            outgoing.Add(raised);
        }

        return outgoing;
    }
}
