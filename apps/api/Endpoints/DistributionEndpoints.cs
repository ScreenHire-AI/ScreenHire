using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Distribution;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Recruiter-dashboard endpoints for job distribution (docs/architecture/job-distribution.md). One-click publish and
/// teardown are <c>POST /v1/jobs/{jobId}/publish</c> and <c>/close</c> (<see cref="JobEndpoints"/>); these list the
/// boards, show where a job stands on each, and take a job down from one board. The tenant filter scopes every query.
/// </summary>
internal static class DistributionEndpoints
{
    public static IEndpointRouteBuilder MapDistributionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1").WithTags("Distribution").RequireAuthorization();

        group.MapGet("/boards", ListBoards).WithName("ListBoards").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/jobs/{jobId:guid}/postings", ListJobPostings).WithName("ListJobPostings").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/jobs/{jobId:guid}/postings/{boardId}/close", CloseJobPosting).WithName("CloseJobPosting")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    // Hidden boards (not configured) are absent; partner-gated boards appear only for cleared, paying organizations.
    private static async Task<List<BoardSummary>> ListBoards(
        ScreenHireDbContext db, ITenantContext tenant, BoardRegistry boards, CancellationToken cancellationToken)
    {
        // A signed-in user who has not onboarded yet has no organization, and so no boards.
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == tenant.OrganizationId, cancellationToken);
        return organization is null ? [] : [.. boards.AvailableTo(organization).Select(a => a.ToSummary())];
    }

    private static async Task<Results<Ok<List<JobPostingStatus>>, NotFound>> ListJobPostings(
        Guid jobId, ScreenHireDbContext db, BoardRegistry boards, CancellationToken cancellationToken)
    {
        if (!await db.Jobs.AnyAsync(j => j.Id == jobId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var postings = await db.JobPostings.AsNoTracking().Where(p => p.JobId == jobId).ToListAsync(cancellationToken);
        return TypedResults.Ok(postings.ToStatuses(boards));
    }

    // Per-board unpublish while the job stays open. A board that cannot drop one job without closing it (the careers
    // page, which is the job's own page, or Google, which would re-index the live page) is a 409: close the job instead.
    private static async Task<Results<Ok<JobPostingStatus>, NotFound, ProblemHttpResult>> CloseJobPosting(
        Guid jobId,
        string boardId,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        DistributionPlanner planner,
        BoardRegistry boards,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var posting = await db.JobPostings.AsNoTracking().SingleOrDefaultAsync(p => p.JobId == jobId && p.BoardId == boardId, cancellationToken);
        if (posting is null)
        {
            return TypedResults.NotFound();
        }

        if (boards.Find(boardId) is { Capabilities.CanUnpublishPerBoard: false } adapter)
        {
            return TypedResults.Problem(
                $"{adapter.Name} cannot drop one job while it stays open; close the job to take it down everywhere.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // Waits out a board answer the worker is saving, so the take-down plans on the posting's latest state.
        await using var transaction = await PostingLocks.BeginForJobAsync(db, jobId, boardId, cancellationToken);
        var changes = await JobEndpoints.TearDownAsync(db, planner, jobId, boardId, cancellationToken);
        foreach (var message in changes.Messages)
        {
            await outbox.PublishAsync(message);
        }

        if (db.ChangeTracker.HasChanges())
        {
            try
            {
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The backstop. A write that skipped the posting lock changed the posting after it was loaded. A close
                // of the job takes the same lock, so this take-down waited for it and planned on the closed posting.
                return JobEndpoints.ChangedMeanwhile();
            }
        }

        var saved = await db.JobPostings.AsNoTracking().SingleAsync(p => p.Id == posting.Id, cancellationToken);
        return TypedResults.Ok(saved.ToStatus(boards));
    }
}
