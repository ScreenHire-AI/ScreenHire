using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Jobs;

/// <summary>
/// Runs in the worker once per <see cref="JobPublished"/>: reads the job back as its organization and records that the
/// worker saw it. Board postings come from the publish transaction itself (docs/architecture/job-distribution.md §5).
/// </summary>
public static partial class JobPublishedHandler
{
    public static async Task Handle(JobPublished message, ScreenHireDbContext db, ILogger<JobPublished> logger, CancellationToken cancellationToken)
    {
        // Row-level security shows the job only to its own organization, so this works only because the worker made the
        // message's organization the tenant (TenantMessageMiddleware). A failure goes through the retry policy.
        if (!await db.Jobs.AsNoTracking().AnyAsync(j => j.Id == message.JobId, cancellationToken))
        {
            throw new InvalidOperationException($"Job {message.JobId} is not visible to organization {message.OrganizationId}.");
        }

        LogJobPublished(logger, message.JobId, message.OrganizationId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} published for organization {OrganizationId}")]
    private static partial void LogJobPublished(ILogger logger, Guid jobId, Guid organizationId);
}
