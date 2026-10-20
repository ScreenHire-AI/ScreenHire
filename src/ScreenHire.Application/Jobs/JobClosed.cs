using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Jobs;

/// <summary>
/// A job stopped taking applications. Published with the change, through the outbox; the dashboard's jobs list
/// refreshes on it. The close transaction itself takes the job's board postings down (docs/architecture/
/// job-distribution.md §5), so nothing here depends on this event arriving.
/// </summary>
public sealed record JobClosed(Guid OrganizationId, Guid JobId, DateTimeOffset ClosedAt) : ITenantEvent;

/// <summary>
/// Runs in the worker once per <see cref="JobClosed"/>: reads the job back as its organization and records that the
/// worker saw it. Teardown runs in the close transaction, not here.
/// </summary>
public static partial class JobClosedHandler
{
    public static async Task Handle(JobClosed message, ScreenHireDbContext db, ILogger<JobClosed> logger, CancellationToken cancellationToken)
    {
        if (!await db.Jobs.AsNoTracking().AnyAsync(j => j.Id == message.JobId, cancellationToken))
        {
            throw new InvalidOperationException($"Job {message.JobId} is not visible to organization {message.OrganizationId}.");
        }

        LogJobClosed(logger, message.JobId, message.OrganizationId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} closed for organization {OrganizationId}")]
    private static partial void LogJobClosed(ILogger logger, Guid jobId, Guid organizationId);
}
