using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Branding;

/// <summary>An organization's public branding, read once for each public page that shows it.</summary>
internal static class PublicBrandings
{
    /// <summary>
    /// The branding of <paramref name="organization"/>, the current tenant: its colours and the images in use that may be
    /// served. An image still scanning, or one the scan blocked, is left out.
    /// </summary>
    public static async Task<PublicBranding> LoadAsync(
        ScreenHireDbContext db, Organization organization, DomainOptions domains, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(domains);
        Guid?[] wanted = [organization.LogoImageId, organization.FaviconImageId, organization.SharingImageId];
        var ids = wanted.OfType<Guid>().ToList();
        var images = ids.Count == 0
            ? []
            : await db.BrandImages.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        var board = domains.CareersBoardUrl(organization.Slug);
        PublicBrandImage? Image(BrandImageKind kind) =>
            images.SingleOrDefault(i => i.Id == organization.ImageId(kind) && i.Kind == kind && i.IsServable)?.ToPublic(board);

        var palette = organization.GetBrandPalette();
        var header = organization.SecondColor is { } second ? new PublicHeaderColors(second, BrandPalette.TextOn(second)) : null;
        return new PublicBranding(
            palette is null ? null : new PublicPalette(palette.Brand, palette.OnBrand, palette.BrandText, palette.BrandUi),
            header,
            organization.HeadingFont,
            Image(BrandImageKind.Logo),
            Image(BrandImageKind.Favicon),
            Image(BrandImageKind.SharingImage));
    }

    /// <summary>The organization as its careers pages show it.</summary>
    public static async Task<PublicOrganization> ToPublicAsync(
        this Organization organization, ScreenHireDbContext db, DomainOptions domains, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organization);
        return new(organization.Name, organization.Slug, await LoadAsync(db, organization, domains, cancellationToken));
    }
}
