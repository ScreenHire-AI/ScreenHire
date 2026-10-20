using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Content;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
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
            .RequireOrganization()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{jobId:guid}", GetJob).WithName("GetJob").Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/{jobId:guid}", UpdateJob).WithName("UpdateJob")
            .RequireAntiforgeryToken()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{jobId:guid}/publish", PublishJob).WithName("PublishJob")
            .RequireAntiforgeryToken()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{jobId:guid}/close", CloseJob).WithName("CloseJob")
            .RequireAntiforgeryToken()
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

    private static async Task<Results<Ok<JobDetail>, NotFound>> GetJob(Guid jobId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var detail = await LoadDetailAsync(db, jobId, cancellationToken);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    // A draft: no event until it is published. With a template version, the job copies that version's workflow, and
    // the version's posting defaults fill whatever the request leaves out.
    private static async Task<Results<Created<JobDetail>, ValidationProblem, ProblemHttpResult>> CreateJob(
        CreateJobRequest request,
        ScreenHireDbContext db,
        ITenantContext tenant,
        JobContentValidator validator,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (request.Description?.Length > JobContentValidator.MaxDescriptionLength)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("description", "At most 50000 characters."));
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
            Clean(request.Department ?? defaults.Department),
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

        return TypedResults.Created($"/v1/jobs/{job.Id}", (await LoadDetailAsync(db, job.Id, cancellationToken))!);
    }

    // Replaces the posting content. Editing an open job does not republish it: the change is live on the careers page,
    // and boards get it when someone publishes again, which freezes a new version (hasUnpublishedChanges until then).
    private static async Task<Results<Ok<JobDetail>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateJob(
        Guid jobId,
        UpdateJobRequest request,
        ScreenHireDbContext db,
        JobContentValidator validator,
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

        if (request.Description?.Length > JobContentValidator.MaxDescriptionLength)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("description", "At most 50000 characters."));
        }

        var content = new JobSnapshot(
            request.Title?.Trim() ?? "",
            request.Slug?.Trim() ?? job.Slug,
            RichText.Sanitize(request.Description),
            Clean(request.Department),
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
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return TypedResults.Problem("Another job took this slug at the same moment; try again.", statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok((await LoadDetailAsync(db, job.Id, cancellationToken))!);
    }

    // Opens the job, or republishes changed content, as a new JobVersion. The version, the job change and JobPublished
    // commit in one transaction (the EF Core outbox), and the worker handles the event. Publishing an open job whose
    // content equals its newest version commits nothing and raises no event.
    private static async Task<Results<Ok<JobDetail>, NotFound, ValidationProblem, ProblemHttpResult>> PublishJob(
        Guid jobId,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        JobPublishValidator validator,
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
        if (job.Publish(now, latest) is { } version)
        {
            db.JobVersions.Add(version);
            await outbox.PublishAsync(new JobPublished(job.OrganizationId, job.Id, version.PublishedAt, version.Version, version.Id));
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }

        return TypedResults.Ok((await LoadDetailAsync(db, job.Id, cancellationToken))!);
    }

    // Closing a closed job commits nothing and raises no second event.
    private static async Task<Results<Ok<JobDetail>, NotFound>> CloseJob(
        Guid jobId, IDbContextOutbox<ScreenHireDbContext> outbox, TimeProvider time, CancellationToken cancellationToken)
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
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }

        return TypedResults.Ok((await LoadDetailAsync(db, job.Id, cancellationToken))!);
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

    /// <summary>The job with its newest version's content and its source template's name and newest version, in one query.</summary>
    internal static async Task<JobDetail?> LoadDetailAsync(ScreenHireDbContext db, Guid jobId, CancellationToken cancellationToken)
    {
        var row = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new
            {
                Job = j,
                Latest = db.JobVersions.Where(v => v.JobId == j.Id).OrderByDescending(v => v.Version).Select(v => v.Snapshot).FirstOrDefault(),
                Template = db.HiringTemplates.Where(t => t.Id == j.SourceTemplateId).Select(t => new { t.Name, t.LatestPublishedVersion }).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row?.Job.ToDetail(row.Latest, row.Template?.Name, row.Template?.LatestPublishedVersion);
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

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
