using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Application.Jobs;

/// <summary>
/// The recurring-message example: the worker's cron schedule sends this every five minutes (see
/// <c>ScreenHireMessaging</c>). It proves the worker reaches the database and logs one system-wide count, no tenant
/// data. It is not about one organization, so it has no tenant of its own.
/// </summary>
public sealed record ReportOpenJobs;

public static partial class ReportOpenJobsHandler
{
    public static async Task Handle(
        ReportOpenJobs message, ScreenHireDbContext db, TenantContext tenant, ILogger<ReportOpenJobs> logger, CancellationToken cancellationToken)
    {
        var counts = await CountOpenJobsByOrganizationAsync(db, tenant, cancellationToken);
        var openJobs = counts.Values.Sum();
        LogOpenJobs(logger, openJobs, counts.Count);
    }

    /// <summary>
    /// Open jobs per organization, counted as each organization in turn. The worker connects as <c>screenhire_app</c>, and
    /// row-level security shows it no jobs at all without a tenant, so a system-wide figure is the sum of per-tenant
    /// ones; there is no path around the policies. One query per organization: fine for a five-minute report, not for a
    /// request.
    /// </summary>
    public static async Task<Dictionary<Guid, int>> CountOpenJobsByOrganizationAsync(
        ScreenHireDbContext db, TenantContext tenant, CancellationToken cancellationToken)
    {
        var organizationIds = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var counts = new Dictionary<Guid, int>(organizationIds.Count);
        foreach (var organizationId in organizationIds)
        {
            tenant.SetOrganization(organizationId);
            counts[organizationId] = await db.Jobs.CountAsync(j => j.Status == JobStatus.Open, cancellationToken);
        }

        return counts;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{OpenJobs} open jobs across {Organizations} organizations")]
    private static partial void LogOpenJobs(ILogger logger, int openJobs, int organizations);
}
