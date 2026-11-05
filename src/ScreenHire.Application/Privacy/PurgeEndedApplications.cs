using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Candidates;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Privacy;

/// <summary>
/// The daily record retention run (docs/architecture/data-requests.md, "Retention"). It has no tenant of its own, and
/// row-level security shows the worker no tenant rows without one, so it fans out one
/// <see cref="PurgeOrganizationEndedApplications"/> per organization. Each organization decides for itself: with retention
/// off (the default) its message does nothing.
/// </summary>
public sealed record PurgeEndedApplications;

/// <summary>Deletes one organization's ended applications older than its retention period at <paramref name="Now"/>, one batch at a time.</summary>
public sealed record PurgeOrganizationEndedApplications(Guid OrganizationId, DateTimeOffset Now) : ITenantMessage;

public static class PurgeEndedApplicationsHandler
{
    public static async Task<OutgoingMessages> Handle(
        PurgeEndedApplications message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new PurgeOrganizationEndedApplications(id, now)));
        return outgoing;
    }
}

/// <summary>
/// Reads the organization's own setting, selects with the same query as the dashboard's preview, and erases up to
/// <see cref="RecordRetentionOptions.BatchSize"/> applications through <see cref="CandidateErasure"/> in the handler's
/// transaction. Its outgoing messages are the erasure's (files, calendar events, duplicate checks, <c>CandidateErased</c>
/// for each candidate left with nothing) and, while more remain, itself again. A re-run finds nothing, so it is idempotent.
/// The team log gets a <c>RetentionApplied</c> entry with the count, and nothing about the people.
/// </summary>
public static partial class PurgeOrganizationEndedApplicationsHandler
{
    public static async Task<OutgoingMessages> Handle(
        PurgeOrganizationEndedApplications message,
        ScreenHireDbContext db,
        CandidateErasure erasure,
        IOptions<RecordRetentionOptions> options,
        ILogger<PurgeOrganizationEndedApplications> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var settings = await db.RetentionSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (settings?.Cutoff(message.Now) is not { } cutoff)
        {
            return outgoing;
        }

        // The worker's tenant sees every job, so the selection is the whole organization's, as the preview's is.
        var selection = await RetentionSelection.FindAsync(db, cutoff, cancellationToken);
        if (selection.ApplicationIds.Count == 0)
        {
            return outgoing;
        }

        var batch = selection.ApplicationIds.Take(Math.Max(1, options.Value.BatchSize)).ToList();
        var erased = await erasure.EraseApplicationsAsync(db, batch, message.Now, cancellationToken);
        if (erased.ApplicationsDeleted == 0)
        {
            return outgoing;
        }

        db.MembershipEvents.Add(new MembershipEvent
        {
            OrganizationId = message.OrganizationId,
            Kind = MembershipEventKind.RetentionApplied,
            ActorUserId = null,
            ItemCount = erased.ApplicationsDeleted,
            OccurredAt = message.Now,
        });

        foreach (var erasureMessage in erased.Messages)
        {
            outgoing.Add(erasureMessage);
        }


        outgoing.Add(new ApplicationsRemoved(message.OrganizationId, erased.ApplicationsDeleted));

        // More are waiting: the next batch is its own message, so no single transaction grows without bound.
        if (selection.ApplicationIds.Count > batch.Count)
        {
            outgoing.Add(message);
        }

        LogApplied(logger, message.OrganizationId, erased.ApplicationsDeleted, erased.CandidatesErased);
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: retention deleted {Applications} ended applications and erased {Candidates} candidates left with none")]
    private static partial void LogApplied(ILogger logger, Guid organizationId, int applications, int candidates);
}
