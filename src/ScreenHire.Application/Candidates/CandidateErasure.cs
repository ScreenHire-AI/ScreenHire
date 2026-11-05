using System.Collections;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Privacy;
using ScreenHire.Application.Scheduling;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Candidates;

/// <summary>
/// Erases a candidate, or only some of their applications, from the database (docs/architecture/candidates.md, "Erasure").
/// One implementation behind the erase route, the Erase action on a data request and the record retention run.
/// </summary>
/// <remarks>
/// It opens no transaction and publishes nothing. It works on the <see cref="ScreenHireDbContext"/> it is given, inside the
/// transaction the caller already opened (the route's explicit one, or the Wolverine handler's), changes tracked entities for
/// the caller's <c>SaveChanges</c> to write, and returns the messages the caller must send in that same save: the route
/// through its outbox, a handler as outgoing messages. The caller owns the typed confirmation.
/// </remarks>
public sealed class CandidateErasure(IApplicantDirectory directory)
{
    /// <summary>
    /// Erases <paramref name="candidateId"/> and, by the database's cascades, every application and row under them. Takes the
    /// candidate's row lock first (a second time is free), so an application arriving now waits and cannot cascade away with
    /// its files missing from the messages. Returns null when the candidate is not there, which the caller treats as gone.
    /// </summary>
    /// <param name="db">The caller's context, inside its open transaction.</param>
    /// <param name="candidateId">The candidate to erase.</param>
    /// <param name="actorUserId">The member who erased, or null when the retention schedule did.</param>
    /// <param name="dataRequestId">The data request that asked for it, stamped on the tombstone; null for the route and retention.</param>
    /// <param name="now">The time of the erasure, for the tombstone and the requests it closes.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <remarks>
    /// The candidate's data requests keep their rows with no candidate: the export columns and any decline reason are cleared,
    /// an open one is completed (erasing answers it), and the copies' blobs are named in an <see cref="EraseDataExports"/> message.
    /// </remarks>
    public async Task<ErasureMessages?> EraseCandidateAsync(
        ScreenHireDbContext db, Guid candidateId, Guid? actorUserId, Guid? dataRequestId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        RequireTransaction(db);
        await LockCandidatesAsync(db, [candidateId], cancellationToken);
        if (await db.Candidates.SingleOrDefaultAsync(c => c.Id == candidateId, cancellationToken) is not { } candidate)
        {
            return null;
        }

        var organizationId = candidate.OrganizationId;
        var applications = await db.Applications.AsNoTracking().Where(a => a.CandidateId == candidateId).ToListAsync(cancellationToken);
        var removal = await ReadAsync(db, organizationId, applications, new Dictionary<Guid, Candidate> { [candidateId] = candidate }, now, cancellationToken);

        // The candidate's requests are tracked here, so the foreign key's SET NULL and these changes are one save.
        var exportBlobs = new List<string>();
        foreach (var request in await db.DataRequests.Where(r => r.CandidateId == candidateId).ToListAsync(cancellationToken))
        {
            if (request.ForgetCandidate(actorUserId, now) is { } blob)
            {
                exportBlobs.Add(blob);
            }
        }

        // A draft saved before applying is keyed by the address, not by a candidate row, so no cascade reaches it.
        db.ApplicationDrafts.RemoveRange(await db.ApplicationDrafts.Where(d => d.Email == candidate.NormalizedEmail).ToListAsync(cancellationToken));
        // The history of a saved note holds its text, keeps no link to the candidate and is append-only for this role. Run
        // before the delete below: the function keeps what belongs to every other candidate's entry.
        await db.Database.ExecuteSqlAsync($"SELECT erase_saved_note_history({candidateId})", cancellationToken);
        db.Candidates.Remove(candidate);
        db.MembershipEvents.Add(new MembershipEvent
        {
            OrganizationId = organizationId,
            Kind = MembershipEventKind.CandidateErased,
            ActorUserId = actorUserId,
            DataRequestId = dataRequestId,
            OccurredAt = now,
        });

        var messages = new ErasureMessages();
        if (removal.Files.Count > 0 || removal.ReplyAttachments.Count > 0)
        {
            messages.Add(new EraseApplicantFiles(organizationId, removal.Files, removal.ReplyAttachments));
        }

        if (exportBlobs.Count > 0)
        {
            messages.Add(new EraseDataExports(organizationId, exportBlobs));
        }

        var deleting = applications.Select(a => a.Id).ToHashSet();
        foreach (var recheck in removal.Rechecks.Where(r => !deleting.Contains(r.ApplicationId)).DistinctBy(r => (r.ApplicationId, r.CauseApplicationId)))
        {
            messages.Add(recheck);
        }

        if (removal.EventsByApplication.Values.SelectMany(e => e).ToList() is { Count: > 0 } events)
        {
            messages.Add(new RemoveInterviewEvents(organizationId, candidateId, events));
        }

        messages.Add(new CandidateErased(organizationId, candidateId, now));
        return messages;
    }

