using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.TalentPool;

/// <summary>
/// A person's invitation to a candidate in the talent pool to apply for <see cref="JobId"/>, queued through the outbox
/// (docs/architecture/talent-pool.md). <see cref="ApplicationId"/> is the application the candidate was kept in mind
/// from, which the email and its Activity belong to. The message carries ids only; the worker renders the organization's
/// wording of <see cref="EmailTemplateIds.TalentPoolInvitation"/> and issues the link to stop being kept, so that link
/// never sits in a message or in the copy the team reads. <see cref="SendId"/> becomes the email's timeline entry, so a
/// redelivered message sends nothing twice.
/// </summary>
public sealed record SendTalentPoolInvitation(Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid SendId, Guid ActorUserId) : ITenantMessage;

/// <summary>
/// Sends one invitation, in the worker. It checks again what the API checked when it queued it, because time passed: the
/// candidate is still in the pool with a permission that holds, and the job still takes applications. An invitation that
/// no longer fits is dropped with a warning and leaves nothing on the timeline. The email goes before its entries
/// commit, so a crash between the two can repeat it once, as with every email to a candidate.
/// </summary>
public static partial class SendTalentPoolInvitationHandler
{
    public static async Task<OutgoingMessages> Handle(
        SendTalentPoolInvitation message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        TalentPoolLinks links,
        TimeProvider time,
        ILogger<SendTalentPoolInvitation> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        if (await db.ApplicationEvents.AnyAsync(e => e.Id == message.SendId, cancellationToken))
        {
            return outgoing; // sent already
        }

        var now = time.GetUtcNow();
        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == message.ApplicationId, cancellationToken);
        var entry = await db.TalentPoolEntries.SingleOrDefaultAsync(e => e.ApplicationId == message.ApplicationId, cancellationToken);
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == message.JobId, cancellationToken);
        var months = await TalentPoolMembership.MonthsAsync(db, cancellationToken);
        var reason = application is null || entry is null ? "the candidate is no longer in the talent pool"
            : !application.AllowsFutureRoles(months, now) ? "the candidate's permission for future roles has ended"
            : job is not { Status: JobStatus.Open } ? "the job no longer takes applications"
            : !email.IsConfigured ? "this host has no email sender"
            : !links.IsConfigured ? "the careers site address is not set up"
            : null;
        if (reason is not null)
        {
            LogNotSent(logger, message.SendId, reason);
            return outgoing;
        }

        if (await store.FindAsync(db, EmailTemplateIds.TalentPoolInvitation, cancellationToken) is not { } template)
        {
            LogNotSent(logger, message.SendId, "the invitation template is missing");
            return outgoing;
        }

        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == application!.CandidateId, cancellationToken);
        var organization = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == application!.OrganizationId, cancellationToken);
        var model = ApplicationEmails.ModelFor(candidate, job!.Title, organization.Name, stageName: null) with
        {
            JobLink = links.JobUrl(organization.Slug, job.Slug),
            RemoveLink = links.RemoveUrl(organization.Slug, links.Issue(organization.Id, candidate.Id)),
        };
        await email.SendAsync(EmailTemplateIds.TalentPoolInvitation, template.Content, candidate.Email, model, cancellationToken);

        // The team's copy says where the candidate's own link was: it lets whoever holds it stop the candidate being kept.
        var kept = renderer.Render(
            template.Content, candidate.Email, model with { RemoveLink = ApplicationEmails.QuestionnaireLinkNotKept }, EmailTemplateIds.TalentPoolInvitation);
        var copy = application!.PrepareEmail(message.SendId, EmailTemplateIds.TalentPoolInvitation, kept.To, kept.Subject, kept.TextBody, kept.HtmlBody ?? "", now);
        db.ApplicationEmails.Add(copy);
        db.ApplicationEvents.Add(application.InvitedToApply(job.Id, now, message.ActorUserId));
        db.ApplicationEvents.Add(application.EmailSent(copy, now, message.ActorUserId));
        entry!.Invited(job.Id, now);

        outgoing.Add(new ApplicationEmailed(application.OrganizationId, application.Id, application.JobId, message.SendId, now));
        outgoing.Add(TalentPoolMembership.Changed(entry, now));
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Talent pool invitation {SendId} was not sent: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid sendId, string reason);
}
