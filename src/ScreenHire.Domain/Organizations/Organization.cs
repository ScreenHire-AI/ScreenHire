namespace ScreenHire.Domain.Organizations;

/// <summary>
/// An employer account — the tenant boundary. Every tenant-owned row carries an <c>OrganizationId</c>.
/// </summary>
public sealed class Organization
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>URL-safe identifier used in careers-page hosts and public API routes.</summary>
    public required string Slug { get; set; }

    /// <summary>Data-residency home region (e.g. <c>eu</c>, <c>us</c>); see ADR-0007.</summary>
    public required string HomeRegion { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// A free-trial account. Partner boards (Indeed's Job Sync rules out trial clients) are never offered to one
    /// (docs/architecture/job-distribution.md §3).
    /// </summary>
    public bool IsTrial { get; set; }

    /// <summary>
    /// Cleared for Indeed: the account is a paying customer whose every public job we may send. Set by us after review,
    /// never by the tenant. Nothing reads it for posting yet; there is no Indeed integration in the MVP.
    /// </summary>
    public bool IndeedEligible { get; set; }

    /// <summary>Whether partner boards with Indeed-style gates may receive this organization's jobs.</summary>
    public bool MayUsePartnerBoards => IndeedEligible && !IsTrial;

    /// <summary>
    /// The employer's brand colour (<c>#rrggbb</c>) for its careers pages, or null for ScreenHire's default. Careers
    /// pages use the <see cref="BrandPalette"/> derived from it, never the raw colour.
    /// </summary>
    public string? BrandColor { get; private set; }

    /// <summary>The careers-page palette, or null when the organization has no brand colour.</summary>
    public BrandPalette? GetBrandPalette() => BrandColor is null ? null : BrandPalette.Derive(BrandColor);

    /// <summary>Sets or clears the brand colour; throws <see cref="FormatException"/> unless it is <c>#rrggbb</c>.</summary>
    public void SetBrandColor(string? color) => BrandColor = color is null ? null : BrandPalette.Normalize(color);
}
