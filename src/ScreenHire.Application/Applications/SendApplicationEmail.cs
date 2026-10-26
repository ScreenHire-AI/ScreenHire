using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Applications;

/// <summary>
/// A person's email to an application's candidate, by hand or as one of a selection. The API rendered it for this
/// candidate and stored the copy (<see cref="ApplicationEmail"/>) under <see cref="SendId"/> in the same transaction, so
/// the message carries ids only: the wording a person typed can hold personal data, which never travels in a message
/// (messaging.md). <see cref="SendId"/> also becomes the id of the timeline entry, so a redelivered message finds its
/// entry and sends nothing twice.
/// </summary>
public sealed record SendApplicationEmail(Guid OrganizationId, Guid ApplicationId, Guid SendId, Guid ActorUserId) : ITenantMessage;

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
public sealed record ApplicationEmailed(Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid EventId, DateTimeOffset SentAt) : IJobEvent;

/// <summary>
/// What happened to one email to a candidate: sent, with its timeline entry, or not sent and why. <c>templateMissing</c>
/// says the template it named no longer exists, which an automation reports as a failure rather than a skip.
/// </summary>
public sealed record ApplicationEmailResult(string Detail, ApplicationEvent? Event, bool TemplateMissing = false);

/// <summary>
/// Renders, keeps and sends emails to an application's candidate, and records each on the timeline (an
/// <see cref="ApplicationEventType.EmailSent"/> entry pointing at its copy). Shared by a person's sends (the API and
/// <see cref="SendApplicationEmailHandler"/>) and the SendEmail, Reject and SendQuestionnaire actions, so every email a
/// candidate gets is kept as it went, appears in Activity and Messages, and keeps the same rules.
/// </summary>
public static class ApplicationEmails
{
    /// <summary>
    /// What a stored copy says in place of the follow-up questions email's signed link. The link lets whoever holds it
    /// answer as the candidate, so it goes only to the candidate and never into the copy the team reads.
    /// </summary>
    public const string QuestionnaireLinkNotKept = "[the candidate's link, not kept in this copy]";

    /// <summary>
    /// Whether <paramref name="templateId"/> skips an application in <paramref name="stage"/>: the stage-change email
    /// never goes to one that has ended ("moved to the next step: Rejected" would be a rejection no Reject action
    /// guarded). The API counts the skips when it queues a send; the worker re-checks when it goes.
    /// </summary>
    public static bool Skips(string? templateId, WorkflowStage? stage) =>
        templateId == EmailTemplateIds.StageChange && stage?.Kind is StageKind.Rejected or StageKind.Withdrawn;

    /// <summary>What a template may say about this candidate's application.</summary>
    public static EmailTemplateModel ModelFor(
        Candidate candidate, string jobTitle, string organizationName, string? stageName, string? reason = null, string? questionnaireLink = null) =>
        new(candidate.FullName, jobTitle, organizationName, stageName, reason, questionnaireLink, candidate.FirstName, candidate.LastName);

    /// <summary>
    /// The copy of <paramref name="content"/> rendered for this candidate, to keep before a person's send goes. The
    /// caller validated the wording and adds the copy to the context.
    /// </summary>
    public static ApplicationEmail Prepare(
        EmailTemplates renderer,
        JobApplication application,
        Candidate candidate,
        string jobTitle,
        string organizationName,
        string? stageName,
        string? templateId,
        EmailTemplateContent content,
        Guid sendId,
        DateTimeOffset now)
    {
        var message = renderer.Render(content, candidate.Email, ModelFor(candidate, jobTitle, organizationName, stageName), templateId);
        return application.PrepareEmail(sendId, templateId, message.To, message.Subject, message.TextBody, message.HtmlBody ?? "", now);
    }

