using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Applications;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Applications;

/// <summary>
/// A candidate's uploaded file needs a malware scan. Published with the application, through the outbox, once per file.
/// </summary>
public sealed record ScanApplicationFile(Guid OrganizationId, Guid ApplicationId, Guid FileId) : ITenantMessage;

/// <summary>
/// Scans a file a candidate uploaded, given where it is in the applicant uploads container. The hook for a real
/// scanner (Microsoft Defender for Storage reports through blob index tags; a ClamAV container reads the blob): until
/// one is configured, <see cref="NoMalwareScanner"/> records that nothing scanned the file.
/// </summary>
public interface IMalwareScanner
{
    Task<MalwareScanStatus> ScanAsync(string blobName, CancellationToken cancellationToken);
}

/// <summary>The local adapter: no scanner is configured, so every file is <see cref="MalwareScanStatus.NotScanned"/>.</summary>
public sealed class NoMalwareScanner : IMalwareScanner
{
    public Task<MalwareScanStatus> ScanAsync(string blobName, CancellationToken cancellationToken) =>
        Task.FromResult(MalwareScanStatus.NotScanned);
}

/// <summary>Runs in the worker: scans the file and records the result on it. A file is scanned once.</summary>
public static partial class ScanApplicationFileHandler
{
    public static async Task Handle(
        ScanApplicationFile message,
        ScreenHireDbContext db,
        IMalwareScanner scanner,
        TimeProvider time,
        ILogger<ScanApplicationFile> logger,
        CancellationToken cancellationToken)
    {
        var file = await db.ApplicationFiles.SingleOrDefaultAsync(f => f.Id == message.FileId, cancellationToken)
            ?? throw new InvalidOperationException($"File {message.FileId} is not visible to organization {message.OrganizationId}.");
        if (file.ScanStatus != MalwareScanStatus.Pending)
        {
            return; // a redelivered message for a file already scanned
        }

        var status = await scanner.ScanAsync(file.BlobName, cancellationToken);
        file.RecordScan(status, time.GetUtcNow());
        LogScanned(logger, file.Id, message.ApplicationId, status);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "File {FileId} of application {ApplicationId} scanned: {Status}")]
    private static partial void LogScanned(ILogger logger, Guid fileId, Guid applicationId, MalwareScanStatus status);
}
