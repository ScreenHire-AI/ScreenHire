using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace ScreenHire.Api.Applications;

/// <summary>Short-lived signed links to one blob, for uploads that go straight from a browser to storage.</summary>
internal static class BlobLinks
{
    /// <summary>
    /// A link that creates <paramref name="blob"/> once, until <paramref name="expiresAt"/>. Create only, not Write: the
    /// link cannot replace the blob after the API has checked it.
    /// </summary>
    public static async Task<Uri> CreateOnlyAsync(
        BlobServiceClient blobs, BlobClient blob, DateTimeOffset expiresAt, TimeProvider time, CancellationToken cancellationToken)
    {
        var sas = new BlobSasBuilder(BlobSasPermissions.Create, expiresAt)
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",
            // The local emulator (Azurite) is plain http; real storage accounts are reached over https only.
            Protocol = blob.Uri.Scheme == Uri.UriSchemeHttps ? SasProtocol.Https : SasProtocol.HttpsAndHttp,
        };

        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sas); // a shared key: the emulator, or a key-based connection string
        }

        // A managed identity has no key to sign with; a user delegation key lasts as long as the link.
        var key = await blobs.GetUserDelegationKeyAsync(time.GetUtcNow().AddMinutes(-5), expiresAt, cancellationToken);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key.Value, blobs.AccountName) }.ToUri();
    }

    /// <summary>The headers a browser sends with a create-only link's PUT.</summary>
    public static IReadOnlyDictionary<string, string> PutHeaders { get; } = new Dictionary<string, string> { ["x-ms-blob-type"] = "BlockBlob" };
}
