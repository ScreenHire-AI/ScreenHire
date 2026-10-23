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

        if (message.Trigger == TriggerType.TimeElapsed)
        {
            // A timer counts from one stage entry. If the application has left it since (moved on, or left and came back,
            // which is a new entry with its own timer), the timer is cancelled: it does nothing.
            var currentEntry = await db.ApplicationEvents.AsNoTracking()
                .Where(e => e.ApplicationId == application.Id && (e.Type == ApplicationEventType.Submitted || e.Type == ApplicationEventType.StageChanged))
                .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
                .Select(e => e.Id)
                .FirstAsync(cancellationToken);
            if (currentEntry != message.EventId)
            {
                LogTimerCancelled(logger, message.ApplicationId, message.AutomationId);
                return [];
            }
        }

        var trigger = new AutomationTriggerEvent(
            message.OrganizationId, application.Id, application.JobId, message.Trigger, message.StageId, message.EventId, message.OccurredAt,
            message.CausedByRunId, message.ActorUserId, message.QuestionnaireId);
        return await AutomationEngine.EvaluateAsync(db, time, application, [trigger], enteredStageId: null, message.AutomationId, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Timer for automation {AutomationId} on application {ApplicationId} cancelled: the application left the stage")]
    private static partial void LogTimerCancelled(ILogger logger, Guid applicationId, string? automationId);
}
