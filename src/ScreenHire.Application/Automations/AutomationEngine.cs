using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Automations;

/// <summary>
/// The workflow engine's evaluation step (docs/architecture/workflow-engine.md §3). For one application and the triggers
/// one event fires, it finds the job's automations, takes the facts snapshot, evaluates each automation, records an
/// <see cref="AutomationRun"/> for every one (the audit, guardrail 3), and returns the messages that carry the work on:
/// the first action of each matched run, issue events, and TimeElapsed timers for a stage the application entered.
/// Everything it writes commits in the calling handler's transaction, with those messages.
/// </summary>
/// <remarks>
/// When no automation is triggered and no timer is due, it writes and returns nothing, so a job without automations
/// costs one read. A run that already exists for an (automation, event) pair is skipped: that is what makes a
/// redelivered or replayed event run each automation once.
/// </remarks>
public static class AutomationEngine
{
    /// <summary>
    /// Evaluates <paramref name="triggers"/> (the triggers one event fires, sharing its id, time and cause) for
    /// <paramref name="application"/>. <paramref name="enteredStageId"/> is the stage the event put the application in,
    /// for TimeElapsed timers, or null; <paramref name="onlyAutomationId"/> limits it to one automation (timers and
    /// manual runs).
    /// </summary>
    public static async Task<OutgoingMessages> EvaluateAsync(
        ScreenHireDbContext db,
        TimeProvider time,
        JobApplication application,
        IReadOnlyList<AutomationTriggerEvent> triggers,
        string? enteredStageId,
        string? onlyAutomationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(triggers);

        var outgoing = new OutgoingMessages();
        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        var workflow = job.Workflow;

        var candidates = triggers
            .SelectMany(trigger => AutomationEvaluator.Triggered(workflow, trigger.Trigger, trigger.StageId).Select(automation => (trigger, automation)))
            .Where(c => onlyAutomationId is null || c.automation.Id == onlyAutomationId)
            .ToList();
        var timers = enteredStageId is null
            ? []
            : workflow.Automations.Where(a => a.Enabled && a.Trigger.Type == TriggerType.TimeElapsed && a.Trigger.StageId == enteredStageId).ToList();
        if (candidates.Count == 0 && timers.Count == 0)
        {
            return outgoing;
        }

        var first = triggers[0];
        var eventIds = triggers.Select(t => t.EventId).Distinct().ToList();
        var done = await db.AutomationRuns.AsNoTracking()
            .Where(r => r.ApplicationId == application.Id && eventIds.Contains(r.EventId))
            .Select(r => new { r.AutomationId, r.EventId })
            .ToListAsync(cancellationToken);
        candidates.RemoveAll(c => done.Exists(d => d.AutomationId == c.automation.Id && d.EventId == c.trigger.EventId));

        var chain = await ChainAsync(db, first.CausedByRunId, cancellationToken);
        var timeline = await db.ApplicationEvents.AsNoTracking()
            .Where(e => e.ApplicationId == application.Id)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        if (candidates.Count > 0)
        {
            // The validator ran when the workflow was saved, but its rules can tighten after that. A workflow that no
            // longer passes runs nothing and shows up as an issue instead (the guardrails hold at run time too).
            var errors = DefinitionValidator.Validate(workflow);
            var invalid = errors.Count > 0 ? errors[0] : null;
            var now = time.GetUtcNow();
            foreach (var (trigger, automation) in candidates)
            {
                // Timing facts are switched off for an application with an accommodation: not evaluated, and not stored.
                var facts = AutomationFacts.FromTimeline(timeline, trigger.EventId, trigger.Trigger, trigger.OccurredAt);
                if (application.AccommodationRequested)
                {
                    facts = facts with { HoursInStage = null };
                }

                var evaluation = invalid is null
                    ? AutomationEvaluator.Evaluate(automation, facts, application.AccommodationRequested, chain)
                    : new AutomationEvaluation(EvaluationOutcome.Blocked, [],
                        $"The job's workflow no longer passes validation, so nothing ran: {invalid.Path}: {invalid.Message}");
                var (run, actions) = AutomationRun.Record(trigger, automation, job.WorkflowVersion, facts, evaluation, chain, now);
                db.AutomationRuns.Add(run);
                db.AutomationActionRuns.AddRange(actions);

                if (actions.Count > 0)
                {
                    outgoing.Add(new ExecuteAutomationAction(run.OrganizationId, actions[0].Id, run.ActionKey(0)));
                }
                else if (run.Status == AutomationRunStatus.Blocked)
                {
                    outgoing.Add(new AutomationRunFinished(run.OrganizationId, run.ApplicationId, run.JobId, run.Id, run.AutomationId, run.Status, now));
                }
            }
        }

        ScheduleTimers(application, first, timers, timeline, outgoing);
        return outgoing;
    }

    /// <summary>
    /// One timer per TimeElapsed automation on the stage just entered, due <c>afterHours</c> after the entry. The timer
    /// carries the entry's timeline id; when it fires, <see cref="RunAutomationsHandler"/> drops it if the application
    /// has left that stage entry since, which is how leaving the stage cancels it.
    /// </summary>
    private static void ScheduleTimers(
        JobApplication application, AutomationTriggerEvent trigger, List<Automation> timers, List<ApplicationEvent> timeline, OutgoingMessages outgoing)
    {
        var entry = timeline.Find(e => e.Id == trigger.EventId);
        if (entry is null)
        {
            return;
        }

        foreach (var automation in timers)
        {
            var due = entry.OccurredAt.AddHours(automation.Trigger.AfterHours ?? 0);
            outgoing.Add(new RunAutomations(
                application.OrganizationId, application.Id, TriggerType.TimeElapsed, automation.Trigger.StageId, entry.Id, due,
                automation.Id, trigger.CausedByRunId).ScheduledAt(due));
        }
    }

    /// <summary>The automations whose actions led to an event caused by <paramref name="causedByRunId"/>, nearest first.</summary>
    private static async Task<IReadOnlyList<string>> ChainAsync(ScreenHireDbContext db, Guid? causedByRunId, CancellationToken cancellationToken)
    {
        if (causedByRunId is null)
        {
            return [];
        }

        var cause = await db.AutomationRuns.AsNoTracking()
            .Where(r => r.Id == causedByRunId)
            .Select(r => new { r.AutomationId, r.Chain })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Automation run {causedByRunId} is not visible to the event's organization.");
        return [cause.AutomationId, .. cause.Chain];
    }
}
