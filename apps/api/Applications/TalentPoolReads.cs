using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.TalentPool;
using ScreenHire.Domain.Applications;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Applications;

/// <summary>
/// Reading the talent pool, for its route and the MCP tool alike, so both apply the same rules: the tenant and job-scope
/// filters, and only people whose permission to be considered for future roles still holds.
/// </summary>
internal static class TalentPoolReads
{
    /// <summary>
    /// Everyone in the pool whose permission still holds: the daily sweep takes out whoever it ended for, and until it runs
    /// they are already off the list. A pool is a few hundred people at most, so it is one read and the rule runs here.
    /// </summary>
    public static async Task<TalentPoolList> ReadAsync(ScreenHireDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var months = await TalentPoolMembership.MonthsAsync(db, cancellationToken);
        var rows = await db.TalentPoolEntries.AsNoTracking()
            .Join(db.Applications, e => e.ApplicationId, a => a.Id, (e, a) => new { Entry = e, Application = a })
            .Join(db.Jobs, x => x.Application.JobId, j => j.Id, (x, j) => new { x.Entry, x.Application, Job = j })
            .Join(db.Candidates, x => x.Entry.CandidateId, c => c.Id, (x, c) => new { x.Entry, x.Application, x.Job, Candidate = c })
            .ToListAsync(cancellationToken);
        var current = rows.Where(r => r.Application.AllowsFutureRoles(months, now)).ToList();

        var keptEventIds = current.Select(r => r.Entry.KeptEventId).ToList();
        var notes = await db.ApplicationEvents.AsNoTracking()
            .Where(e => keptEventIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Data.Note, cancellationToken);
        var candidateIds = current.Select(r => r.Entry.CandidateId).ToList();
        var applied = (await db.Applications.AsNoTracking()
                .Where(a => candidateIds.Contains(a.CandidateId))
                .Select(a => new { a.CandidateId, a.JobId })
                .ToListAsync(cancellationToken))
            .ToLookup(a => a.CandidateId, a => a.JobId);
        var invitedJobIds = current.Select(r => r.Entry.LastInvitedJobId).OfType<Guid>().Distinct().ToList();
        var invitedTitles = await db.Jobs.AsNoTracking()
            .Where(j => invitedJobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.Title, cancellationToken);

        var people = current
            .OrderBy(r => r.Candidate.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new TalentPoolMember(
                r.Candidate.Id, r.Candidate.FullName, r.Candidate.Email, r.Application.Id, r.Job.Id, r.Job.Title,
                notes.GetValueOrDefault(r.Entry.KeptEventId), r.Application.Tags, r.Entry.KeptAt, r.Entry.KeptByUserId,
                r.Application.FutureRolesUntil(months)!.Value, r.Entry.LastInvitedJobId,
                r.Entry.LastInvitedJobId is { } invited ? invitedTitles.GetValueOrDefault(invited) : null, r.Entry.LastInvitedAt,
                [.. applied[r.Candidate.Id].Distinct()]))
            .ToList();
        return new TalentPoolList(months, people);
    }
}
