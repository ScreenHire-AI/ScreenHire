using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine.Attributes;

namespace ScreenHire.Application.Portal;

/// <summary>
/// Email a candidate the link to their page (docs/architecture/candidate-portal.md). Queued by the public form for an
/// address that has applied, after it claimed the candidate's cooldown. The message carries ids only: the worker issues
/// the signed link itself, so the link never sits in the outbox table. <see cref="Key"/> is built from the candidate and the
/// time of the claim, so a redelivered envelope sends nothing twice.
/// </summary>
public sealed record SendPortalLink(Guid OrganizationId, Guid CandidateId, string Key) : ITenantMessage
{
    /// <summary>The idempotency key for the claim made at <paramref name="claimedAt"/>: stable ids and the claim's own instant, never a new <see cref="Guid"/>.</summary>
    public static string KeyFor(Guid candidateId, DateTimeOffset claimedAt) =>
        string.Create(CultureInfo.InvariantCulture, $"portal:{candidateId:N}:{claimedAt.UtcTicks}");
}

/// <summary>
/// Sends the link, in the worker. A candidate erased since the request, an address that bounced, or a host with no email
/// sender or no careers address is dropped quietly with a log line: nothing can be sent, and a retry would not change that.
/// </summary>
public static partial class SendPortalLinkHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(SendPortalLink.Key))]
    public static async Task Handle(
        SendPortalLink message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        CandidatePortalLinks links,
        ILogger<SendPortalLink> logger,
        CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.AsNoTracking().SingleOrDefaultAsync(c => c.Id == message.CandidateId, cancellationToken);
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == message.OrganizationId, cancellationToken);
        var reason = candidate is null ? "the candidate is gone"
            : organization is null ? "the organization is gone"
            : !candidate.CanReceiveEmail ? "the candidate's address bounced or marked an email as spam"
            : !email.IsConfigured ? "this host has no email sender"
            : !links.IsConfigured ? "the careers site address is not set up"
            : null;
        if (reason is not null)
        {
            LogNotSent(logger, message.CandidateId, reason);
            return;
        }

        var link = links.Url(organization!.Slug, links.Issue(organization.Id, candidate!.Id));
        await email.SendAsync(PortalEmails.LinkEmail(candidate.Email, organization.Name, link, links.LinkLifetime), cancellationToken);
        LogSent(logger, message.CandidateId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Portal link for candidate {CandidateId} was not sent: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid candidateId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent the portal link to candidate {CandidateId}")]
    private static partial void LogSent(ILogger logger, Guid candidateId);
}

/// <summary>The words of the portal's sign-in email: ScreenHire's own, not an organization template, because the team has nothing to say yet.</summary>
public static class PortalEmails
{
    /// <summary>The email with the link to the candidate's page. Says what the link opens and how long it works; promises nothing about an application.</summary>
    public static EmailMessage LinkEmail(string to, string organizationName, string link, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(link);
        var works = Describe(lifetime);
        var subject = $"Your link to your applications at {organizationName}";
        var text = $"""
            We received a request for a link to your applications at {organizationName}.

            Open this link to see where each application stands, finish a saved one, and read or answer your messages: {link}

            The link works for {works}. Anyone who has it can open your page, so please do not forward this email.

            If you didn't ask for this you can ignore this email.
            """;
        var org = WebUtility.HtmlEncode(organizationName);
        var html = $"""
            <p>We received a request for a link to your applications at {org}.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">See your applications and messages</a></p>
            <p>The link works for {works}. Anyone who has it can open your page, so please do not forward this email.</p>
            <p>If you didn't ask for this you can ignore this email.</p>
            """;
        return new EmailMessage(to, subject, text, html);
    }

    /// <summary>A span as the email says it: whole days as days, whole hours as hours, anything else as minutes (at least one).</summary>
    public static string Describe(TimeSpan lifetime)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(lifetime.TotalMinutes));
        if (minutes % (24 * 60) == 0)
        {
            var days = minutes / (24 * 60);
            return days == 1 ? "1 day" : string.Create(CultureInfo.InvariantCulture, $"{days} days");
        }

        if (minutes % 60 == 0)
        {
            var hours = minutes / 60;
            return hours == 1 ? "1 hour" : string.Create(CultureInfo.InvariantCulture, $"{hours} hours");
        }

        return minutes == 1 ? "1 minute" : string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes");
    }
}
