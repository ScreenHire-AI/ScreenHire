using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Infrastructure.Content;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Email;

/// <summary>One file that came with a reply, before it is checked and stored.</summary>
internal sealed record InboundAttachment(string FileName, byte[] Content);

/// <summary>
/// A reply as the provider posted it, reduced to what ScreenHire keeps. <c>Recipients</c> are the envelope's, the
/// addresses that actually reached the provider, not the To header the candidate may have edited.
/// </summary>
internal sealed record ParsedInboundEmail(
    IReadOnlyList<string> Recipients,
    string FromAddress,
    string? FromName,
    string Subject,
    string? Text,
    string? Html,
    string? MessageId,
    bool AutoReply,
    IReadOnlyList<InboundAttachment> Attachments);

internal enum InboundOutcome
{
    /// <summary>The reply is on the application.</summary>
    Stored,

    /// <summary>The same message arrived before: nothing changed.</summary>
    Duplicate,

    /// <summary>Not a reply to one of our emails: no valid reply address, an email that never went, or an application that is gone.</summary>
    Ignored,
}

/// <summary>
/// Turns a parsed reply into a stored reply (docs/architecture/email.md, "Replies"), the same way whether SendGrid's
/// Inbound Parse webhook or the sandbox's dev route delivered it. The signed reply address is the check: it names the
/// organization and the send, so a post without a valid one changes nothing and says nothing. The reply, its timeline
/// entry, its attachments' scans and the event for the person who sent the original commit together.
/// </summary>
internal sealed partial class InboundEmailIngest(
    ReplyAddresses replies,
    EmailAttachmentStore attachments,
    TenantContext tenant,
    TimeProvider time,
    ILogger<InboundEmailIngest> logger)
{
    /// <summary>The most attachments a reply keeps.</summary>
    public const int MaxAttachments = 10;

    /// <summary>The most bytes one attachment may hold.</summary>
    public const int MaxAttachmentBytes = 10 * 1024 * 1024;

    public Task<InboundOutcome> IngestAsync(ParsedInboundEmail mail, IDbContextOutbox<ScreenHireDbContext> outbox, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(outbox);
        var target = mail.Recipients.Select(replies.TryParse).FirstOrDefault(t => t is not null);
        return target is not { } reply
            ? Task.FromResult(InboundOutcome.Ignored)
            : IngestForAsync(reply, mail, outbox, clearsEmailIssue: true, cancellationToken);
    }

    /// <summary>
    /// Stores <paramref name="mail"/> as the answer to the email <paramref name="reply"/> names. The webhook gets the target
    /// from the signed reply address; the candidate's page (docs/architecture/candidate-portal.md) names it itself, from the
    /// application it is showing. <paramref name="clearsEmailIssue"/> is whether writing from the candidate's address proves it
    /// works: true for an email that arrived from that address, false for a reply typed on the page, which says nothing
    /// about the mailbox and must not turn email back on for someone who marked it as spam.
    /// </summary>
    public async Task<InboundOutcome> IngestForAsync(
        ReplyTarget reply, ParsedInboundEmail mail, IDbContextOutbox<ScreenHireDbContext> outbox, bool clearsEmailIssue, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(outbox);
        tenant.SetPublicOrganization(reply.OrganizationId); // acts for the address's organization, never for whoever is signed in
        var db = outbox.DbContext;
        var send = await db.ApplicationEmails.AsNoTracking().SingleOrDefaultAsync(e => e.Id == reply.SendId, cancellationToken);
        if (send is null)
        {
            return InboundOutcome.Ignored;
        }

        // The reply must answer an email that went: the copy of one still waiting, or never sent, has no timeline entry.
        var sentBy = await db.ApplicationEvents.AsNoTracking()
            .Where(e => e.Id == send.Id && e.Type == ApplicationEventType.EmailSent)
            .Select(e => new { e.ActorUserId })
            .SingleOrDefaultAsync(cancellationToken);
        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == send.ApplicationId, cancellationToken);
        if (sentBy is null || application is null)
        {
            return InboundOutcome.Ignored;
        }

        if (mail.MessageId is { Length: > 0 } messageId
            && await db.InboundEmails.AnyAsync(e => e.MessageId == messageId, cancellationToken))
        {
            return InboundOutcome.Duplicate;
        }

        var now = time.GetUtcNow();
        var replyId = Guid.CreateVersion7();
        var pending = new List<InboundEmailAttachment>();
        var notKept = 0;
        foreach (var file in mail.Attachments)
        {
            if (pending.Count == MaxAttachments || !AttachmentFiles.TryAccept(file, out var accepted))
            {
                notKept++;
                continue;
            }

            var attachmentId = Guid.CreateVersion7();
            var blobName = $"{application.OrganizationId}/{application.Id}/{replyId}/{attachmentId}/{accepted.FileName}";
            await attachments.SaveAsync(blobName, file.Content, accepted.ContentType, cancellationToken);
            pending.Add(new InboundEmailAttachment
            {
                Id = attachmentId,
                OrganizationId = application.OrganizationId,
                ApplicationId = application.Id,
                InboundEmailId = replyId,
                BlobName = blobName,
                FileName = accepted.FileName,
                ContentType = accepted.ContentType,
                SizeBytes = file.Content.Length,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(file.Content)),
            });
        }

        var (text, html) = Bodies(mail);
        if (notKept > 0)
        {
            var note = notKept == 1
                ? "[1 attachment was not kept: ScreenHire keeps PDF, Word and image files under 10 MB.]"
                : $"[{notKept} attachments were not kept: ScreenHire keeps PDF, Word and image files under 10 MB.]";
            text = text.Length == 0 ? note : $"{text}\n\n{note}";
        }

        var stored = new InboundEmail
        {
            Id = replyId,
            OrganizationId = application.OrganizationId,
            ApplicationId = application.Id,
            SendId = send.Id,
            MessageId = mail.MessageId is { Length: > 0 } id ? Cut(id, 998) : null,
            FromAddress = Cut(mail.FromAddress, 320),
            FromName = mail.FromName is { Length: > 0 } name ? Cut(name, 200) : null,
            Subject = Cut(string.IsNullOrWhiteSpace(mail.Subject) ? "(no subject)" : mail.Subject, InboundEmail.MaxSubjectLength),
            TextBody = text,
            HtmlBody = html,
            AutoReply = mail.AutoReply,
            ReceivedAt = now,
        };

        db.InboundEmails.Add(stored);
        db.InboundEmailAttachments.AddRange(pending);
        db.ApplicationEvents.Add(application.EmailReceived(stored));
        foreach (var attachment in pending)
        {
            await outbox.PublishAsync(new ScanEmailAttachment(application.OrganizationId, application.Id, attachment.Id));
        }

        await outbox.PublishAsync(new CandidateReplied(
            application.OrganizationId, application.Id, application.JobId, stored.Id, now, sentBy.ActorUserId, stored.AutoReply));

        // A candidate who writes from the address the provider flagged has a working address after all. An automatic answer
        // is not the candidate writing: an out-of-office must not turn email back on for someone who marked it as spam.
        var candidate = await db.Candidates.SingleOrDefaultAsync(c => c.Id == application.CandidateId, cancellationToken);
        if (clearsEmailIssue && candidate is not null && !mail.AutoReply && !candidate.CanReceiveEmail
            && candidate.NormalizedEmail == Candidate.Normalize(mail.FromAddress) && candidate.ClearEmailIssue())
        {
            await outbox.PublishAsync(new CandidateEmailStatusChanged(
                application.OrganizationId, candidate.Id, nameof(CandidateEmailIssue.None), now));
        }

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // The same message posted twice at once: the other post stored it.
            return InboundOutcome.Duplicate;
        }

        LogStored(logger, stored.Id, application.Id, pending.Count);
        return InboundOutcome.Stored;
    }

    private static (string Text, string Html) Bodies(ParsedInboundEmail mail)
    {
        var html = mail.Html is { Length: > 0 } rawHtml ? RichText.Sanitize(Cut(rawHtml, InboundEmail.MaxBodyLength)) : "";
        var text = mail.Text is { Length: > 0 } rawText ? Cut(rawText, InboundEmail.MaxBodyLength) : "";
        if (string.IsNullOrWhiteSpace(text) && html.Length > 0)
        {
            text = RichText.ToPlainText(html);
        }

        return (text.Trim(), html);
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];

    [LoggerMessage(Level = LogLevel.Information, Message = "Reply {ReplyId} stored on application {ApplicationId} with {Attachments} attachments")]
    private static partial void LogStored(ILogger logger, Guid replyId, Guid applicationId, int attachments);
}
