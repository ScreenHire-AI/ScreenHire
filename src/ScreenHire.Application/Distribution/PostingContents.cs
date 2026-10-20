using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Distribution;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Distribution;

/// <summary>Loads what adapters read: a posting's frozen job version, with its organization and first publish time.</summary>
public static class PostingContents
{
    /// <summary>The content of <paramref name="posting"/>'s version, or null if that version or its job is not visible.</summary>
    public static async Task<PostingContent?> LoadAsync(ScreenHireDbContext db, JobPosting posting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(posting);
        var row = await (
                from version in db.JobVersions.AsNoTracking()
                join job in db.Jobs.AsNoTracking() on version.JobId equals job.Id
                join organization in db.Organizations.AsNoTracking() on job.OrganizationId equals organization.Id
                where version.JobId == posting.JobId && version.Version == posting.JobVersion
                select new { version, job.PublishedAt, organization.Name, organization.Slug })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new PostingContent(
                posting.JobId, row.version.Version, row.version.Snapshot, row.PublishedAt ?? row.version.PublishedAt,
                row.version.PublishedAt, posting.Url, row.Name, row.Slug);
    }

    /// <summary>
    /// The content of every active posting on <paramref name="boardId"/>, or only <paramref name="jobId"/>'s, for a feed or
    /// the public job-board API, newest first. One query.
    /// </summary>
    public static async Task<List<PostingContent>> ActiveOnBoardAsync(
        ScreenHireDbContext db, string boardId, string organizationName, string organizationSlug, CancellationToken cancellationToken,
        Guid? jobId = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        var rows = await (
                from posting in db.JobPostings.AsNoTracking()
                // JobPosting.IsActive in SQL; change both together.
                where posting.BoardId == boardId
                      && (jobId == null || posting.JobId == jobId)
                      && (posting.State == PostingState.Queued || posting.State == PostingState.Submitted || posting.State == PostingState.Live)
                join version in db.JobVersions.AsNoTracking()
                    on new { posting.JobId, Version = posting.JobVersion } equals new { version.JobId, version.Version }
                join job in db.Jobs.AsNoTracking() on posting.JobId equals job.Id
                orderby job.PublishedAt descending
                select new { posting.JobId, posting.Url, version.Version, version.Snapshot, VersionPublishedAt = version.PublishedAt, job.PublishedAt })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new PostingContent(
            r.JobId, r.Version, r.Snapshot, r.PublishedAt ?? r.VersionPublishedAt, r.VersionPublishedAt, r.Url, organizationName, organizationSlug))];
    }
}
