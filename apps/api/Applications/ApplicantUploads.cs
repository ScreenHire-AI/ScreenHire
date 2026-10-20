using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Api.Applications;

/// <summary>
/// Candidate uploads go straight from the candidate (or the careers server, when the browser runs no JavaScript) to
/// blob storage through a short-lived, create-only link this class issues, so file bytes never pass through the API.
/// Nothing is stored until the application is submitted, when <see cref="CheckAsync"/> confirms the blob.
/// </summary>
/// <remarks>
/// A blob's name is <c>{organizationId}/{jobId}/{questionId}/{uploadId}/{fileName}</c>. Every lookup builds the prefix
/// from the organization and job in the route, so an upload id from another organization or job is simply not found.
/// The link cannot cap the size of what is written, so <see cref="CheckAsync"/> deletes anything too large or of the
/// wrong type, and uploads never claimed by an application are left to the container's lifecycle rule
/// (docs/architecture/apply-flow.md).
/// </remarks>
internal sealed class ApplicantUploads(BlobServiceClient blobs, IOptions<ApplyOptions> options, TimeProvider time)
{
    private ApplyOptions Settings => options.Value;

    private BlobContainerClient Container => blobs.GetBlobContainerClient(Settings.Container);

    /// <summary>The origin the links point at, for the careers pages' <c>connect-src</c>.</summary>
    public string Origin => blobs.Uri.GetLeftPart(UriPartial.Authority);

    public long MaxBytes => Settings.MaxUploadBytes;

    /// <summary>A create-only link for one new blob, valid for <see cref="ApplyOptions.UploadLinkLifetime"/>.</summary>
    public async Task<UploadTicket> IssueAsync(
        Guid organizationId, Guid jobId, string questionId, string safeFileName, CancellationToken cancellationToken)
    {
        var uploadId = Guid.NewGuid(); // random, not time-ordered: the id is what lets a later request claim the file
        var blob = Container.GetBlobClient(Prefix(organizationId, jobId, questionId, uploadId) + safeFileName);
        var expiresAt = time.GetUtcNow() + Settings.UploadLinkLifetime;
        // Create only, not Write: the link writes the blob once and cannot replace it after CheckAsync has passed it.
        var sas = new BlobSasBuilder(BlobSasPermissions.Create, expiresAt)
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",
            // The local emulator (Azurite) is plain http; real storage accounts are reached over https only.
            Protocol = blob.Uri.Scheme == Uri.UriSchemeHttps ? SasProtocol.Https : SasProtocol.HttpsAndHttp,
        };

