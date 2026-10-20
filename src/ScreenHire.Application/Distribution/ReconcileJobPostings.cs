using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Distribution;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Application.Distribution;

/// <summary>
/// The reconciliation sweep, every five minutes in the worker: finds push-board postings whose status check is overdue
/// or whose request never got through (a schedule lost with a crash, a dead-lettered call) and sends each a
/// <see cref="ReconcileJobPosting"/>, which runs as its organization. Like <c>ReportOpenJobs</c> it reads each
/// organization in turn and never goes around row-level security.
/// </summary>
public sealed record ReconcileJobPostings;

public static class ReconcileJobPostingsHandler
{
    public static async Task<IReadOnlyList<ReconcileJobPosting>> Handle(
        ReconcileJobPostings message,
        ScreenHireDbContext db,
        TenantContext tenant,
        BoardRegistry boards,
        IOptions<DistributionOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var pushBoards = boards.All.Where(a => a.Capabilities.Mode == BoardMode.Push).Select(a => a.BoardId).ToList();
        if (pushBoards.Count == 0)
        {
            return [];
        }

        var now = time.GetUtcNow();
        var stuckBefore = now - options.Value.ResendAfter;
        var organizationIds = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var due = new List<ReconcileJobPosting>();
        foreach (var organizationId in organizationIds)
        {
            tenant.SetOrganization(organizationId);
            due.AddRange(await db.JobPostings.AsNoTracking()
                .Where(p => pushBoards.Contains(p.BoardId))
                .Where(p => p.NextCheckAt <= now
                    || (p.NextCheckAt == null && p.UpdatedAt <= stuckBefore
                        && (p.State == PostingState.Queued || p.State == PostingState.Closing)))
                .Select(p => new ReconcileJobPosting(p.OrganizationId, p.Id))
                .ToListAsync(cancellationToken));
        }

        return due;
    }
}
