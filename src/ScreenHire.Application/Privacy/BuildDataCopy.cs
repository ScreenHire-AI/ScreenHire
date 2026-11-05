using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Privacy;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Privacy;

/// <summary>
/// Build the copy of a candidate's data for a request and email them the link to download it (docs/architecture/data-requests.md,
/// "The copy"). Published when a team member chooses Send a copy. It carries ids only: the file is built from the database here.
/// </summary>
public sealed record BuildDataCopy(Guid OrganizationId, Guid RequestId) : ITenantMessage
{
    /// <summary>One copy per request, however many envelopes carry the intent.</summary>
    public string IdempotencyKey => $"copy:{RequestId:N}";
}

/// <summary>
/// Builds the file, writes it to its deterministic blob, records it on the request and mails the link. A request that is
/// gone, no longer open, not a copy request, or whose candidate was erased since is finished work, so it returns quietly.
/// The blob name is stable and the request records the send once, so a redelivery writes the same file and sends nothing
/// twice. The email goes before the handler's transaction commits, so a crash between the two can repeat it once, as with
/// every email to a candidate.
/// </summary>
public static partial class BuildDataCopyHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(BuildDataCopy.IdempotencyKey))]
    public static async Task<DataRequestChanged?> Handle(
        BuildDataCopy message,
        ScreenHireDbContext db,
        IDataExportStore exports,
        TransactionalEmail email,
        DataRequestLinks links,
        IOptions<DataRequestOptions> options,
        TimeProvider time,
        ILogger<BuildDataCopy> logger,
        CancellationToken cancellationToken)
    {
        // The row locks below last only as long as the transaction Wolverine opens around this handler.
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Building a copy needs the handler's transaction, or its row locks end at once.");
        }

        // Read without tracking: a tracked row would keep these values through the re-read after the locks.
        var target = await db.DataRequests.AsNoTracking().Where(r => r.Id == message.RequestId).Select(r => r.CandidateId).SingleOrDefaultAsync(cancellationToken);
        if (target is { } lockedCandidateId)
        {
            // Erasure locks the candidate and decline locks the request, so this handler waits for both and they wait for it.
            // Candidate first, as erasure takes them. Without this an erasure could commit between the checks below and the
            // upload, and a file of the erased person's data would be written and its link emailed to them.
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM candidates WHERE id = {lockedCandidateId} FOR UPDATE", cancellationToken);
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM data_requests WHERE id = {message.RequestId} FOR UPDATE", cancellationToken);
        }

        var request = await db.DataRequests.SingleOrDefaultAsync(r => r.Id == message.RequestId, cancellationToken);
        if (request is not { Kind: DataRequestKind.Copy, Status: DataRequestStatus.Open, CandidateId: { } candidateId })
        {
            LogSkipped(logger, message.RequestId, "the request is gone, closed, not a copy or has no candidate");
            return null;
        }

        var candidate = await db.Candidates.AsNoTracking().SingleOrDefaultAsync(c => c.Id == candidateId, cancellationToken);
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == message.OrganizationId, cancellationToken);
        if (candidate is null || organization is null)
        {
            LogSkipped(logger, message.RequestId, "the candidate or the organization is gone");
            return null;
        }

        var now = time.GetUtcNow();
        var source = await DataExportLoader.LoadAsync(db, candidate, cancellationToken);
        var blobName = CandidateDataExport.BlobNameFor(message.OrganizationId, message.RequestId);
        await exports.SaveAsync(blobName, CandidateDataExport.Build(source, now), CandidateDataExport.ContentType, cancellationToken);

        // The request records the send once; a repeat changes nothing and sends nothing.
        if (!request.CopySent(blobName, now + options.Value.ExportLifetime, userId: null, now))
        {
            return null;
        }

        db.MembershipEvents.Add(new MembershipEvent
        {
            OrganizationId = message.OrganizationId,
            Kind = MembershipEventKind.DataRequestCopySent,
            ActorUserId = null,
            DataRequestId = request.Id,
            OccurredAt = now,
        });

        var reason = !candidate.CanReceiveEmail ? "the candidate's address bounced or marked an email as spam"
            : !email.IsConfigured ? "this host has no email sender"
            : !links.IsConfigured ? "the careers site address is not set up"
            : null;
        if (reason is null)
        {
            var link = links.DownloadUrl(organization.Slug, links.IssueDownload(organization.Id, request.Id));
            await email.SendAsync(DataRequestAnswerEmails.CopyEmail(candidate.Email, organization.Name, link, options.Value.ExportLifetime), cancellationToken);
            LogSent(logger, request.Id);
        }
        else
        {
            // The file is kept: the team can see the copy exists, and the candidate can ask again.
            LogNotSent(logger, request.Id, reason);
        }

        return new DataRequestChanged(message.OrganizationId, request.Id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Data request {RequestId}: no copy was built: {Reason}")]
    private static partial void LogSkipped(ILogger logger, Guid requestId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Data request {RequestId}: the copy is ready and its link was sent")]
    private static partial void LogSent(ILogger logger, Guid requestId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Data request {RequestId}: the copy is ready but its link was not sent: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid requestId, string reason);
}

/// <summary>Reads what the copy of a candidate's data holds, in a few queries for the whole candidate and never one per application.</summary>
internal static class DataExportLoader
{
    public static async Task<DataExportSource> LoadAsync(ScreenHireDbContext db, Candidate candidate, CancellationToken cancellationToken)
    {
        var applications = await db.Applications.AsNoTracking()
            .Where(a => a.CandidateId == candidate.Id).OrderBy(a => a.SubmittedAt).ThenBy(a => a.Id).ToListAsync(cancellationToken);
        var ids = applications.Select(a => a.Id).ToList();
        var jobIds = applications.Select(a => a.JobId).Distinct().ToList();
        var jobs = await db.Jobs.AsNoTracking().Where(j => jobIds.Contains(j.Id))
            .Select(j => new { j.Id, j.Title, j.Workflow }).ToDictionaryAsync(j => j.Id, cancellationToken);
        var sent = await db.ApplicationEmails.AsNoTracking().Where(e => ids.Contains(e.ApplicationId)).OrderBy(e => e.CreatedAt).ToListAsync(cancellationToken);
        var replies = await db.InboundEmails.AsNoTracking().Where(e => ids.Contains(e.ApplicationId)).OrderBy(e => e.ReceivedAt).ToListAsync(cancellationToken);
        var attachments = await db.InboundEmailAttachments.AsNoTracking().Where(a => ids.Contains(a.ApplicationId)).ToListAsync(cancellationToken);
        var notes = await db.ApplicationNotes.AsNoTracking().IgnoreAutoIncludes().Where(n => ids.Contains(n.ApplicationId)).OrderBy(n => n.CreatedAt).ToListAsync(cancellationToken);
        var files = await db.ApplicationFiles.AsNoTracking().Where(f => ids.Contains(f.ApplicationId)).OrderBy(f => f.UploadedAt).ToListAsync(cancellationToken);
        var savedNote = await db.TalentPoolEntries.AsNoTracking().Where(e => e.CandidateId == candidate.Id).Select(e => e.Note).FirstOrDefaultAsync(cancellationToken);

        var items = applications.Select(application =>
        {
            var job = jobs[application.JobId];
            var questions = ApplicationForm.QuestionsFor(job.Workflow);
            return new DataExportApplication(
                job.Title,
                application.SubmittedAt,
                job.Workflow.FindStage(application.CurrentStageId)?.Name,
                questions,
                application.Answers,
                application.PrivacyNoticeVersion,
                application.FutureRolesConsentGivenAt is not null,
                [.. sent.Where(e => e.ApplicationId == application.Id).Select(e => new DataExportEmail(e.To, e.Subject, e.TextBody, e.CreatedAt))],
                [.. replies.Where(r => r.ApplicationId == application.Id).Select(r => new DataExportReply(
                    r.FromAddress, r.Subject, r.TextBody, r.ReceivedAt,
                    [.. attachments.Where(a => a.InboundEmailId == r.Id).Select(a => a.FileName)]))],
                [.. notes.Where(n => n.ApplicationId == application.Id).Select(n => new DataExportNote(n.Text, n.CreatedAt))],
                [.. files.Where(f => f.ApplicationId == application.Id)
                    .Select(f => new DataExportFile(questions.FirstOrDefault(q => q.Id == f.QuestionId)?.Label, f.FileName))]);
        });

        return new DataExportSource(candidate.FullName, candidate.Email, candidate.Phone, candidate.CreatedAt, [.. items], savedNote);
    }
}

/// <summary>
/// Tell a candidate why the team declined their data request (docs/architecture/data-requests.md). Published with the
/// decline, through the outbox. Ids only: the reason stays on the request and is read here, so it never sits in the outbox table.
/// </summary>
public sealed record SendDataRequestDeclined(Guid OrganizationId, Guid RequestId) : ITenantMessage
{
    /// <summary>A request is declined once, so one email per request.</summary>
    public string IdempotencyKey => $"declined:{RequestId:N}";
}

/// <summary>
/// Mails the team's reason to the candidate, in ScreenHire's words around it. A request that is gone or not declined, a
/// candidate who was erased since, an address that bounced and a host with no email sender are dropped quietly: nothing can be
/// sent, and a retry would not change that.
/// </summary>
public static partial class SendDataRequestDeclinedHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(SendDataRequestDeclined.IdempotencyKey))]
    public static async Task Handle(
        SendDataRequestDeclined message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        ILogger<SendDataRequestDeclined> logger,
        CancellationToken cancellationToken)
    {
        var request = await db.DataRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == message.RequestId, cancellationToken);
        if (request is not { Status: DataRequestStatus.Declined, DeclineReason: { } reason, CandidateId: { } candidateId })
        {
            LogNotSent(logger, message.RequestId, "the request is gone, not declined or has no candidate");
            return;
        }

        var candidate = await db.Candidates.AsNoTracking().SingleOrDefaultAsync(c => c.Id == candidateId, cancellationToken);
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == message.OrganizationId, cancellationToken);
        var skip = candidate is null ? "the candidate is gone"
            : organization is null ? "the organization is gone"
            : !candidate.CanReceiveEmail ? "the candidate's address bounced or marked an email as spam"
            : !email.IsConfigured ? "this host has no email sender"
            : null;
        if (skip is not null)
        {
            LogNotSent(logger, message.RequestId, skip);
            return;
        }

        await email.SendAsync(DataRequestAnswerEmails.DeclinedEmail(candidate!.Email, organization!.Name, request.Kind, reason), cancellationToken);
        LogSent(logger, message.RequestId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Data request {RequestId}: the decline was not emailed: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid requestId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Data request {RequestId}: the decline was emailed")]
    private static partial void LogSent(ILogger logger, Guid requestId);
}

