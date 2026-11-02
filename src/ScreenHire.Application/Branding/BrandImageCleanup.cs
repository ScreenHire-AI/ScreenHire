using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Branding;

/// <summary>
/// The brand images container, as the worker reads it: the API's <c>ScreenHire:Branding:Container</c> setting, with the
/// same default as its <c>BrandingOptions</c>.
/// </summary>
public static class BrandImagesContainer
{
    public static string From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration["ScreenHire:Branding:Container"] is { Length: > 0 } name ? name : "brand-images";
    }
}

/// <summary>A blob in the brand images container: its name and when storage first created it.</summary>
public sealed record StoredBrandBlob(string Name, DateTimeOffset CreatedAt);

/// <summary>
/// What the clean-up of abandoned brand image uploads needs from storage: the blobs under an organization's prefix and
/// a way to delete one. The worker registers the blob storage version; <see cref="NoBrandImageStore"/> stands in where
/// no storage is configured.
/// </summary>
public interface IBrandImageStore
{
    Task<IReadOnlyList<StoredBrandBlob>> ListAsync(string prefix, CancellationToken cancellationToken);

    /// <summary>Deletes the blob; a blob that is already gone is fine.</summary>
    Task DeleteAsync(string blobName, CancellationToken cancellationToken);
}

/// <summary>The local adapter with no storage: nothing is listed and nothing is deleted.</summary>
public sealed class NoBrandImageStore : IBrandImageStore
{
    public Task<IReadOnlyList<StoredBrandBlob>> ListAsync(string prefix, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredBrandBlob>>([]);

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The daily sweep for brand images nobody saved (branding.md section 4): an upload that is still not part of the
/// organization's branding a day after it was made. It has no tenant of its own, and row-level security shows the
/// worker no tenant rows without one, so it fans out one <see cref="PurgeOrganizationBrandImageUploads"/> per
/// organization.
/// </summary>
public sealed record PurgeExpiredBrandImageUploads;

/// <summary>
/// Removes one organization's abandoned uploads made by <paramref name="Cutoff"/>: images never saved into its branding,
/// and blobs that never became an image because nobody verified them.
/// </summary>
public sealed record PurgeOrganizationBrandImageUploads(Guid OrganizationId, DateTimeOffset Cutoff) : ITenantMessage;

public static class PurgeExpiredBrandImageUploadsHandler
{
    /// <summary>How long an upload can wait to be saved before it counts as abandoned.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    public static async Task<OutgoingMessages> Handle(
        PurgeExpiredBrandImageUploads message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - Grace;
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new PurgeOrganizationBrandImageUploads(id, cutoff)));
        return outgoing;
    }
}

public static partial class PurgeOrganizationBrandImageUploadsHandler
{
    public static async Task Handle(
        PurgeOrganizationBrandImageUploads message,
        ScreenHireDbContext db,
        IBrandImageStore store,
        ILogger<PurgeOrganizationBrandImageUploads> logger,
        CancellationToken cancellationToken)
    {
        // An image someone saved into the branding keeps its SavedAt for good, because emails already sent link to it
        // even after a newer logo replaces it. Only images that never had one are abandoned.
        var abandoned = await db.BrandImages.AsNoTracking()
            .Where(i => i.SavedAt == null && i.UploadedAt <= message.Cutoff)
            .Select(i => new { i.Id, i.BlobName })
            .ToListAsync(cancellationToken);

        var images = 0;
        foreach (var image in abandoned)
        {
            // One image at a time, the row first and only while it is still unsaved: an owner who saves it between the
            // read above and this delete keeps both the row and the blob. If the blob delete fails, the handler's
            // transaction rolls the row back and the next run tries again.
            if (await db.BrandImages.Where(i => i.Id == image.Id && i.SavedAt == null).ExecuteDeleteAsync(cancellationToken) == 1)
            {
                await store.DeleteAsync(image.BlobName, cancellationToken);
                images++;
            }
        }

        // A blob nobody verified has no row at all: the dashboard asked for a link, sent the bytes and never came back.
        var known = (await db.BrandImages.AsNoTracking().Select(i => i.BlobName).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var blobs = 0;
        foreach (var blob in await store.ListAsync($"{message.OrganizationId}/", cancellationToken))
        {
            if (blob.CreatedAt <= message.Cutoff && !known.Contains(blob.Name))
            {
                await store.DeleteAsync(blob.Name, cancellationToken);
                blobs++;
            }
        }

        LogPurged(logger, message.OrganizationId, images, blobs);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: removed {Images} abandoned brand images and {Blobs} unverified uploads")]
    private static partial void LogPurged(ILogger logger, Guid organizationId, int images, int blobs);
}
