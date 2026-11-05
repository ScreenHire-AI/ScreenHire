using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Privacy;

/// <summary>
/// Email a candidate the link that opens their data request choices (docs/architecture/data-requests.md). Queued by the
/// public request form for an address that has applied, after it claimed the candidate's cooldown. The message carries ids
/// only: the worker issues the signed link itself, so the link never sits in the outbox table. <see cref="Key"/> is built
/// from the candidate and the time of the claim, so a redelivered envelope sends nothing twice.
/// </summary>
public sealed record SendDataRequestLink(Guid OrganizationId, Guid CandidateId, string Key) : ITenantMessage
{
    /// <summary>The idempotency key for the claim made at <paramref name="claimedAt"/>: stable ids and the claim's own instant, never a new <see cref="Guid"/>.</summary>
    public static string KeyFor(Guid candidateId, DateTimeOffset claimedAt) =>
        string.Create(CultureInfo.InvariantCulture, $"{candidateId:N}:{claimedAt.UtcTicks}");
}

/// <summary>
/// Sends the link, in the worker. A candidate who was erased since the request, whose address bounced, or a host with no
/// email sender or no careers address is dropped quietly with a log line: nothing can be sent, and a retry would not
/// change that. The email goes before the handler ends, so a crash between the two can repeat it once, as with every email
/// to a candidate.
/// </summary>
public static partial class SendDataRequestLinkHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(SendDataRequestLink.Key))]
    public static async Task Handle(
        SendDataRequestLink message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        DataRequestLinks links,
        ILogger<SendDataRequestLink> logger,
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

        var link = links.LinkUrl(organization!.Slug, links.IssueLink(organization.Id, candidate!.Id));
        await email.SendAsync(DataRequestEmails.LinkEmail(candidate.Email, organization.Name, link, links.LinkLifetime), cancellationToken);
        LogSent(logger, message.CandidateId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Data request link for candidate {CandidateId} was not sent: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid candidateId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent the data request link to candidate {CandidateId}")]
    private static partial void LogSent(ILogger logger, Guid candidateId);
}

/// <summary>The words of the emails of the data request flow: ScreenHire's own, not an organization template, because they go before a team member has anything to say.</summary>
public static class DataRequestEmails
{
    /// <summary>The email with the link that opens the choices. States what the link does and how long it works; promises no outcome.</summary>
    public static EmailMessage LinkEmail(string to, string organizationName, string link, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(link);
        var works = Describe(lifetime);
        var subject = $"Your link to ask {organizationName} about your data";
        var text = $"""
            We received a request for a link to ask {organizationName} about the data it holds from your job applications.

            Open this link to choose between asking for a copy of your data and asking for it to be erased: {link}

            The link opens a page where you make that choice. It works for {works}. Nothing is requested until you choose there.

            If you didn't ask for this you can ignore this email.
            """;
        var org = WebUtility.HtmlEncode(organizationName);
        var html = $"""
            <p>We received a request for a link to ask {org} about the data it holds from your job applications.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Choose between a copy of your data and erasure</a></p>
            <p>The link opens a page where you make that choice. It works for {works}. Nothing is requested until you choose there.</p>
            <p>If you didn't ask for this you can ignore this email.</p>
            """;
        return new EmailMessage(to, subject, text, html);
    }

    /// <summary>A span as the email says it: whole hours as hours, anything else as minutes (at least one).</summary>
    public static string Describe(TimeSpan lifetime)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(lifetime.TotalMinutes));
        if (minutes % 60 == 0)
        {
            var hours = minutes / 60;
            return hours == 1 ? "1 hour" : string.Create(CultureInfo.InvariantCulture, $"{hours} hours");
        }

        return minutes == 1 ? "1 minute" : string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes");
    }
}
