using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Notifications;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.SendingDomains;

/// <summary>
/// The daily re-check of every organization's sending domain (docs/architecture/notifications.md, "The daily email domain
/// check"). It has no tenant of its own, so it fans out one <see cref="RecheckSendingDomain"/> per organization. Only where the
/// installation uses real DNS: the sandbox's DNS lives in the API's memory, which the worker cannot see.
/// </summary>
public sealed record RecheckSendingDomains;

/// <summary>
/// Looks at one organization's sending domain. <paramref name="Confirming"/> is the second look, an hour after a verified
/// domain failed a daily one. <paramref name="Key"/> is built from the organization, the day and which look it is, so a
/// redelivered message sends nothing twice.
/// </summary>
public sealed record RecheckSendingDomain(Guid OrganizationId, DateOnly CheckedOn, bool Confirming, string Key) : ITenantMessage
{
    public static RecheckSendingDomain Daily(Guid organizationId, DateOnly day) => new(organizationId, day, false, KeyFor(organizationId, day, false));

    public static RecheckSendingDomain Confirm(Guid organizationId, DateOnly day) => new(organizationId, day, true, KeyFor(organizationId, day, true));

    private static string KeyFor(Guid organizationId, DateOnly day, bool confirming) =>
        string.Create(CultureInfo.InvariantCulture, $"{organizationId:N}:{day:yyyyMMdd}:{(confirming ? "confirm" : "daily")}");
}

/// <summary><c>ScreenHire:SendingDomainRecheck</c>.</summary>
public sealed class SendingDomainRecheckOptions
{
    public const string Section = "ScreenHire:SendingDomainRecheck";

    /// <summary>How long after a verified domain fails a daily look the second look comes. One failed lookup changes nothing until it agrees.</summary>
    public TimeSpan ConfirmDelay { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>What one look at a domain leads to.</summary>
public enum SendingDomainRecheckAction
{
    /// <summary>Save what the look found (the records and the last-checked time) and tell nobody.</summary>
    Save,

    /// <summary>A verified domain failed a daily look: change nothing and look again after the delay.</summary>
    ScheduleConfirm,

    /// <summary>The second look agrees: the domain falls back to Pending, the Owners are told once.</summary>
    LoseAndNotify,

    /// <summary>A domain the Owners were told about passes: save it and clear the notification.</summary>
    Recover,

    /// <summary>
    /// A domain the Owners were told about still fails: save the look and raise the bell alert again, which changes nothing for an
    /// Owner who has it. An Owner whose copy the retention purge removed, or who joined since, gets it back. No second email.
    /// </summary>
    Remind,
}

public static class SendingDomainRecheck
{
    /// <summary>
    /// Only a domain that is verified and healthy can be lost, and only a second failing look loses it. A domain that was
    /// never verified is saved after each look and never raises anything: its owner is still adding the records.
    /// </summary>
    public static SendingDomainRecheckAction Decide(SendingDomainStatus status, bool hasLostVerification, bool probePassed, bool confirming)
    {
        if (hasLostVerification)
        {
            return probePassed ? SendingDomainRecheckAction.Recover : SendingDomainRecheckAction.Remind;
        }

        if (status != SendingDomainStatus.Verified || probePassed)
        {
            return SendingDomainRecheckAction.Save;
        }

        return confirming ? SendingDomainRecheckAction.LoseAndNotify : SendingDomainRecheckAction.ScheduleConfirm;
    }
}

public static class RecheckSendingDomainsHandler
{
    public static async Task<OutgoingMessages> Handle(
        RecheckSendingDomains message, ScreenHireDbContext db, SendingDomainService service, TimeProvider time, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        if (service.Mode != SendingDomainMode.SendGrid)
        {
            return outgoing;
        }

        var day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        outgoing.AddRange(organizations.Select(id => RecheckSendingDomain.Daily(id, day)));
        return outgoing;
    }
}

public static partial class RecheckSendingDomainHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(RecheckSendingDomain.Key))]
    public static async Task<OutgoingMessages> Handle(
        RecheckSendingDomain message,
        ScreenHireDbContext db,
        SendingDomainService service,
        IOptions<SendingDomainRecheckOptions> options,
        TimeProvider time,
        ILogger<RecheckSendingDomain> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var domain = await db.SendingDomains.SingleOrDefaultAsync(cancellationToken);
        if (domain is null || domain.ProviderDomainId is null || !service.IsAvailable)
        {
            return outgoing;
        }

        // A lookup or provider error is not caught: the retry policy and the dead-letter table depend on seeing it.
        var probe = await service.ProbeAsync(domain, cancellationToken);
        var action = SendingDomainRecheck.Decide(domain.Status, domain.HasLostVerification, probe.Verified, message.Confirming);
        switch (action)
        {
            case SendingDomainRecheckAction.Save:
                await service.RecordAsync(db, domain, probe, cancellationToken);
                break;

            case SendingDomainRecheckAction.ScheduleConfirm:
                // Nothing is saved: the domain keeps sending as the company until a second look agrees.
                outgoing.Delay(RecheckSendingDomain.Confirm(message.OrganizationId, message.CheckedOn), options.Value.ConfirmDelay);
                LogScheduled(logger, message.OrganizationId, domain.Id);
                break;

            case SendingDomainRecheckAction.Recover:
                await service.RecordAsync(db, domain, probe, cancellationToken);
                outgoing.Add(new ClearNotification(message.OrganizationId, NotificationKinds.SendingDomainFailing, domain.Id.ToString("N")));
                LogRecovered(logger, message.OrganizationId, domain.Id);
                break;

            case SendingDomainRecheckAction.Remind:
                await service.RecordAsync(db, domain, probe, cancellationToken);
                outgoing.Add(SendingDomainAlerts.Raise(domain));
                break;

            case SendingDomainRecheckAction.LoseAndNotify:
                await service.RecordAsync(db, domain, probe, cancellationToken);
                outgoing.AddRange(await SendingDomainAlerts.LoseAsync(db, domain, time.GetUtcNow(), cancellationToken));
                await db.SaveChangesAsync(cancellationToken);
                break;
        }

        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: sending domain {DomainId} failed its daily check, a second look is scheduled")]
    private static partial void LogScheduled(ILogger logger, Guid organizationId, Guid domainId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: sending domain {DomainId} passes again")]
    private static partial void LogRecovered(ILogger logger, Guid organizationId, Guid domainId);
}

/// <summary>
/// The one way a verified sending domain is marked lost, used by the daily re-check's second look and by a Check records that finds a
/// verified domain failing: the mark, the Owners' bell alert, and one email message per Owner. The emails are messages of their own,
/// so a failing send retries by itself and never rolls back the mark or the alert.
/// </summary>
public static class SendingDomainAlerts
{
    /// <summary>The Owners' bell alert for <paramref name="domain"/>. Raising it again changes nothing for an Owner who has it.</summary>
    public static RaiseNotification Raise(SendingDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new RaiseNotification(
            domain.OrganizationId,
            Permission.ManageOwners,
            NotificationKinds.SendingDomainFailing,
            domain.Id.ToString("N"),
            new Dictionary<string, string> { ["domain"] = domain.Domain },
            SendingDomainFailingEmail.Path);
    }

    /// <summary>
    /// Marks <paramref name="domain"/> lost and returns the messages that tell its Owners, for the caller to send in the same save.
    /// Nothing when it was already marked: the Owners were told then.
    /// </summary>
    public static async Task<IReadOnlyList<object>> LoseAsync(ScreenHireDbContext db, SendingDomain domain, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(domain);
        if (!domain.MarkVerificationLost(now))
        {
            return [];
        }

        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var owners = await NotificationRecipients.ForAsync(db, domain.OrganizationId, Permission.ManageOwners, cancellationToken);
        var messages = new List<object> { Raise(domain) };
        messages.AddRange(owners.Select(o => o.UserId).Distinct().Select(userId => EmailSendingDomainFailing.For(domain, userId, day)));
        return messages;
    }
}

/// <summary>
/// Emails one Owner that the sending domain stopped verifying. <paramref name="Key"/> is the domain, the day it was lost and the
/// Owner, so a redelivered message sends nothing twice.
/// </summary>
public sealed record EmailSendingDomainFailing(Guid OrganizationId, Guid DomainId, Guid UserId, string Key) : ITenantMessage
{
    public static EmailSendingDomainFailing For(SendingDomain domain, Guid userId, DateOnly lostOn)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain.OrganizationId, domain.Id, userId, string.Create(CultureInfo.InvariantCulture, $"{domain.Id:N}:{lostOn:yyyyMMdd}:{userId:N}"));
    }
}

public static partial class EmailSendingDomainFailingHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(EmailSendingDomainFailing.Key))]
    public static async Task Handle(
        EmailSendingDomainFailing message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        IOptions<DashboardLinkOptions> links,
        ILogger<EmailSendingDomainFailing> logger,
        CancellationToken cancellationToken)
    {
        var link = links.Value.Url(SendingDomainFailingEmail.Path);
        if (!email.IsConfigured || link is null)
        {
            LogNotEmailed(logger, message.OrganizationId, "this host has no email sender or no dashboard address");
            return;
        }

        // The domain may have recovered or been removed before this ran: then there is nothing to tell.
        var domain = await db.SendingDomains.AsNoTracking().SingleOrDefaultAsync(d => d.Id == message.DomainId, cancellationToken);
        if (domain is not { HasLostVerification: true })
        {
            return;
        }

        var organization = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == message.OrganizationId).Select(o => o.Name).SingleOrDefaultAsync(cancellationToken);
        var to = await db.Users.AsNoTracking().Where(u => u.Id == message.UserId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken);
        if (organization is null || string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        await email.SendAsync(SendingDomainFailingEmail.Compose(to, organization, domain.Domain, link), cancellationToken);
        LogEmailed(logger, message.OrganizationId, message.UserId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: an Owner was not emailed about the sending domain: {Reason}")]
    private static partial void LogNotEmailed(ILogger logger, Guid organizationId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: emailed Owner {UserId} that the sending domain stopped verifying")]
    private static partial void LogEmailed(ILogger logger, Guid organizationId, Guid userId);
}

/// <summary>The email Owners get when the sending domain stops verifying: ScreenHire's own wording, the domain and a link.</summary>
public static class SendingDomainFailingEmail
{
    /// <summary>Settings, Email domain.</summary>
    public const string Path = "/settings/sending-domain";

    public static EmailMessage Compose(string to, string organizationName, string domain, Uri link)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(link);
        var line = $"The email domain {domain} no longer passes its DNS check for {organizationName}. Until it does, candidate email is sent from ScreenHire's address instead of yours.";
        var text = $"""
            {line}

            Open Settings > Email domain to see which records need a look and check them again: {link}
            """;
        var html = $"""
            <p>{WebUtility.HtmlEncode(line)}</p>
            <p><a href="{WebUtility.HtmlEncode(link.ToString())}">Open Settings &gt; Email domain</a> to see which records need a look and check them again.</p>
            """;
        return new EmailMessage(to, $"{domain} stopped verifying", text, html);
    }
}
