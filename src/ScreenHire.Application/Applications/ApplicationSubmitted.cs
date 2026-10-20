using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Integrity;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Applications;

/// <summary>
/// An application arrived in its job's first stage. <see cref="EventId"/> is its timeline event, the key the workflow
/// engine uses to run each automation once per event (docs/architecture/workflow-engine.md §3). Ids only: no
/// candidate details.
/// </summary>
public sealed record ApplicationSubmitted(
    Guid OrganizationId, Guid ApplicationId, Guid JobId, string StageId, Guid EventId, DateTimeOffset SubmittedAt) : ITenantEvent;

/// <summary>
/// Runs in the worker once per <see cref="ApplicationSubmitted"/>: the workflow engine's <c>ApplicationSubmitted</c>
/// trigger, and <c>StageEntered</c> for the first stage, which the application has just entered (it also starts that
/// stage's TimeElapsed timers). It also starts the integrity pipeline (<see cref="AnalyzeApplicationIntegrity"/>).
/// </summary>
public static partial class ApplicationSubmittedHandler
{
    public static async Task<OutgoingMessages> Handle(
        ApplicationSubmitted message, ScreenHireDbContext db, TimeProvider time, ILogger<ApplicationSubmitted> logger, CancellationToken cancellationToken)
    {
        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == message.ApplicationId, cancellationToken)
            ?? throw new InvalidOperationException($"Application {message.ApplicationId} is not visible to organization {message.OrganizationId}.");

        LogSubmitted(logger, message.ApplicationId, message.JobId);
        var outgoing = await AutomationEngine.EvaluateAsync(
            db, time, application,
            [
                new(message.OrganizationId, message.ApplicationId, message.JobId, TriggerType.ApplicationSubmitted, null, message.EventId, message.SubmittedAt),
                new(message.OrganizationId, message.ApplicationId, message.JobId, TriggerType.StageEntered, message.StageId, message.EventId, message.SubmittedAt),
            ],
            enteredStageId: message.StageId,
            onlyAutomationId: null,
            cancellationToken);

        // The integrity pipeline is its own message, so it retries and fails apart from the automations above.
        outgoing.Add(new AnalyzeApplicationIntegrity(message.OrganizationId, message.ApplicationId));
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Application {ApplicationId} submitted to job {JobId}")]
    private static partial void LogSubmitted(ILogger logger, Guid applicationId, Guid jobId);
}
