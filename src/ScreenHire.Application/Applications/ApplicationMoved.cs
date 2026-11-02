using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Scheduling;
using ScreenHire.Application.Slack;
using ScreenHire.Application.Webhooks;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Applications;

/// <summary>
/// An application moved from one stage of its job's workflow to another. <see cref="EventId"/> is the timeline event,
/// so the engine's <c>StageExited</c> and <c>StageEntered</c> triggers can run once per move. <see cref="ActorUserId"/>
/// is the member who moved it, null for an API key or an automation (teammates' changes as toasts, realtime.md).
/// </summary>
public sealed record ApplicationMoved(
    Guid OrganizationId, Guid ApplicationId, Guid JobId, string FromStageId, string ToStageId, Guid EventId, DateTimeOffset MovedAt,
    Guid? ActorUserId = null)
    : IJobEvent;

/// <summary>
/// Runs in the worker once per <see cref="ApplicationMoved"/>: the workflow engine's <c>StageExited</c> trigger for the
/// stage left and <c>StageEntered</c> for the stage entered, which also starts that stage's TimeElapsed timers. A move an
/// automation made links its timeline entry to that run, so the engine can follow the chain (depth limit, cycles). It
/// also raises the <c>application.moved</c> webhook event (<see cref="WebhookEvents"/>) and its Slack post (<see cref="SlackEvents"/>).
/// </summary>
public static partial class ApplicationMovedHandler
{
    public static async Task<OutgoingMessages> Handle(
        ApplicationMoved message, ScreenHireDbContext db, SlackAccess slack, TimeProvider time, ILogger<ApplicationMoved> logger, CancellationToken cancellationToken)
    {
        // An application erased since the move has nothing left to run, tell or post about: done, not failed.
        if (await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == message.ApplicationId, cancellationToken) is not { } application)
        {
            return [];
        }

        LogMoved(logger, message.ApplicationId, message.FromStageId, message.ToStageId);
        var causedByRunId = await db.ApplicationEvents.AsNoTracking()
            .Where(e => e.Id == message.EventId)
            .Select(e => e.Data)
            .SingleOrDefaultAsync(cancellationToken);
        AutomationTriggerEvent Trigger(TriggerType type, string stageId) =>
            new(message.OrganizationId, message.ApplicationId, message.JobId, type, stageId, message.EventId, message.MovedAt, causedByRunId?.AutomationRunId);

        var outgoing = await AutomationEngine.EvaluateAsync(
            db, time, application,
            [Trigger(TriggerType.StageExited, message.FromStageId), Trigger(TriggerType.StageEntered, message.ToStageId)],
            enteredStageId: message.ToStageId,
            onlyAutomationId: null,
            cancellationToken);
        outgoing.Add(WebhookEvents.From(message));
        // An interview offered or booked on an application that has now ended is cancelled. The handler checks the stage, so
        // a move that did not end it, or a move back, cancels nothing; this only asks for the check when there is an interview.
        if (await db.InterviewRequests.AsNoTracking().AnyAsync(
                r => r.ApplicationId == message.ApplicationId && (r.Status == InterviewStatus.Open || r.Status == InterviewStatus.Booked), cancellationToken))
        {
            outgoing.Add(new EndApplicationInterviews(message.OrganizationId, message.ApplicationId, message.EventId));
        }

        if (SlackEvents.From(message, slack) is { } post)
        {
            outgoing.Add(post);
        }

        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Application {ApplicationId} moved from {FromStageId} to {ToStageId}")]
    private static partial void LogMoved(ILogger logger, Guid applicationId, string fromStageId, string toStageId);
}
