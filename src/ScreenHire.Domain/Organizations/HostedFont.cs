namespace ScreenHire.Domain.Organizations;

/// <summary>
/// A Google Fonts family ScreenHire downloaded once and serves from its own storage, so candidates' browsers never ask
/// a font host for it (ADR-0031, docs/architecture/branding.md section 3). One row per family for every organization:
/// font files are public, licensed for redistribution, and the same bytes for everyone, so the row is not
/// tenant-owned. It is written once, after every file and the licence are in storage, and never changed: the files'
/// addresses are cached for a year.
/// </summary>
public sealed class HostedFont
{
    private HostedFont()
    {
        Id = Family = Category = Version = string.Empty;
        Files = [];
    }

    public HostedFont(
        string id, string family, string category, FontLicense license, string version, IReadOnlyList<HostedFontFile> files,
        DateTimeOffset hostedAt)
    {
        if (!HeadingFonts.IsValidId(id) || !HeadingFonts.IsValidFamily(family))
        {
            throw new ArgumentException("A hosted font needs a valid id and family name.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentOutOfRangeException.ThrowIfZero(files.Count, nameof(files));
        Id = id;
        Family = family;
        Category = category;
        License = license;
        Version = version;
        Files = [.. files];
        HostedAt = hostedAt;
    }

    /// <summary>The Fontsource id, such as <c>roboto-slab</c>.</summary>
    public string Id { get; private set; }

    /// <summary>The family name, such as <c>Roboto Slab</c>.</summary>
    public string Family { get; private set; }

    /// <summary>Fontsource's category: <c>sans-serif</c>, <c>serif</c>, <c>display</c>, <c>handwriting</c> or <c>monospace</c>.</summary>
    public string Category { get; private set; }

    /// <summary>The licence the family is under. Its text is stored beside the files as <see cref="LicenseFileName"/>.</summary>
    public FontLicense License { get; private set; }

    /// <summary>The release that was downloaded (Fontsource's package version).</summary>
    public string Version { get; private set; }

    /// <summary>The files in storage, one per subset and weight (or weight range, for a variable font).</summary>
    public IReadOnlyList<HostedFontFile> Files { get; private set; }

    public DateTimeOffset HostedAt { get; private set; }

    /// <summary>The licence text's file name beside the font files.</summary>
    public const string LicenseFileName = "LICENSE.txt";
}

/// <summary>
/// One WOFF2 file of a <see cref="HostedFont"/>: its subset (<c>latin</c>, <c>latin-ext</c>), its weight (<c>700</c>,
/// or <c>100 900</c> for a variable font), the unicode range the subset covers, and its file name in storage, which is
/// also the last part of its public address.
/// </summary>
public sealed record HostedFontFile(string Subset, string Weight, string UnicodeRange, string FileName, long SizeBytes);

/// <summary>The licences a hosted font may be under. Any other licence is refused (ADR-0031).</summary>
public enum FontLicense
{
    /// <summary>The SIL Open Font License 1.1.</summary>
    Ofl,

    /// <summary>The Apache License 2.0.</summary>
    Apache,
}

public static class FontLicenses
{
    /// <summary>The licence an SPDX id names, or null for one a hosted font may not be under.</summary>
    public static FontLicense? FromSpdx(string? spdx) => spdx?.Trim().ToUpperInvariant() switch
    {
        "OFL-1.1" => FontLicense.Ofl,
        "APACHE-2.0" => FontLicense.Apache,
        _ => null,
    };

    /// <summary>The licence's name, as the font picker shows it.</summary>
    public static string Name(this FontLicense license) => license switch
    {
        FontLicense.Ofl => "SIL Open Font License",
        _ => "Apache License 2.0",
    };
}
