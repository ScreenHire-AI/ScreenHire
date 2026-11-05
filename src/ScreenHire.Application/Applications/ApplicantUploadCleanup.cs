using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Verification.Content;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Applications;

/// <summary>
/// The daily sweep for files uploaded along the way to an application that was never sent (apply-flow.md section 3): a
/// candidate's file sits in storage from the moment it is uploaded, and only a submitted application gets a row for it
/// (<c>ApplicationFile</c>). A day after the upload the privacy page promises it is gone. It has no tenant of its own, and
/// row-level security shows the worker no tenant rows without one, so it fans out one
/// <see cref="PurgeOrganizationApplicantUploads"/> per organization. A storage lifecycle rule cannot do this: it could not
/// tell a claimed file from an unclaimed one, and would delete submitted resumes too.
/// </summary>
public sealed record PurgeExpiredApplicantUploads;

/// <summary>Removes one organization's uploads made by <paramref name="Cutoff"/> that no application claimed.</summary>
public sealed record PurgeOrganizationApplicantUploads(Guid OrganizationId, DateTimeOffset Cutoff) : ITenantMessage;

public static class UnclaimedUploads
{
    /// <summary>How long an upload can wait for its application to be sent before it counts as abandoned.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    /// <summary>The blobs made by <paramref name="cutoff"/> that no application file row names.</summary>
    public static IReadOnlyList<string> Select(IEnumerable<StoredUploadBlob> blobs, IReadOnlySet<string> claimed, DateTimeOffset cutoff)
    {
        ArgumentNullException.ThrowIfNull(blobs);
        ArgumentNullException.ThrowIfNull(claimed);
        return [.. blobs.Where(blob => blob.CreatedAt <= cutoff && !claimed.Contains(blob.Name)).Select(blob => blob.Name)];
    }
}

public static class PurgeExpiredApplicantUploadsHandler
{
    public static async Task<OutgoingMessages> Handle(
        PurgeExpiredApplicantUploads message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - UnclaimedUploads.Grace;
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new PurgeOrganizationApplicantUploads(id, cutoff)));
        return outgoing;
    }
}

public static partial class PurgeOrganizationApplicantUploadsHandler
{
    // Idempotent: deleting a blob that is already gone is a no-op, so a redelivery does no harm.
    public static async Task Handle(
        PurgeOrganizationApplicantUploads message,
        ScreenHireDbContext db,
        IApplicantFileStore store,
        ILogger<PurgeOrganizationApplicantUploads> logger,
        CancellationToken cancellationToken)
    {
        var claimed = (await db.ApplicationFiles.AsNoTracking().Select(f => f.BlobName).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var blobs = await store.ListAsync($"{message.OrganizationId}/", cancellationToken);

        var removed = 0;
        foreach (var name in UnclaimedUploads.Select(blobs, claimed, message.Cutoff))
        {
            // Asked again just before the delete: a candidate who sends the application between the read above and now
            // keeps the file. Only a sweep that runs while someone submits a day-old upload could meet this.
            if (await db.ApplicationFiles.AnyAsync(f => f.BlobName == name, cancellationToken))
            {
                continue;
            }

            await store.DeleteAsync(name, cancellationToken);
            removed++;
        }

        LogPurged(logger, message.OrganizationId, removed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: removed {Files} uploads that no application claimed")]
    private static partial void LogPurged(ILogger logger, Guid organizationId, int files);
}
