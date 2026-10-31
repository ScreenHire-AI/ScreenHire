using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Applications;

/// <summary>
/// The daily sweep for answers saved to finish later (apply-flow.md §7 and §8): drafts of the apply form, and a
/// follow-up questionnaire's saved answers, 30 days after their first save. It has no tenant of its own, and row-level
/// security shows the worker no tenant rows without one, so it fans out one
/// <see cref="PurgeOrganizationApplicationDrafts"/> per organization.
/// </summary>
public sealed record PurgeExpiredApplicationDrafts;

/// <summary>Deletes one organization's saved answers whose time ran out by <paramref name="Cutoff"/>.</summary>
public sealed record PurgeOrganizationApplicationDrafts(Guid OrganizationId, DateTimeOffset Cutoff) : ITenantMessage;

public static class PurgeExpiredApplicationDraftsHandler
{
    public static async Task<OutgoingMessages> Handle(
        PurgeExpiredApplicationDrafts message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new PurgeOrganizationApplicationDrafts(id, now)));
        return outgoing;
    }
}

public static partial class PurgeOrganizationApplicationDraftsHandler
{
    public static async Task Handle(
        PurgeOrganizationApplicationDrafts message,
        ScreenHireDbContext db,
        ILogger<PurgeOrganizationApplicationDrafts> logger,
        CancellationToken cancellationToken)
    {
        var drafts = await db.ApplicationDrafts.Where(d => d.ExpiresAt <= message.Cutoff).ExecuteDeleteAsync(cancellationToken);

        // Through the domain method, not ExecuteUpdate: the request clears all three draft members together.
        var requests = await db.QuestionnaireRequests.Where(r => r.DraftExpiresAt != null && r.DraftExpiresAt <= message.Cutoff)
            .ToListAsync(cancellationToken);
        foreach (var request in requests)
        {
            request.ClearDraft();
        }

        await db.SaveChangesAsync(cancellationToken);
        LogPurged(logger, message.OrganizationId, drafts, requests.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: purged {Drafts} application drafts and {FollowUps} follow-up drafts")]
    private static partial void LogPurged(ILogger logger, Guid organizationId, int drafts, int followUps);
}
