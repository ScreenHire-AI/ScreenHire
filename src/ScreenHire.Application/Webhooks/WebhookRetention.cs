using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Webhooks;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Webhooks;

/// <summary>
/// The daily sweep of webhook delivery logs: finished deliveries older than
/// <see cref="WebhookOptions.DeliveryRetention"/> go. It has no tenant of its own, so it fans out one
/// <see cref="PurgeOrganizationWebhookDeliveries"/> per organization, as the verification retention sweep does.
/// </summary>
public sealed record PurgeExpiredWebhookDeliveries;

/// <summary>Deletes one organization's deliveries that finished before <paramref name="Cutoff"/>. Pending ones stay.</summary>
public sealed record PurgeOrganizationWebhookDeliveries(Guid OrganizationId, DateTimeOffset Cutoff) : ITenantMessage;

public static class PurgeExpiredWebhookDeliveriesHandler
{
    public static async Task<OutgoingMessages> Handle(
        PurgeExpiredWebhookDeliveries message, ScreenHireDbContext db, IOptions<WebhookOptions> options, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - options.Value.DeliveryRetention;
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new PurgeOrganizationWebhookDeliveries(id, cutoff)));
        return outgoing;
    }
}

public static partial class PurgeOrganizationWebhookDeliveriesHandler
{
    public static async Task Handle(
        PurgeOrganizationWebhookDeliveries message, ScreenHireDbContext db, ILogger<PurgeOrganizationWebhookDeliveries> logger,
        CancellationToken cancellationToken)
    {
        var purged = await db.WebhookDeliveries
            .Where(d => d.Status != WebhookDeliveryStatus.Pending && d.CompletedAt < message.Cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        LogPurged(logger, message.OrganizationId, purged);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: purged {Deliveries} webhook deliveries")]
    private static partial void LogPurged(ILogger logger, Guid organizationId, int deliveries);
}
