using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Applications;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Applications;

/// <summary>
/// A candidate answered an email ScreenHire sent them (docs/architecture/email.md, "Replies"). The reply is on the
/// application's Messages and Activity; open dashboards refetch them, and the person who sent the original email gets a
/// notification card and, from the worker, an email. Ids only: every member who sees the job receives it.
/// </summary>
/// <remarks>
/// <c>inboundEmailId</c>: The reply (<see cref="InboundEmail"/>), which is also its timeline entry's id.
/// <c>senderUserId</c>: Who sent the email being answered; null when an automation did, and then every member who sees
/// the job gets the card. <c>autoReply</c>: An out-of-office or other automatic answer, which notifies nobody.
/// </remarks>
public sealed record CandidateReplied(
    Guid OrganizationId,
    Guid ApplicationId,
    Guid JobId,
    Guid InboundEmailId,
    DateTimeOffset ReceivedAt,
    Guid? SenderUserId,
    bool AutoReply) : IJobEvent;

/// <summary>
/// The email provider reported the candidate's address as undeliverable or as a spam complaint, or the flag was cleared:
/// open dashboards refetch the candidate. <see cref="Issue"/> is a <see cref="ScreenHire.Domain.Candidates.CandidateEmailIssue"/>
/// name, <c>None</c> when cleared. Ids only; the reason stays on the candidate.
/// </summary>
public sealed record CandidateEmailStatusChanged(Guid OrganizationId, Guid CandidateId, string Issue, DateTimeOffset ChangedAt) : ITenantEvent;

/// <summary>A reply's attachment needs a malware scan. Published with the reply, through the outbox, once per attachment.</summary>
public sealed record ScanEmailAttachment(Guid OrganizationId, Guid ApplicationId, Guid AttachmentId) : ITenantMessage;

/// <summary>
/// The blob container a candidate's reply attachments go to, as the worker reads it: the API's
/// <c>ScreenHire:Email:AttachmentContainer</c> setting, with the same default as the API.
/// </summary>
public static class EmailAttachmentsContainer
{
    public const string Default = "email-attachments";

    public static string From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration["ScreenHire:Email:AttachmentContainer"] is { Length: > 0 } name ? name : Default;
    }
}

/// <summary>
/// Runs in the worker: scans a reply's attachment with the same <see cref="IMalwareScanner"/> as an applicant's upload
/// and records the result. An attachment is scanned once, so a redelivered message changes nothing.
/// </summary>
public static partial class ScanEmailAttachmentHandler
{
    public static async Task Handle(
        ScanEmailAttachment message,
        ScreenHireDbContext db,
        IMalwareScanner scanner,
        IConfiguration configuration,
        TimeProvider time,
        ILogger<ScanEmailAttachment> logger,
        CancellationToken cancellationToken)
    {
        var attachment = await db.InboundEmailAttachments.SingleOrDefaultAsync(a => a.Id == message.AttachmentId, cancellationToken);
        if (attachment is null || attachment.ScanStatus != MalwareScanStatus.Pending)
        {
            return; // gone with its application, or scanned already
        }

        var status = await scanner.ScanAsync(EmailAttachmentsContainer.From(configuration), attachment.BlobName, cancellationToken);
        attachment.RecordScan(status, time.GetUtcNow()); // saved with the handler's transaction (AutoApplyTransactions)
        LogScanned(logger, attachment.Id, message.ApplicationId, status);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email attachment {AttachmentId} of application {ApplicationId} scanned: {Status}")]
    private static partial void LogScanned(ILogger logger, Guid attachmentId, Guid applicationId, MalwareScanStatus status);
}

/// <summary>
/// The email the person who sent a candidate an email gets when the candidate answers. ScreenHire's own wording, like
/// the mention email, and never the reply's text, so candidate details stay in ScreenHire.
/// </summary>
public static class ReplyEmail
{
    /// <summary>Where the email's link goes: the candidate's Messages tab.</summary>
    public static string MessagesPath(Guid applicationId) => $"/applications/{applicationId}?tab=messages";

    public static EmailMessage Compose(string to, string candidateName, string jobTitle, string organizationName, Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        var where = $"{jobTitle} at {organizationName}";
        var text = $"""
            {candidateName} replied to your email about {where}.

            Read the reply: {link}
            """;
        var html = $"""
            <p>{WebUtility.HtmlEncode(candidateName)} replied to your email about {WebUtility.HtmlEncode(where)}.</p>
            <p><a href="{WebUtility.HtmlEncode(link.ToString())}">Read the reply</a></p>
            """;
        return new EmailMessage(to, $"{candidateName} replied", text, html);
    }
}

/// <summary>
/// Emails the person who sent the original email, in the worker. Idempotent on its own: the reply's
/// <see cref="InboundEmail.NotifiedAt"/> records the send, so a redelivery finds it and sends nothing. Nobody is told
/// about an automatic answer, an email an automation sent (no one to tell: the dashboard card goes to the job's
/// members), or by a person who has left or can no longer see the job (ADR-0015). The email goes before the row
/// commits, so a crash between the two can repeat it once, as with a mention.
/// </summary>
public static partial class CandidateRepliedHandler
{
    public static async Task Handle(
        CandidateReplied message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        IOptions<DashboardLinkOptions> links,
        TimeProvider time,
        ILogger<CandidateReplied> logger,
        CancellationToken cancellationToken)
    {
        if (message.AutoReply || message.SenderUserId is not { } senderId)
        {
            return;
        }

        var reply = await db.InboundEmails.SingleOrDefaultAsync(r => r.Id == message.InboundEmailId, cancellationToken);
        if (reply is null || reply.NotifiedAt is not null)
        {
            return; // removed since, or told already
        }

        var link = links.Value.Url(ReplyEmail.MessagesPath(message.ApplicationId));
        if (!email.IsConfigured || link is null)
        {
            LogNotSent(logger, reply.Id, "this host has no email sender or no dashboard address");
            return;
        }

        if (!await JobAccessQueries.SeesJobAsync(db, message.OrganizationId, senderId, message.JobId, cancellationToken))
        {
            LogNotSent(logger, reply.Id, "the sender left the organization or cannot see the job");
            return;
        }

        var to = await db.Users.Where(u => u.Id == senderId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken);
        var place = await (
            from application in db.Applications.AsNoTracking()
            join candidate in db.Candidates on application.CandidateId equals candidate.Id
            join job in db.Jobs on application.JobId equals job.Id
            join organization in db.Organizations on application.OrganizationId equals organization.Id
            where application.Id == message.ApplicationId
            select new { Candidate = candidate.FullName, Job = job.Title, Organization = organization.Name }).SingleOrDefaultAsync(cancellationToken);
        if (to is null || place is null)
        {
            LogNotSent(logger, reply.Id, "the person or the application is gone");
            return;
        }

        await email.SendAsync(ReplyEmail.Compose(to, place.Candidate, place.Job, place.Organization, link), cancellationToken);
        reply.MarkNotified(time.GetUtcNow());
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reply {ReplyId} did not notify its sender: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid replyId, string reason);
}
