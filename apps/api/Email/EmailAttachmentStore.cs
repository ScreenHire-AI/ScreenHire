using System.Net.Mime;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using ScreenHire.Application.Applications;

namespace ScreenHire.Api.Email;

/// <summary>
/// Where the attachments of a candidate's reply are kept: a private blob container (Azurite locally) that only the API
/// writes and the worker reads for the malware scan. A reviewer downloads one through a short-lived read link, and only
/// after the scan says it is not infected, as with an applicant's upload.
/// </summary>
internal sealed class EmailAttachmentStore(BlobServiceClient blobs, IConfiguration configuration, TimeProvider time)
{
    /// <summary>How long a reviewer's download link lasts.</summary>
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    private BlobContainerClient Container => blobs.GetBlobContainerClient(EmailAttachmentsContainer.From(configuration));

    public async Task SaveAsync(string blobName, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        await Container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        using var stream = new MemoryStream(content, writable: false);
        await Container.GetBlobClient(blobName).UploadAsync(
            stream, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
    }

    /// <summary>A read-only link to one stored attachment that saves it under <paramref name="fileName"/> rather than opening it.</summary>
    public async Task<Uri> IssueReadLinkAsync(string blobName, string fileName, CancellationToken cancellationToken)
    {
        var blob = Container.GetBlobClient(blobName);
        var expiresAt = time.GetUtcNow() + LinkLifetime;
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, expiresAt)
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",
            Protocol = blob.Uri.Scheme == Uri.UriSchemeHttps ? SasProtocol.Https : SasProtocol.HttpsAndHttp,
            ContentDisposition = new ContentDisposition { DispositionType = "attachment", FileName = fileName }.ToString(),
        };

        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sas);
        }

        var key = await blobs.GetUserDelegationKeyAsync(time.GetUtcNow().AddMinutes(-5), expiresAt, cancellationToken);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key.Value, blobs.AccountName) }.ToUri();
    }

    /// <summary>Development only: the container exists before the first reply, as the applicant uploads container does.</summary>
    public Task ProvisionDevelopmentStorageAsync(CancellationToken cancellationToken = default) =>
        Container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
}
