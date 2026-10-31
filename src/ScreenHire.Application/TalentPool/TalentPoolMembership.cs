using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.TalentPool;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.TalentPool;

/// <summary>
/// Someone joined or left the talent pool, or was invited from it: open dashboards refetch the pool and the
/// application they were kept from. <see cref="ApplicationId"/> is that application.
/// </summary>
public sealed record TalentPoolChanged(Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid CandidateId, DateTimeOffset ChangedAt)
    : IJobEvent;

/// <summary>
/// Joining and leaving the talent pool, shared by the API's routes, the public link to stop being kept and the worker's
/// expiry sweep, so every way in or out keeps the same record: an entry while they are in, and the reason on the kept
/// application's Activity when they leave. Never reads verification output (ADR-0004). The caller saves.
/// </summary>
public static class TalentPoolMembership
{
    /// <summary>The organization's permission period in months: its setting, or the default.</summary>
    public static async Task<int> MonthsAsync(ScreenHireDbContext db, CancellationToken cancellationToken) =>
        TalentPoolSettings.MonthsOf(await db.TalentPoolSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken));

    /// <summary>
    /// Takes the candidate of <paramref name="entry"/> out of the pool: deletes the entry and records why on the
    /// application they were kept from (<paramref name="keptFrom"/>). Returns the event for open dashboards.
    /// </summary>
    public static TalentPoolChanged Leave(
        ScreenHireDbContext db,
        TalentPoolEntry entry,
        JobApplication keptFrom,
        TalentPoolExit reason,
        DateTimeOffset now,
        Guid? actorUserId,
        Guid? otherJobId = null,
        Guid? otherApplicationId = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(keptFrom);
        if (keptFrom.Id != entry.ApplicationId)
        {
            throw new InvalidOperationException("The entry was not kept from this application.");
        }

        db.TalentPoolEntries.Remove(entry);
        db.ApplicationEvents.Add(keptFrom.LeftTalentPool(reason, now, actorUserId, otherJobId, otherApplicationId));
        return Changed(entry, now);
    }

    /// <summary>
    /// The candidate of <paramref name="candidateId"/> applied, or was added to <paramref name="newApplication"/>'s job:
    /// they leave the pool, and the application they were kept from names the new one. Null when they were not in it.
    /// </summary>
    public static async Task<TalentPoolChanged?> LeaveForNewApplicationAsync(
        ScreenHireDbContext db,
        Guid candidateId,
        JobApplication newApplication,
        TalentPoolExit reason,
        DateTimeOffset now,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newApplication);
        if (await db.TalentPoolEntries.SingleOrDefaultAsync(e => e.CandidateId == candidateId, cancellationToken) is not { } entry)
        {
            return null;
        }

        var keptFrom = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == entry.ApplicationId, cancellationToken);
        return Leave(db, entry, keptFrom, reason, now, actorUserId, newApplication.JobId, newApplication.Id);
    }

    /// <summary>The event for a change to <paramref name="entry"/>'s place in the pool.</summary>
    public static TalentPoolChanged Changed(TalentPoolEntry entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new TalentPoolChanged(entry.OrganizationId, entry.ApplicationId, entry.JobId, entry.CandidateId, now);
    }
}
