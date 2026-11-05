using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Candidates;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Organizations;
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
        CandidateErasure erasure,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        if (!tenant.SeesAllJobs || tenant.OrganizationId is null)
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

        // The one erasure shared with the data requests' Erase action and record retention. It deletes through `db` (this
        // transaction) and hands back the messages, which go out in the same save.
        var messages = await erasure.EraseCandidateAsync(db, candidateId, tenant.UserId, dataRequestId: null, time.GetUtcNow(), cancellationToken);
        if (messages is null)
        {
            return TypedResults.NotFound();
        }

        // Each message goes out by its own type, so the files, the calendars and the dashboards each hear their part.
        foreach (var message in messages)
        {
            await outbox.PublishAsync(message);
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