    /// <summary>
    /// Sends a copy a person's send stored, exactly as it is, and records it on the timeline. The caller checked that it
    /// may still go and saves the entry.
    /// </summary>
    public static async Task<ApplicationEvent> SendCopyAsync(
        ScreenHireDbContext db,
        TransactionalEmail email,
        JobApplication application,
        ApplicationEmail copy,
        Guid? actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await email.SendAsync(new EmailMessage(copy.To, copy.Subject, copy.TextBody, copy.HtmlBody), cancellationToken);
        var entry = application.EmailSent(copy, now, actorUserId);
        db.ApplicationEvents.Add(entry);
        return entry;
    }

    /// <summary>
    /// Renders the organization's wording of <paramref name="templateId"/> for the candidate and sends it, with
    /// <paramref name="reason"/> for the rejection email and <paramref name="questionnaireLink"/> for the follow-up
    /// questions email, then keeps the copy and records it. The copy says <see cref="QuestionnaireLinkNotKept"/> in
    /// place of the link. Not sent: no email sender, a template the organization deleted, or a send that
    /// <see cref="Skips"/>. The caller saves the copy and the entry.
    /// </summary>
    public static async Task<ApplicationEmailResult> SendAsync(
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplates renderer,
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
        var model = ModelFor(candidate, job.Title, organization.Name, stage?.Name, reason, questionnaireLink);
        var sent = await email.SendAsync(templateId, template.Content, candidate.Email, model, cancellationToken);
        var kept = questionnaireLink is null
            ? sent
            : renderer.Render(template.Content, candidate.Email, model with { QuestionnaireLink = QuestionnaireLinkNotKept }, templateId);
        var copy = application.PrepareEmail(eventId, templateId, kept.To, kept.Subject, kept.TextBody, kept.HtmlBody ?? "", now);
        var entry = application.EmailSent(copy, now, actorUserId, automationRunId);
        db.ApplicationEmails.Add(copy);
        db.ApplicationEvents.Add(entry);
        return new($"Sent the \"{template.Name}\" email.", entry);
    }
}

/// <summary>
/// Sends one email a person asked for, in the worker: the copy the API stored, exactly as it is. Idempotent on its own:
/// the send's id is its timeline entry's id, so a redelivery that finds the entry does nothing. The email goes before the
/// entry commits, so a crash between the two can repeat it once, as with the SendEmail action.
/// </summary>
public static partial class SendApplicationEmailHandler
{
    public static async Task<OutgoingMessages> Handle(
        SendApplicationEmail message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        TimeProvider time,
        ILogger<SendApplicationEmail> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        if (await db.ApplicationEvents.AnyAsync(e => e.Id == message.SendId, cancellationToken))
        {
            return outgoing; // sent already
        }

        var copy = await db.ApplicationEmails.SingleOrDefaultAsync(e => e.Id == message.SendId, cancellationToken);
        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == message.ApplicationId, cancellationToken);
        if (copy is null || application is null)
        {
            // The application was deleted since (its copy with it), or the message was queued before copies were kept.
            LogNotSent(logger, message.SendId, "the application or its copy of the email is gone");
            return outgoing;
        }

        if (!email.IsConfigured)
        {
            // This host has no email sender. The copy of an email that never went is not kept.
            db.ApplicationEmails.Remove(copy);
            LogNotSent(logger, message.SendId, "this host has no email sender");
            return outgoing;
        }

        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        if (copy.TemplateId is { } templateId
            && (EmailTemplateIds.IsActionOnly(templateId) || ApplicationEmails.Skips(templateId, job.Workflow.FindStage(application.CurrentStageId))))
        {
            // An email only an action sends (the API refuses both), or the stage-change email to an application that
            // ended since it was queued. The copy of an email that never went is not kept.
            db.ApplicationEmails.Remove(copy);
            LogNotSent(logger, message.SendId, "the email does not fit the application any more");
            return outgoing;
        }

        var now = time.GetUtcNow();
        await ApplicationEmails.SendCopyAsync(db, email, application, copy, message.ActorUserId, now, cancellationToken);
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
        EmailTemplates renderer,
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
            db, email, renderer, store, application, job, message.TemplateId, message.Reason, message.QuestionnaireLink, message.SendId,
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
