using FluentValidation;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The audit record of one automation evaluated for one application (workflow-engine.md §2, guardrail 3): the trigger,
/// the facts its conditions saw, which rules held, and what each action did.
/// </summary>
/// <remarks>
/// <c>eventId</c>: The trigger event: a timeline entry, or a manual run's request id.
/// <c>chain</c>: The automations whose actions led to this run, nearest first.
/// <c>detail</c>: Why it was skipped, blocked or failed.
/// <c>triggerQuestionnaireId</c>: For a QuestionnaireCompleted trigger, the follow-up questionnaire that was answered.
/// </remarks>
public sealed record AutomationRunEntry(
    Guid Id,
    Guid ApplicationId,
    Guid JobId,
    string AutomationId,
    string AutomationName,
    int WorkflowVersion,
    TriggerType Trigger,
    string? TriggerStageId,
    Guid EventId,
    DateTimeOffset EventOccurredAt,
    IReadOnlyList<string> Chain,
    Guid? ActorUserId,
    AutomationFacts Facts,
    IReadOnlyList<RuleResult> Rules,
    AutomationRunStatus Status,
    string? Detail,
    IReadOnlyList<AutomationActionEntry> Actions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    string? TriggerQuestionnaireId = null);

public sealed record AutomationActionEntry(int Index, AutomationAction Action, AutomationActionStatus Status, string? Detail, DateTimeOffset? FinishedAt);

/// <summary>
/// An automation that did not do its job: blocked (a loop, the depth limit, a workflow that no longer validates) or
/// failed (an action failed after its retries). Newest first; the run has the full record.
/// </summary>
public sealed record AutomationIssue(
    Guid RunId,
    Guid ApplicationId,
    Guid JobId,
    string AutomationId,
    string AutomationName,
    AutomationRunStatus Status,
    string? Detail,
    DateTimeOffset CreatedAt);

/// <summary>
/// Something a person should do about an application: a review, a to-do, a rejection to decide on, or a scorecard a
/// teammate asked for. A review and a requested scorecard are done when the scorecard is submitted; the others are
/// ticked off by hand.
/// </summary>
/// <remarks>
/// <c>assigneeUserId</c>: Who should do it; null for anyone on the hiring team.
/// <c>completedByUserId</c>: Who finished it; null while it is open.
/// <c>scorecardTemplateId</c>: The library scorecard a Scorecard task asks for; null on every other kind.
/// </remarks>
public sealed record ApplicationTaskEntry(
    Guid Id,
    Guid ApplicationId,
    ApplicationTaskKind Kind,
    string Note,
    Guid? AssigneeUserId,
    Guid? AutomationRunId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    Guid? CompletedByUserId,
    string? ScorecardTemplateId = null);

/// <summary>A task in a list across applications (My tasks): the task, and the candidate and job it is about.</summary>
/// <remarks>
/// <c>assigneeUserId</c>: Who should do it; null for anyone on the hiring team.
/// <c>completedByUserId</c>: Who finished it; null while it is open.
/// <c>scorecardTemplateId</c>: The library scorecard a Scorecard task asks for; null on every other kind.
/// </remarks>
public sealed record TaskListItem(
    Guid Id,
    Guid ApplicationId,
    Guid JobId,
    string CandidateName,
    string JobTitle,
    ApplicationTaskKind Kind,
    string Note,
    Guid? AssigneeUserId,
    Guid? AutomationRunId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    Guid? CompletedByUserId,
    string? ScorecardTemplateId = null);

/// <summary>Which tasks a list shows: the open ones, or the ones done.</summary>
public enum TaskStatusFilter
{
    Open,
    Completed,
}

/// <summary>
/// Whose tasks a list shows: the signed-in person's (assigned to them), the team's (assigned to nobody in particular),
/// or everyone's (every task the person can see).
/// </summary>
public enum TaskScope
{
    Mine,
    Team,
    Everyone,
}

/// <summary>A task for the hiring team about one application, from a person or an API key.</summary>
/// <remarks>
/// <c>assigneeUserId</c>: The member who should do it, a user id as on the team list; null for anyone on the hiring team.
/// They must see the job: all jobs, or on its hiring team.
/// </remarks>
public sealed record CreateApplicationTaskRequest(string Note, Guid? AssigneeUserId = null);

/// <summary>A manual automation was queued. <c>eventId</c> keys its run: find it in the application's automation runs.</summary>
public sealed record ManualAutomationQueued(Guid ApplicationId, string AutomationId, Guid EventId);

internal static class AutomationMappings
{
    public static AutomationRunEntry ToEntry(this AutomationRun run, IEnumerable<AutomationActionRun> actions) =>
        new(run.Id, run.ApplicationId, run.JobId, run.AutomationId, run.AutomationName, run.WorkflowVersion, run.Trigger, run.TriggerStageId,
            run.EventId, run.EventOccurredAt, run.Chain, run.ActorUserId, run.Facts, run.RuleResults, run.Status, run.Detail,
            [.. actions.OrderBy(a => a.Index).Select(a => new AutomationActionEntry(a.Index, a.Action, a.Status, a.Detail, a.FinishedAt))],
            run.CreatedAt, run.FinishedAt, run.TriggerQuestionnaireId);

    public static AutomationIssue ToIssue(this AutomationRun run) =>
        new(run.Id, run.ApplicationId, run.JobId, run.AutomationId, run.AutomationName, run.Status, run.Detail, run.CreatedAt);

    public static ApplicationTaskEntry ToEntry(this ApplicationTask task) =>
        new(task.Id, task.ApplicationId, task.Kind, task.Note, task.AssigneeUserId, task.AutomationRunId, task.CreatedAt, task.CompletedAt,
            task.CompletedByUserId, task.ScorecardTemplateId);
}

internal sealed class CreateApplicationTaskRequestValidator : AbstractValidator<CreateApplicationTaskRequest>
{
    public CreateApplicationTaskRequestValidator()
    {
        RuleFor(r => r.Note).NotEmpty().Must(note => !string.IsNullOrWhiteSpace(note)).WithMessage("Say what the task is.")
            .MaximumLength(ApplicationTask.MaxNoteLength);
    }
}
