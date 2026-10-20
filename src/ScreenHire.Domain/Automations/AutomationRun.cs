using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Automations;

/// <summary>
/// The audit record of one automation evaluated for one application on one trigger event (workflow-engine.md §2,
/// guardrail 3, and §6 <c>automation_runs</c>): the workflow version, the trigger, the facts snapshot the conditions saw,
/// the result and what happened to each action. <c>(ApplicationId, AutomationId, EventId)</c> is unique, so an event that
/// is delivered or replayed twice runs each automation once.
/// </summary>
public sealed class AutomationRun : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    public Guid JobId { get; init; }

    /// <summary>The automation's id in the job's workflow.</summary>
    public required string AutomationId { get; init; }

    /// <summary>The automation's name when it ran, so the record still reads after the workflow changes.</summary>
    public required string AutomationName { get; init; }

    /// <summary>The job's workflow version the automation came from.</summary>
    public int WorkflowVersion { get; init; }

    public TriggerType Trigger { get; init; }

    public string? TriggerStageId { get; init; }

    /// <summary>
    /// The event that fired the trigger: a timeline entry for submissions, moves and timers (the stage entry they count
    /// from), or the request id of a manual run.
    /// </summary>
    public Guid EventId { get; init; }

    /// <summary>When the trigger event happened; facts such as hours in stage are measured at this instant.</summary>
    public DateTimeOffset EventOccurredAt { get; init; }

    /// <summary>The run whose action caused the trigger event, or null for an event a person or the candidate caused.</summary>
    public Guid? CausedByRunId { get; init; }

    /// <summary>The automations whose actions led here, nearest first; <see cref="AutomationEvaluator.MaxDepth"/> caps it.</summary>
    public List<string> Chain { get; init; } = [];

    /// <summary>The signed-in user who ran a manual automation.</summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>The facts snapshot the conditions were evaluated against.</summary>
    public required AutomationFacts Facts { get; init; }

    public required IReadOnlyList<RuleResult> RuleResults { get; init; }

    public AutomationRunStatus Status { get; private set; }

    /// <summary>Why it was skipped, blocked or failed, in plain language.</summary>
    public string? Detail { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>The run's record of evaluating <paramref name="automation"/>, with a pending row per action when it matched.</summary>
    public static (AutomationRun Run, IReadOnlyList<AutomationActionRun> Actions) Record(
        AutomationTriggerEvent trigger,
        Automation automation,
        int workflowVersion,
        AutomationFacts facts,
        AutomationEvaluation evaluation,
        IReadOnlyList<string> chain,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(evaluation);

        var status = evaluation.Outcome switch
        {
            EvaluationOutcome.Matched => AutomationRunStatus.Running,
            EvaluationOutcome.NotMatched => AutomationRunStatus.NotMatched,
            EvaluationOutcome.Skipped => AutomationRunStatus.Skipped,
            _ => AutomationRunStatus.Blocked,
        };
        var run = new AutomationRun
        {
            OrganizationId = trigger.OrganizationId,
            ApplicationId = trigger.ApplicationId,
            JobId = trigger.JobId,
            AutomationId = automation.Id,
            AutomationName = automation.Name,
            WorkflowVersion = workflowVersion,
            Trigger = trigger.Trigger,
            TriggerStageId = trigger.StageId,
            EventId = trigger.EventId,
            EventOccurredAt = trigger.OccurredAt,
            CausedByRunId = trigger.CausedByRunId,
            Chain = [.. chain],
            ActorUserId = trigger.ActorUserId,
            Facts = facts,
            RuleResults = evaluation.Rules,
            Status = status,
            Detail = evaluation.Reason,
            CreatedAt = now,
            FinishedAt = status == AutomationRunStatus.Running ? null : now,
        };

        IReadOnlyList<AutomationActionRun> actions = status == AutomationRunStatus.Running
            ? [.. automation.Actions.Select((action, index) => new AutomationActionRun
            {
                OrganizationId = run.OrganizationId,
                RunId = run.Id,
                Index = index,
                Action = action,
            })]
            : [];
        return (run, actions);
    }

    /// <summary>Every action finished.</summary>
    public void Complete(DateTimeOffset now)
    {
        Status = AutomationRunStatus.Completed;
        FinishedAt = now;
    }

    /// <summary>An action failed for good: the run stops there, and the automation issues list shows it.</summary>
    public void Fail(string detail, DateTimeOffset now)
    {
        Status = AutomationRunStatus.Failed;
        Detail = detail;
        FinishedAt = now;
    }

    /// <summary>The idempotency key of action <paramref name="index"/>: (application, automation, event, action).</summary>
    public string ActionKey(int index) => $"{ApplicationId}:{AutomationId}:{EventId}:{index}";
}

/// <summary>
/// One action of a run that matched, executed in order as its own durable message. The action is copied from the
/// workflow when the run is recorded, so a later edit to the workflow never changes what a queued run does.
/// </summary>
public sealed class AutomationActionRun : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid RunId { get; init; }

    /// <summary>The action's position in the automation; actions run in this order.</summary>
    public int Index { get; init; }

    public required AutomationAction Action { get; init; }

    public AutomationActionStatus Status { get; private set; } = AutomationActionStatus.Pending;

    /// <summary>What happened, in plain language: "Sent stage-change", "Already tagged", or why it failed.</summary>
    public string? Detail { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public void Complete(string detail, DateTimeOffset now) => Finish(AutomationActionStatus.Completed, detail, now);

    public void Skip(string detail, DateTimeOffset now) => Finish(AutomationActionStatus.Skipped, detail, now);

    public void Fail(string detail, DateTimeOffset now) => Finish(AutomationActionStatus.Failed, detail, now);

    private void Finish(AutomationActionStatus status, string detail, DateTimeOffset now)
    {
        if (Status != AutomationActionStatus.Pending)
        {
            throw new InvalidOperationException($"Action {Index} of run {RunId} already finished as {Status}.");
        }

        Status = status;
        Detail = detail;
        FinishedAt = now;
    }
}

/// <summary>What fired a trigger, as the engine records it.</summary>
/// <remarks>
/// <c>eventId</c>: The timeline entry (or manual request) the run is keyed on. <c>stageId</c>: For stage and time
/// triggers. <c>causedByRunId</c>: The automation run whose action raised this event, if one did.
/// </remarks>
public sealed record AutomationTriggerEvent(
    Guid OrganizationId,
    Guid ApplicationId,
    Guid JobId,
    TriggerType Trigger,
    string? StageId,
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid? CausedByRunId = null,
    Guid? ActorUserId = null);

public enum AutomationRunStatus
{
    /// <summary>The conditions did not hold; nothing ran.</summary>
    NotMatched,

    /// <summary>Not evaluated for this application (accommodation).</summary>
    Skipped,

    /// <summary>Refused: a loop, the depth limit, or a workflow that no longer validates. Shown as an automation issue.</summary>
    Blocked,

    /// <summary>The conditions held and the actions are running.</summary>
    Running,
    Completed,

    /// <summary>An action failed after its retries. Shown as an automation issue.</summary>
    Failed,
}

public enum AutomationActionStatus
{
    Pending,
    Completed,
    Skipped,
    Failed,
}
