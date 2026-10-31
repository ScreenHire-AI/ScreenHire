using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Applications;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Branding;

/// <summary>
/// A brand image an Owner or Admin uploaded needs a malware scan before it can be saved into the branding or served.
/// Published with the upload's check, through the outbox. <paramref name="Container"/> is the brand images container.
/// </summary>
public sealed record ScanBrandImage(Guid OrganizationId, Guid ImageId, string Container) : ITenantMessage;

/// <summary>
/// Runs in the worker: scans the image and records the result on it. An image is scanned once, so a redelivered message
/// changes nothing; an image that is gone (its organization was deleted) has nothing to scan.
/// </summary>
public static partial class ScanBrandImageHandler
{
    public static async Task Handle(
        ScanBrandImage message,
        ScreenHireDbContext db,
        IMalwareScanner scanner,
        TimeProvider time,
        ILogger<ScanBrandImage> logger,
        CancellationToken cancellationToken)
    {
        var image = await db.BrandImages.SingleOrDefaultAsync(i => i.Id == message.ImageId, cancellationToken);
        if (image is null || image.ScanStatus != MalwareScanStatus.Pending)
        {
            return;
        }

        var status = await scanner.ScanAsync(message.Container, image.BlobName, cancellationToken);
        image.RecordScan(status, time.GetUtcNow()); // saved with the handler's transaction (AutoApplyTransactions)
        LogScanned(logger, image.Id, status);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Brand image {ImageId} scanned: {Status}")]
    private static partial void LogScanned(ILogger logger, Guid imageId, MalwareScanStatus status);
}
