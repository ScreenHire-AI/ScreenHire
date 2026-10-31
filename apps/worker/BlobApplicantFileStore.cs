using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Verification.Content;

namespace ScreenHire.Worker;

/// <summary>
/// Reads candidates' uploads from the applicant uploads container (<c>ScreenHire:Apply:Container</c>, the API's setting),
/// for content forensics. Read only: the worker never writes or deletes an upload. A missing blob throws, so the message
/// retries and then dead-letters rather than recording a file as read when it was not.
/// </summary>
internal sealed class BlobApplicantFileStore(BlobServiceClient blobs, IConfiguration configuration) : IApplicantFileStore
{
    private readonly string container = ApplicantUploadsContainer.From(configuration);

    public async Task<StoredFile> ReadAsync(string blobName, long maxBytes, CancellationToken cancellationToken)
    {
        var blob = blobs.GetBlobContainerClient(container).GetBlobClient(blobName);
        var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
        if (properties.Value.ContentLength > maxBytes)
        {
            return StoredFile.NotRead("The file is too large to read.");
        }

        var content = await blob.DownloadContentAsync(cancellationToken);
        return new StoredFile(content.Value.Content.ToArray());
    }
}
