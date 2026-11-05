using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Candidates;
using ScreenHire.Application.Privacy;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Privacy;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The team's side of data requests (docs/architecture/data-requests.md): the list, and the three ways to answer one: send
/// the candidate a copy, erase them, or decline with a reason. People only: an organization API key does not reach these
/// routes, and nothing here is in the MCP server. Every route needs the Erase candidates permission and a person who sees
/// every job, because a copy and an erasure reach every application a candidate has; anyone else gets a 404 and learns
/// nothing.
/// </summary>
internal static class DataRequestEndpoints
{
    /// <summary>How long an answered request stays in the list.</summary>
    private static readonly TimeSpan AnsweredStaysFor = TimeSpan.FromDays(90);

    public static IEndpointRouteBuilder MapDataRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/data-requests").WithTags("Data requests").RequireAuthorization();
        group.MapGet("", ListDataRequests).WithName("ListDataRequests")
            .RequirePermission(Permission.EraseCandidates)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{id:guid}/send-copy", SendDataRequestCopy).WithName("SendDataRequestCopy")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EraseCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{id:guid}/erase", EraseDataRequest).WithName("EraseDataRequest")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EraseCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{id:guid}/decline", DeclineDataRequest).WithName("DeclineDataRequest")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EraseCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // Earliest due date first. Every open request, and the ones answered in the last 90 days, so the list does not grow for
    // ever. The candidate is joined while they exist; the request keeps no name or address of its own.
    private static async Task<Results<Ok<DataRequestList>, NotFound>> ListDataRequests(
        ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!tenant.SeesAllJobs)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        var since = now - AnsweredStaysFor;
        var requests = await db.DataRequests.AsNoTracking()
            .Where(r => r.Status == DataRequestStatus.Open || r.ResolvedAt >= since)
            .OrderBy(r => r.DueAt).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
        var candidateIds = requests.Select(r => r.CandidateId).OfType<Guid>().Distinct().ToList();
        var candidates = candidateIds.Count == 0
            ? []
            : await db.Candidates.AsNoTracking().Where(c => candidateIds.Contains(c.Id))
                .Select(c => new DataRequestCandidate(c.Id, c.FullName, c.Email)).ToDictionaryAsync(c => c.Id, cancellationToken);

        var items = requests.Select(r => new DataRequestEntry(
            r.Id, r.Kind, r.Status, r.ReceivedAt, r.DueAt, r.IsOverdue(now), r.ResolvedAt, r.CopySentAt, r.DeclineReason,
            r.CandidateId is { } id ? candidates.GetValueOrDefault(id) : null)).ToList();
        return TypedResults.Ok(new DataRequestList(items, requests.Count(r => r.Status == DataRequestStatus.Open)));
    }

    // Queues the copy. The worker builds the file, emails the candidate the link and completes the request; until then it
    // stays open, so a second click publishes the same message again, which the worker's idempotency key drops.
    private static async Task<Results<Accepted, NotFound, ProblemHttpResult>> SendDataRequestCopy(
        Guid id, IDbContextOutbox<ScreenHireDbContext> outbox, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (!tenant.SeesAllJobs || tenant.OrganizationId is not { } organizationId)
        {
            return TypedResults.NotFound();
        }

        var request = await outbox.DbContext.DataRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return TypedResults.NotFound();
        }

        if (request is not { Kind: DataRequestKind.Copy, Status: DataRequestStatus.Open, CandidateId: not null })
        {
            return NotOpen("Only an open request for a copy can be sent a copy.");
        }

        await outbox.PublishAsync(new BuildDataCopy(organizationId, request.Id));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    // Erases the candidate behind an erasure request through the same service as the erase route, with the request's id on
    // the tombstone. The service completes this request and closes the candidate's other open ones, so the erasure answers
    // them all. The typed name is checked here, as on the erase route.
    private static async Task<Results<NoContent, NotFound, ValidationProblem, ProblemHttpResult>> EraseDataRequest(
        Guid id,
        EraseDataRequestRequest body,
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

        // No CommitAsync: SaveChangesAndFlushMessagesAsync commits this transaction with the outbox rows. The row lock makes
        // an application arriving now wait, as on the erase route.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var request = await db.DataRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return TypedResults.NotFound();
        }

        if (request is not { Kind: DataRequestKind.Erasure, Status: DataRequestStatus.Open, CandidateId: { } candidateId })
        {
            return NotOpen("Only an open request for erasure can be answered by erasing.");
        }

        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM candidates WHERE id = {candidateId} FOR UPDATE", cancellationToken);
        if (await db.Candidates.SingleOrDefaultAsync(c => c.Id == candidateId, cancellationToken) is not { } candidate)
        {
            return TypedResults.NotFound();
        }

        if (!string.Equals(body.Confirmation?.Trim(), candidate.FullName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("confirmation", "Type the candidate's full name to confirm."));
        }

        var messages = await erasure.EraseCandidateAsync(db, candidateId, tenant.UserId, request.Id, time.GetUtcNow(), cancellationToken);
        if (messages is null)
        {
            return TypedResults.NotFound();
        }

        foreach (var message in messages)
        {
            await outbox.PublishAsync(message);
        }

        await outbox.PublishAsync(new DataRequestChanged(request.OrganizationId, request.Id));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // Declines with a reason, which the worker emails to the candidate. The reason stays on the request; the team log
    // records who declined and which request, never the reason.
    private static async Task<Results<NoContent, NotFound, ValidationProblem, ProblemHttpResult>> DeclineDataRequest(
        Guid id,
        DeclineDataRequestRequest body,
        IValidator<DeclineDataRequestRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!tenant.SeesAllJobs || tenant.OrganizationId is not { } organizationId || tenant.UserId is not { } userId)
        {
            return TypedResults.NotFound();
        }

        if (await validator.ProblemsAsync(body, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var db = outbox.DbContext;
        var request = await db.DataRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return TypedResults.NotFound();
        }

        if (request.Status != DataRequestStatus.Open)
        {
            return NotOpen("Only an open request can be declined.");
        }

        var now = time.GetUtcNow();
        request.Decline(body.Reason, userId, now);
        db.MembershipEvents.Add(new MembershipEvent
        {
            OrganizationId = organizationId,
            Kind = MembershipEventKind.DataRequestDeclined,
            ActorUserId = userId,
            DataRequestId = request.Id,
            OccurredAt = now,
        });
        await outbox.PublishAsync(new SendDataRequestDeclined(organizationId, request.Id));
        await outbox.PublishAsync(new DataRequestChanged(organizationId, request.Id));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult NotOpen(string detail) =>
        TypedResults.Problem(title: "The request cannot be answered that way", detail: detail, statusCode: StatusCodes.Status409Conflict);
}