    /// <summary>
    /// Deletes the applications in <paramref name="applicationIds"/> (record retention). A candidate left with no application
    /// is erased whole by <see cref="EraseCandidateAsync"/>, with no actor; one who keeps another application loses only the
    /// selected ones. A candidate who is saved (has a saved candidates entry) is skipped entirely, whatever the ids say.
    /// Ids that are no longer there are skipped.
    /// </summary>
    /// <remarks>
    /// Locks the affected candidates' rows in id order first, so an application arriving now waits, and the interviewers'
    /// calendar locks together before any message is built, so two runs never wait on each other in a circle. For a partly
    /// removed candidate the interview events message carries the application's id as its cause, so a later erasure of the
    /// whole candidate (cause: the candidate) is not dropped as a duplicate of it.
    /// </remarks>
    public async Task<ApplicationErasure> EraseApplicationsAsync(
        ScreenHireDbContext db, IReadOnlyList<Guid> applicationIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(applicationIds);
        RequireTransaction(db);
        var messages = new ErasureMessages();
        var wanted = applicationIds.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return new ApplicationErasure(messages, 0, 0);
        }

        var candidateIds = await db.Applications.AsNoTracking().Where(a => wanted.Contains(a.Id)).Select(a => a.CandidateId).Distinct().ToListAsync(cancellationToken);
        await LockCandidatesAsync(db, candidateIds, cancellationToken);