/// <summary>The words of the emails that answer a data request: ScreenHire's own, around what the team said, and no promise about any outcome.</summary>
public static class DataRequestAnswerEmails
{
    /// <summary>The email with the link to the copy. States what it is, that it expires, and when.</summary>
    public static EmailMessage CopyEmail(string to, string organizationName, string link, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(link);
        var works = Describe(lifetime);
        var subject = $"Your copy of your data from {organizationName}";
        var text = $"""
            {organizationName} has prepared the copy of your data that you asked for.

            Download it here: {link}

            The copy is a single JSON file. The link works for {works}, and the file is deleted after that. You can ask for a new copy at any time.

            If you didn't ask for this, you can ignore this email.
            """;
        var org = WebUtility.HtmlEncode(organizationName);
        var html = $"""
            <p>{org} has prepared the copy of your data that you asked for.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Download your copy</a></p>
            <p>The copy is a single JSON file. The link works for {works}, and the file is deleted after that. You can ask for a new copy at any time.</p>
            <p>If you didn't ask for this, you can ignore this email.</p>
            """;
        return new EmailMessage(to, subject, text, html);
    }

    /// <summary>The email that carries the team's reason for declining. The reason is the team's words; ScreenHire adds only what the message is about.</summary>
    public static EmailMessage DeclinedEmail(string to, string organizationName, DataRequestKind kind, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var what = kind == DataRequestKind.Copy ? "a copy of your data" : "erasure of your data";
        var subject = $"About your request to {organizationName}";
        var text = $"""
            You asked {organizationName} for {what}. The team has not done what you asked, and gave this reason:

            {reason}

            This message is from {organizationName}'s team, sent through ScreenHire. If you have questions about it, contact {organizationName} directly.
            """;
        var org = WebUtility.HtmlEncode(organizationName);
        var html = $"""
            <p>You asked {org} for {what}. The team has not done what you asked, and gave this reason:</p>
            <blockquote>{WebUtility.HtmlEncode(reason).ReplaceLineEndings("<br>")}</blockquote>
            <p>This message is from {org}'s team, sent through ScreenHire. If you have questions about it, contact {org} directly.</p>
            """;
        return new EmailMessage(to, subject, text, html);
    }

