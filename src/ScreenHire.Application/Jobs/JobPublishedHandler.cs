using Microsoft.Extensions.Logging;

namespace ScreenHire.Application.Jobs;

/// <summary>
/// Runs in the worker once per <see cref="JobPublished"/>. Job distribution (docs/architecture/job-distribution.md)
/// hangs its board postings off this event; today it only records that the worker saw it.
/// </summary>
public static partial class JobPublishedHandler
{
    public static void Handle(JobPublished message, ILogger<JobPublished> logger) =>
        LogJobPublished(logger, message.JobId, message.OrganizationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} published for organization {OrganizationId}")]
    private static partial void LogJobPublished(ILogger logger, Guid jobId, Guid organizationId);
}
