using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Distribution;
using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Content;
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
            .RequirePermission(Permission.EditJobs)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        // A board's own title and description. Reading is open to any signed-in member, like the postings list; the
        // preview changes nothing but is a POST (it carries a body), so it asks for the same permission as saving.
        group.MapGet("/jobs/{jobId:guid}/postings/{boardId}/customization", GetPostingCustomization).WithName("GetPostingCustomization")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/jobs/{jobId:guid}/postings/{boardId}/customization/preview", PreviewPostingCustomization).WithName("PreviewPostingCustomization")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditJobs)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/jobs/{jobId:guid}/postings/{boardId}/customization", CustomizePosting).WithName("CustomizePosting")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditJobs)
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
        return TypedResults.Ok(postings.ToStatuses(boards, await LatestSnapshotAsync(db, jobId, cancellationToken)));
    }

    private static Task<JobSnapshot?> LatestSnapshotAsync(ScreenHireDbContext db, Guid jobId, CancellationToken cancellationToken) =>
        db.JobVersions.AsNoTracking()
            .Where(v => v.JobId == jobId)
            .OrderByDescending(v => v.Version)
            .Select(v => v.Snapshot)
            .FirstOrDefaultAsync(cancellationToken);

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
        return TypedResults.Ok(saved.ToStatus(boards, await LatestSnapshotAsync(db, jobId, cancellationToken)));
    }

    // What the Customize sheet opens with: the board's own texts next to the job's, and what the job changed since.
    private static async Task<Results<Ok<PostingCustomization>, NotFound, ProblemHttpResult>> GetPostingCustomization(
        Guid jobId, string boardId, ScreenHireDbContext db, BoardRegistry boards, IOptions<DomainOptions> domains, CancellationToken cancellationToken)
    {
        var (target, conflict) = await LoadTargetAsync(db, boards, domains.Value, jobId, boardId, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        if (target is null)
        {
            return TypedResults.NotFound();
        }

        var job = target.Content.Snapshot;
        var posting = await db.JobPostings.AsNoTracking().SingleOrDefaultAsync(p => p.JobId == jobId && p.BoardId == boardId, cancellationToken);
        var changes = new List<PostingTextChange>();
        if (posting?.TitleOverride is not null && posting.TitleBase != job.Title)
        {
            changes.Add(new("Title", posting.TitleBase ?? "", job.Title));
        }

        if (posting?.DescriptionOverride is not null && posting.DescriptionBase != job.Description)
        {
            changes.Add(new("Description", RichTextToPlain.Convert(posting.DescriptionBase ?? "") ?? "", RichTextToPlain.Convert(job.Description) ?? ""));
        }

        return TypedResults.Ok(new PostingCustomization(
            boardId, target.Adapter.Name, target.Content.Version,
            new(posting?.TitleOverride, job.Title), new(posting?.DescriptionOverride, job.Description), changes));
    }

    // What the board would receive for the sheet's draft, and what its checks say, so a failing check shows while the
    // person is still editing. Nothing is saved or sent.
    private static async Task<Results<Ok<PostingPreview>, NotFound, ValidationProblem, ProblemHttpResult>> PreviewPostingCustomization(
        Guid jobId, string boardId, CustomizePostingRequest request, ScreenHireDbContext db, BoardRegistry boards,
        IOptions<DomainOptions> domains, TimeProvider time, CancellationToken cancellationToken)
    {
        var (target, conflict) = await LoadTargetAsync(db, boards, domains.Value, jobId, boardId, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        if (target is null)
        {
            return TypedResults.NotFound();
        }

        var (title, description, problem) = Texts(request, target.Content.Snapshot);
        if (problem is not null)
        {
            return problem;
        }

        var draft = new JobPosting { OrganizationId = target.OrganizationId, JobId = jobId, BoardId = boardId };
        draft.Customize(title, description, target.Content.Snapshot, time.GetUtcNow());
        var effective = PostingContents.For(draft, target.Content);
        return TypedResults.Ok(new PostingPreview(effective.Snapshot.Title, effective.Snapshot.Description, target.Adapter.Validate(effective)));
    }

    // Changes what one board receives and sends it there, and only there. The board's checks run on the customized text
    // first: if any fails, nothing is saved or sent and the answer says what failed, so the sheet keeps the person's
    // edits. No job version is made; the board gets the job's newest version with its own title and description.
    private static async Task<Results<Ok<JobPostingStatus>, NotFound, ValidationProblem, ProblemHttpResult>> CustomizePosting(
        Guid jobId,
        string boardId,
        CustomizePostingRequest request,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        DistributionPlanner planner,
        BoardRegistry boards,
        IOptions<DomainOptions> domains,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var (target, conflict) = await LoadTargetAsync(db, boards, domains.Value, jobId, boardId, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        if (target is null)
        {
            return TypedResults.NotFound();
        }

        // The sheet holds the text of the version it opened on. Against a newer one, a field left as the job's would read as
        // the board's own and pin the old text, and a field that is the board's own would hide the job's change.
        if (request.JobVersion is { } version && version != target.Content.Version)
        {
            return TypedResults.Problem(
                "The job was published again while this was open. Close this and open it again to see what changed.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var job = target.Content.Snapshot;
        var (title, description, problem) = Texts(request, job);
        if (problem is not null)
        {
            return problem;
        }

        // Waits out a board answer the worker is saving, so the posting below is its latest state.
        await using var transaction = await PostingLocks.BeginForJobAsync(db, jobId, boardId, cancellationToken);
        var now = time.GetUtcNow();
        var posting = await db.JobPostings.SingleOrDefaultAsync(p => p.JobId == jobId && p.BoardId == boardId, cancellationToken);
        if (posting?.State is PostingState.Closing or PostingState.Closed)
        {
            // A board the job was taken down from stays down until someone posts it again; saving text must not do that.
            return TypedResults.Problem(
                $"The job was taken down from {target.Adapter.Name}. Post it again first, then customize it.", statusCode: StatusCodes.Status409Conflict);
        }

        var isNew = posting is null;
        posting ??= new JobPosting { OrganizationId = target.OrganizationId, JobId = jobId, BoardId = boardId, CreatedAt = now };
        var changed = posting.Customize(title, description, job, now);
        posting.BoardLabel = target.Adapter.Name;

        var issues = target.Adapter.Validate(PostingContents.For(posting, target.Content));
        if (issues.Count > 0)
        {
            // Not saved: the context is dropped with the request, and the transaction rolls back on dispose.
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["board"] = [.. issues] });
        }

        var changes = await planner.RepostAsync(target.Organization, target.Content, posting, isNew, resend: changed, cancellationToken);
        db.JobPostings.AddRange(changes.Added);
        foreach (var message in changes.Messages)
        {
            await outbox.PublishAsync(message);
        }

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || exception.IsUniqueViolation())
        {
            return JobEndpoints.ChangedMeanwhile();
        }

        return TypedResults.Ok(posting.ToStatus(boards, job));
    }

    private sealed record Target(Organization Organization, IJobBoardAdapter Adapter, PostingContent Content)
    {
        public Guid OrganizationId => Organization.Id;
    }

    // The job's newest published content as a board would be sent it, for a board that takes text of its own. A job or
    // board that does not exist, or that this organization may not use, is a 404 (null target); a board that sends no text
    // of its own, or a job that is not open, is a 409.
    private static async Task<(Target? Target, ProblemHttpResult? Conflict)> LoadTargetAsync(
        ScreenHireDbContext db, BoardRegistry boards, DomainOptions domains, Guid jobId, string boardId, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return (null, null);
        }

        var organization = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == job.OrganizationId, cancellationToken);
        var adapter = boards.AvailableTo(organization).FirstOrDefault(a => a.BoardId == boardId);
        if (adapter is null)
        {
            return (null, null);
        }

        if (!adapter.Capabilities.CanCustomize)
        {
            return (null, TypedResults.Problem(
                $"{adapter.Name} shows the job as it is and cannot take text of its own; change the job's details instead.",
                statusCode: StatusCodes.Status409Conflict));
        }

        var latest = job.Status == JobStatus.Open
            ? await db.JobVersions.AsNoTracking().Where(v => v.JobId == jobId).OrderByDescending(v => v.Version).FirstOrDefaultAsync(cancellationToken)
            : null;
        if (latest is null)
        {
            return (null, TypedResults.Problem("Only a job that is open can be customized for a board.", statusCode: StatusCodes.Status409Conflict));
        }

        var content = new PostingContent(
            job.Id, latest.Version, latest.Snapshot, job.PublishedAt!.Value, latest.PublishedAt,
            domains.CareersJobUrl(organization.Slug, job.Slug), organization.Name, organization.Slug);
        return (new Target(organization, adapter, content), null);
    }

    // The request as the texts to store: a blank field, or one that reads as the job's own, follows the job (null). The
    // description is sanitized as the job's own is, and a draft that sanitizes to the job's keeps the job's exact string.
    private static (string? Title, string? Description, ValidationProblem? Problem) Texts(CustomizePostingRequest request, JobSnapshot job)
    {
        var title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        if (title?.Length > JobContentValidator.MaxTitleLength)
        {
            return (null, null, TypedResults.ValidationProblem(ValidationExtensions.Problem("title", $"At most {JobContentValidator.MaxTitleLength} characters.")));
        }

        if (request.Description?.Length > JobContentValidator.MaxDescriptionLength)
        {
            return (null, null, TypedResults.ValidationProblem(
                ValidationExtensions.Problem("description", $"At most {JobContentValidator.MaxDescriptionLength} characters.")));
        }

        var description = request.Description is null ? null : RichText.Sanitize(request.Description);
        if (description is null || RichText.IsBlank(description))
        {
            description = null;
        }
        else if (description == RichText.Sanitize(job.Description))
        {
            description = job.Description;
        }

        return (title, description, null);
    }
}
