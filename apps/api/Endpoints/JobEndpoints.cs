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
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;
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
        var group = app.MapGroup("/v1/jobs").WithTags("Jobs").RequireAuthorization();

        // 401 comes from the authentication middleware, so the handlers' signatures cannot declare it.
        group.MapGet("/", ListJobs).WithName("ListJobs").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/", CreateJob).WithName("CreateJob")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{jobId:guid}", GetJob).WithName("GetJob").Produces(StatusCodes.Status401Unauthorized);
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

        return [.. rows.Select(r => r.Job.ToSummary(r.LatestTemplateVersion))];
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
    // index), so a closed job stays closed.
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

        if (job.Status == JobStatus.Closed)
        {
            return TypedResults.Problem("A closed job cannot be republished; create a new job instead.", statusCode: StatusCodes.Status409Conflict);
        }

        var organization = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == job.OrganizationId, cancellationToken);
        if (request?.Boards is { } requested)
        {
            var available = boards.AvailableTo(organization).Select(a => a.BoardId).ToHashSet(StringComparer.Ordinal);
            if (requested.FirstOrDefault(b => !available.Contains(b)) is { } unknown)
            {
                return TypedResults.ValidationProblem(
                    ValidationExtensions.Problem("boards", $"'{unknown}' is not a board this organization can post to."));
            }
        }

        var now = time.GetUtcNow();
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
            domains.Value.CareersJobUrl(organization.Slug, job.Slug), organization.Name, organization.Slug);
        var changes = await planner.PublishAsync(organization, content, postings, request?.Boards, cancellationToken);
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

        return TypedResults.Ok((await LoadDetailAsync(db, boards, job.Id, cancellationToken))!);
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

        if (job.Close(time.GetUtcNow()))
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

        return TypedResults.Ok((await LoadDetailAsync(db, boards, job.Id, cancellationToken))!);
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

    /// <summary>
    /// The job with its newest version's content, its source template's name and newest version, and its postings, in
    /// one query.
    /// </summary>
    internal static async Task<JobDetail?> LoadDetailAsync(ScreenHireDbContext db, BoardRegistry boards, Guid jobId, CancellationToken cancellationToken)
    {
        var row = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new
            {
                Job = j,
                Latest = db.JobVersions.Where(v => v.JobId == j.Id).OrderByDescending(v => v.Version).Select(v => v.Snapshot).FirstOrDefault(),
                Template = db.HiringTemplates.Where(t => t.Id == j.SourceTemplateId).Select(t => new { t.Name, t.LatestPublishedVersion }).FirstOrDefault(),
                Postings = db.JobPostings.Where(p => p.JobId == j.Id).ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row?.Job.ToDetail(row.Latest, row.Template?.Name, row.Template?.LatestPublishedVersion, row.Postings.ToStatuses(boards));
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

    private static ProblemHttpResult ChangedMeanwhile() =>
        TypedResults.Problem("Someone else changed this job at the same moment; refresh and try again.", statusCode: StatusCodes.Status409Conflict);
}
