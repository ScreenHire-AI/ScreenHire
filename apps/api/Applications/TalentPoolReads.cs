using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.History;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;

namespace ScreenHire.Api.Applications;

/// <summary>
/// Reading the saved candidates, for their route and the MCP tool alike, so both apply the same rules: the tenant and
/// job-scope filters, and only people whose permission for future roles still holds (it has no end date).
/// </summary>
internal static class TalentPoolReads
{
    // What counts as contacting a candidate: the events that record an email to them. Replies are not read yet (roadmap
    // item 1); when they are, their event type joins this list.
    private static readonly ApplicationEventType[] ContactTypes = [ApplicationEventType.EmailSent];

    /// <summary>
    /// Everyone saved whose permission still holds. A pool is a few hundred people at most, so it is one read and the rule
    /// runs here. <paramref name="userId"/> is the member reading, for the reviewer gate on Rating (null for an API key,
    /// which has no scorecards to give). The rating is the one the candidate page shows; the last email is the newest to
    /// the candidate over every application the reader sees, whichever job it was for.
    /// </summary>
    public static async Task<TalentPoolList> ReadAsync(ScreenHireDbContext db, Guid? userId, CancellationToken cancellationToken)
    {
        var rows = await db.TalentPoolEntries.AsNoTracking()
            .Join(db.Applications, e => e.ApplicationId, a => a.Id, (e, a) => new { Entry = e, Application = a })
            .Join(db.Jobs, x => x.Application.JobId, j => j.Id, (x, j) => new { x.Entry, x.Application, Job = j })
            .Join(db.Candidates, x => x.Entry.CandidateId, c => c.Id, (x, c) => new { x.Entry, x.Application, x.Job, Candidate = c })
            .ToListAsync(cancellationToken);
        var current = rows.Where(r => r.Application.AllowsFutureRoles).ToList();

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

        var ratings = await RatingReads.ForAsync(db, [.. current.Select(r => r.Application.Id)], userId, cancellationToken);
        var lastContacted = await db.ApplicationEvents.AsNoTracking()
            .Where(e => ContactTypes.Contains(e.Type))
            .Join(db.Applications.Where(a => candidateIds.Contains(a.CandidateId)), e => e.ApplicationId, a => a.Id, (e, a) => new { a.CandidateId, e.OccurredAt })
            .GroupBy(x => x.CandidateId)
            .Select(g => new { CandidateId = g.Key, At = g.Max(x => x.OccurredAt) })
            .ToDictionaryAsync(x => x.CandidateId, x => x.At, cancellationToken);
        var edits = await ChangeHistoryQueries.LastAsync(
            db, ChangeSubjectKind.TalentPoolEntry, [.. current.Select(r => r.Entry.Id.ToString("D"))], cancellationToken);

        var people = current
            .OrderBy(r => r.Candidate.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new TalentPoolMember(
                r.Candidate.Id, r.Candidate.FullName, r.Candidate.Email, r.Application.Id, r.Job.Id, r.Job.Title,
                r.Entry.Note, edits.TryGetValue(r.Entry.Id.ToString("D"), out var edit) ? LastUpdate.FromChange(edit, null) : null,
                r.Application.Tags, r.Entry.KeptAt, r.Entry.KeptByUserId,
                ratings.GetValueOrDefault(r.Application.Id) ?? ApplicationRating.NotRated,
                lastContacted.TryGetValue(r.Candidate.Id, out var contacted) ? contacted : null,
                r.Entry.LastInvitedJobId,
                r.Entry.LastInvitedJobId is { } invited ? invitedTitles.GetValueOrDefault(invited) : null, r.Entry.LastInvitedAt,
                [.. applied[r.Candidate.Id].Distinct()]))
            .ToList();
        return new TalentPoolList(people);
    }
}
