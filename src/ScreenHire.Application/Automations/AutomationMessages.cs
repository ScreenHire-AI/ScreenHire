using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Application.Automations;

/// <summary>
/// Evaluate the automations one trigger fires for one application. Submissions and moves evaluate inline in their own
/// handlers; this command carries the triggers that come from elsewhere: a TimeElapsed timer (scheduled when the
/// application entered the stage), a manual run from the dashboard, and the events later features raise
/// (QuestionnaireCompleted, IntegrityAssessed, ReviewerDecision). Ids only.
/// </summary>
/// <remarks>
/// <c>eventId</c>: The event the runs are keyed on: for a timer, the timeline entry of the stage entry it counts from;
/// for a manual run, the request's id; for a completed questionnaire, its QuestionnaireCompleted timeline entry.
/// <c>automationId</c>: Only this automation (timers and manual runs). <c>occurredAt</c>: When the trigger happened; facts
/// are measured at this instant, so a late delivery sees the same facts. <c>questionnaireId</c>: For QuestionnaireCompleted,
/// the follow-up questionnaire that was answered.
/// </remarks>
public sealed record RunAutomations(
    Guid OrganizationId,
    Guid ApplicationId,
    TriggerType Trigger,
    string? StageId,
    Guid EventId,
    DateTimeOffset OccurredAt,
    string? AutomationId = null,
    Guid? CausedByRunId = null,
    Guid? ActorUserId = null,
    string? QuestionnaireId = null) : ITenantMessage
{
    /// <summary>One intent per trigger event: a timer scheduled twice, or a replayed event, runs once.</summary>
    public string IdempotencyKey => $"{ApplicationId}:{AutomationId ?? "*"}:{Trigger}:{EventId}";
}

/// <summary>
/// Run one action of an automation run, then the next. Keyed (application, automation, event, action index) as
/// workflow-engine.md §3 describes; the action row's status makes a redelivery a no-op.
/// </summary>
public sealed record ExecuteAutomationAction(Guid OrganizationId, Guid ActionRunId, string IdempotencyKey) : ITenantMessage;

/// <summary>
/// An action failed after every retry and its message went to the dead-letter table. Published by the error policy
/// (<see cref="AutomationFailures"/>); its handler marks the action and the run failed, so the automation issues list
/// shows it. <c>exceptionType</c> only: exception messages can quote addresses or other personal data.
/// </summary>
public sealed record AutomationActionFailed(Guid OrganizationId, Guid ActionRunId, string ExceptionType) : ITenantMessage;

/// <summary>
/// An automation run ended in a way the hiring team should see: completed, failed, or blocked (a loop, the depth limit,
/// or a workflow that no longer validates). Quiet outcomes (conditions not met, skipped) raise nothing. Ids and the
/// status only; the dashboard reads the run's detail through the API.
/// </summary>
public sealed record AutomationRunFinished(
    Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid RunId, string AutomationId, AutomationRunStatus Status, DateTimeOffset FinishedAt)
    : IJobEvent;

/// <summary>A NotifyTeam action ran: the dashboard shows the run's note to the hiring team.</summary>
public sealed record TeamNotified(Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid RunId, DateTimeOffset NotifiedAt) : IJobEvent;
