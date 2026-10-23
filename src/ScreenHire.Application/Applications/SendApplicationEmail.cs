using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Applications;

/// <summary>
/// A person sends one of the organization's email templates to an application's candidate, by hand or as one of a
/// selection. <see cref="SendId"/> is created once, in the request that queued the send, and becomes the id of the
/// timeline entry, so a redelivered message finds its entry and sends nothing twice.
/// </summary>
public sealed record SendApplicationEmail(Guid OrganizationId, Guid ApplicationId, string TemplateId, Guid SendId, Guid ActorUserId)
    : ITenantMessage;

/// <summary>
/// An automation action's email to an application's candidate (SendEmail, Reject, SendQuestionnaire), queued through the
/// outbox in the action's own transaction. It leaves only if the action commits, so an action that is retried after a
/// conflict (two automations changing one application at once) sends one email, not two. <see cref="SendId"/> becomes
/// the timeline entry's id, so a redelivered message sends nothing twice. The action checked the template and the
/// stage; the handler checks again when it sends.
/// </summary>
/// <remarks>
/// <c>reason</c>: The rejection email's reason. <c>questionnaireLink</c>: The follow-up questions email's signed link.
/// </remarks>
public sealed record SendAutomationEmail(
    Guid OrganizationId,
    Guid ApplicationId,
    string TemplateId,
    Guid SendId,
    Guid AutomationRunId,
    string? Reason = null,
    string? QuestionnaireLink = null) : ITenantMessage;

/// <summary>An email went to an application's candidate and is on its timeline: open dashboards refetch its activity.</summary>
public sealed record ApplicationEmailed(Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid EventId, DateTimeOffset SentAt) : ITenantEvent;

/// <summary>
/// What happened to one email to a candidate: sent, with its timeline entry, or not sent and why. <c>templateMissing</c>
/// says the template it named no longer exists, which an automation reports as a failure rather than a skip.
/// </summary>
public sealed record ApplicationEmailResult(string Detail, ApplicationEvent? Event, bool TemplateMissing = false);

/// <summary>
/// Sends a template to an application's candidate and records it on the timeline (an <see cref="ApplicationEventType.EmailSent"/>
/// entry). Shared by a person's sends (<see cref="SendApplicationEmailHandler"/>) and the SendEmail and Reject actions,
/// so every email a candidate gets appears in Activity and every send keeps the same rules.
/// </summary>
public static class ApplicationEmails
{
    /// <summary>
    /// Whether <paramref name="templateId"/> skips an application in <paramref name="stage"/>: the stage-change email
    /// never goes to one that has ended ("moved to the next step: Rejected" would be a rejection no Reject action
    /// guarded). The API counts the skips when it queues a send; this method re-checks when it goes.
    /// </summary>
    public static bool Skips(string templateId, WorkflowStage? stage) =>
        templateId == EmailTemplateIds.StageChange && stage?.Kind is StageKind.Rejected or StageKind.Withdrawn;

    /// <summary>
    /// Renders the organization's wording of <paramref name="templateId"/> for the candidate and sends it, with
    /// <paramref name="reason"/> for the rejection email and <paramref name="questionnaireLink"/> for the follow-up
    /// questions email. Not sent: no
    /// email sender, a template the organization deleted, or a send that <see cref="Skips"/>. The caller saves the entry.
    /// </summary>
    public static async Task<ApplicationEmailResult> SendAsync(
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplateStore store,
        JobApplication application,
        Job job,
        string templateId,
        string? reason,
        string? questionnaireLink,
        Guid eventId,
        Guid? actorUserId,
        Guid? automationRunId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!email.IsConfigured)
        {
            return new("Email is not set up, so nothing was sent.", null);
        }

        var stage = job.Workflow.FindStage(application.CurrentStageId);
        if (Skips(templateId, stage))
        {
            return new("The application has ended, so the stage-change email was not sent.", null);
        }