        // After the locks: nothing new can attach to these candidates until the transaction ends.
        var held = await db.Applications.AsNoTracking().Where(a => candidateIds.Contains(a.CandidateId))
            .Select(a => new { a.Id, a.CandidateId, a.OrganizationId }).ToListAsync(cancellationToken);
        var saved = (await db.TalentPoolEntries.AsNoTracking().Where(e => candidateIds.Contains(e.CandidateId)).Select(e => e.CandidateId).Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();
        var wantedSet = wanted.ToHashSet();
        var wholeCandidates = new List<Guid>();
        var partlyRemoved = new List<Guid>();
        foreach (var group in held.Where(a => !saved.Contains(a.CandidateId)).GroupBy(a => a.CandidateId).OrderBy(g => g.Key))
        {
            var selected = group.Where(a => wantedSet.Contains(a.Id)).Select(a => a.Id).ToList();
            if (selected.Count == group.Count())
            {
                wholeCandidates.Add(group.Key);
            }
            else
            {
                partlyRemoved.AddRange(selected);
            }
        }

        if (wholeCandidates.Count == 0 && partlyRemoved.Count == 0)
        {
            return new ApplicationErasure(messages, 0, 0);
        }

        var wholeSet = wholeCandidates.ToHashSet();
        var gone = held.Where(a => wholeSet.Contains(a.CandidateId)).Select(a => a.Id).Concat(partlyRemoved).ToHashSet();

        // Every interviewer's calendar lock, taken once in sorted order, before the per-candidate reads take theirs again.
        var attendeeUsers = await db.InterviewAttendees.AsNoTracking().Where(a => gone.Contains(a.ApplicationId)).Select(a => a.UserId).ToListAsync(cancellationToken);
        if (attendeeUsers.Count > 0)
        {
            await InterviewLocks.TakeAsync(db, held[0].OrganizationId, attendeeUsers, cancellationToken);
        }

        foreach (var candidateId in wholeCandidates)
        {
            if (await EraseCandidateAsync(db, candidateId, actorUserId: null, dataRequestId: null, now, cancellationToken) is { } whole)
            {
                messages.AddRange(whole);
            }
        }

        if (partlyRemoved.Count > 0)
        {
            var applications = await db.Applications.Where(a => partlyRemoved.Contains(a.Id)).ToListAsync(cancellationToken);
            var owners = applications.Select(a => a.CandidateId).Distinct().ToList();
            var candidates = await db.Candidates.AsNoTracking().Where(c => owners.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
            var removal = await ReadAsync(db, applications[0].OrganizationId, applications, candidates, now, cancellationToken);
            db.Applications.RemoveRange(applications);

            if (removal.Files.Count > 0 || removal.ReplyAttachments.Count > 0)
            {
                messages.Add(new EraseApplicantFiles(applications[0].OrganizationId, removal.Files, removal.ReplyAttachments));
            }

            foreach (var recheck in removal.Rechecks.DistinctBy(r => (r.ApplicationId, r.CauseApplicationId)))
            {
                messages.Add(recheck);
            }

            foreach (var (applicationId, events) in removal.EventsByApplication.Where(e => e.Value.Count > 0).OrderBy(e => e.Key))
            {
                messages.Add(new RemoveInterviewEvents(applications[0].OrganizationId, applicationId, events));
            }
        }

        // An application that is going anyway needs no new assessment because another one went.
        messages.DropRechecksFor(gone);
        return new ApplicationErasure(messages, gone.Count, wholeCandidates.Count);
    }

    private static void RequireTransaction(ScreenHireDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Erasing needs the caller's transaction, so the deletes and the messages commit together.");
        }
    }

    // The same lock the erase route takes: a foreign key insert (a new application) takes a share lock on the candidate row,
    // so it waits here. In id order, so two erasures never wait on each other in a circle.
    private static async Task LockCandidatesAsync(ScreenHireDbContext db, IReadOnlyCollection<Guid> candidateIds, CancellationToken cancellationToken)
    {
        var ids = candidateIds.Distinct().Order().ToArray();
        if (ids.Length > 0)
        {
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM candidates WHERE id = ANY({ids}) ORDER BY id FOR UPDATE", cancellationToken);
        }
    }

