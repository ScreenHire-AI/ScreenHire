using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Applications;
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
/// nothing. An external effect (an email, a webhook) happens before that commit: a crash in between can repeat it once,
/// which the receiver's idempotency key covers for webhooks.
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
        IWebhookSender webhooks,
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
        var context = new ActionContext(db, email, webhooks, run, action, application, job, time.GetUtcNow(), outgoing);

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
            ActionType.SendEmail => SendEmailAsync(context, action.EmailTemplate!, reason: null, cancellationToken),
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

    private static async Task<(AutomationActionStatus, string)> SendEmailAsync(
        ActionContext context, string templateId, string? reason, CancellationToken cancellationToken)
    {
        if (!context.Email.IsConfigured)
        {
            return Skipped("Email is not set up, so nothing was sent.");
        }

        var candidate = await context.Db.Candidates.AsNoTracking().SingleAsync(c => c.Id == context.Application.CandidateId, cancellationToken);
        var organization = await context.Db.Organizations.AsNoTracking().SingleAsync(o => o.Id == context.Run.OrganizationId, cancellationToken);
        var stage = context.Job.Workflow.FindStage(context.Application.CurrentStageId);
        await context.Email.SendAsync(
            templateId,
            candidate.Email,
            new EmailTemplateModel(candidate.FullName, context.Job.Title, organization.Name, stage?.Name, reason),
            cancellationToken);
        return Completed($"Sent the {templateId} email.");
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
        context.Db.ApplicationTasks.Add(new ApplicationTask
        {
            OrganizationId = context.Run.OrganizationId,
            ApplicationId = context.Application.Id,
            Kind = kind,
            Note = note,
            AssigneeUserId = assigneeUserId,
            AutomationRunId = context.Run.Id,
            CreatedAt = context.Now,
        });
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
        // changed while the action waited. Anything short of a valid, opted-in knockout goes to a person.
        var automation = context.Job.Workflow.Automations.FirstOrDefault(a => a.Id == context.Run.AutomationId);
        var stillValid = DefinitionValidator.Validate(context.Job.Workflow).Count == 0;
        if (automation is not { AllowAutoReject: true } || !stillValid || !context.Email.IsConfigured)
        {
            return AddTask(context, ApplicationTaskKind.RejectionReview, action.Reason!, null,
                automation is not { AllowAutoReject: true } ? "Queued for a person to decide: the automation is no longer opted in to rejecting."
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

        await SendEmailAsync(context, EmailTemplateIds.ApplicationRejected, action.Reason, cancellationToken);
        Move(context, rejected, $"Rejected by the automation \"{context.Run.AutomationName}\": {action.Reason}");
        return Completed("Rejected, and told the candidate why.");
    }

    private static async Task<(AutomationActionStatus, string)> CallWebhookAsync(ActionContext context, Uri url, CancellationToken cancellationToken)
    {
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
        IWebhookSender Webhooks,
        AutomationRun Run,
        AutomationActionRun Action,
        JobApplication Application,
        Job Job,
        DateTimeOffset Now,
        OutgoingMessages Outgoing);
}
