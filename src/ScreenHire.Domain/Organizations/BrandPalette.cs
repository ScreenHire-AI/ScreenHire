using System.Globalization;

namespace ScreenHire.Domain.Organizations;

/// <summary>
/// The colours a tenant careers page takes from the employer's brand colour (docs/design/design-system.md §7), each
/// checked against WCAG contrast minimums on the candidate page background and surface:
/// <list type="bullet">
/// <item><see cref="Brand"/> fills buttons; <see cref="OnBrand"/> is the text on it (4.5:1).</item>
/// <item><see cref="BrandText"/> is brand-coloured text on the page (4.5:1).</item>
/// <item><see cref="BrandUi"/> is borders and focus rings (3:1).</item>
/// </list>
/// A colour that fails is adjusted in OKLCH lightness, keeping its hue and chroma, until it passes. Darkening always
/// reaches a passing colour, so every well-formed colour has a palette and only malformed input is refused.
/// </summary>
public sealed partial record BrandPalette(string Brand, string OnBrand, string BrandText, string BrandUi, bool BrandAdjusted)
{
    public const double TextContrast = 4.5;

    public const double UiContrast = 3.0;

    // The candidate page background and surface (`--candidate-background`, `--candidate-surface` in
    // packages/ui/src/tokens.css) and the candidate ink (`--candidate-foreground`). Brand colours are checked on both
    // backgrounds, because careers pages put brand text on each.
    private static readonly Rgb PageBackground = Rgb.Parse("#fbf6ee");
    private static readonly Rgb Surface = Rgb.Parse("#ffffff");
    private static readonly Rgb Ink = Rgb.Parse("#231f1a");
    private static readonly Rgb White = Rgb.Parse("#ffffff");

    /// <summary>Whether <paramref name="color"/> is a <c>#rrggbb</c> colour.</summary>
    public static bool IsValid(string? color) => color is not null && Rgb.TryParse(color, out _);

    /// <summary>Lower-case <c>#rrggbb</c>; throws on anything else.</summary>
    public static string Normalize(string color) => Rgb.Parse(color).ToHex();

    /// <summary>The palette for <paramref name="brandColor"/>, a <c>#rrggbb</c> colour.</summary>
    public static BrandPalette Derive(string brandColor)
    {
        var brand = Rgb.Parse(brandColor);
        var adjusted = false;

        // White or ink, whichever reads better on the brand fill. If neither reaches 4.5:1 (a mid-tone), darken the
        // brand until white does.
        var onBrand = Contrast(brand, White) >= Contrast(brand, Ink) ? White : Ink;
        if (Contrast(brand, onBrand) < TextContrast)
        {
            brand = Darken(brand, [White], TextContrast);
            onBrand = White;
            adjusted = true;
        }

        var text = Darken(brand, [PageBackground, Surface], TextContrast);
        var ui = Darken(brand, [PageBackground, Surface], UiContrast);
        return new BrandPalette(brand.ToHex(), onBrand.ToHex(), text.ToHex(), ui.ToHex(), adjusted);
    }

    /// <summary>The WCAG 2.2 contrast ratio of two <c>#rrggbb</c> colours, from 1 to 21.</summary>
    public static double ContrastRatio(string first, string second) => Contrast(Rgb.Parse(first), Rgb.Parse(second));

    private static double Contrast(Rgb first, Rgb second)
    {
        var (lighter, darker) = (Math.Max(first.Luminance, second.Luminance), Math.Min(first.Luminance, second.Luminance));
        return (lighter + 0.05) / (darker + 0.05);
    }

    // Steps OKLab lightness down, keeping hue and chroma (reduced only where the colour leaves the sRGB gamut), until the
    // colour reaches the contrast on every background. Black passes everything these backgrounds need.
    private static Rgb Darken(Rgb color, Rgb[] backgrounds, double minimum)
    {
        var lab = Oklab.From(color);
        for (var lightness = lab.L; lightness > 0; lightness -= 0.005)
        {
            var candidate = (lab with { L = lightness }).ToRgbInGamut();
            if (backgrounds.All(background => Contrast(candidate, background) >= minimum))
            {
                return candidate;
            }
        }

        return new Rgb(0, 0, 0);
    }

    private readonly record struct Rgb(byte R, byte G, byte B)
    {
        public double Luminance => (0.2126 * Linear(R)) + (0.7152 * Linear(G)) + (0.0722 * Linear(B));

        public static Rgb Parse(string color) =>
            TryParse(color, out var rgb) ? rgb : throw new FormatException($"'{color}' is not a #rrggbb colour.");

        public static bool TryParse(string color, out Rgb rgb)
        {
            rgb = default;
            var value = color.Trim();
            if (value.Length != 7 || value[0] != '#'
                || !int.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var packed))
            {
                return false;
            }

            rgb = new Rgb((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
            return true;
        }

        public static double Linear(byte channel)
        {
            var c = channel / 255d;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        public string ToHex() => $"#{R:x2}{G:x2}{B:x2}";
    }

    // OKLab (Björn Ottosson, 2020): perceptual lightness L in 0..1 and two opponent axes. OKLCH is its polar form, so
    // changing L alone keeps hue and chroma.
    private readonly record struct Oklab(double L, double A, double B)
    {
        public static Oklab From(Rgb rgb)
        {
            var (r, g, b) = (Rgb.Linear(rgb.R), Rgb.Linear(rgb.G), Rgb.Linear(rgb.B));
            var l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
            var m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
            var s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));
            return new Oklab(
                (0.2104542553 * l) + (0.7936177850 * m) - (0.0040720468 * s),
                (1.9779984951 * l) - (2.4285922050 * m) + (0.4505937099 * s),
                (0.0259040371 * l) + (0.7827717662 * m) - (0.8086757660 * s));
        }

        // Halves the chroma until the colour fits sRGB, then rounds to bytes.
        public Rgb ToRgbInGamut()
        {
            var scale = 1d;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var (r, g, b) = ToLinear(A * scale, B * scale);
                if (InGamut(r) && InGamut(g) && InGamut(b))
                {
                    return new Rgb(ToByte(r), ToByte(g), ToByte(b));
                }

                scale /= 2;
            }

            var (gr, gg, gb) = ToLinear(0, 0);
            return new Rgb(ToByte(gr), ToByte(gg), ToByte(gb));
        }

        private (double R, double G, double B) ToLinear(double a, double b)
        {
            var l = Math.Pow(L + (0.3963377774 * a) + (0.2158037573 * b), 3);
            var m = Math.Pow(L - (0.1055613458 * a) - (0.0638541728 * b), 3);
            var s = Math.Pow(L - (0.0894841775 * a) - (1.2914855480 * b), 3);
            return (
                (4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s),
                (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s),
                (-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s));
        }

        private static bool InGamut(double channel) => channel is >= -0.0001 and <= 1.0001;

        private static byte ToByte(double linear)
        {
            var c = Math.Clamp(linear, 0, 1);
            var srgb = c <= 0.0031308 ? 12.92 * c : (1.055 * Math.Pow(c, 1 / 2.4)) - 0.055;
            return (byte)Math.Round(Math.Clamp(srgb, 0, 1) * 255);
        }
    }
}