    /// <summary>A span as the email says it: whole days as days, otherwise as <see cref="DataRequestEmails.Describe"/> does.</summary>
    public static string Describe(TimeSpan lifetime)
    {
        if (lifetime >= TimeSpan.FromDays(1) && lifetime.Ticks % TimeSpan.TicksPerDay == 0)
        {
            var days = (int)lifetime.TotalDays;
            return days == 1 ? "1 day" : string.Create(CultureInfo.InvariantCulture, $"{days} days");
        }

        return DataRequestEmails.Describe(lifetime);
    }
}

/// <summary>
/// The daily sweep of copies whose download window ended (docs/architecture/data-requests.md). It has no tenant of its own,
/// and row-level security shows the worker no tenant rows without one, so it fans out one
/// <see cref="PurgeOrganizationDataExports"/> per organization.
/// </summary>
public sealed record PurgeExpiredDataExports;

/// <summary>Deletes one organization's copies whose download window ended by <paramref name="Now"/>.</summary>
public sealed record PurgeOrganizationDataExports(Guid OrganizationId, DateTimeOffset Now) : ITenantMessage;

public static class PurgeExpiredDataExportsHandler
{
    public static async Task<OutgoingMessages> Handle(
        PurgeExpiredDataExports message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new PurgeOrganizationDataExports(id, now)));
        return outgoing;
    }
}

public static partial class PurgeOrganizationDataExportsHandler
{
    // Idempotent without a key: an expired request is selected until its columns are cleared, and deleting a blob that is gone is a no-op.
    public static async Task<OutgoingMessages> Handle(
        PurgeOrganizationDataExports message,
        ScreenHireDbContext db,
        IDataExportStore exports,
        ILogger<PurgeOrganizationDataExports> logger,
        CancellationToken cancellationToken)
    {
        var expired = await db.DataRequests
            .Where(r => r.ExportBlobName != null && r.ExportExpiresAt != null && r.ExportExpiresAt <= message.Now)
            .ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        foreach (var request in expired)
        {
            await exports.DeleteAsync(request.ExportBlobName!, cancellationToken);
            request.ClearExport();
            outgoing.Add(new DataRequestChanged(message.OrganizationId, request.Id));
        }

        if (expired.Count > 0)
        {
            LogPurged(logger, message.OrganizationId, expired.Count);
        }

        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: deleted {Copies} expired copies of candidates' data")]
    private static partial void LogPurged(ILogger logger, Guid organizationId, int copies);
}
