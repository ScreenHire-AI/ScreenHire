using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Applications;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.TalentPool;

/// <summary>
/// The daily sweep that takes people out of the talent pool once their permission has run out
/// (docs/architecture/talent-pool.md). It has no tenant of its own, and row-level security shows the worker no tenant rows
/// without one, so it fans out one <see cref="ExpireOrganizationTalentPool"/> per organization, as the integrity data
/// purge does.
/// </summary>
public sealed record ExpireTalentPool;

/// <summary>Takes out of one organization's pool everyone whose permission ended by <paramref name="Now"/>.</summary>
public sealed record ExpireOrganizationTalentPool(Guid OrganizationId, DateTimeOffset Now) : ITenantMessage;

public static class ExpireTalentPoolHandler
{
    public static async Task<OutgoingMessages> Handle(ExpireTalentPool message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new ExpireOrganizationTalentPool(id, now)));
        return outgoing;
    }
}

/// <summary>
/// Leaves the candidate's kept application as it is: it has ended, and its data follows the organization's normal
/// retention. Only the pool entry goes, with "permission ran out" on the application's Activity. Idempotent: a
/// redelivery finds no entry to remove.
/// </summary>
public static partial class ExpireOrganizationTalentPoolHandler
{
    public static async Task<OutgoingMessages> Handle(
        ExpireOrganizationTalentPool message,
        ScreenHireDbContext db,
        ILogger<ExpireOrganizationTalentPool> logger,
        CancellationToken cancellationToken)
    {
        var months = await TalentPoolMembership.MonthsAsync(db, cancellationToken);
        // A pool is a few hundred people at most, so the rule runs here rather than as date arithmetic in SQL.
        var kept = await db.TalentPoolEntries
            .Join(db.Applications.AsNoTracking(), e => e.ApplicationId, a => a.Id, (entry, application) => new { entry, application })
            .ToListAsync(cancellationToken);

        var outgoing = new OutgoingMessages();
        foreach (var row in kept.Where(r => !r.application.AllowsFutureRoles(months, message.Now)))
        {
            outgoing.Add(TalentPoolMembership.Leave(db, row.entry, row.application, TalentPoolExit.Expired, message.Now, actorUserId: null));
        }

        LogExpired(logger, message.OrganizationId, outgoing.Count);
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: {Count} people left the talent pool as their permission ran out")]
    private static partial void LogExpired(ILogger logger, Guid organizationId, int count);
}
