using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Email;

/// <summary>
/// The top of every email to a candidate (docs/architecture/branding.md): the organization's logo, or its name when it
/// has none, on a band of its second colour, or on white. The logo is a plain image at one public address that is the
/// same for every recipient, with no query string and nothing that names the candidate, the application or the email,
/// so it can never work as a tracking pixel.
/// </summary>
public sealed record EmailHeader(string OrganizationName, EmailLogo? Logo = null, string? Background = null)
{
    /// <summary>How tall the logo is drawn, at most, and how wide.</summary>
    public const int LogoHeight = 40;

    public const int LogoMaxWidth = 240;

    /// <summary>
    /// The header for <paramref name="organization"/>, with <paramref name="logo"/> when it is the organization's servable
    /// logo, linked under its careers board <paramref name="careersBoardUrl"/>.
    /// </summary>
    public static EmailHeader For(Organization organization, BrandImage? logo, string careersBoardUrl)
    {
        ArgumentNullException.ThrowIfNull(organization);
        var image = logo is { IsServable: true, Kind: BrandImageKind.Logo } && logo.Id == organization.LogoImageId
            ? EmailLogo.Scaled(logo.UrlUnder(careersBoardUrl), logo.Width, logo.Height)
            : null;
        return new EmailHeader(organization.Name, image, organization.SecondColor);
    }
}

/// <summary>A logo's address and the size an email draws it at, in CSS pixels.</summary>
public sealed record EmailLogo(string Url, int Width, int Height)
{
    /// <summary>Scales a <paramref name="width"/> by <paramref name="height"/> image down to fit the header, never up.</summary>
    public static EmailLogo Scaled(string url, int width, int height)
    {
        var scale = Math.Min(1d, Math.Min((double)EmailHeader.LogoHeight / height, (double)EmailHeader.LogoMaxWidth / width));
        return new EmailLogo(url, Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}
