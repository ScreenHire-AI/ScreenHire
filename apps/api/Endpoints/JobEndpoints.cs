using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Content;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Distribution;
using ScreenHire.Application.Jobs;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Recruiter-dashboard endpoints for jobs. They need a signed-in user, and the tenant query filter (backed by row-level
/// security) scopes every query to that user's organization, so no handler filters by organization itself. Another
/// organization's job is a 404, exactly like a job that does not exist.
/// </summary>
internal static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/jobs").WithTags("Jobs").AllowApiKeys();

        // 401 comes from the authentication middleware, so the handlers' signatures cannot declare it.
        group.MapGet("/", ListJobs).WithName("ListJobs").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/", CreateJob).WithName("CreateJob")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{jobId:guid}", GetJob).WithName("GetJob").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{jobId:guid}/history", GetJobHistory).WithName("GetJobHistory").Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/{jobId:guid}", UpdateJob).WithName("UpdateJob")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{jobId:guid}/publish", PublishJob).WithName("PublishJob")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{jobId:guid}/close", CloseJob).WithName("CloseJob")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{jobId:guid}/versions", ListJobVersions).WithName("ListJobVersions").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{jobId:guid}/versions/{version:int}", GetJobVersion).WithName("GetJobVersion").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{jobId:guid}/workflow/template-diff", GetJobTemplateDiff).WithName("GetJobTemplateDiff")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/{jobId:guid}/workflow", UpdateJobWorkflow).WithName("UpdateJobWorkflow")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{jobId:guid}/workflow/apply-template-update", ApplyTemplateUpdate).WithName("ApplyJobTemplateUpdate")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<List<JobSummary>> ListJobs(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Jobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new
            {
                Job = j,
                LatestTemplateVersion = db.HiringTemplates.Where(t => t.Id == j.SourceTemplateId).Select(t => t.LatestPublishedVersion).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        var changes = await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.Job, [.. rows.Select(r => r.Job.Id.ToString("D"))], cancellationToken);

        return [.. rows.Select(r => r.Job.ToSummary(changes.GetValueOrDefault(r.Job.Id.ToString("D")), r.LatestTemplateVersion))];
    }

    // Every change to the job, newest first: its posting, its status and its workflow.
    private static async Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetJobHistory(
        Guid jobId, Guid? before, int? limit, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (ChangeHistoryRoutes.LimitProblem(limit) is { } problem)
        {
            return problem;
        }

        if (!await db.Jobs.AnyAsync(j => j.Id == jobId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return await ChangeHistoryRoutes.PageAsync(db, ChangeSubjectKind.Job, jobId.ToString("D"), before, limit, cancellationToken) is { } page
            ? TypedResults.Ok(page)
            : ChangeHistoryRoutes.CursorProblem();
    }

    private static async Task<Results<Ok<JobDetail>, NotFound>> GetJob(
        Guid jobId, ScreenHireDbContext db, BoardRegistry boards, CancellationToken cancellationToken)
    {
        var detail = await LoadDetailAsync(db, boards, jobId, cancellationToken);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    // A draft: no event until it is published. With a template version, the job copies that version's workflow, and
    // the version's posting defaults fill whatever the request leaves out.
    private static async Task<Results<Created<JobDetail>, ValidationProblem, ProblemHttpResult>> CreateJob(
        CreateJobRequest request,
        ScreenHireDbContext db,
        ITenantContext tenant,
        JobContentValidator validator,
        BoardRegistry boards,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (TooLongToSanitize(request.Description) is { } tooLong)
        {
            return tooLong;
        }

        HiringTemplateVersion? template = null;
        if (request.TemplateVersionId is { } versionId)
        {
            // Through the tenant filter: an own or a system template. Another organization's version is not found.
            template = await db.HiringTemplateVersions.AsNoTracking()
                .SingleOrDefaultAsync(v => v.Id == versionId && v.Status == TemplateVersionStatus.Published, cancellationToken);
            if (template is null)
            {
                return TypedResults.ValidationProblem(
                    ValidationExtensions.Problem("templateVersionId", "No published template version has this id."));
            }
        }

        var defaults = template?.Definition.PostingDefaults ?? PostingDefaults.None;
        var title = request.Title?.Trim() ?? "";
        var slug = request.Slug?.Trim() ?? await FreeSlugAsync(db, Slugs.FromTitle(title), cancellationToken);
        var content = new JobSnapshot(
            title,
            slug,
            RichText.Sanitize(request.Description ?? defaults.Description),
            Text.NullIfBlank(request.Department ?? defaults.Department),
            request.EmploymentType ?? defaults.EmploymentType ?? EmploymentType.FullTime,
            request.WorkplaceType ?? defaults.WorkplaceType ?? WorkplaceType.Onsite,
            [.. request.Locations ?? []],
            [.. request.ApplicantLocationRequirements ?? []],
            ToSalary(request.Salary, defaults),
            request.ValidThrough);

        if (await validator.ProblemsAsync(content, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (request.Slug is not null && await db.Jobs.AnyAsync(j => j.Slug == content.Slug, cancellationToken))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("slug", "Another job already uses this slug."));
        }

        var now = time.GetUtcNow();
        var job = new Job { OrganizationId = tenant.OrganizationId!.Value, Title = content.Title, Slug = content.Slug, CreatedAt = now };
        job.Revise(content, now);
        if (template is not null)
        {
            job.UseTemplate(template);
        }

        db.Jobs.Add(job);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return TypedResults.Problem("Another job took this slug at the same moment; try again.", statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Created($"/v1/jobs/{job.Id}", (await LoadDetailAsync(db, boards, job.Id, cancellationToken))!);
    }

    // Replaces the posting content. Editing an open job does not republish it: the change is live on the careers page,
    // and boards get it when someone publishes again, which freezes a new version (hasUnpublishedChanges until then).
    private static async Task<Results<Ok<JobDetail>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateJob(
        Guid jobId,
        UpdateJobRequest request,
        ScreenHireDbContext db,
        JobContentValidator validator,
        BoardRegistry boards,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (job.Status == JobStatus.Closed)
        {
            return TypedResults.Problem("A closed job cannot be changed; create a new job instead.", statusCode: StatusCodes.Status409Conflict);
        }

        if (TooLongToSanitize(request.Description) is { } tooLong)
        {
            return tooLong;
        }

        var content = new JobSnapshot(
            request.Title?.Trim() ?? "",
            request.Slug?.Trim() ?? job.Slug,
            RichText.Sanitize(request.Description),
            Text.NullIfBlank(request.Department),
            request.EmploymentType,
            request.WorkplaceType,
            [.. request.Locations ?? []],
            [.. request.ApplicantLocationRequirements ?? []],
            ToSalary(request.Salary, PostingDefaults.None),
            request.ValidThrough);

        if (await validator.ProblemsAsync(content, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (content.Slug != job.Slug)
        {
            if (job.PublishedAt is not null)
            {
                return TypedResults.Problem("A published job keeps its slug, because its URL is already live.", statusCode: StatusCodes.Status409Conflict);
            }

            if (await db.Jobs.AnyAsync(j => j.Slug == content.Slug && j.Id != job.Id, cancellationToken))
            {
                return TypedResults.ValidationProblem(ValidationExtensions.Problem("slug", "Another job already uses this slug."));
            }
        }

        job.Revise(content, time.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ChangedMeanwhile();
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return TypedResults.Problem("Another job took this slug at the same moment; try again.", statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok((await LoadDetailAsync(db, boards, job.Id, cancellationToken))!);
    }

    // Opens the job, or republishes changed content, as a new JobVersion, and posts that version to boards in the same
    // transaction (one-click publish, docs/architecture/job-distribution.md): the version, the job change, the postings,
    // JobPublished and each push board's command commit together through the EF Core outbox, and the response shows
    // every board's state and estimate. Publishing an open job whose content equals its newest version freezes nothing
    // and raises no JobPublished; it still posts to boards named in the request, or to every board if the job is on
    // none. A publish that races another publish, an edit or a close gets 409 (the job's row version, or a unique
    // index), so a closed job stays closed. A board answer the worker is saving is not a race: the publish waits for it.
    private static async Task<Results<Ok<JobDetail>, NotFound, ValidationProblem, ProblemHttpResult>> PublishJob(
        Guid jobId,
        PublishJobRequest? request,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        JobPublishValidator validator,
        DistributionPlanner planner,
        BoardRegistry boards,
        IOptions<DomainOptions> domains,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        switch ((await PublishAsync(outbox, validator, planner, boards, domains.Value, job, request?.Boards, time.GetUtcNow(), cancellationToken))?.Result)
        {
            case ValidationProblem invalid:
                return invalid;
            case ProblemHttpResult problem:
                return problem;
        }

        return TypedResults.Ok((await LoadDetailAsync(db, boards, job.Id, cancellationToken))!);
    }

    /// <summary>
    /// The publish itself, shared by the endpoint and the local demo (<c>DevelopmentDemo</c>): opens or republishes the
    /// tracked <paramref name="job"/>, posts it to <paramref name="requestedBoards"/> (or to every board when it is on
    /// none) and commits through the outbox. Null when it committed or had nothing to change; otherwise the refusal.
    /// </summary>
    internal static async Task<Results<ValidationProblem, ProblemHttpResult>?> PublishAsync(
        IDbContextOutbox<ScreenHireDbContext> outbox,
        JobPublishValidator validator,
        DistributionPlanner planner,
        BoardRegistry boards,
        DomainOptions domains,
        Job job,
        IReadOnlyList<string>? requestedBoards,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        if (job.Status == JobStatus.Closed)
        {
            return TypedResults.Problem("A closed job cannot be republished; create a new job instead.", statusCode: StatusCodes.Status409Conflict);
        }

        // Waits out a board answer the worker is saving, so the postings below are its latest state, never a 409.
        await using var transaction = await PostingLocks.BeginForJobAsync(db, job.Id, boardId: null, cancellationToken);

        var organization = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == job.OrganizationId, cancellationToken);
        if (requestedBoards is { } requested)
        {
            var available = boards.AvailableTo(organization).Select(a => a.BoardId).ToHashSet(StringComparer.Ordinal);
            if (requested.FirstOrDefault(b => !available.Contains(b)) is { } unknown)
            {
                return TypedResults.ValidationProblem(
                    ValidationExtensions.Problem("boards", $"'{unknown}' is not a board this organization can post to."));
            }
        }

        if (job is { ValidThrough: null, PublishedAt: null, SourceTemplateVersionId: { } sourceVersionId })
        {
            // The template's posting duration counts from the first publish.
            var validForDays = await db.HiringTemplateVersions.AsNoTracking()
                .Where(v => v.Id == sourceVersionId)
                .Select(v => v.Definition)
                .SingleOrDefaultAsync(cancellationToken);
            if (validForDays?.PostingDefaults.ValidForDays is { } days)
            {
                job.ValidThrough = now.AddDays(days);
            }
        }

        if (job.ValidThrough <= now)
        {
            return TypedResults.Problem("The posting's validThrough date has passed; move it forward before publishing.", statusCode: StatusCodes.Status409Conflict);
        }

        if (await validator.ProblemsAsync(job.Snapshot(), cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var latest = await db.JobVersions.AsNoTracking()
            .Where(v => v.JobId == job.Id)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(cancellationToken);
        var version = job.Publish(now, latest);
        if (version is not null)
        {
            db.JobVersions.Add(version);
            await outbox.PublishAsync(new JobPublished(job.OrganizationId, job.Id, version.PublishedAt, version.Version, version.Id));
        }

        // Publish returns no version only for an open job whose newest version matches, so one of the two exists.
        var current = version ?? latest!;
        var postings = await db.JobPostings.Where(p => p.JobId == job.Id).ToListAsync(cancellationToken);
        var content = new PostingContent(
            job.Id, current.Version, current.Snapshot, job.PublishedAt!.Value, current.PublishedAt,
            domains.CareersJobUrl(organization.Slug, job.Slug), organization.Name, organization.Slug);
        var changes = await planner.PublishAsync(organization, content, postings, requestedBoards, cancellationToken);
        db.JobPostings.AddRange(changes.Added);
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
            catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || exception.IsUniqueViolation())
            {
                return ChangedMeanwhile();
            }
        }

        return null;
    }

    // Closes the job and tears it down everywhere in the same transaction (one-click teardown): the careers page serves
    // 410 Gone at once, feeds and the job-board API drop it at once, Google's URL_DELETED is queued, and each push board
    // is asked to remove it. Each posting reports its own state and estimate; only what is confirmed is Closed. Closing a
    // closed job commits nothing and raises no second event.
    private static async Task<Results<Ok<JobDetail>, NotFound, ProblemHttpResult>> CloseJob(
        Guid jobId,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        DistributionPlanner planner,
        BoardRegistry boards,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (await CloseAsync(outbox, planner, job, time.GetUtcNow(), cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return TypedResults.Ok((await LoadDetailAsync(db, boards, job.Id, cancellationToken))!);
    }

    /// <summary>
    /// The close itself, shared by the endpoint and the local demo: closes the tracked <paramref name="job"/>, tears it
    /// down on every board and commits through the outbox. Null when it committed or had nothing to change.
    /// </summary>
    internal static async Task<ProblemHttpResult?> CloseAsync(
        IDbContextOutbox<ScreenHireDbContext> outbox, DistributionPlanner planner, Job job, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        await using var transaction = await PostingLocks.BeginForJobAsync(db, job.Id, boardId: null, cancellationToken);
        if (job.Close(now))
        {
            await outbox.PublishAsync(new JobClosed(job.OrganizationId, job.Id, job.ClosedAt!.Value));
        }

        var changes = await TearDownAsync(db, planner, job.Id, boardId: null, cancellationToken);
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
                return ChangedMeanwhile();
            }
        }

        return null;
    }

    /// <summary>
    /// Takes the job down from one board, or from every board, through the planner; the caller publishes the messages
    /// and saves. Adapters that need content get the newest version's.
    /// </summary>
    internal static async Task<DistributionChanges> TearDownAsync(
        ScreenHireDbContext db, DistributionPlanner planner, Guid jobId, string? boardId, CancellationToken cancellationToken)
    {
        var postings = await db.JobPostings
            .Where(p => p.JobId == jobId && (boardId == null || p.BoardId == boardId))
            .ToListAsync(cancellationToken);
        if (postings.Count == 0)
        {
            return new DistributionChanges([], []);
        }

        var content = await PostingContents.LoadAsync(db, postings.MaxBy(p => p.JobVersion)!, cancellationToken);
        return await planner.CloseAsync(content, postings, boardId, cancellationToken);
    }

    private static async Task<Results<Ok<List<JobVersionSummary>>, NotFound>> ListJobVersions(
        Guid jobId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Jobs.AnyAsync(j => j.Id == jobId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var versions = await db.JobVersions.AsNoTracking()
            .Where(v => v.JobId == jobId)
            .OrderBy(v => v.Version)
            .Select(v => new JobVersionSummary(v.Id, v.Version, v.PublishedAt))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(versions);
    }

    private static async Task<Results<Ok<JobVersionDetail>, NotFound>> GetJobVersion(
        Guid jobId, int version, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var found = await db.JobVersions.AsNoTracking()
            .SingleOrDefaultAsync(v => v.JobId == jobId && v.Version == version, cancellationToken);
        return found is null ? TypedResults.NotFound() : TypedResults.Ok(found.ToDetail());
    }

    // 404 for a job that was not created from a template: there is nothing to compare with.
    private static async Task<Results<Ok<JobTemplateDiff>, NotFound>> GetJobTemplateDiff(
        Guid jobId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is not { SourceTemplateId: { } templateId, SourceTemplateVersionId: { } sourceVersionId, SourceTemplateVersion: { } sourceVersion })
        {
            return TypedResults.NotFound();
        }

        var latest = await db.HiringTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == templateId && v.Status == TemplateVersionStatus.Published)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new JobTemplateDiff(
            templateId, sourceVersionId, sourceVersion, latest.Id, latest.Version, latest.Version > sourceVersion,
            WorkflowDiff.Compare(job.Workflow, latest.Definition.Workflow)));
    }

    // Tailors the job's own copy of its workflow (workflow-engine.md §1). Validated like a template, guardrails included,
    // so a job can never hold a definition a template could not. It does not republish the job: the careers form reads
    // the workflow live, and boards never see it.
    private static async Task<Results<Ok<JobDetail>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateJobWorkflow(
        Guid jobId,
        UpdateJobWorkflowRequest request,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        BoardRegistry boards,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (WorkflowConflict(job, request.ExpectedWorkflowVersion) is { } conflict)
        {
            return conflict;
        }

        if ((await EmailTemplateReferences.ValidateAsync(db, request.Workflow, cancellationToken)).ToProblems("workflow") is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (WorkflowDiff.Compare(job.Workflow, request.Workflow).Count == 0)
        {
            return TypedResults.Ok((await LoadDetailAsync(db, boards, job.Id, cancellationToken))!);
        }

        if (await OccupiedStagesProblemAsync(db, job, request.Workflow, cancellationToken) is { } occupied)
        {
            return occupied;
        }

        job.ReviseWorkflow(request.Workflow, time.GetUtcNow());
        return await SaveWorkflowAsync(outbox, boards, job, cancellationToken);
    }

    // "Apply update": the job takes a published version of its source template, replacing its own edits as the diff
    // showed. The version is validated again, because the rules may have tightened since it was published.
    private static async Task<Results<Ok<JobDetail>, NotFound, ValidationProblem, ProblemHttpResult>> ApplyTemplateUpdate(
        Guid jobId,
        ApplyTemplateUpdateRequest request,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        BoardRegistry boards,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (WorkflowConflict(job, request.ExpectedWorkflowVersion) is { } conflict)
        {
            return conflict;
        }

        var version = await db.HiringTemplateVersions.AsNoTracking()
            .SingleOrDefaultAsync(v => v.Id == request.TemplateVersionId && v.Status == TemplateVersionStatus.Published, cancellationToken);
        if (version is null || version.TemplateId != job.SourceTemplateId)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "templateVersionId", "No published version of this job's template has this id."));
        }

        if ((await EmailTemplateReferences.ValidateAsync(db, version.Definition.Workflow, cancellationToken)).ToProblems("workflow") is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (await OccupiedStagesProblemAsync(db, job, version.Definition.Workflow, cancellationToken) is { } occupied)
        {
            return occupied;
        }

        job.ApplyTemplateUpdate(version, time.GetUtcNow());
        return await SaveWorkflowAsync(outbox, boards, job, cancellationToken);
    }

    private static ProblemHttpResult? WorkflowConflict(Job job, int expectedWorkflowVersion) =>
        job.Status == JobStatus.Closed
            ? TypedResults.Problem("A closed job cannot be changed; create a new job instead.", statusCode: StatusCodes.Status409Conflict)
            : job.WorkflowVersion != expectedWorkflowVersion
                ? TypedResults.Problem(
                    $"Someone else saved this job's workflow (now version {job.WorkflowVersion}); reload it and make your change again.",
                    statusCode: StatusCodes.Status409Conflict)
                : null;

    // An application must always be in a stage of its job's workflow, so a stage that holds any cannot be dropped. There
    // is no stage mapping yet: the recruiter moves those candidates first.
    private static async Task<ValidationProblem?> OccupiedStagesProblemAsync(
        ScreenHireDbContext db, Job job, HiringWorkflow workflow, CancellationToken cancellationToken)
    {
        var kept = workflow.Stages.Select(s => s.Id).ToList();
        var stranded = await db.Applications.AsNoTracking()
            .Where(a => a.JobId == job.Id && !kept.Contains(a.CurrentStageId))
            .GroupBy(a => a.CurrentStageId)
            .Select(g => new { StageId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        if (stranded.Count == 0)
        {
            return null;
        }

        var messages = stranded
            .OrderBy(s => s.StageId, StringComparer.Ordinal)
            .Select(s => $"{job.Workflow.FindStage(s.StageId)?.Name ?? s.StageId} still has {s.Count} {(s.Count == 1 ? "candidate" : "candidates")}. " +
                "Move them to another stage before removing it.")
            .ToArray();
        return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["workflow.stages"] = messages });
    }

    // The change and JobWorkflowChanged commit together, so open dashboards refetch the job's stages.
    private static async Task<Results<Ok<JobDetail>, NotFound, ValidationProblem, ProblemHttpResult>> SaveWorkflowAsync(
        IDbContextOutbox<ScreenHireDbContext> outbox, BoardRegistry boards, Job job, CancellationToken cancellationToken)
    {
        await outbox.PublishAsync(new JobWorkflowChanged(job.OrganizationId, job.Id, job.WorkflowVersion, job.UpdatedAt!.Value));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ChangedMeanwhile();
        }

        return TypedResults.Ok((await LoadDetailAsync(outbox.DbContext, boards, job.Id, cancellationToken))!);
    }

    /// <summary>
    /// The job with its newest version's content, its source template's name and newest version, and its postings, in
    /// one query.
    /// </summary>
    internal static async Task<JobDetail?> LoadDetailAsync(ScreenHireDbContext db, BoardRegistry boards, Guid jobId, CancellationToken cancellationToken)
    {
        var subjectId = jobId.ToString("D");
        var row = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new
            {
                Job = j,
                Latest = db.JobVersions.Where(v => v.JobId == j.Id).OrderByDescending(v => v.Version).Select(v => v.Snapshot).FirstOrDefault(),
                Template = db.HiringTemplates.Where(t => t.Id == j.SourceTemplateId).Select(t => new { t.Name, t.LatestPublishedVersion }).FirstOrDefault(),
                Postings = db.JobPostings.Where(p => p.JobId == j.Id).ToList(),
                LastChange = db.ChangeRecords
                    .Where(r => r.SubjectKind == ChangeSubjectKind.Job && r.SubjectId == subjectId)
                    .OrderByDescending(r => r.Sequence)
                    .Select(r => new ChangeStamp(r.OccurredAt, r.ActorUserId, r.ActorApiKeyId))
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row?.Job.ToDetail(
            row.Latest, row.Template?.Name, row.Template?.LatestPublishedVersion, row.Postings.ToStatuses(boards), row.LastChange);
    }

    private static async Task<string> FreeSlugAsync(ScreenHireDbContext db, string slug, CancellationToken cancellationToken)
    {
        var prefix = slug + "-";
        var taken = await db.Jobs.AsNoTracking()
            .Where(j => j.Slug == slug || j.Slug.StartsWith(prefix))
            .Select(j => j.Slug)
            .ToListAsync(cancellationToken);
        return Slugs.FirstFree(slug, taken.ToHashSet(StringComparer.Ordinal));
    }

    private static JobSalary? ToSalary(SalaryRequest? salary, PostingDefaults defaults) =>
        salary is null
            ? null
            : new JobSalary(salary.Min, salary.Max, salary.Currency ?? defaults.SalaryCurrency ?? "", salary.Period ?? defaults.SalaryPeriod ?? SalaryPeriod.Year);

    // Checked before sanitizing, so an oversized body is refused without parsing it. The validator checks the result.
    private static ValidationProblem? TooLongToSanitize(string? description) =>
        description?.Length > JobContentValidator.MaxDescriptionLength
            ? TypedResults.ValidationProblem(
                ValidationExtensions.Problem("description", $"At most {JobContentValidator.MaxDescriptionLength} characters."))
            : null;

    internal static ProblemHttpResult ChangedMeanwhile() =>
        TypedResults.Problem("Someone else changed this job at the same moment; refresh and try again.", statusCode: StatusCodes.Status409Conflict);
}
