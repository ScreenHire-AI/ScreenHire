using System.Text.RegularExpressions;

namespace ScreenHire.Domain.Organizations;

/// <summary>
/// The heading font an employer picks for its careers pages (ADR-0031, docs/architecture/branding.md section 3). It is any
/// Google Fonts family under the SIL Open Font License or Apache 2.0, named by its Fontsource id (<c>roboto-slab</c>),
/// with the family name people know it by (<c>Roboto Slab</c>). An organization with none uses ScreenHire's, Bricolage
/// Grotesque.
/// </summary>
public sealed record HeadingFont(string Id, string Family)
{
    /// <summary>The longest id the column holds; Google's longest in November 2026 was 32 characters.</summary>
    public const int MaxIdLength = 64;

    /// <summary>The longest family name the column holds.</summary>
    public const int MaxFamilyLength = 100;
}

/// <summary>The rules every heading font id and family name follow, and the six fonts ScreenHire bundles.</summary>
public static partial class HeadingFonts
{
    /// <summary>
    /// The fonts ScreenHire ships in <c>packages/ui</c> (<c>@fontsource-variable</c> packages, <c>heading-fonts.ts</c>),
    /// which the careers pages load from their own bundle: nothing is downloaded when one is picked. These were the only
    /// choices before ADR-0031, and they stay.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Bundled { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["inter"] = "Inter",
        ["montserrat"] = "Montserrat",
        ["nunito"] = "Nunito",
        ["fraunces"] = "Fraunces",
        ["playfair-display"] = "Playfair Display",
    };

    /// <summary>
    /// Whether <paramref name="id"/> has a font id's shape: lower-case letters and digits in words joined by single
    /// hyphens, as every Fontsource id is. An id goes into blob names, URLs and the careers stylesheet only after this.
    /// </summary>
    public static bool IsValidId(string? id) => id is { Length: > 0 and <= HeadingFont.MaxIdLength } && IdPattern().IsMatch(id);

    /// <summary>
    /// Whether <paramref name="family"/> is a family name the careers stylesheet may carry: letters, digits and single
    /// spaces, as every Google Fonts family name is. Anything else is refused rather than escaped.
    /// </summary>
    public static bool IsValidFamily(string? family) =>
        family is { Length: > 0 and <= HeadingFont.MaxFamilyLength } && FamilyPattern().IsMatch(family);

    /// <summary>The bundled fonts' Fontsource categories, which pick their fallback stack.</summary>
    public static IReadOnlyDictionary<string, string> BundledCategories { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["inter"] = "sans-serif",
        ["montserrat"] = "sans-serif",
        ["nunito"] = "sans-serif",
        ["fraunces"] = "serif",
        ["playfair-display"] = "serif",
    };

    /// <summary>Whether <paramref name="id"/> is one of the six fonts ScreenHire bundles.</summary>
    public static bool IsBundled(string id) => Bundled.ContainsKey(id);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z0-9]+( [A-Za-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex FamilyPattern();
}
