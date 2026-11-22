namespace ScreenHire.Api.Fonts;

/// <summary>
/// Where heading fonts come from: Fontsource in production (<see cref="FontsourceSource"/>), the bundled sandbox locally
/// and in tests (<see cref="SandboxFontSource"/>). Only the API calls it, when a person lists the catalogue, opens a
/// preview or saves a family that is not hosted yet; a candidate's browser never reaches it.
/// </summary>
internal interface IFontSource
{
    /// <summary>Every family the source has, unfiltered: <see cref="HeadingFontCatalogue"/> keeps the ones we may host.</summary>
    Task<IReadOnlyList<SourceFont>> ListAsync(CancellationToken cancellationToken);

    /// <summary>One family with its files, or null when the source has no family by that id.</summary>
    Task<SourceFontDetail?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>The bytes of one of <paramref name="font"/>'s files.</summary>
    Task<byte[]> DownloadAsync(SourceFontDetail font, SourceFontFile file, CancellationToken cancellationToken);

    /// <summary>The family's licence text, or null when its package has none (and so it is not hosted).</summary>
    Task<string?> LicenseTextAsync(SourceFontDetail font, CancellationToken cancellationToken);
}

/// <summary>
/// A family as the source lists it. <c>License</c> is an SPDX id; <c>Google</c> is whether Google Fonts publishes it;
/// <c>Styles</c> are the extra picker filters (Rounded, Slab) the source knows of, if any.
/// </summary>
internal sealed record SourceFont(
    string Id, string Family, string Category, string License, bool Google, IReadOnlyList<string> Subsets, IReadOnlyList<int> Weights,
    IReadOnlyList<string> Styles);

/// <summary>A family with everything needed to download it: its version, each subset's unicode range and its files.</summary>
internal sealed record SourceFontDetail(
    string Id, string Family, string Category, string License, bool Google, string Version, IReadOnlyDictionary<string, string> UnicodeRanges,
    IReadOnlyList<SourceFontFile> Files);

/// <summary>One upright WOFF2 file: a subset at one weight, and where the source keeps it.</summary>
internal sealed record SourceFontFile(string Subset, int Weight, Uri Url);
