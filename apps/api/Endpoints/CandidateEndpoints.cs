using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Candidates;
using ScreenHire.Application.Scheduling;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The candidate routes only people use: the history across all of a candidate's applications and erasing a candidate.
/// They are not in the <c>/v1/candidates</c> group of <see cref="ApplicationEndpoints"/>, which accepts organization API
/// keys: no integration and no MCP tool reads this history or erases anyone (ADR-0011). Tenant-scoped like every
/// dashboard route, and the job-access filter applies as everywhere.
/// </summary>
internal static class CandidateEndpoints
{
    public static IEndpointRouteBuilder MapCandidateEndpoints(this IEndpointRouteBuilder app)
    {
        var candidates = app.MapGroup("/v1/candidates").WithTags("Candidates").RequireAuthorization();
        candidates.MapGet("/{candidateId:guid}/history", GetCandidateHistory).WithName("GetCandidateHistory")
            .Produces(StatusCodes.Status401Unauthorized);
        candidates.MapPost("/{candidateId:guid}/erase", EraseCandidate).WithName("EraseCandidate")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EraseCandidates)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // Every application's timeline in one list, newest first. Applications outside the reader's job access are not in
    // `db.Applications`, so their entries never appear. One query each for the applications, events and notes.
    private static async Task<Results<Ok<List<CandidateHistoryEntry>>, NotFound>> GetCandidateHistory(
        Guid candidateId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Candidates.AnyAsync(c => c.Id == candidateId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var applications = await db.Applications.AsNoTracking()
            .Where(a => a.CandidateId == candidateId)
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { ApplicationId = a.Id, JobId = j.Id, j.Title, j.Workflow })
            .ToListAsync(cancellationToken);
        var ids = applications.Select(a => a.ApplicationId).ToList();
        var events = await db.ApplicationEvents.AsNoTracking()
            .Where(e => ids.Contains(e.ApplicationId) && e.Type != ApplicationEventType.VerificationAssessed)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .ToListAsync(cancellationToken);
        var noteIds = events.Select(e => e.Data.NoteId).OfType<Guid>().Distinct().ToList();
        var notes = await db.ApplicationNotes.AsNoTracking().IgnoreAutoIncludes()
            .Where(n => noteIds.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, cancellationToken);
        var keyIds = events.Select(e => e.Data.ApiKeyId).OfType<Guid>().Distinct().ToList();
        var keyNames = keyIds.Count == 0
            ? []
            : await db.ApiKeys.AsNoTracking().Where(k => keyIds.Contains(k.Id)).ToDictionaryAsync(k => k.Id, k => k.Name, cancellationToken);
        var byApplication = applications.ToDictionary(a => a.ApplicationId);

        return TypedResults.Ok(events.Select(e =>
        {
            var application = byApplication[e.ApplicationId];
            var entry = e.ToTimelineEntry(
                e.Data.NoteId is { } noteId ? notes.GetValueOrDefault(noteId) : null,
                e.Data.ApiKeyId is { } keyId ? keyNames.GetValueOrDefault(keyId) : null);
            // A reviewer with a scorecard still to give must not read others' (ADR-0013); the history never shows them.
            string? NameOf(string? stageId) => stageId is null ? null : application.Workflow.FindStage(stageId)?.Name;
            return new CandidateHistoryEntry(
                e.ApplicationId, application.JobId, application.Title, entry with { ScorecardId = null, Recommendation = null },
                NameOf(entry.FromStageId), NameOf(entry.ToStageId), NameOf(entry.StageId));
        }).ToList());
    }

