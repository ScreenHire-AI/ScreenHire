using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Automations;

/// <summary>Runs in the worker for every <see cref="RunAutomations"/>: timers, manual runs and triggers from other features.</summary>
public static partial class RunAutomationsHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(RunAutomations.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        RunAutomations message, ScreenHireDbContext db, TimeProvider time, ILogger<RunAutomations> logger, CancellationToken cancellationToken)
    {
        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == message.ApplicationId, cancellationToken)
            ?? throw new InvalidOperationException($"Application {message.ApplicationId} is not visible to organization {message.OrganizationId}.");

        if (message.Trigger is TriggerType.TimeElapsed or TriggerType.SlaBreached)
        {
            // A timer counts from one stage entry. If the application has left it since (moved on, or left and came back,
            // which is a new entry with its own timer), the timer is cancelled: it does nothing.
            var currentEntry = await db.ApplicationEvents.AsNoTracking()
                .Where(e => e.ApplicationId == application.Id && (e.Type == ApplicationEventType.Submitted || e.Type == ApplicationEventType.StageChanged))
                .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
                .Select(e => new { e.Id, e.OccurredAt })
                .FirstAsync(cancellationToken);
            if (currentEntry.Id != message.EventId)
            {
                LogTimerCancelled(logger, message.ApplicationId, message.AutomationId);
                return [];
            }

            // A breach timer was due when the stage's time limit at entry passed. The limit may have changed since: if the
            // stage has none now, or a longer one that has not passed, nothing is breached. A timer is never moved, so a
            // longer limit drops this one (workflow-engine.md section 8).
            if (message.Trigger == TriggerType.SlaBreached)
            {
                var workflow = await db.Jobs.AsNoTracking().Where(j => j.Id == application.JobId).Select(j => j.Workflow).SingleAsync(cancellationToken);
                var stage = message.StageId is null ? null : workflow.FindStage(message.StageId);
                if (stage?.SlaDueAt(currentEntry.OccurredAt) is not { } due || due > message.OccurredAt)
                {
                    LogBreachDropped(logger, message.ApplicationId, message.AutomationId);
                    return [];
                }
            }
        }

        var trigger = new AutomationTriggerEvent(
            message.OrganizationId, application.Id, application.JobId, message.Trigger, message.StageId, message.EventId, message.OccurredAt,
            message.CausedByRunId, message.ActorUserId);
        return await AutomationEngine.EvaluateAsync(db, time, application, [trigger], enteredStageId: null, message.AutomationId, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Timer for automation {AutomationId} on application {ApplicationId} cancelled: the application left the stage")]
    private static partial void LogTimerCancelled(ILogger logger, Guid applicationId, string? automationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Time limit timer for automation {AutomationId} on application {ApplicationId} dropped: the stage's time limit was removed or has not passed")]
    private static partial void LogBreachDropped(ILogger logger, Guid applicationId, string? automationId);
}