        Uri url;
        if (blob.CanGenerateSasUri)
        {
            url = blob.GenerateSasUri(sas); // a shared key: the emulator, or a key-based connection string
        }
        else
        {
            // A managed identity has no key to sign with; a user delegation key lasts as long as the link.
            var key = await blobs.GetUserDelegationKeyAsync(time.GetUtcNow().AddMinutes(-5), expiresAt, cancellationToken);
            url = new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key.Value, blobs.AccountName) }.ToUri();
        }

        return new UploadTicket(uploadId, url, "PUT", new Dictionary<string, string> { ["x-ms-blob-type"] = "BlockBlob" }, expiresAt, MaxBytes);
    }

    /// <summary>
    /// Finds the blob an upload id refers to and checks it: no larger than <see cref="MaxBytes"/>, an extension
    /// <paramref name="question"/> accepts, and content that starts the way that type does. A file that fails is
    /// deleted, so the candidate can only try again with another upload.
    /// </summary>
    public async Task<UploadCheck> CheckAsync(
        Guid organizationId, Guid jobId, Question question, Guid uploadId, CancellationToken cancellationToken)
    {
        var prefix = Prefix(organizationId, jobId, question.Id, uploadId);
        BlobItem? item = null;
        await foreach (var found in Container.GetBlobsAsync(new GetBlobsOptions { Prefix = prefix }, cancellationToken))
        {
            item = found;
            break;
        }

        if (item is null)
        {
            return UploadCheck.Missing;
        }

        var blob = Container.GetBlobClient(item.Name);
        var fileName = item.Name[prefix.Length..];
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var size = item.Properties.ContentLength ?? 0;
        if (size > MaxBytes)
        {
            await DeleteAsync(blob, cancellationToken);
            return UploadCheck.Failed(FileTypes.TooLarge(MaxBytes));
        }

        if (size == 0)
        {
            await DeleteAsync(blob, cancellationToken);
            return UploadCheck.Failed("This file is empty.");
        }

        var accepted = FileTypes.AcceptedBy(question.AcceptedFileTypes);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var head = new byte[FileTypes.SignatureLength];
        var headLength = 0;
        await using (var content = await blob.OpenReadAsync(new BlobOpenReadOptions(allowModifications: false), cancellationToken))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                var take = Math.Min(read, head.Length - headLength);
                buffer.AsSpan(0, take).CopyTo(head.AsSpan(headLength));
                headLength += take;
                hash.AppendData(buffer, 0, read);
            }
        }

        if (!accepted.Contains(extension) || !FileTypes.Matches(extension, head.AsSpan(0, headLength)))
        {
            await DeleteAsync(blob, cancellationToken);
            return UploadCheck.Failed($"This file is not a {FileTypes.Describe(accepted)} file. Upload a file of one of those types.");
        }

        return UploadCheck.Ok(new VerifiedUpload(
            question.Id, uploadId, item.Name, fileName, FileTypes.ContentTypeOf(extension)!, size,
            Convert.ToHexStringLower(hash.GetHashAndReset())));
    }

    /// <summary>
    /// Local development only (the synthetic-applicant demo): stores <paramref name="content"/> as a new upload for
    /// <paramref name="question"/> and checks it exactly as a candidate's upload is checked.
    /// </summary>
    public async Task<VerifiedUpload> StoreDevelopmentFileAsync(
        Guid organizationId, Guid jobId, Question question, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        var uploadId = Guid.NewGuid();
        var blob = Container.GetBlobClient(Prefix(organizationId, jobId, question.Id, uploadId) + (FileTypes.SafeFileName(fileName) ?? "file.pdf"));
        await blob.UploadAsync(new BinaryData(content), cancellationToken);
        var check = await CheckAsync(organizationId, jobId, question, uploadId, cancellationToken);
        return check.Upload ?? throw new InvalidOperationException($"The demo file {fileName} failed the upload checks: {check.Problem}");
    }

    /// <summary>
    /// Local development and tests only: creates the container and lets browsers upload to the emulator from any origin
    /// (the upload link is the credential). A real storage account gets both from its deployment.
    /// </summary>
    public async Task ProvisionDevelopmentStorageAsync(CancellationToken cancellationToken = default)
    {
        await Container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        var properties = (await blobs.GetPropertiesAsync(cancellationToken)).Value;
        properties.Cors =
        [
            new BlobCorsRule
            {
                AllowedOrigins = "*",
                AllowedMethods = "PUT,OPTIONS",
                AllowedHeaders = "*",
                ExposedHeaders = "*",
                MaxAgeInSeconds = 3600,
            },
        ];
        await blobs.SetPropertiesAsync(properties, cancellationToken);
    }

    private static string Prefix(Guid organizationId, Guid jobId, string questionId, Guid uploadId) =>
        $"{organizationId}/{jobId}/{questionId}/{uploadId}/";

    private static async Task DeleteAsync(BlobClient blob, CancellationToken cancellationToken)
    {
        try
        {
            await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException)
        {
            // The check's answer stands either way; an undeleted blob is left to the lifecycle rule.
        }
    }
}

/// <summary>An upload that passed <see cref="ApplicantUploads.CheckAsync"/>.</summary>
internal sealed record VerifiedUpload(
    string QuestionId, Guid UploadId, string BlobName, string FileName, string ContentType, long SizeBytes, string Sha256);

/// <summary>The outcome of checking an upload: missing, failed with a message for the candidate, or verified.</summary>
internal sealed record UploadCheck(VerifiedUpload? Upload, string? Problem, bool Found)
{
    public static UploadCheck Missing { get; } = new(null, null, false);

    public static UploadCheck Failed(string problem) => new(null, problem, true);

    public static UploadCheck Ok(VerifiedUpload upload) => new(upload, null, true);
}
