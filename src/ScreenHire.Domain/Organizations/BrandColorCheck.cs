using System.Globalization;

namespace ScreenHire.Domain.Organizations;

/// <summary>The pairs of colours the branding check measures (docs/design/design-system.md section 18).</summary>
public enum ContrastPairName
{
    /// <summary>White or ink, whichever reads better, on a button filled with the brand colour.</summary>
    ButtonText,

    /// <summary>The brand colour as link text on the careers page background.</summary>
    Links,

    /// <summary>White or ink, whichever reads better, on a header band in the second colour.</summary>
    HeaderText,
}

/// <summary>One measured pair: <see cref="Ratio"/> is cut down to one decimal, never rounded up past the minimum.</summary>
public sealed record ContrastPair(ContrastPairName Name, string Foreground, string Background, double Ratio, double Minimum, bool Passes);

/// <summary>
/// A colour's check: whether every pair it is used in reaches WCAG 2.2 AA, the text colour that goes on it, each pair,
/// and the nearest shade that passes when it fails.
/// </summary>
public sealed record ColorCheck(string Color, bool Passes, string TextColor, IReadOnlyList<ContrastPair> Pairs, string? Suggestion)
{
    /// <summary>One sentence per failing pair, then the suggested shade: what the API says when a save is refused.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = Pairs.Where(p => !p.Passes).Select(p => Describe(Color, p)).ToList();
        if (problems.Count > 0 && Suggestion is not null)
        {
            problems[^1] += $" {Suggestion} is the nearest shade that passes.";
        }

        return problems;
    }

    private static string Describe(string color, ContrastPair pair)
    {
        var ratio = pair.Ratio.ToString("0.0", CultureInfo.InvariantCulture);
        var minimum = pair.Minimum.ToString("0.0", CultureInfo.InvariantCulture);
        return pair.Name switch
        {
            ContrastPairName.ButtonText => $"Text on buttons in {color} is hard to read: {ratio}:1, and it needs {minimum}:1.",
            ContrastPairName.Links => $"Links in {color} are hard to read on the page: {ratio}:1, and they need {minimum}:1.",
            _ => $"Text on a header in {color} is hard to read: {ratio}:1, and it needs {minimum}:1.",
        };
    }
}

/// <summary>
/// Checks an employer's colours before they save (ADR-0018). A colour is used as it is given: the brand colour fills
/// buttons and colours links and focus rings, and the second colour fills the header band of the careers pages and of
/// every email to candidates. A colour that fails is refused with the nearest shade that passes, found by moving its
/// OKLab lightness and keeping its hue.
/// </summary>
public static class BrandColorCheck
{
    /// <summary>The brand colour: text on its buttons, and links in it on the page background, both at 4.5:1.</summary>
    public static ColorCheck CheckBrand(string color)
    {
        var brand = BrandPalette.Normalize(color);
        var text = BrandPalette.TextOn(brand);
        ContrastPair[] pairs =
        [
            Pair(ContrastPairName.ButtonText, text, brand),
            Pair(ContrastPairName.Links, brand, BrandPalette.PageBackgroundColor),
        ];
        var passes = pairs.All(p => p.Passes);
        return new ColorCheck(brand, passes, text, pairs, passes ? null : BrandPalette.DarkenForLinks(brand));
    }

    /// <summary>The second colour: text on a header band filled with it, at 4.5:1.</summary>
    public static ColorCheck CheckSecond(string color)
    {
        var second = BrandPalette.Normalize(color);
        var text = BrandPalette.TextOn(second);
        ContrastPair[] pairs = [Pair(ContrastPairName.HeaderText, text, second)];
        var passes = pairs[0].Passes;
        return new ColorCheck(second, passes, text, pairs, passes ? null : BrandPalette.NearestWithReadableText(second));
    }

    private static ContrastPair Pair(ContrastPairName name, string foreground, string background)
    {
        var ratio = BrandPalette.ContrastRatio(foreground, background);
        return new ContrastPair(
            name, foreground, background, Math.Floor(ratio * 10) / 10, BrandPalette.TextContrast, ratio >= BrandPalette.TextContrast);
    }
}

public sealed partial record BrandPalette
{
    /// <summary>The careers page background, the harder of the two backgrounds links sit on.</summary>
    public static string PageBackgroundColor => PageBackground.ToHex();

    /// <summary>White or ink, whichever has more contrast on <paramref name="background"/>.</summary>
    public static string TextOn(string background)
    {
        var fill = Rgb.Parse(background);
        return (Contrast(fill, White) >= Contrast(fill, Ink) ? White : Ink).ToHex();
    }

    // Links need a dark colour on a light page, so the only way to a passing brand colour is down. A colour dark enough
    // for links on the page background is dark enough for white text on it too.
    internal static string DarkenForLinks(string color) =>
        Darken(Rgb.Parse(color), [PageBackground, White], TextContrast).ToHex();

    // A fill needs white or ink at 4.5:1 on it; a mid-tone has neither. Step lightness both ways and take the nearer
    // shade that gives one of them, the darker on a tie.
    internal static string NearestWithReadableText(string color)
    {
        var lab = Oklab.From(Rgb.Parse(color));
        for (var step = 0.005; step <= 1; step += 0.005)
        {
            foreach (var lightness in new[] { lab.L - step, lab.L + step })
            {
                if (lightness is < 0 or > 1)
                {
                    continue;
                }

                var candidate = (lab with { L = lightness }).ToRgbInGamut();
                if (Math.Max(Contrast(candidate, White), Contrast(candidate, Ink)) >= TextContrast)
                {
                    return candidate.ToHex();
                }
            }
        }

        return Ink.ToHex();
    }
}
