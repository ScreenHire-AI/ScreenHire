using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using ScreenHire.Application.Privacy;

namespace ScreenHire.Worker;

/// <summary>
/// Deletes copies of candidates' data from the private data exports container (<c>ScreenHire:DataRequests:ExportContainer</c>)
/// when their candidate is erased. A blob that is already gone is not an error, so a redelivered message does no harm.
/// </summary>
internal sealed class BlobDataExportStore(BlobServiceClient blobs, IConfiguration configuration) : IDataExportStore
{
    private readonly string container = DataExportsContainer.From(configuration);

    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken) =>
        await blobs.GetBlobContainerClient(container).GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
}
