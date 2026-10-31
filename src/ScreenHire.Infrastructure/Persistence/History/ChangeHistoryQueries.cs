using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.History;

namespace ScreenHire.Infrastructure.Persistence.History;

/// <summary>
/// When an object last changed and who changed it: a member, an API key, or neither (ScreenHire). <c>Created</c>: that
/// change created the object, so nothing has changed it since.
/// </summary>
public sealed record ChangeStamp(DateTimeOffset At, Guid? UserId, Guid? ApiKeyId, bool Created = false);

/// <summary>Reads of the change history, through the tenant filter and row-level security like every tenant read.</summary>
public static class ChangeHistoryQueries
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 100;

    /// <summary>
    /// An object's changes, newest first: up to <paramref name="limit"/> records older than the record
    /// <paramref name="before"/>, and the id of this page's last record when there is a next page. Null when
    /// <paramref name="before"/> is not one of this object's records.
    /// </summary>
    /// <remarks>
    /// The cursor is a record's id, never its <see cref="ChangeRecord.Sequence"/>: the sequence counts every
    /// organization's records, so the gap between two of an organization's own would tell it how busy the others are.
    /// </remarks>
    public static async Task<(IReadOnlyList<ChangeRecord> Records, Guid? NextBefore)?> PageAsync(
        ScreenHireDbContext db, ChangeSubjectKind kind, string subjectId, Guid? before, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var query = db.ChangeRecords.AsNoTracking().Where(r => r.SubjectKind == kind && r.SubjectId == subjectId);
        if (before is { } cursor)
        {
            var position = await query.Where(r => r.Id == cursor).Select(r => (long?)r.Sequence).SingleOrDefaultAsync(cancellationToken);
            if (position is null)
            {
                return null;
            }

            query = query.Where(r => r.Sequence < position);
        }

        var page = await query.OrderByDescending(r => r.Sequence).Take(limit + 1).ToListAsync(cancellationToken);
        return page.Count > limit ? (page[..limit], page[limit - 1].Id) : (page, null);
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
            .Select(r => new { r.SubjectId, r.OccurredAt, r.ActorUserId, r.ActorApiKeyId, r.Created })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.SubjectId, r => new ChangeStamp(r.OccurredAt, r.ActorUserId, r.ActorApiKeyId, r.Created), StringComparer.Ordinal);
    }

    /// <summary>The newest change of one object, or null when it has none.</summary>
    public static async Task<ChangeStamp?> LastAsync(ScreenHireDbContext db, ChangeSubjectKind kind, string subjectId, CancellationToken cancellationToken) =>
        (await LastAsync(db, kind, [subjectId], cancellationToken)).GetValueOrDefault(subjectId);
}
