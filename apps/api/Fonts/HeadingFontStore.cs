using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace ScreenHire.Api.Fonts;

/// <summary>
/// The private <c>heading-fonts</c> container (<see cref="FontsOptions.Container"/>). A hosted family's files are
/// <c>{id}/{version}/{file}</c>, its licence <c>{id}/{version}/LICENSE.txt</c>, and a picker preview
/// <c>previews/{id}.woff2</c>. Names hold only checked ids and versions, and a name's bytes never change once written.
/// </summary>
internal sealed class HeadingFontStore(BlobServiceClient blobs, IOptions<FontsOptions> options)
{
    private BlobContainerClient Container => blobs.GetBlobContainerClient(options.Value.Container);

    public static string FileBlob(string id, string version, string fileName) => $"{id}/{version}/{fileName}";

    public static string PreviewBlob(string id) => $"previews/{id}.woff2";

    /// <summary>Writes <paramref name="bytes"/> at <paramref name="name"/>, replacing a partial write from an earlier attempt.</summary>
    public async Task WriteAsync(string name, byte[] bytes, string contentType, CancellationToken cancellationToken) =>
        await Container.GetBlobClient(name).UploadAsync(
            BinaryData.FromBytes(bytes), new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);

    /// <summary>The blob's content, or null when there is none.</summary>
    public async Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            return (await Container.GetBlobClient(name).DownloadStreamingAsync(cancellationToken: cancellationToken)).Value.Content;
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    /// <summary>Development creates the container at start; production's is created with the deployment (owner action).</summary>
    public Task ProvisionDevelopmentStorageAsync(CancellationToken cancellationToken = default) =>
        Container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
}
