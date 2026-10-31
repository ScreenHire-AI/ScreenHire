using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Integrity.Content;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Applications;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Applications;

/// <summary>
/// A candidate's uploaded file needs a malware scan. Published with the application, through the outbox, once per file.
/// </summary>
public sealed record ScanApplicationFile(Guid OrganizationId, Guid ApplicationId, Guid FileId) : ITenantMessage;

/// <summary>
/// Scans an uploaded file, given its container (the applicant uploads or the brand images) and where it is in it. The
/// hook for a real scanner (Microsoft Defender for Storage reports through blob index tags; a ClamAV container reads the
/// blob): until one is configured, <see cref="NoMalwareScanner"/> records that nothing scanned the file.
/// </summary>
public interface IMalwareScanner
{
    Task<MalwareScanStatus> ScanAsync(string container, string blobName, CancellationToken cancellationToken);
}

/// <summary>The local adapter: no scanner is configured, so every file is <see cref="MalwareScanStatus.NotScanned"/>.</summary>
public sealed class NoMalwareScanner : IMalwareScanner
{
    public Task<MalwareScanStatus> ScanAsync(string container, string blobName, CancellationToken cancellationToken) =>
        Task.FromResult(MalwareScanStatus.NotScanned);
}

/// <summary>
/// Runs in the worker: scans the file and records the result on it, then has it read for content forensics
/// (<see cref="ExtractApplicationDocument"/>), which skips a file the scan flagged. A file is scanned once.
/// </summary>
public static partial class ScanApplicationFileHandler
{
    public static async Task<OutgoingMessages> Handle(
        ScanApplicationFile message,
        ScreenHireDbContext db,
        IMalwareScanner scanner,
        IConfiguration configuration,
        TimeProvider time,
        ILogger<ScanApplicationFile> logger,
        CancellationToken cancellationToken)
    {
        var file = await db.ApplicationFiles.SingleOrDefaultAsync(f => f.Id == message.FileId, cancellationToken)
            ?? throw new InvalidOperationException($"File {message.FileId} is not visible to organization {message.OrganizationId}.");
        if (file.ScanStatus != MalwareScanStatus.Pending)
        {
            return []; // a redelivered message for a file already scanned, whose reading was sent with the scan
        }

        // The API's setting, as the worker's file store reads it.
        var container = configuration["ScreenHire:Apply:Container"] is { Length: > 0 } name ? name : "applicant-uploads";
        var status = await scanner.ScanAsync(container, file.BlobName, cancellationToken);
        file.RecordScan(status, time.GetUtcNow());
        LogScanned(logger, file.Id, message.ApplicationId, status);
        return [new ExtractApplicationDocument(message.OrganizationId, message.ApplicationId, file.Id)];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "File {FileId} of application {ApplicationId} scanned: {Status}")]
    private static partial void LogScanned(ILogger logger, Guid fileId, Guid applicationId, MalwareScanStatus status);
}
