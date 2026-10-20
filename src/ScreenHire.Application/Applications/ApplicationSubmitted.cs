using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Applications;

/// <summary>
/// An application arrived in its job's first stage. <see cref="EventId"/> is its timeline event, the key the workflow
/// engine will use to run each automation once per event (docs/architecture/workflow-engine.md §3). Ids only: no
/// candidate details.
/// </summary>
public sealed record ApplicationSubmitted(
    Guid OrganizationId, Guid ApplicationId, Guid JobId, string StageId, Guid EventId, DateTimeOffset SubmittedAt) : ITenantEvent;

/// <summary>
/// Runs in the worker once per <see cref="ApplicationSubmitted"/>. The workflow engine's <c>ApplicationSubmitted</c>
/// trigger hangs off this event; today it reads the application back as its organization and logs it.
/// </summary>
public static partial class ApplicationSubmittedHandler
{
    public static async Task Handle(
        ApplicationSubmitted message, ScreenHireDbContext db, ILogger<ApplicationSubmitted> logger, CancellationToken cancellationToken)
    {
        if (!await db.Applications.AsNoTracking().AnyAsync(a => a.Id == message.ApplicationId, cancellationToken))
        {
            throw new InvalidOperationException($"Application {message.ApplicationId} is not visible to organization {message.OrganizationId}.");
        }

        LogSubmitted(logger, message.ApplicationId, message.JobId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Application {ApplicationId} submitted to job {JobId}")]
    private static partial void LogSubmitted(ILogger logger, Guid applicationId, Guid jobId);
}