        if (await store.FindAsync(db, templateId, cancellationToken) is not { } template)
        {
            return new("The email template no longer exists, so nothing was sent.", null, TemplateMissing: true);
        }

        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == application.CandidateId, cancellationToken);
        var organization = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == application.OrganizationId, cancellationToken);
        var sent = await email.SendAsync(
            templateId,
            template.Content,
            candidate.Email,
            new EmailTemplateModel(candidate.FullName, job.Title, organization.Name, stage?.Name, reason, questionnaireLink),
            cancellationToken);
        var entry = application.EmailSent(eventId, templateId, sent.Subject, now, actorUserId, automationRunId);
        db.ApplicationEvents.Add(entry);
        return new($"Sent the \"{template.Name}\" email.", entry);
    }
}

/// <summary>
/// Sends one email a person asked for, in the worker. Idempotent on its own: the send's id is its timeline entry's id,
/// so a redelivery that finds the entry does nothing. The email goes before the entry commits, so a crash between the
/// two can repeat it once, as with the SendEmail action.
/// </summary>
public static partial class SendApplicationEmailHandler
{
    public static async Task<OutgoingMessages> Handle(
        SendApplicationEmail message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplateStore store,
        TimeProvider time,
        ILogger<SendApplicationEmail> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        if (await db.ApplicationEvents.AnyAsync(e => e.Id == message.SendId, cancellationToken))
        {
            return outgoing; // sent already
        }

        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == message.ApplicationId, cancellationToken);
        if (application is null || EmailTemplateIds.IsActionOnly(message.TemplateId))
        {
            // Gone since it was queued, or an email only an action sends: the rejection email (Reject) or the follow-up
            // questions email (SendQuestionnaire). The API refuses both too.
            LogNotSent(logger, message.SendId, "the application or the template cannot be sent to");
            return outgoing;
        }

        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        var now = time.GetUtcNow();
        var result = await ApplicationEmails.SendAsync(
            db, email, store, application, job, message.TemplateId, reason: null, questionnaireLink: null, message.SendId, message.ActorUserId, automationRunId: null, now, cancellationToken);
        if (result.Event is null)
        {
            LogNotSent(logger, message.SendId, result.Detail);
            return outgoing;
        }

        outgoing.Add(new ApplicationEmailed(application.OrganizationId, application.Id, application.JobId, message.SendId, now));
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email send {SendId} was not sent: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid sendId, string reason);
}

/// <summary>
/// Sends one email an automation action queued, in the worker, linked to its run on the timeline. Idempotent like a
/// person's send: the send id is the timeline entry's id. The email goes before the entry commits, so a crash between
/// the two can repeat it once.
/// </summary>
public static partial class SendAutomationEmailHandler
{
    public static async Task<OutgoingMessages> Handle(
        SendAutomationEmail message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplateStore store,
        TimeProvider time,
        ILogger<SendAutomationEmail> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        if (await db.ApplicationEvents.AnyAsync(e => e.Id == message.SendId, cancellationToken))
        {
            return outgoing; // sent already
        }

        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == message.ApplicationId, cancellationToken);
        if (application is null)
        {
            LogNotSent(logger, message.SendId, message.AutomationRunId, "the application was deleted");
            return outgoing;
        }

        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        var now = time.GetUtcNow();
        var result = await ApplicationEmails.SendAsync(
            db, email, store, application, job, message.TemplateId, message.Reason, message.QuestionnaireLink, message.SendId,
            actorUserId: null, message.AutomationRunId, now, cancellationToken);
        if (result.Event is null)
        {
            LogNotSent(logger, message.SendId, message.AutomationRunId, result.Detail);
            return outgoing;
        }

        outgoing.Add(new ApplicationEmailed(application.OrganizationId, application.Id, application.JobId, message.SendId, now));
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Automation email {SendId} of run {RunId} was not sent: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid sendId, Guid runId, string reason);
}
