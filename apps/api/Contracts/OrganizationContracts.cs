namespace ScreenHire.Api.Contracts;

/// <summary>
/// The organization's careers-page branding. <c>palette</c> is what careers pages use, derived from
/// <c>brandColor</c> and contrast-checked (docs/design/design-system.md §7); both are null without a brand colour.
/// <c>brandAdjusted</c> means the colour was darkened so text on it stays readable.
/// </summary>
public sealed record OrganizationBranding(string? BrandColor, PublicBranding? Palette, bool BrandAdjusted);

/// <summary>Sets the brand colour (<c>#rrggbb</c>), or clears it with null to use ScreenHire's default.</summary>
public sealed record UpdateBrandingRequest(string? BrandColor);
