using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Branding;

/// <summary>
/// The top of an organization's emails to candidates (<see cref="EmailHeader"/>): its name, its second colour and its
/// logo, linked on its careers board. The API and the worker both render candidate emails, so both use this.
/// </summary>
public sealed class EmailHeaders(IOptions<CareersSiteOptions> careers)
{
    /// <summary>The header for <paramref name="organization"/>; reads its logo within the current tenant.</summary>
    public async Task<EmailHeader> ForAsync(ScreenHireDbContext db, Organization organization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(organization);
        var logo = organization.LogoImageId is { } id
            ? await db.BrandImages.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken)
            : null;
        return EmailHeader.For(organization, logo, careers.Value.BoardUrl(organization.Slug));
    }
}
