using FluentValidation;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The organization's branding (docs/architecture/branding.md): the brand colour (buttons and links), the second colour
/// (the header band of its careers pages and emails), the heading font and the images in use, each null for
/// ScreenHire's default. <c>lastUpdated</c> is the branding's last change ("by ScreenHire" while it is all the
/// default); its history is <c>/branding/history</c>.
/// </summary>
public sealed record OrganizationBranding(
    string? BrandColor,
    string? SecondColor,
    HeadingFont? HeadingFont,
    BrandingImage? Logo,
    BrandingImage? Favicon,
    BrandingImage? SharingImage,
    LastUpdate LastUpdated);

/// <summary>
/// Replaces the whole branding. Colours are <c>#rrggbb</c> and must pass the contrast check
/// (<c>/branding/contrast</c>); each image id is one of the organization's uploads of that kind that passed its scan.
/// Null is ScreenHire's default, or no image.
/// </summary>
public sealed record UpdateBrandingRequest(
    string? BrandColor,
    string? SecondColor = null,
    HeadingFont? HeadingFont = null,
    Guid? LogoId = null,
    Guid? FaviconId = null,
    Guid? SharingImageId = null);

/// <summary>
/// An uploaded brand image. <c>scanStatus</c> is the malware scan's: a Pending or Infected image cannot be saved or
/// served. <c>url</c> is its public address on the careers board, null until it may be served.
/// </summary>
public sealed record BrandingImage(
    Guid Id, BrandImageKind Kind, MalwareScanStatus ScanStatus, string ContentType, int Width, int Height, long SizeBytes, string? Url);

/// <summary>Asks for a link to upload one brand image of <c>kind</c>: a PNG or JPEG of <c>sizeBytes</c>.</summary>
public sealed record BrandImageUploadRequest(BrandImageKind Kind, string FileName, long SizeBytes);

/// <summary>
/// The contrast check of the colours asked about, each null when it was not asked: whether every pair the colour is
/// used in reaches WCAG 2.2 AA, the text colour that goes on it, the pairs, and the nearest passing shade when it fails.
/// </summary>
public sealed record BrandingContrast(ColorContrast? BrandColor, ColorContrast? SecondColor);

/// <summary>One colour's check. <c>problems</c> says each failing pair in words, as a refused save does.</summary>
public sealed record ColorContrast(
    string Color, bool Passes, string TextColor, IReadOnlyList<ContrastPairCheck> Pairs, string? Suggestion, IReadOnlyList<string> Problems);

/// <summary>One pair: <c>ratio</c> is cut down to one decimal, so a failing pair never reads as its minimum.</summary>
public sealed record ContrastPairCheck(ContrastPairName Name, string Foreground, string Background, double Ratio, double Minimum, bool Passes);

/// <summary>
/// An organization's branding as its careers pages, the apply flow and the embeddable widget use it. Every part is null
/// for ScreenHire's default. <c>colors</c> come from the brand colour; <c>header</c> is the band in the second colour,
/// with white or ink text on it; image <c>url</c>s are absolute addresses on the careers board.
/// </summary>
public sealed record PublicBranding(
    PublicPalette? Colors,
    PublicHeaderColors? Header,
    HeadingFont? HeadingFont,
    PublicBrandImage? Logo,
    PublicBrandImage? Favicon,
    PublicBrandImage? SharingImage);

/// <summary>
/// The careers-page colours from the brand colour (docs/design/design-system.md section 18): <c>brand</c> fills,
/// <c>onBrand</c> is text on it, <c>brandText</c> is links on the page and <c>brandUi</c> is borders and focus rings.
/// A colour saved since the contrast check passes as it is, so all three are the colour itself; an older one is darkened
/// until it passes. All <c>#rrggbb</c>.
/// </summary>
public sealed record PublicPalette(string Brand, string OnBrand, string BrandText, string BrandUi);

/// <summary>The header band: its fill, the second colour, and the white or ink text on it. Both <c>#rrggbb</c>.</summary>
public sealed record PublicHeaderColors(string Background, string Foreground);

/// <summary>A brand image as a public page links it: <c>url</c> is the same for every page, email and person.</summary>
public sealed record PublicBrandImage(Guid Id, string ContentType, int Width, int Height, string Url);

internal sealed class BrandImageUploadRequestValidator : AbstractValidator<BrandImageUploadRequest>
{
    public BrandImageUploadRequestValidator()
    {
        RuleFor(r => r.FileName).NotEmpty().MaximumLength(255);
        RuleFor(r => r.SizeBytes).GreaterThan(0).WithMessage("This file is empty.");
    }
}

internal static class BrandingMappings
{
    public static BrandingImage ToBrandingImage(this BrandImage image, string careersBoardUrl) =>
        new(image.Id, image.Kind, image.ScanStatus, image.ContentType, image.Width, image.Height, image.SizeBytes,
            image.IsServable ? image.UrlUnder(careersBoardUrl) : null);

    public static PublicBrandImage ToPublic(this BrandImage image, string careersBoardUrl) =>
        new(image.Id, image.ContentType, image.Width, image.Height, image.UrlUnder(careersBoardUrl));

    public static ColorContrast ToContrast(this ColorCheck check) =>
        new(check.Color, check.Passes, check.TextColor,
            [.. check.Pairs.Select(p => new ContrastPairCheck(p.Name, p.Foreground, p.Background, p.Ratio, p.Minimum, p.Passes))],
            check.Suggestion, check.Problems());
}
