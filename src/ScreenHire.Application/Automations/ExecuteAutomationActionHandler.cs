using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Slack;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Automations;

/// <summary>
/// Runs one action of an automation run in the worker, records what happened on its <see cref="AutomationActionRun"/>,
/// and sends the next action, or finishes the run after the last. Everything the action changes in the database (a
/// move, a tag, a task) commits with its status in one transaction, so a redelivery finds the action done and does
/// nothing. An email is queued in that transaction (<see cref="SendAutomationEmail"/>), so it leaves only once the action
/// commits: an action retried after a conflict sends one email. A webhook happens before the commit: a crash in between
/// can repeat it once, which the receiver's idempotency key covers.
/// </summary>
/// <remarks>
/// A failure that retrying cannot fix (a reviewer who left the organization) fails the action at once. Anything that
/// throws goes through the retry policy; after the last retry <see cref="AutomationFailures"/> records the failure.
/// </remarks>
public static partial class ExecuteAutomationActionHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(ExecuteAutomationAction.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        ExecuteAutomationAction message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplateStore templates,
        IWebhookSender webhooks,
        QuestionnaireLinks links,
        SlackAccess slack,
        TimeProvider time,
        ILogger<ExecuteAutomationAction> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var action = await db.AutomationActionRuns.SingleOrDefaultAsync(a => a.Id == message.ActionRunId, cancellationToken)
            ?? throw new InvalidOperationException($"Automation action {message.ActionRunId} is not visible to organization {message.OrganizationId}.");
        var run = await db.AutomationRuns.SingleAsync(r => r.Id == action.RunId, cancellationToken);
        if (action.Status != AutomationActionStatus.Pending || run.Status != AutomationRunStatus.Running)
        {
            return outgoing; // done already: a redelivery, or the run stopped
        }

        var application = await db.Applications.SingleAsync(a => a.Id == run.ApplicationId, cancellationToken);
        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == run.JobId, cancellationToken);
        var context = new ActionContext(db, email, templates, webhooks, links, slack, run, action, application, job, time.GetUtcNow(), outgoing);

        var (status, detail) = await ExecuteAsync(context, cancellationToken);
        var now = context.Now;
        LogAction(logger, action.Action.Type, action.Index, run.Id, status);
        switch (status)
        {
            case AutomationActionStatus.Completed:
                action.Complete(detail, now);
                break;
            case AutomationActionStatus.Skipped:
                action.Skip(detail, now);
                break;
            default:
                action.Fail(detail, now);
                run.Fail(detail, now);
                outgoing.Add(Finished(run, now));
                return outgoing;
        }

        var next = await db.AutomationActionRuns.AsNoTracking()
            .Where(a => a.RunId == run.Id && a.Index == action.Index + 1)
            .Select(a => (Guid?)a.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (next is { } nextId)
        {
            outgoing.Add(new ExecuteAutomationAction(run.OrganizationId, nextId, run.ActionKey(action.Index + 1)));
        }
        else
        {
            run.Complete(now);
            outgoing.Add(Finished(run, now));
        }

        return outgoing;
    }

    internal static AutomationRunFinished Finished(AutomationRun run, DateTimeOffset now) =>
        new(run.OrganizationId, run.ApplicationId, run.JobId, run.Id, run.AutomationId, run.Status, now);

    private static Task<(AutomationActionStatus, string)> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var action = context.Action.Action;
        return action.Type switch
        {
            ActionType.SendEmail => SendEmailAsync(context, action.EmailTemplate!, reason: null, questionnaireLink: null, cancellationToken),
            ActionType.SendQuestionnaire => SendQuestionnaireAsync(context, action.QuestionnaireId!, cancellationToken),
            ActionType.MoveToStage => Task.FromResult(MoveToStage(context, action.StageId!)),
            ActionType.AddTag => Task.FromResult(AddTag(context, action.Tag!)),
            ActionType.AssignReviewer => AssignReviewerAsync(context, action.AssigneeUserId, cancellationToken),
            ActionType.NotifyTeam => Task.FromResult(NotifyTeam(context)),
            ActionType.CreateTask => Task.FromResult(AddTask(context, ApplicationTaskKind.Task, action.Note!, null, "Created a task.")),
            ActionType.QueueForRejectionReview => Task.FromResult(
                AddTask(context, ApplicationTaskKind.RejectionReview, action.Reason!, null, "Queued for a person to decide.")),
            ActionType.Reject => RejectAsync(context, action, cancellationToken),
            ActionType.CallWebhook => CallWebhookAsync(context, new Uri(action.Url!), cancellationToken),
            _ => Task.FromResult(Skipped($"{action.Type} is not available yet, so nothing happened.")),
        };
    }

    // Checks what can be checked now (email set up, the stage-change rule, the template still there) and queues the email
    // in this action's transaction, so it goes only if the action commits. SendAutomationEmailHandler sends it and puts
    // it on the timeline, linked to this run (ApplicationEmails).
    private static async Task<(AutomationActionStatus, string)> SendEmailAsync(
        ActionContext context, string templateId, string? reason, string? questionnaireLink, CancellationToken cancellationToken)
    {
        var (template, refusal) = await TemplateToSendAsync(context, templateId, cancellationToken);
        if (template is null)
        {
            return refusal;
        }

        QueueEmail(context, templateId, reason, questionnaireLink);
        return Completed($"Sending the \"{template.Name}\" email.");
    }

    // The template, or why the email cannot go now.
    private static async Task<(OrganizationEmailTemplate? Template, (AutomationActionStatus, string) Refusal)> TemplateToSendAsync(
        ActionContext context, string templateId, CancellationToken cancellationToken)
    {
        if (!context.Email.IsConfigured)
        {
            return (null, Skipped("Email is not set up, so nothing was sent."));
        }

        if (ApplicationEmails.Skips(templateId, CurrentStage(context)))
        {
            return (null, Skipped("The application has ended, so the stage-change email was not sent."));
        }

        // An organization template deleted after the workflow was saved is a broken action, not a quiet skip.
        return await context.Templates.FindAsync(context.Db, templateId, cancellationToken) is { } template
            ? (template, default)
            : (null, Failed("The email template no longer exists, so nothing was sent."));
    }

    private static void QueueEmail(ActionContext context, string templateId, string? reason, string? questionnaireLink) =>
        context.Outgoing.Add(new SendAutomationEmail(
            context.Run.OrganizationId, context.Application.Id, templateId, Guid.CreateVersion7(), context.Run.Id, reason, questionnaireLink));

    /// <summary>
    /// Emails the candidate a signed link to a follow-up questionnaire (workflow-engine.md §8). One request per application
    /// and questionnaire: the first send creates it, a later one sends a fresh link to the same request while it is
    /// unanswered, and once it is answered nothing is sent. Skipped without email, as SendEmail is, and for an application
    /// that has ended. Every check runs before the request changes: a send that stops must not leave a request that
    /// looks sent again, with a fresh expiry and a higher count, while no email went.
    /// </summary>
    private static async Task<(AutomationActionStatus, string)> SendQuestionnaireAsync(
        ActionContext context, string questionnaireId, CancellationToken cancellationToken)
    {
        var (template, refusal) = await TemplateToSendAsync(context, EmailTemplateIds.QuestionnaireRequest, cancellationToken);
        if (template is null)
        {
            return refusal;
        }

        if (!context.Links.IsConfigured)
        {
            return Failed("The careers site address is not set up, so no link could be sent.");
        }

        if (CurrentStage(context)?.Kind.IsTerminal() == true)
        {
            return Skipped("The application has ended, so no questionnaire was sent.");
        }

        if (context.Job.Workflow.FindFollowUp(questionnaireId) is not { } questionnaire)
        {
            return Failed($"The workflow has no follow-up questionnaire '{questionnaireId}' any more.");
        }

        var application = context.Application;
        var request = await context.Db.QuestionnaireRequests
            .SingleOrDefaultAsync(r => r.ApplicationId == application.Id && r.QuestionnaireId == questionnaireId, cancellationToken);
        if (request is { IsCompleted: true })
        {
            return Skipped($"The candidate has answered \"{questionnaire.Name}\" already, so nothing was sent.");
        }

        if (request is null)
        {
            request = QuestionnaireRequest.Create(application, questionnaire, context.Now);
            context.Db.QuestionnaireRequests.Add(request);
        }
        else
        {
            request.Resend(questionnaire, context.Now);
        }

        var slug = await context.Db.Organizations.AsNoTracking()
            .Where(o => o.Id == application.OrganizationId).Select(o => o.Slug).SingleAsync(cancellationToken);
        QueueEmail(context, EmailTemplateIds.QuestionnaireRequest, reason: null, context.Links.Url(slug, context.Links.Issue(request)));
        context.Db.ApplicationEvents.Add(request.SentEvent(context.Now, context.Run.Id));
        return Completed(request.TimesSent == 1
            ? $"Sent the \"{questionnaire.Name}\" questionnaire."
            : $"Sent the \"{questionnaire.Name}\" questionnaire again, with a fresh link.");
    }

    private static (AutomationActionStatus, string) MoveToStage(ActionContext context, string stageId)
    {
        if (CurrentStage(context)?.Kind.IsTerminal() == true)
        {
            return Skipped("The application has already ended, so automations no longer move it.");
        }

        if (context.Job.Workflow.FindStage(stageId) is not { } target)
        {
            return Failed($"The workflow has no stage '{stageId}' any more.");
        }

        return Move(context, target, $"Moved by the automation \"{context.Run.AutomationName}\".")
            ? Completed($"Moved to {target.Name}.")
            : Skipped($"Already in {target.Name}.");
    }

    // The move, its timeline entry linked to this run, and the ApplicationMoved event that fires the next stage's
    // triggers. False when the application is already there.
    private static bool Move(ActionContext context, WorkflowStage target, string note)
    {
        var from = context.Application.CurrentStageId;
        if (context.Application.MoveTo(target, context.Now, actorUserId: null, note, context.Run.Id) is not { } moved)
        {
            return false;
        }

        context.Db.ApplicationEvents.Add(moved);
        context.Outgoing.Add(new ApplicationMoved(
            context.Application.OrganizationId, context.Application.Id, context.Application.JobId, from, target.Id, moved.Id, context.Now));
        return true;
    }

    private static (AutomationActionStatus, string) AddTag(ActionContext context, string tag)
    {
        if (context.Application.AddTag(tag, context.Now, actorUserId: null, context.Run.Id) is not { } added)
        {
            return Skipped($"Already tagged {tag}.");
        }

        context.Db.ApplicationEvents.Add(added);
        return Completed($"Tagged {tag}.");
    }

    private static async Task<(AutomationActionStatus, string)> AssignReviewerAsync(
        ActionContext context, Guid? assigneeUserId, CancellationToken cancellationToken)
    {
        if (assigneeUserId is { } userId
            && !await context.Db.OrganizationMembers.AnyAsync(m => m.UserId == userId && m.OrganizationId == context.Run.OrganizationId, cancellationToken))
        {
            return Failed("The reviewer is not a member of the organization any more.");
        }

        return AddTask(context, ApplicationTaskKind.Review, "Review this application.", assigneeUserId,
            assigneeUserId is null ? "Asked the hiring team for a review." : "Assigned a reviewer.");
    }

    private static (AutomationActionStatus, string) NotifyTeam(ActionContext context)
    {
        context.Outgoing.Add(new TeamNotified(
            context.Run.OrganizationId, context.Application.Id, context.Application.JobId, context.Run.Id, context.Now));
        return Completed("Notified the hiring team.");
    }

    private static (AutomationActionStatus, string) AddTask(
        ActionContext context, ApplicationTaskKind kind, string note, Guid? assigneeUserId, string detail)
    {
        var task = new ApplicationTask
        {
            OrganizationId = context.Run.OrganizationId,
            ApplicationId = context.Application.Id,
            Kind = kind,
            Note = note,
            AssigneeUserId = assigneeUserId,
            StageId = context.Application.CurrentStageId,
            AutomationRunId = context.Run.Id,
            CreatedAt = context.Now,
        };
        context.Db.ApplicationTasks.Add(task);
        // A task given to one person reaches them in Slack too, when they linked it (docs/architecture/slack.md).
        if (assigneeUserId is { } assignee && context.Slack.IsConfigured)
        {
            context.Outgoing.Add(new SendSlackDirectMessage(
                task.OrganizationId, SlackDirectMessageKind.Task, task.Id, assignee, task.ApplicationId, context.Application.JobId, ActorUserId: null));
        }

        return Completed(detail);
    }

    /// <summary>
    /// The one action that rejects without a person, allowed only for an opted-in knockout automation (the validator
    /// checked, and this re-checks the workflow and the opt-in). The candidate is always told why; if email is not set up, the candidate
    /// cannot be told, so a person decides instead (workflow-engine.md §2, guardrail 2).
    /// </summary>
    private static async Task<(AutomationActionStatus, string)> RejectAsync(
        ActionContext context, AutomationAction action, CancellationToken cancellationToken)
    {
        // The run was recorded when the workflow validated; re-check now, because the workflow or the rules may have
        // changed while the action waited. Anything short of a valid, opted-in knockout from the same workflow version
        // (the conditions that matched are that version's) goes to a person.
        var automation = context.Job.Workflow.Automations.FirstOrDefault(a => a.Id == context.Run.AutomationId);
        var sameVersion = context.Run.WorkflowVersion == context.Job.WorkflowVersion;
        var stillValid = DefinitionValidator.Validate(context.Job.Workflow).Count == 0;
        if (automation is not { AllowAutoReject: true } || !sameVersion || !stillValid || !context.Email.IsConfigured)
        {
            return AddTask(context, ApplicationTaskKind.RejectionReview, action.Reason!, null,
                automation is not { AllowAutoReject: true } ? "Queued for a person to decide: the automation is no longer opted in to rejecting."
                : !sameVersion ? "Queued for a person to decide: the job's workflow changed after the conditions were checked."
                : !stillValid ? "Queued for a person to decide: the job's workflow no longer passes validation."
                : "Queued for a person to decide: email is not set up, so the candidate could not be told why.");
        }

        if (CurrentStage(context)?.Kind.IsTerminal() == true)
        {
            return Skipped("The application has already ended.");
        }

        if (context.Job.Workflow.Stages.FirstOrDefault(s => s.Kind == StageKind.Rejected) is not { } rejected)
        {
            return Failed("The workflow has no rejected stage.");
        }

        await SendEmailAsync(context, EmailTemplateIds.ApplicationRejected, action.Reason, questionnaireLink: null, cancellationToken);
        Move(context, rejected, $"Rejected by the automation \"{context.Run.AutomationName}\": {action.Reason}");
        return Completed("Rejected, and told the candidate why.");
    }

    private static async Task<(AutomationActionStatus, string)> CallWebhookAsync(ActionContext context, Uri url, CancellationToken cancellationToken)
    {
        if (!context.Webhooks.IsConfigured)
        {
            return Failed("Webhook signing is not set up (ScreenHire:Webhooks:SigningKey), so nothing was sent.");
        }

        var run = context.Run;
        var status = await context.Webhooks.PostAsync(
            url,
            run.ActionKey(context.Action.Index),
            new WebhookPayload("automation.action", run.OrganizationId, run.ApplicationId, run.JobId, run.AutomationId, run.Id, run.EventId, context.Now),
            cancellationToken);
        return Completed($"Called the webhook ({status}).");
    }

    private static WorkflowStage? CurrentStage(ActionContext context) => context.Job.Workflow.FindStage(context.Application.CurrentStageId);

    private static (AutomationActionStatus, string) Completed(string detail) => (AutomationActionStatus.Completed, detail);

    private static (AutomationActionStatus, string) Skipped(string detail) => (AutomationActionStatus.Skipped, detail);

    private static (AutomationActionStatus, string) Failed(string detail) => (AutomationActionStatus.Failed, detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "{ActionType} (action {Index}) of automation run {RunId}: {Status}")]
    private static partial void LogAction(ILogger logger, ActionType actionType, int index, Guid runId, AutomationActionStatus status);

    private sealed record ActionContext(
        ScreenHireDbContext Db,
        TransactionalEmail Email,
        EmailTemplateStore Templates,
        IWebhookSender Webhooks,
        QuestionnaireLinks Links,
        SlackAccess Slack,
        AutomationRun Run,
        AutomationActionRun Action,
        JobApplication Application,
        Job Job,
        DateTimeOffset Now,
        OutgoingMessages Outgoing);
}
