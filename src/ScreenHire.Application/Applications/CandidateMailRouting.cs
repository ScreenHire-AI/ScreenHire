using Microsoft.EntityFrameworkCore;
using MimeKit;
using ScreenHire.Application.Portal;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Applications;

/// <summary>
/// Decides how an email to a candidate is addressed (ADR-0022): the reply address that brings the candidate's answer
/// back to the application, the organization's own From address once its sending domain is verified, and the ids the
/// provider echoes in its event webhook. Used for every email ScreenHire sends about an application; the sign-in link,
/// the invitation and the saved-application link are not candidate correspondence and keep ScreenHire's address.
/// </summary>
public sealed class CandidateMailRouting(ReplyAddresses replies, CandidatePortalLinks portal)
{
    /// <summary>The custom argument naming the organization; the event webhook reads it to find the tenant.</summary>
    public const string OrganizationArgument = "organizationId";

    /// <summary>The custom argument naming the send (an <c>application_emails</c> id).</summary>
    public const string EmailArgument = "emailId";

    /// <summary>
    /// The route for send <paramref name="sendId"/> of <paramref name="organizationId"/>. Reads the organization's sending
    /// domain through <paramref name="db"/>, which is already scoped to the tenant. With <paramref name="candidateId"/>
    /// the route also carries the candidate's link to their page, which <see cref="MailRoute.Apply"/> puts at the end of
    /// the email (and where no careers host is configured, no link).
    /// </summary>
    public async Task<MailRoute> ForAsync(
        ScreenHireDbContext db, Guid organizationId, Guid sendId, CancellationToken cancellationToken, Guid? candidateId = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        var domain = await db.SendingDomains.AsNoTracking()
            .Where(d => d.Status == SendingDomainStatus.Verified)
            .SingleOrDefaultAsync(cancellationToken);

        string? from = null;
        string? portalLink = null;
        if (domain is not null || (candidateId is not null && portal.IsConfigured))
        {
            var organization = await db.Organizations.AsNoTracking().Where(o => o.Id == organizationId)
                .Select(o => new { o.Name, o.Slug }).SingleAsync(cancellationToken);
            if (domain is not null)
            {
                from = new MailboxAddress(organization.Name, domain.FromAddress).ToString();
            }

            if (candidateId is { } candidate)
            {
                portalLink = portal.UrlFor(organization.Slug, organizationId, candidate);
            }
        }

        return new MailRoute(
            from,
            replies.For(organizationId, sendId, domain?.ReplyHost),
            new Dictionary<string, string>
            {
                [OrganizationArgument] = organizationId.ToString("D"),
                [EmailArgument] = sendId.ToString("D"),
            },
            portalLink);
    }

    /// <summary>
    /// A route for an email that is not correspondence on an application (the data request emails): ScreenHire's own
    /// address and no reply address, but still the candidate's link to their page.
    /// </summary>
    public async Task<MailRoute> PortalOnlyAsync(ScreenHireDbContext db, Guid organizationId, Guid candidateId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!portal.IsConfigured)
        {
            return MailRoute.None;
        }

        var slug = await db.Organizations.AsNoTracking().Where(o => o.Id == organizationId).Select(o => o.Slug).SingleAsync(cancellationToken);
        return new MailRoute(PortalLink: portal.UrlFor(slug, organizationId, candidateId));
    }
}
