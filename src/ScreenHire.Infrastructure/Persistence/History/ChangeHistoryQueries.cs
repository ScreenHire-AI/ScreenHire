using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.History;

namespace ScreenHire.Infrastructure.Persistence.History;

/// <summary>When an object last changed and who changed it: a member, an API key, or neither (ScreenHire).</summary>
public sealed record ChangeStamp(DateTimeOffset At, Guid? UserId, Guid? ApiKeyId);

/// <summary>Reads of the change history, through the tenant filter and row-level security like every tenant read.</summary>
public static class ChangeHistoryQueries
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 100;

    /// <summary>
    /// An object's changes, newest first: up to <paramref name="limit"/> records older than the cursor
    /// <paramref name="before"/>, and the cursor of the next page, or null when this page is the last.
    /// </summary>
    public static async Task<(IReadOnlyList<ChangeRecord> Records, long? NextBefore)> PageAsync(
        ScreenHireDbContext db, ChangeSubjectKind kind, string subjectId, long? before, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var query = db.ChangeRecords.AsNoTracking().Where(r => r.SubjectKind == kind && r.SubjectId == subjectId);
        if (before is { } cursor)
        {
            query = query.Where(r => r.Sequence < cursor);
        }

        var page = await query.OrderByDescending(r => r.Sequence).Take(limit + 1).ToListAsync(cancellationToken);
        return page.Count > limit ? (page[..limit], page[limit - 1].Sequence) : (page, null);
    }

    /// <summary>The newest change of each of <paramref name="subjectIds"/> that has one, in one query.</summary>
    public static async Task<IReadOnlyDictionary<string, ChangeStamp>> LastAsync(
        ScreenHireDbContext db, ChangeSubjectKind kind, IReadOnlyCollection<string> subjectIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (subjectIds.Count == 0)
        {
            return new Dictionary<string, ChangeStamp>(StringComparer.Ordinal);
        }

        var records = db.ChangeRecords.AsNoTracking().Where(r => r.SubjectKind == kind && subjectIds.Contains(r.SubjectId));
        var newest = records.GroupBy(r => r.SubjectId).Select(g => g.Max(r => r.Sequence));
        var rows = await records
            .Where(r => newest.Contains(r.Sequence))
            .Select(r => new { r.SubjectId, r.OccurredAt, r.ActorUserId, r.ActorApiKeyId })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.SubjectId, r => new ChangeStamp(r.OccurredAt, r.ActorUserId, r.ActorApiKeyId), StringComparer.Ordinal);
    }

    /// <summary>The newest change of one object, or null when it has none.</summary>
    public static async Task<ChangeStamp?> LastAsync(ScreenHireDbContext db, ChangeSubjectKind kind, string subjectId, CancellationToken cancellationToken) =>
        (await LastAsync(db, kind, [subjectId], cancellationToken)).GetValueOrDefault(subjectId);
}
