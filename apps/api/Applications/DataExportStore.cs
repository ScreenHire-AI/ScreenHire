using System.Net.Mime;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using ScreenHire.Application.Privacy;

namespace ScreenHire.Api.Applications;

/// <summary>
/// The API's half of the store of copies of candidates' data: a short read link for the download page, issued the way
/// <see cref="ApplicantUploads.IssueReadLinkAsync"/> issues one (a SAS from the account key on the emulator, from a
/// user delegation key against a real account). The worker writes and deletes the blobs, so the API refuses both.
/// </summary>
internal sealed class DataExportStore(BlobServiceClient blobs, IConfiguration configuration, TimeProvider time) : IDataExportStore
{
    /// <summary>How long a read link works: long enough to start the download, too short to pass around.</summary>
    public static readonly TimeSpan ReadLinkLifetime = TimeSpan.FromMinutes(5);

    private readonly string container = DataExportsContainer.From(configuration);

    public async Task<string?> IssueReadLinkAsync(string blobName, string fileName, CancellationToken cancellationToken)
    {
        var blob = blobs.GetBlobContainerClient(container).GetBlobClient(blobName);
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var now = time.GetUtcNow();
        var expiresAt = now + ReadLinkLifetime;
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, expiresAt)
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",
            Protocol = blob.Uri.Scheme == Uri.UriSchemeHttps ? SasProtocol.Https : SasProtocol.HttpsAndHttp,
            // Storage answers with this header, so the copy is saved, never rendered on the storage origin.
            ContentDisposition = new ContentDisposition { DispositionType = "attachment", FileName = fileName }.ToString(),
        };

        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sas).ToString();
        }

        var key = await blobs.GetUserDelegationKeyAsync(now.AddMinutes(-5), expiresAt, cancellationToken);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key.Value, blobs.AccountName) }.ToUri().ToString();
    }

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Only the worker deletes copies of candidates' data.");

    public Task SaveAsync(string blobName, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Only the worker writes copies of candidates' data.");
}
