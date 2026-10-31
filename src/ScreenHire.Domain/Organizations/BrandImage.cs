using ScreenHire.Domain.Applications;

namespace ScreenHire.Domain.Organizations;

/// <summary>What a brand image is for, which decides its limits (<see cref="BrandImageRules"/>).</summary>
public enum BrandImageKind
{
    /// <summary>At the top of the careers pages and of every email to candidates.</summary>
    Logo,

    /// <summary>The careers pages' icon in a browser tab.</summary>
    Favicon,

    /// <summary>The picture a link to the careers pages shows when someone shares it (Open Graph).</summary>
    SharingImage,
}

/// <summary>
/// An image an Owner or Admin uploaded on Settings, Branding: a PNG or JPEG in the brand images container, checked for
/// its type, size and pixel size when it was uploaded, then scanned for malware. It goes live only when someone saves
/// it into the organization's branding, and it is served only while its scan allows (<see cref="IsServable"/>). The
/// bytes never change under its id, so its address can be cached for good.
/// </summary>
public sealed class BrandImage : ITenantOwned
{
    /// <summary>The upload's id, random rather than time-ordered: it is part of the image's public address.</summary>
    public Guid Id { get; init; }

    public Guid OrganizationId { get; init; }

    public BrandImageKind Kind { get; init; }

    /// <summary>Where the bytes are, within the brand images container.</summary>
    public required string BlobName { get; init; }

    /// <summary><c>image/png</c> or <c>image/jpeg</c>, from the file's first bytes.</summary>
    public required string ContentType { get; init; }

    public long SizeBytes { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public MalwareScanStatus ScanStatus { get; private set; } = MalwareScanStatus.Pending;

    public DateTimeOffset? ScannedAt { get; private set; }

    public DateTimeOffset UploadedAt { get; init; }

    public Guid? UploadedByUserId { get; init; }

    /// <summary>
    /// Clean, or not scanned because no scanner is configured, as applicant files are offered (docs/architecture/branding.md).
    /// A pending or infected image is never served or saved.
    /// </summary>
    public bool IsServable => ScanStatus is MalwareScanStatus.Clean or MalwareScanStatus.NotScanned;

    /// <summary>The file extension its public address ends with.</summary>
    public string Extension => ContentType == "image/png" ? "png" : "jpg";

    /// <summary>
    /// Its public address on the organization's careers board, <paramref name="careersBoardUrl"/>: the careers pages
    /// serve it there from the brand images container. The same address for every page, email and person.
    /// </summary>
    public string UrlUnder(string careersBoardUrl)
    {
        ArgumentNullException.ThrowIfNull(careersBoardUrl);
        return $"{careersBoardUrl.TrimEnd('/')}/brand/images/{Id:D}.{Extension}";
    }

    /// <summary>Records a scan's result, once; a second result changes nothing and returns false.</summary>
    public bool RecordScan(MalwareScanStatus status, DateTimeOffset now)
    {
        if (status == MalwareScanStatus.Pending)
        {
            throw new ArgumentException("A scan result cannot be Pending.", nameof(status));
        }

        if (ScanStatus != MalwareScanStatus.Pending)
        {
            return false;
        }

        ScanStatus = status;
        ScannedAt = now;
        return true;
    }
}

/// <summary>
/// The limits on each kind of brand image (ADR-0018): PNG or JPEG only, never SVG, a size in bytes, and a pixel size.
/// Every side is at most 4,096 pixels, so a small file cannot unpack into an enormous picture on a candidate's device.
/// </summary>
public sealed record BrandImageRules(BrandImageKind Kind, long MaxBytes, int MinWidth, int MinHeight, bool Square)
{
    public const int MaxSide = 4096;

    public static BrandImageRules For(BrandImageKind kind) => kind switch
    {
        BrandImageKind.Logo => new(kind, 1024 * 1024, 1, 1, false),
        BrandImageKind.Favicon => new(kind, 256 * 1024, 32, 32, true),
        _ => new(kind, 5 * 1024 * 1024, 600, 315, false),
    };

    /// <summary>The kind's name in a sentence: "logo", "favicon", "sharing image".</summary>
    public string Noun => Kind switch
    {
        BrandImageKind.Logo => "logo",
        BrandImageKind.Favicon => "favicon",
        _ => "sharing image",
    };

    /// <summary>"1 MB", "256 KB": the size limit as the page says it.</summary>
    public string MaxSize => MaxBytes >= 1024 * 1024 ? $"{MaxBytes / (1024 * 1024)} MB" : $"{MaxBytes / 1024} KB";

    /// <summary>What is wrong with an image of this size, or null when it fits.</summary>
    public string? Problem(long sizeBytes, int width, int height)
    {
        if (sizeBytes > MaxBytes)
        {
            return $"This file is larger than {MaxSize}.";
        }

        if (width > MaxSide || height > MaxSide)
        {
            return $"This image is {width} by {height} pixels. Each side can be at most {MaxSide} pixels.";
        }

        if (Square && width != height)
        {
            return $"A favicon is square, and this image is {width} by {height} pixels.";
        }

        return width < MinWidth || height < MinHeight
            ? $"A {Noun} is at least {MinWidth} by {MinHeight} pixels, and this image is {width} by {height}."
            : null;
    }
}
