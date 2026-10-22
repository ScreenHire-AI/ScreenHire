using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Applications, their timeline and candidates. Tenant-scoped like every dashboard route: another organization's job,
/// application or candidate is a 404. Applications move only when a person moves them (CLAUDE.md non-negotiable 2):
/// there is no rejection path here that a signal could drive.
/// </summary>
internal static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/v1/jobs/{jobId:guid}/applications").WithTags("Applications").AllowApiKeys();
        jobs.MapGet("/", ListJobApplications).WithName("ListJobApplications").Produces(StatusCodes.Status401Unauthorized);
        jobs.MapPost("/", CreateApplication).WithName("CreateApplication")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        var applications = app.MapGroup("/v1/applications").WithTags("Applications").AllowApiKeys();
        applications.MapGet("/", ListApplications).WithName("ListApplications").Produces(StatusCodes.Status401Unauthorized);
        applications.MapGet("/{applicationId:guid}", GetApplication).WithName("GetApplication").Produces(StatusCodes.Status401Unauthorized);
        applications.MapPost("/{applicationId:guid}/move", MoveApplication).WithName("MoveApplication")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapGet("/{applicationId:guid}/timeline", GetApplicationTimeline).WithName("GetApplicationTimeline")
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapGet("/{applicationId:guid}/files/{fileId:guid}", DownloadApplicationFile).WithName("DownloadApplicationFile")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        var candidates = app.MapGroup("/v1/candidates").WithTags("Candidates").AllowApiKeys();
        candidates.MapGet("/", ListCandidates).WithName("ListCandidates").Produces(StatusCodes.Status401Unauthorized);
        candidates.MapGet("/{candidateId:guid}", GetCandidate).WithName("GetCandidate").Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<List<ApplicationSummary>>, NotFound>> ListJobApplications(
        Guid jobId, ClaimsPrincipal user, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        var rows = await db.Applications.AsNoTracking()
            .Where(a => a.JobId == jobId)
            .OrderByDescending(a => a.SubmittedAt)
            .Join(db.Candidates, a => a.CandidateId, c => c.Id, (a, c) => new { Application = a, Candidate = c })
            .ToListAsync(cancellationToken);
        var integrity = await IntegritySummariesAsync(user, db, [.. rows.Select(r => r.Application.Id)], cancellationToken);
        return TypedResults.Ok(rows.Select(r => r.Application.ToSummary(job, r.Candidate, integrity.GetValueOrDefault(r.Application.Id))).ToList());
    }

    // Every application in the organization, newest first: the dashboard's candidates table across jobs.
    private static async Task<List<ApplicationSummary>> ListApplications(
        ClaimsPrincipal user, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Applications.AsNoTracking()
            .OrderByDescending(a => a.SubmittedAt)
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .Join(db.Candidates, x => x.Application.CandidateId, c => c.Id, (x, c) => new { x.Application, x.Job, Candidate = c })
            .ToListAsync(cancellationToken);
        var integrity = await IntegritySummariesAsync(user, db, [.. rows.Select(r => r.Application.Id)], cancellationToken);
        return [.. rows.Select(r => r.Application.ToSummary(r.Job, r.Candidate, integrity.GetValueOrDefault(r.Application.Id)))];
    }

    // A recruiter adds an application by hand (ApplicationSubmission): the candidate is found by email within the
    // organization, or created, and the application starts in the job's first stage.
    private static async Task<Results<Created<ApplicationDetail>, NotFound, ValidationProblem, ProblemHttpResult>> CreateApplication(
        Guid jobId,
        CreateApplicationRequest request,
        IValidator<CreateApplicationRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var db = outbox.DbContext;
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (job.Status == JobStatus.Closed)
        {
            return TypedResults.Problem("A closed job takes no applications.", statusCode: StatusCodes.Status409Conflict);
        }

        var submitted = await ApplicationSubmission.SubmitAsync(
            outbox, job, request.Candidate,
            new ApplicationDeclarations(request.AccommodationRequested, request.AiUseDeclaration, request.AiUseDetails),
            ApplicationIntake.Recruiter, [], time.GetUtcNow(), tenant.UserId, cancellationToken);
        if (submitted is not { } created)
        {
            return TypedResults.Problem("This candidate already applied to this job.", statusCode: StatusCodes.Status409Conflict);
        }

        var (application, candidate) = created;
        return TypedResults.Created($"/v1/applications/{application.Id}", application.ToDetail(job, candidate, []));
    }

    private static async Task<Results<Ok<ApplicationDetail>, NotFound>> GetApplication(
        Guid applicationId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var row = await LoadAsync(db, applicationId, cancellationToken);
        if (row is not { } found)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(found.Application.ToDetail(found.Job, found.Candidate, await FilesOfAsync(db, applicationId, cancellationToken)));
    }

    // A person moves an application to any stage of its job's workflow. The timeline event and ApplicationMoved commit
    // with the move; a move to the current stage commits nothing. Two people moving one application at once: the second
    // gets 409 (optimistic concurrency on the row), never a lost update.
    private static async Task<Results<Ok<ApplicationDetail>, NotFound, ValidationProblem, ProblemHttpResult>> MoveApplication(
        Guid applicationId,
        MoveApplicationRequest request,
        IValidator<MoveApplicationRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var db = outbox.DbContext;
        var application = await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        if (application is null)
        {
            return TypedResults.NotFound();
        }

        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == application.CandidateId, cancellationToken);
        var target = job.Workflow.FindStage(request.ToStageId);
        if (target is null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("toStageId", "The job's workflow has no stage with this id."));
        }

        var from = application.CurrentStageId;
        var now = time.GetUtcNow();
        if (application.MoveTo(target, now, tenant.UserId, request.Note) is { } moved)
        {
            db.ApplicationEvents.Add(moved);
            await outbox.PublishAsync(new ApplicationMoved(
                application.OrganizationId, application.Id, application.JobId, from, target.Id, moved.Id, now));
            try
            {
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return TypedResults.Problem("Someone else moved this application at the same moment; refresh and try again.", statusCode: StatusCodes.Status409Conflict);
            }
        }

        return TypedResults.Ok(application.ToDetail(job, candidate, await FilesOfAsync(db, application.Id, cancellationToken)));
    }

    private static async Task<Results<Ok<List<ApplicationTimelineEntry>>, NotFound>> GetApplicationTimeline(
        Guid applicationId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var events = await db.ApplicationEvents.AsNoTracking()
            .Where(e => e.ApplicationId == applicationId)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(events.Select(e => e.ToTimelineEntry()).ToList());
    }

    // Sends a reviewer to a short-lived read link for one of the application's files, such as the resume. A file the
    // scanner found malware in is never offered, and neither is one still waiting for its scan.
    private static async Task<Results<RedirectHttpResult, NotFound, ProblemHttpResult>> DownloadApplicationFile(
        Guid applicationId, Guid fileId, ScreenHireDbContext db, ApplicantUploads uploads, CancellationToken cancellationToken)
    {
        var file = await db.ApplicationFiles.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == fileId && f.ApplicationId == applicationId, cancellationToken);
        if (file is null)
        {
            return TypedResults.NotFound();
        }

        return file.ScanStatus switch
        {
            MalwareScanStatus.Infected => TypedResults.Problem(
                "The malware scan found a threat in this file, so it is not available.", statusCode: StatusCodes.Status409Conflict),
            MalwareScanStatus.Pending => TypedResults.Problem(
                "This file is still being scanned for malware. Try again in a minute.", statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Redirect((await uploads.IssueReadLinkAsync(file.BlobName, file.FileName, cancellationToken)).ToString()),
        };
    }

    private static async Task<List<CandidateSummary>> ListCandidates(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var candidates = await db.Candidates.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        return [.. candidates.Select(c => c.ToSummary())];
    }

    private static async Task<Results<Ok<CandidateDetail>, NotFound>> GetCandidate(
        Guid candidateId, ClaimsPrincipal user, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.AsNoTracking().SingleOrDefaultAsync(c => c.Id == candidateId, cancellationToken);
        if (candidate is null)
        {
            return TypedResults.NotFound();
        }

        var rows = await db.Applications.AsNoTracking()
            .Where(a => a.CandidateId == candidateId)
            .OrderByDescending(a => a.SubmittedAt)
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .ToListAsync(cancellationToken);
        var integrity = await IntegritySummariesAsync(user, db, [.. rows.Select(r => r.Application.Id)], cancellationToken);
        return TypedResults.Ok(new CandidateDetail(
            candidate.Id, candidate.FullName, candidate.Email, candidate.Phone, candidate.CreatedAt,
            [.. rows.Select(r => r.Application.ToSummary(r.Job, candidate, integrity.GetValueOrDefault(r.Application.Id)))]));
    }

    // Integrity output is evidence for the person reviewing an application (ADR-0004), so an API key never receives it:
    // an integrator's system could reject on it, which ScreenHire's own automations may not (ADR-0011).
    private static async Task<Dictionary<Guid, IntegritySummary>> IntegritySummariesAsync(
        ClaimsPrincipal user, ScreenHireDbContext db, IReadOnlyCollection<Guid> applicationIds, CancellationToken cancellationToken) =>
        user.IsApiKey() ? [] : await IntegrityReads.SummariesAsync(db, applicationIds, cancellationToken);

    private static async Task<List<ApplicationFile>> FilesOfAsync(ScreenHireDbContext db, Guid applicationId, CancellationToken cancellationToken) =>
        await db.ApplicationFiles.AsNoTracking()
            .Where(f => f.ApplicationId == applicationId)
            .OrderBy(f => f.UploadedAt)
            .ThenBy(f => f.QuestionId)
            .ToListAsync(cancellationToken);

    private static async Task<(JobApplication Application, Job Job, Candidate Candidate)?> LoadAsync(
        ScreenHireDbContext db, Guid applicationId, CancellationToken cancellationToken)
    {
        var row = await db.Applications.AsNoTracking()
            .Where(a => a.Id == applicationId)
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .Join(db.Candidates, x => x.Application.CandidateId, c => c.Id, (x, c) => new { x.Application, x.Job, Candidate = c })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.Application, row.Job, row.Candidate);
    }
}
