using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using ScreenHire.Application.Privacy;

namespace ScreenHire.Worker;

/// <summary>
/// Writes copies of candidates' data to the private data exports container (<c>ScreenHire:DataRequests:ExportContainer</c>)
/// and deletes them when their candidate is erased or their download window ends. A blob that is already gone is not an
/// error, so a redelivered message does no harm. Read links are the API's job, not the worker's.
/// </summary>
internal sealed class BlobDataExportStore(BlobServiceClient blobs, IConfiguration configuration) : IDataExportStore
{
    private readonly string container = DataExportsContainer.From(configuration);

    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken) =>
        await blobs.GetBlobContainerClient(container).GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public async Task SaveAsync(string blobName, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        var blob = blobs.GetBlobContainerClient(container).GetBlobClient(blobName);
        await blob.UploadAsync(
            BinaryData.FromBytes(content), new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
    }

    // The emailed download link goes through the API, which issues the read link after it checks the signed token.
    public Task<string?> IssueReadLinkAsync(string blobName, string fileName, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Only the API issues read links to copies of candidates' data.");
}
