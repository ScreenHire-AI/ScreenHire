using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Jobs;

/// <summary>
/// The recurring-message example: the worker's cron schedule sends this every five minutes (see
/// <c>ScreenHireMessaging</c>). It proves the worker reaches the database and replaces the scaffold's heartbeat loop.
/// It logs one system-wide count and no tenant data. Once row-level security lands it sees no rows unless the tenancy
/// work gives it a role that may count across organizations; replace it then rather than bypass RLS.
/// </summary>
public sealed record ReportOpenJobs;

public static partial class ReportOpenJobsHandler
{
    public static async Task Handle(ReportOpenJobs message, ScreenHireDbContext db, ILogger<ReportOpenJobs> logger, CancellationToken cancellationToken)
    {
        var openJobs = await db.Jobs.CountAsync(j => j.Status == JobStatus.Open, cancellationToken);
        LogOpenJobs(logger, openJobs);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{OpenJobs} open jobs across all organizations")]
    private static partial void LogOpenJobs(ILogger logger, int openJobs);
}
