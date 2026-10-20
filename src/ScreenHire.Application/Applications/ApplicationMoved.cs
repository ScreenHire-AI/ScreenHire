using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Applications;

/// <summary>
/// An application moved from one stage of its job's workflow to another. <see cref="EventId"/> is the timeline event,
/// so the engine's <c>StageExited</c> and <c>StageEntered</c> triggers can run once per move.
/// </summary>
public sealed record ApplicationMoved(
    Guid OrganizationId, Guid ApplicationId, Guid JobId, string FromStageId, string ToStageId, Guid EventId, DateTimeOffset MovedAt)
    : ITenantEvent;

/// <summary>
/// Runs in the worker once per <see cref="ApplicationMoved"/>. Stage triggers hang off this event; today it reads the
/// application back as its organization and logs the move.
/// </summary>
public static partial class ApplicationMovedHandler
{
    public static async Task Handle(
        ApplicationMoved message, ScreenHireDbContext db, ILogger<ApplicationMoved> logger, CancellationToken cancellationToken)
    {
        if (!await db.Applications.AsNoTracking().AnyAsync(a => a.Id == message.ApplicationId, cancellationToken))
        {
            throw new InvalidOperationException($"Application {message.ApplicationId} is not visible to organization {message.OrganizationId}.");
        }

        LogMoved(logger, message.ApplicationId, message.FromStageId, message.ToStageId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Application {ApplicationId} moved from {FromStageId} to {ToStageId}")]
    private static partial void LogMoved(ILogger logger, Guid applicationId, string fromStageId, string toStageId);
}
