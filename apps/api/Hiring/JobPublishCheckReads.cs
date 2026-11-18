using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Distribution;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// The check before a job goes live (docs/architecture/job-distribution.md), for <c>GET /v1/jobs/{jobId}/publish-check</c> and
/// the MCP <c>check_job</c> tool alike, so a person, an API key and an assistant see one list. The tenant and job-scope
/// filters decide which jobs are visible.
/// </summary>
internal static class JobPublishCheckReads
{
    /// <summary>
    /// What to check on the job before it goes to the boards a publish would use: every available board for a draft, the
    /// boards it is on for an open job. A closed job has nothing to check. Null when the job is not visible.
    /// </summary>
    public static async Task<JobPublishCheck?> ReadAsync(ScreenHireDbContext db, BoardRegistry boards, Guid jobId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(boards);
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        if (job.Status == JobStatus.Closed)
        {
            return new JobPublishCheck([]);
        }

        var organization = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == job.OrganizationId, cancellationToken);
        var postings = await db.JobPostings.AsNoTracking().Where(p => p.JobId == job.Id).ToListAsync(cancellationToken);
        var snapshot = job.Snapshot();
        var chosen = DistributionPlanner.ChosenBoards([.. boards.AvailableTo(organization).Select(a => a.BoardId)], postings, boardIds: null);
        var targets = chosen
            .Select(boards.Find)
            .OfType<IJobBoardAdapter>()
            .Select(adapter => (
                Board: adapter,
                Content: postings.FirstOrDefault(p => p.BoardId == adapter.BoardId) is { IsCustomized: true } posting ? posting.WithOverrides(snapshot) : snapshot))
            .ToList();

        // The built-in templates' definitions live only in the migrations, so their prompts are read from the rows.
        var definitions = await db.HiringTemplateVersions.AsNoTracking()
            .Where(v => v.OrganizationId == null)
            .Select(v => v.Definition)
            .ToListAsync(cancellationToken);
        var prompts = TemplatePrompts.From(definitions.Select(d => d.PostingDefaults.Description));
        return new JobPublishCheck(PublishChecks.Run(snapshot, targets, prompts));
    }
}
