using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Branding;

/// <summary>
/// Brand images go from the dashboard straight to the brand images container through a create-only link, as candidate
/// uploads do (<see cref="ApplicantUploads"/>), so file bytes never pass through the API on the way in. The check then
/// reads the blob back and keeps it only if it is a PNG or JPEG within its kind's limits (<see cref="BrandImageRules"/>);
/// anything else is deleted.
/// </summary>
/// <remarks>
/// A blob's name is <c>{organizationId}/{uploadId}/{kind}/{fileName}</c>. Every lookup builds the prefix from the
/// signed-in organization, so another organization's upload id is simply not found.
/// </remarks>
internal sealed class BrandImageUploads(BlobServiceClient blobs, IOptions<BrandingOptions> options, TimeProvider time)
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg"];

    public string ContainerName => options.Value.Container;

    private BlobContainerClient Container => blobs.GetBlobContainerClient(ContainerName);

    /// <summary>Whether <paramref name="fileName"/> names a type brand images may be: PNG or JPEG, never SVG.</summary>
    public static bool HasImageExtension(string fileName) => Extensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    /// <summary>A create-only link for one new image of <paramref name="kind"/>.</summary>
    public async Task<UploadTicket> IssueAsync(Guid organizationId, BrandImageKind kind, string safeFileName, CancellationToken cancellationToken)
    {
        var uploadId = Guid.NewGuid(); // random, not time-ordered: the id is the image's public address
        var blob = Container.GetBlobClient($"{organizationId}/{uploadId}/{kind}/{safeFileName}");
        var expiresAt = time.GetUtcNow() + options.Value.UploadLinkLifetime;
        var url = await BlobLinks.CreateOnlyAsync(blobs, blob, expiresAt, time, cancellationToken);
        return new UploadTicket(uploadId, url, "PUT", BlobLinks.PutHeaders, expiresAt, BrandImageRules.For(kind).MaxBytes);
    }

    /// <summary>
    /// Finds the blob <paramref name="uploadId"/> names and checks it: its kind's size limit, a PNG or JPEG extension
    /// whose first bytes match it, and a pixel size its kind allows. A blob that fails is deleted.
    /// </summary>
    public async Task<BrandImageCheck> CheckAsync(Guid organizationId, Guid uploadId, Guid? uploadedBy, CancellationToken cancellationToken)
    {
        var prefix = $"{organizationId}/{uploadId}/";
        BlobItem? item = null;
        await foreach (var found in Container.GetBlobsAsync(new GetBlobsOptions { Prefix = prefix }, cancellationToken))
        {
            item = found;
            break;
        }

        if (item is null || item.Name[prefix.Length..].Split('/') is not [var kindName, var fileName]
            || !Enum.TryParse<BrandImageKind>(kindName, out var kind))
        {
            return BrandImageCheck.Missing;
        }

        var blob = Container.GetBlobClient(item.Name);
        var rules = BrandImageRules.For(kind);
        var size = item.Properties.ContentLength ?? 0;
        if (size == 0 || size > rules.MaxBytes)
        {
            await DeleteAsync(blob, cancellationToken);
            return BrandImageCheck.Failed(size == 0 ? "This file is empty." : $"This file is larger than {rules.MaxSize}.");
        }

        var bytes = (await blob.DownloadContentAsync(cancellationToken)).Value.Content.ToArray();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!HasImageExtension(fileName) || !FileTypes.Matches(extension, bytes) || ImageDimensions.Read(bytes) is not var (width, height))
        {
            await DeleteAsync(blob, cancellationToken);
            return BrandImageCheck.Failed("This file is not a PNG or JPEG image. Upload a PNG or JPEG.");
        }

        if (rules.Problem(bytes.Length, width, height) is { } problem)
        {
            await DeleteAsync(blob, cancellationToken);
            return BrandImageCheck.Failed(problem);
        }

        return BrandImageCheck.Ok(new BrandImage
        {
            Id = uploadId,
            OrganizationId = organizationId,
            Kind = kind,
            BlobName = item.Name,
            ContentType = FileTypes.ContentTypeOf(extension)!,
            SizeBytes = bytes.Length,
            Width = width,
            Height = height,
            UploadedAt = time.GetUtcNow(),
            UploadedByUserId = uploadedBy,
        });
    }

    /// <summary>The image's bytes, to serve. Null when the blob is gone.</summary>
    public async Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken)
    {
        try
        {
            return await Container.GetBlobClient(blobName).OpenReadAsync(new BlobOpenReadOptions(allowModifications: false), cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    /// <summary>Local development and tests only: creates the private container. The account's CORS rule comes from <see cref="ApplicantUploads"/>.</summary>
    public Task ProvisionDevelopmentStorageAsync(CancellationToken cancellationToken = default) =>
        Container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

    private static async Task DeleteAsync(BlobClient blob, CancellationToken cancellationToken)
    {
        try
        {
            await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException)
        {
            // The check's answer stands either way; an undeleted blob is never served, because no row names it.
        }
    }
}

/// <summary>The outcome of checking a brand image upload: missing, failed with the reason, or a new image to keep.</summary>
internal sealed record BrandImageCheck(BrandImage? Image, string? Problem, bool Found)
{
    public static BrandImageCheck Missing { get; } = new(null, null, false);

    public static BrandImageCheck Failed(string problem) => new(null, problem, true);

    public static BrandImageCheck Ok(BrandImage image) => new(image, null, true);
}
