using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Jobs;

/// <summary>
/// A job stopped taking applications. Published with the change, through the outbox. Job distribution will take its
/// board postings down from here (docs/architecture/job-distribution.md); today the dashboard's jobs list refreshes on it.
/// </summary>
public sealed record JobClosed(Guid OrganizationId, Guid JobId, DateTimeOffset ClosedAt) : ITenantEvent;

/// <summary>
/// Runs in the worker once per <see cref="JobClosed"/>. Teardown (docs/architecture/job-distribution.md §2) hangs off
/// this event; today it reads the job back as its organization and records that the worker saw it.
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
