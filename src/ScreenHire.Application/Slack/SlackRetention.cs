using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Slack;

/// <summary>
/// Daily: the records that Slack messages went, and the sandbox's own messages, past <see cref="SlackOptions.MessageRetention"/>.
/// The worker sees no organization's rows without a tenant, so it hands each organization's sweep to a tenant message,
/// as <c>PurgeExpiredWebhookDeliveries</c> does.
/// </summary>
public sealed record PurgeExpiredSlackMessages;

/// <summary>Deletes one organization's Slack records and sandbox messages from before <paramref name="Cutoff"/>.</summary>
public sealed record PurgeOrganizationSlackMessages(Guid OrganizationId, DateTimeOffset Cutoff) : ITenantMessage;

public static class PurgeExpiredSlackMessagesHandler
{
    public static async Task<OutgoingMessages> Handle(
        PurgeExpiredSlackMessages message, ScreenHireDbContext db, IOptions<SlackOptions> options, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - options.Value.MessageRetention;
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new PurgeOrganizationSlackMessages(id, cutoff)));
        return outgoing;
    }
}

public static partial class PurgeOrganizationSlackMessagesHandler
{
    public static async Task Handle(
        PurgeOrganizationSlackMessages message, ScreenHireDbContext db, ILogger<PurgeOrganizationSlackMessages> logger,
        CancellationToken cancellationToken)
    {
        var records = await db.SlackMessages.Where(m => m.At < message.Cutoff).ExecuteDeleteAsync(cancellationToken);
        var sandbox = await db.SlackSandboxMessages.Where(m => m.PostedAt < message.Cutoff).ExecuteDeleteAsync(cancellationToken);
        LogPurged(logger, message.OrganizationId, records, sandbox);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Organization {OrganizationId}: purged {Records} Slack message records and {Sandbox} sandbox messages")]
    private static partial void LogPurged(ILogger logger, Guid organizationId, int records, int sandbox);
}
