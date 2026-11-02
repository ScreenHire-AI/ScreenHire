using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using ScreenHire.Application.Branding;

namespace ScreenHire.Worker;

/// <summary>
/// The brand images container (<c>ScreenHire:Branding:Container</c>, the API's setting) for the daily clean-up of uploads
/// nobody saved. It lists one organization's blobs and deletes the abandoned ones; it never writes or reads an image.
/// </summary>
internal sealed class BlobBrandImageStore(BlobServiceClient blobs, IConfiguration configuration) : IBrandImageStore
{
    private readonly BlobContainerClient container = blobs.GetBlobContainerClient(BrandImagesContainer.From(configuration));

    public async Task<IReadOnlyList<StoredBrandBlob>> ListAsync(string prefix, CancellationToken cancellationToken)
    {
        var found = new List<StoredBrandBlob>();
        try
        {
            await foreach (var item in container.GetBlobsAsync(new GetBlobsOptions { Prefix = prefix }, cancellationToken))
            {
                // A blob always has a creation time; the fallback keeps it too new to delete rather than guessing.
                found.Add(new StoredBrandBlob(item.Name, item.Properties.CreatedOn ?? DateTimeOffset.MaxValue));
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // The API creates the container when it starts; before that no upload has been made, so nothing is abandoned.
        }

        return found;
    }

    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken) =>
        await container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
}
