using Microsoft.EntityFrameworkCore;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// The per-interviewer Postgres advisory locks that order every change to an interviewer's booked interviews
/// (docs/architecture/interview-scheduling.md): a booking, a move, a cancellation and the worker's calendar writes all take
/// them, so one cannot slip between another's check and its write. The locks last until the current transaction ends.
/// </summary>
public static class InterviewLocks
{
    /// <summary>
    /// Takes the lock of each of <paramref name="userIds"/> in sorted order, so two callers never wait on each other in a
    /// circle. A lock outside a transaction would be released at once, so that is refused.
    /// </summary>
    public static async Task TakeAsync(ScreenHireDbContext db, Guid organizationId, IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(userIds);
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The interview locks last for a transaction, and none is open.");
        }

        foreach (var userId in userIds.Distinct().Order())
        {
            var key = $"interview-calendar:{organizationId:N}:{userId:N}";
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        }
    }
}
