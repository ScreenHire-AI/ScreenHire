using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Verification.Content;

namespace ScreenHire.Application.Applications;

/// <summary>
/// Deletes an erased candidate's uploaded files from storage. The erase request removes the database rows in its own
/// transaction and sends this in the same one, so the blobs are never left behind by a crash between the two. The blob names
/// are paths that can hold a file name, so the message carries them only until the handler has deleted them.
/// </summary>
public sealed record EraseApplicantFiles(Guid OrganizationId, IReadOnlyList<string> BlobNames) : ITenantMessage;

public static partial class EraseApplicantFilesHandler
{
    // Idempotent without a key: deleting a blob that is already gone is a no-op, so a redelivery does no harm.
    public static async Task Handle(
        EraseApplicantFiles message, IApplicantFileStore files, ILogger<EraseApplicantFiles> logger, CancellationToken cancellationToken)
    {
        foreach (var name in message.BlobNames.Distinct(StringComparer.Ordinal))
        {
            await files.DeleteAsync(name, cancellationToken);
        }

        LogErased(logger, message.OrganizationId, message.BlobNames.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: deleted {Files} files of an erased candidate")]
    private static partial void LogErased(ILogger logger, Guid organizationId, int files);
}
