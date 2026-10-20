using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
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
        var jobs = app.MapGroup("/v1/jobs/{jobId:guid}/applications").WithTags("Applications").RequireAuthorization();
        jobs.MapGet("/", ListJobApplications).WithName("ListJobApplications").Produces(StatusCodes.Status401Unauthorized);
        jobs.MapPost("/", CreateApplication).WithName("CreateApplication")
            .RequireAntiforgeryToken()
            .RequireOrganization()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        var applications = app.MapGroup("/v1/applications").WithTags("Applications").RequireAuthorization();
        applications.MapGet("/{applicationId:guid}", GetApplication).WithName("GetApplication").Produces(StatusCodes.Status401Unauthorized);
        applications.MapPost("/{applicationId:guid}/move", MoveApplication).WithName("MoveApplication")
            .RequireAntiforgeryToken()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapGet("/{applicationId:guid}/timeline", GetApplicationTimeline).WithName("GetApplicationTimeline")
            .Produces(StatusCodes.Status401Unauthorized);

        var candidates = app.MapGroup("/v1/candidates").WithTags("Candidates").RequireAuthorization();
        candidates.MapGet("/", ListCandidates).WithName("ListCandidates").Produces(StatusCodes.Status401Unauthorized);
        candidates.MapGet("/{candidateId:guid}", GetCandidate).WithName("GetCandidate").Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<List<ApplicationSummary>>, NotFound>> ListJobApplications(
        Guid jobId, ScreenHireDbContext db, CancellationToken cancellationToken)
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
        return TypedResults.Ok(rows.Select(r => r.Application.ToSummary(job, r.Candidate)).ToList());
    }

    // A recruiter adds an application by hand. The candidate is found by email within the organization, or created.
    // The application starts in the job's first stage, and its Submitted event and ApplicationSubmitted commit with it.
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

        var now = time.GetUtcNow();
        var email = Candidate.Normalize(request.Candidate.Email);
        var candidate = await db.Candidates.SingleOrDefaultAsync(c => c.NormalizedEmail == email, cancellationToken);
        if (candidate is null)
        {
            candidate = Candidate.Create(job.OrganizationId, request.Candidate.FullName, request.Candidate.Email, request.Candidate.Phone, now);
            db.Candidates.Add(candidate);
        }
        else if (await db.Applications.AnyAsync(a => a.JobId == jobId && a.CandidateId == candidate.Id, cancellationToken))
        {
            return TypedResults.Problem("This candidate already applied to this job.", statusCode: StatusCodes.Status409Conflict);
        }

        var (application, submitted) = JobApplication.Submit(
            job, candidate, request.AccommodationRequested, request.AiUseDeclaration, request.AiUseDetails, now, tenant.UserId);
        db.Applications.Add(application);
        db.ApplicationEvents.Add(submitted);
        await outbox.PublishAsync(new ApplicationSubmitted(
            application.OrganizationId, application.Id, job.Id, application.CurrentStageId, submitted.Id, now));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return TypedResults.Problem("This candidate applied at the same moment; refresh to see the application.", statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Created($"/v1/applications/{application.Id}", application.ToDetail(job, candidate));
    }

    private static async Task<Results<Ok<ApplicationDetail>, NotFound>> GetApplication(
        Guid applicationId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var row = await LoadAsync(db, applicationId, cancellationToken);
        return row is null ? TypedResults.NotFound() : TypedResults.Ok(row.Value.Application.ToDetail(row.Value.Job, row.Value.Candidate));
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

        return TypedResults.Ok(application.ToDetail(job, candidate));
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

    private static async Task<List<CandidateSummary>> ListCandidates(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var candidates = await db.Candidates.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        return [.. candidates.Select(c => c.ToSummary())];
    }

    private static async Task<Results<Ok<CandidateDetail>, NotFound>> GetCandidate(
        Guid candidateId, ScreenHireDbContext db, CancellationToken cancellationToken)
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
        return TypedResults.Ok(new CandidateDetail(
            candidate.Id, candidate.FullName, candidate.Email, candidate.Phone, candidate.CreatedAt,
            [.. rows.Select(r => r.Application.ToSummary(r.Job, candidate))]));
    }

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
