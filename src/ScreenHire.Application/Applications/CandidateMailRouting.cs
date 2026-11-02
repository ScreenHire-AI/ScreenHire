using Microsoft.EntityFrameworkCore;
using MimeKit;
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
public sealed class CandidateMailRouting(ReplyAddresses replies)
{
    /// <summary>The custom argument naming the organization; the event webhook reads it to find the tenant.</summary>
    public const string OrganizationArgument = "organizationId";

    /// <summary>The custom argument naming the send (an <c>application_emails</c> id).</summary>
    public const string EmailArgument = "emailId";

    /// <summary>
    /// The route for send <paramref name="sendId"/> of <paramref name="organizationId"/>. Reads the organization's sending
    /// domain through <paramref name="db"/>, which is already scoped to the tenant.
    /// </summary>
    public async Task<MailRoute> ForAsync(ScreenHireDbContext db, Guid organizationId, Guid sendId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var domain = await db.SendingDomains.AsNoTracking()
            .Where(d => d.Status == SendingDomainStatus.Verified)
            .SingleOrDefaultAsync(cancellationToken);

        string? from = null;
        if (domain is not null)
        {
            var name = await db.Organizations.AsNoTracking().Where(o => o.Id == organizationId).Select(o => o.Name).SingleAsync(cancellationToken);
            from = new MailboxAddress(name, domain.FromAddress).ToString();
        }

        return new MailRoute(
            from,
            replies.For(organizationId, sendId, domain?.ReplyHost),
            new Dictionary<string, string>
            {
                [OrganizationArgument] = organizationId.ToString("D"),
                [EmailArgument] = sendId.ToString("D"),
            });
    }
}
