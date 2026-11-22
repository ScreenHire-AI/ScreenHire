using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Distribution;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Postings the team makes themselves, on boards we do not integrate with (docs/architecture/job-distribution.md, "Posted by
/// you"), and how many applications came through each link of a job. The listing address a person gives is only stored and
/// shown as a link: nothing here fetches or reads it (CLAUDE.md non-negotiable 5). Every change raises
/// <see cref="JobPostingsChanged"/>, so open dashboards refetch the job's postings as they do for a board's answer.
/// </summary>
internal static class ManualPostingEndpoints
{
    public static IEndpointRouteBuilder MapManualPostingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/jobs/{jobId:guid}").WithTags("Distribution").RequireAuthorization();

        group.MapGet("/manual-postings", ListManualPostings).WithName("ListManualPostings").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/source-applications", ListSourceApplications).WithName("ListSourceApplications")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/manual-postings", CreateManualPosting).WithName("CreateManualPosting")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditJobs)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/manual-postings/{postingId:guid}", UpdateManualPosting).WithName("UpdateManualPosting")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditJobs)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/manual-postings/{postingId:guid}/updated", MarkManualPostingUpdated).WithName("MarkManualPostingUpdated")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditJobs)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/manual-postings/{postingId:guid}/taken-down", TakeDownManualPosting).WithName("TakeDownManualPosting")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditJobs)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<ManualPostingList>, NotFound>> ListManualPostings(
        Guid jobId, ScreenHireDbContext db, IOptions<DomainOptions> domains, CancellationToken cancellationToken)
    {
        var target = await LoadJobAsync(db, domains.Value, jobId, cancellationToken);
        if (target is null)
        {
            return TypedResults.NotFound();
        }

        var postings = await db.ManualPostings.AsNoTracking().Where(p => p.JobId == jobId).ToListAsync(cancellationToken);
        return TypedResults.Ok(new ManualPostingList(
            target.JobUrl,
            [.. postings.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).Select(p => p.ToStatus(target))]));
    }

    // One count per link the job's applications came through. The careers page counts the applications with no board tag
    // that came neither through the organization's own site nor from a recruiter; Google for Jobs and the public job-board
    // API send candidates to that same untagged page, so theirs are among them.
    private static async Task<Results<Ok<List<SourceApplications>>, NotFound>> ListSourceApplications(
        Guid jobId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Jobs.AnyAsync(j => j.Id == jobId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var rows = await db.Applications.AsNoTracking()
            .Where(a => a.JobId == jobId && a.Source == ApplicationSource.CareersSite && (a.SourceBoard != null || a.SourceSite == null))
            .GroupBy(a => a.SourceBoard)
            .Select(g => new { Board = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(rows
            .Select(r => new SourceApplications(r.Board ?? Boards.CareersPage, r.Count))
            .OrderBy(r => r.Source, StringComparer.Ordinal)
            .ToList());
    }

    private static async Task<Results<Created<ManualPostingStatus>, NotFound, ValidationProblem, ProblemHttpResult>> CreateManualPosting(
        Guid jobId,
        CreateManualPostingRequest request,
        IValidator<CreateManualPostingRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        IOptions<DomainOptions> domains,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var db = outbox.DbContext;
        var target = await LoadJobAsync(db, domains.Value, jobId, cancellationToken);
        if (target is null)
        {
            return TypedResults.NotFound();
        }

        // A tracked link leads to the job's careers page, which takes applications only while the job is open.
        if (target.Job.Status != JobStatus.Open)
        {
            return TypedResults.Problem(
                "Publish the job before you post it yourself: its careers page takes applications only while it is open.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var sourceKey = request.SourceKey ?? ManualPosting.SourceKeyFor(request.BoardName);
        if (Boards.All.Contains(sourceKey))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["boardName"] = ["ScreenHire posts to this board itself. Pick it in the list above, or name the board another way."],
            });
        }

        var now = time.GetUtcNow();
        var postedOn = request.PostedOn ?? DateOnly.FromDateTime(now.UtcDateTime);
        if (request.EndsOn < postedOn)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["endsOn"] = [ManualPostingRules.EndsOnMessage] });
        }

        var content = target.Job.Snapshot();
        var existing = await db.ManualPostings.SingleOrDefaultAsync(p => p.JobId == jobId && p.SourceKey == sourceKey, cancellationToken);
        ManualPosting posting;
        if (existing is null)
        {
            posting = ManualPosting.Create(
                target.Job.OrganizationId, jobId, request.BoardName, sourceKey, request.ListingUrl, postedOn, request.EndsOn, content, now);
            db.ManualPostings.Add(posting);
        }
        else if (existing.IsTakenDown)
        {
            // The board again, after it was taken down: one row per link, so the old link and the new one count together.
            existing.PostAgain(request.BoardName, request.ListingUrl, postedOn, request.EndsOn, content, now);
            posting = existing;
        }
        else
        {
            return TypedResults.Problem(
                $"This job is already posted on {existing.BoardName} with that link. Edit that posting in the list instead.",
                statusCode: StatusCodes.Status409Conflict);
        }

        await outbox.PublishAsync(new JobPostingsChanged(target.Job.OrganizationId, jobId, now));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // Two saves of the same board at once: the unique index kept one.
            return TypedResults.Problem(
                "This job was just posted on that board with the same link. Edit that posting in the list instead.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Created($"/v1/jobs/{jobId}/manual-postings/{posting.Id}", posting.ToStatus(target));
    }

    // Edit, Mark as updated and Mark as taken down work on a closed job too: tidying up the boards after a close is what
    // "Still posted by you" asks for.
    private static async Task<Results<Ok<ManualPostingStatus>, NotFound, ValidationProblem>> UpdateManualPosting(
        Guid jobId,
        Guid postingId,
        UpdateManualPostingRequest request,
        IValidator<UpdateManualPostingRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        IOptions<DomainOptions> domains,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        return await ChangeAsync(outbox, domains.Value, time, jobId, postingId, (posting, _, now) =>
        {
            posting.Edit(request.BoardName, request.ListingUrl, request.PostedOn, request.EndsOn, now);
            return true;
        }, cancellationToken) is { } status ? TypedResults.Ok(status) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<ManualPostingStatus>, NotFound>> MarkManualPostingUpdated(
        Guid jobId, Guid postingId, IDbContextOutbox<ScreenHireDbContext> outbox, IOptions<DomainOptions> domains, TimeProvider time,
        CancellationToken cancellationToken) =>
        await ChangeAsync(outbox, domains.Value, time, jobId, postingId, (posting, job, now) =>
        {
            posting.MarkUpdated(job.Snapshot(), now);
            return true;
        }, cancellationToken) is { } status ? TypedResults.Ok(status) : TypedResults.NotFound();

    private static async Task<Results<Ok<ManualPostingStatus>, NotFound>> TakeDownManualPosting(
        Guid jobId, Guid postingId, IDbContextOutbox<ScreenHireDbContext> outbox, IOptions<DomainOptions> domains, TimeProvider time,
        CancellationToken cancellationToken) =>
        await ChangeAsync(outbox, domains.Value, time, jobId, postingId, (posting, _, now) => posting.TakeDown(now), cancellationToken)
            is { } status ? TypedResults.Ok(status) : TypedResults.NotFound();

    // Loads the posting and its job, applies the change, and saves it with the event when it changed anything. Null when
    // the job or the posting is not the reader's to see.
    private static async Task<ManualPostingStatus?> ChangeAsync(
        IDbContextOutbox<ScreenHireDbContext> outbox,
        DomainOptions domains,
        TimeProvider time,
        Guid jobId,
        Guid postingId,
        Func<ManualPosting, Job, DateTimeOffset, bool> change,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var target = await LoadJobAsync(db, domains, jobId, cancellationToken);
        var posting = target is null
            ? null
            : await db.ManualPostings.SingleOrDefaultAsync(p => p.Id == postingId && p.JobId == jobId, cancellationToken);
        if (target is null || posting is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (change(posting, target.Job, now))
        {
            await outbox.PublishAsync(new JobPostingsChanged(target.Job.OrganizationId, jobId, now));
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }

        return posting.ToStatus(target);
    }

    private static async Task<JobTarget?> LoadJobAsync(ScreenHireDbContext db, DomainOptions domains, Guid jobId, CancellationToken cancellationToken)
    {
        var row = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new { Job = j, OrganizationSlug = db.Organizations.Where(o => o.Id == j.OrganizationId).Select(o => o.Slug).First() })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new JobTarget(row.Job, domains.CareersJobUrl(row.OrganizationSlug, row.Job.Slug));
    }

    private static ManualPostingStatus ToStatus(this ManualPosting posting, JobTarget target) =>
        new(posting.Id, posting.BoardName, posting.SourceKey, BoardUrls.WithSource(target.JobUrl, posting.SourceKey), posting.ListingUrl,
            posting.PostedOn, posting.EndsOn, posting.ContentAt, posting.TakenDownAt, posting.ChangedSince(target.Job.Snapshot()),
            posting.CreatedAt, posting.UpdatedAt);

    private sealed record JobTarget(Job Job, string JobUrl);
}