    // What the applications' removal has to tell the rest of the system, read before the rows go. A person has a handful of
    // applications, so the directory is asked once for each, the same question the duplicate check asks.
    private async Task<Removal> ReadAsync(
        ScreenHireDbContext db,
        Guid organizationId,
        IReadOnlyList<JobApplication> applications,
        Dictionary<Guid, Candidate> candidates,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var applicationIds = applications.Select(a => a.Id).ToList();
        var jobIds = applications.Select(a => a.JobId).Distinct().ToList();
        var jobs = await db.Jobs.AsNoTracking().Where(j => jobIds.Contains(j.Id)).ToDictionaryAsync(j => j.Id, cancellationToken);
        var files = await db.ApplicationFiles.AsNoTracking().Where(f => applicationIds.Contains(f.ApplicationId)).ToListAsync(cancellationToken);
        var replyAttachments = await db.InboundEmailAttachments.AsNoTracking()
            .Where(a => applicationIds.Contains(a.ApplicationId)).Select(a => a.BlobName).ToListAsync(cancellationToken);
        var observations = await db.SubmissionObservations.AsNoTracking()
            .Where(o => applicationIds.Contains(o.ApplicationId)).ToDictionaryAsync(o => o.ApplicationId, cancellationToken);

        // The events on interviewers' calendars hold the candidate's name and address, and the rows that say where they are
        // go with the application. So their places are read now, under the interviewers' locks (a calendar write in flight
        // finishes and saves its event ids first), and a message deletes them quietly once this commits.
        var interviewers = await db.InterviewAttendees.AsNoTracking().Where(a => applicationIds.Contains(a.ApplicationId)).Select(a => a.UserId).ToListAsync(cancellationToken);
        await InterviewLocks.TakeAsync(db, organizationId, interviewers, cancellationToken);
        var interviews = await db.InterviewRequests.AsNoTracking().Where(r => applicationIds.Contains(r.ApplicationId)).ToDictionaryAsync(r => r.Id, cancellationToken);
        var eventsByApplication = applicationIds.ToDictionary(id => id, _ => new List<InterviewEventToRemove>());
        foreach (var attendee in await db.InterviewAttendees.AsNoTracking().Where(a => applicationIds.Contains(a.ApplicationId)).ToListAsync(cancellationToken))
        {
            if (interviews.TryGetValue(attendee.InterviewId, out var interview)
                && (interview.Status == InterviewStatus.Booked
                    || (interview.Status == InterviewStatus.Cancelled && attendee.ProviderEventId is not null && attendee.SyncStatus != InterviewSyncStatus.Synced)))
            {
                eventsByApplication[attendee.ApplicationId].Add(
                    new InterviewEventToRemove(attendee.InterviewId, attendee.UserId, attendee.ProviderEventId, interview.WindowStart, interview.WindowEnd));
            }
        }

        var rechecks = new List<RecheckApplicationDuplicates>();
        foreach (var application in applications)
        {
            var context = new VerificationContext(
                application, candidates[application.CandidateId], jobs[application.JobId], observations.GetValueOrDefault(application.Id),
                [.. files.Where(f => f.ApplicationId == application.Id)], now);
            var related = (await directory.FindSharedAsync(context, cancellationToken)).SelectMany(m => m.ApplicationIds).Distinct();
            rechecks.AddRange(related.Select(id => new RecheckApplicationDuplicates(organizationId, id, application.Id, CauseErased: true)));
        }

        return new Removal([.. files.Select(f => f.BlobName)], replyAttachments, eventsByApplication, rechecks);
    }

    private sealed record Removal(
        IReadOnlyList<string> Files,
        IReadOnlyList<string> ReplyAttachments,
        Dictionary<Guid, List<InterviewEventToRemove>> EventsByApplication,
        List<RecheckApplicationDuplicates> Rechecks);
}

/// <summary>What <see cref="CandidateErasure.EraseApplicationsAsync"/> removed: the messages to send, and how many.</summary>
/// <param name="Messages">Send every one, in the same save as the deletes.</param>
/// <param name="ApplicationsDeleted">Applications deleted, including those of candidates erased whole.</param>
/// <param name="CandidatesErased">Candidates left with no application and erased whole.</param>
public sealed record ApplicationErasure(ErasureMessages Messages, int ApplicationsDeleted, int CandidatesErased);

/// <summary>
/// The messages an erasure asks its caller to send in the same save as its deletes: the applicant files and data copies to
/// delete from storage, interview events to remove from calendars, duplicate checks to run again, and the
/// <see cref="CandidateErased"/> event. Each element is a message; publish them by their own type.
/// </summary>
public sealed class ErasureMessages : IReadOnlyList<object>
{
    private readonly List<object> items = [];

    public int Count => items.Count;

    public object this[int index] => items[index];

    public IEnumerator<object> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal void Add(object message) => items.Add(message);

    internal void AddRange(IEnumerable<object> messages) => items.AddRange(messages);

    internal void DropRechecksFor(IReadOnlySet<Guid> applicationIds) =>
        items.RemoveAll(m => m is RecheckApplicationDuplicates recheck && applicationIds.Contains(recheck.ApplicationId));
}
