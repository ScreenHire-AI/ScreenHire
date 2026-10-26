using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Content;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// Which jobs a list reads: optionally those whose title contains <paramref name="Search"/>, those in one
/// <paramref name="Status"/>, and at most <paramref name="Limit"/>. The HTTP list passes none of them.
/// </summary>
internal sealed record JobFilter(string? Search = null, JobStatus? Status = null, int? Limit = null);

/// <summary>
/// Reading the job list, for the hiring API's <c>GET /v1/jobs</c> and the MCP tools alike. The tenant and job-scope
/// filters decide which jobs the reader sees.
/// </summary>
internal static class JobReads
{
    /// <summary>The jobs the filter names, newest first.</summary>
    public static async Task<List<JobSummary>> ListAsync(ScreenHireDbContext db, JobFilter filter, CancellationToken cancellationToken)
    {
        var jobs = db.Jobs.AsNoTracking();
        if (filter.Status is { } status)
        {
            jobs = jobs.Where(j => j.Status == status);
        }

        if (Like.Contains(filter.Search) is { } pattern)
        {
            jobs = jobs.Where(j => EF.Functions.ILike(j.Title, pattern, Like.Escape));
        }

        var ordered = jobs.OrderByDescending(j => j.CreatedAt).ThenBy(j => j.Id);
        var rows = await (filter.Limit is { } limit ? ordered.Take(limit) : ordered)
            .Select(j => new
            {
                Job = j,
                LatestTemplateVersion = db.HiringTemplates.Where(t => t.Id == j.SourceTemplateId).Select(t => t.LatestPublishedVersion).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        var changes = await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.Job, [.. rows.Select(r => r.Job.Id.ToString("D"))], cancellationToken);

        return [.. rows.Select(r => r.Job.ToSummary(changes.GetValueOrDefault(r.Job.Id.ToString("D")), r.LatestTemplateVersion))];
    }
}
