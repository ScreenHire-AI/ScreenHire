using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The heading fonts a person may pick (docs/architecture/branding.md section 3), sorted by name. <c>complete</c> is
/// false while the font catalogue cannot be reached: then only the six bundled fonts and the ones already hosted are
/// listed.
/// </summary>
public sealed record HeadingFontCatalogueResponse(IReadOnlyList<HeadingFontOption> Fonts, bool Complete);

/// <summary>
/// One family: <c>id</c> is what the branding saves, <c>styles</c> are the picker's filters it falls under, and
/// <c>bundled</c> is whether ScreenHire ships it (no download, and no preview route: the dashboard has it).
/// </summary>
public sealed record HeadingFontOption(string Id, string Family, IReadOnlyList<HeadingFontStyle> Styles, FontLicense License, bool Bundled);

/// <summary>The picker's style filters.</summary>
public enum HeadingFontStyle
{
    Sans,
    Serif,
    Display,
    Rounded,
    Slab,
}

/// <summary>The organization's heading font: its id and the family name people know it by.</summary>
public sealed record BrandingHeadingFont(string Id, string Family);

/// <summary>
/// The heading font as the careers pages load it. A bundled font comes from the careers bundle by its id and has no
/// files. A hosted one lists its files, each served on the careers origin at
/// <c>brand/fonts/{id}/{fileName}</c>: its subset, its weight and the unicode range the subset covers, for an
/// <c>@font-face</c> rule each. <c>category</c> picks the fallback (serif or sans-serif).
/// </summary>
public sealed record PublicHeadingFont(string Id, string Family, string Category, bool Bundled, IReadOnlyList<PublicHeadingFontFile> Files);

/// <summary>One file of a hosted heading font.</summary>
public sealed record PublicHeadingFontFile(string FileName, string Subset, string Weight, string UnicodeRange);

internal static class HeadingFontMappings
{
    public static PublicHeadingFont ToPublic(this HostedFont font) =>
        new(font.Id, font.Family, font.Category, false, [.. font.Files.Select(f => new PublicHeadingFontFile(f.FileName, f.Subset, f.Weight, f.UnicodeRange))]);
}