    // Erases the candidate and, by the database's cascades, every application, answer, file row, note, email, task, scorecard
    // and piece of verification data under them, in the one transaction that records the tombstone. Needs a person who sees
    // every job: erasing reaches every application, so one on Only jobs they're on gets a 404 and learns nothing. The files'
    // blobs and their replies' attachments go with a message sent in the same transaction. The applications that shared a
    // detail with the erased ones are checked again afterwards, so no level keeps resting on a person who is gone.
    private static async Task<Results<NoContent, NotFound, ValidationProblem>> EraseCandidate(
        Guid candidateId,
        EraseCandidateRequest request,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        IApplicantDirectory directory,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        if (!tenant.SeesAllJobs || tenant.OrganizationId is not { } organizationId)
        {
            return TypedResults.NotFound();
        }

        // No CommitAsync here: SaveChangesAndFlushMessagesAsync commits this transaction with the outbox rows. The row lock
        // makes an application arriving now wait, so one cannot cascade away with its files missing from the erasure below.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM candidates WHERE id = {candidateId} FOR UPDATE", cancellationToken);
        if (await db.Candidates.SingleOrDefaultAsync(c => c.Id == candidateId, cancellationToken) is not { } candidate)
        {
            return TypedResults.NotFound();
        }

        if (!string.Equals(request.Confirmation?.Trim(), candidate.FullName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("confirmation", "Type the candidate's full name to confirm."));
        }

        var now = time.GetUtcNow();
        var applications = await db.Applications.AsNoTracking().Where(a => a.CandidateId == candidateId).ToListAsync(cancellationToken);
        var applicationIds = applications.Select(a => a.Id).ToList();
        var jobIds = applications.Select(a => a.JobId).Distinct().ToList();
        var jobs = await db.Jobs.AsNoTracking().Where(j => jobIds.Contains(j.Id)).ToDictionaryAsync(j => j.Id, cancellationToken);
        var files = await db.ApplicationFiles.AsNoTracking().Where(f => applicationIds.Contains(f.ApplicationId)).ToListAsync(cancellationToken);
        var replyAttachments = await db.InboundEmailAttachments.AsNoTracking()
            .Where(a => applicationIds.Contains(a.ApplicationId)).Select(a => a.BlobName).ToListAsync(cancellationToken);
        var observations = await db.SubmissionObservations.AsNoTracking()
            .Where(o => applicationIds.Contains(o.ApplicationId)).ToDictionaryAsync(o => o.ApplicationId, cancellationToken);

        // The events on interviewers' calendars hold the candidate's name and address, and the rows that say where they are
        // go with the candidate. So their places are read now, under the interviewers' locks (a calendar write in flight
        // finishes and saves its event ids first), and a message deletes them quietly once this commits.
        var interviewers = await db.InterviewAttendees.AsNoTracking().Where(a => applicationIds.Contains(a.ApplicationId)).Select(a => a.UserId).ToListAsync(cancellationToken);
        await InterviewLocks.TakeAsync(db, organizationId, interviewers, cancellationToken);
        var interviews = await db.InterviewRequests.AsNoTracking().Where(r => applicationIds.Contains(r.ApplicationId)).ToDictionaryAsync(r => r.Id, cancellationToken);
        var eventsToRemove = (await db.InterviewAttendees.AsNoTracking().Where(a => applicationIds.Contains(a.ApplicationId)).ToListAsync(cancellationToken))
            .Where(a => interviews[a.InterviewId] is { } i
                && (i.Status == InterviewStatus.Booked
                    || (i.Status == InterviewStatus.Cancelled && a.ProviderEventId is not null && a.SyncStatus != InterviewSyncStatus.Synced)))
            .Select(a => new InterviewEventToRemove(
                a.InterviewId, a.UserId, a.ProviderEventId, interviews[a.InterviewId].StartsAt, interviews[a.InterviewId].StartsAt + interviews[a.InterviewId].Duration))
            .ToList();

        // Who shared a detail with these applications, read before the rows go. A person has a handful of applications, so
        // this asks the directory once for each, the same question the duplicate check asks.
        var rechecks = new List<RecheckApplicationDuplicates>();
        foreach (var application in applications)
        {
            var context = new VerificationContext(
                application, candidate, jobs[application.JobId], observations.GetValueOrDefault(application.Id),
                [.. files.Where(f => f.ApplicationId == application.Id)], now);
            var related = (await directory.FindSharedAsync(context, cancellationToken)).SelectMany(m => m.ApplicationIds).Distinct();
            rechecks.AddRange(related.Select(id => new RecheckApplicationDuplicates(organizationId, id, application.Id, CauseErased: true)));
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
            ActorUserId = tenant.UserId,
            OccurredAt = now,
        });

        if (files.Count > 0 || replyAttachments.Count > 0)
        {
            await outbox.PublishAsync(new EraseApplicantFiles(organizationId, [.. files.Select(f => f.BlobName)], replyAttachments));
        }

        foreach (var recheck in rechecks.DistinctBy(r => (r.ApplicationId, r.CauseApplicationId)))
        {
            await outbox.PublishAsync(recheck);
        }

        if (eventsToRemove.Count > 0)
        {
            await outbox.PublishAsync(new RemoveInterviewEvents(organizationId, candidateId, eventsToRemove));
        }

        await outbox.PublishAsync(new CandidateErased(organizationId, candidateId, now));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
