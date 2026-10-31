using FluentValidation;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.TalentPool;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Integrity;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The careers apply flow's writes: upload links for file questions, the check of an uploaded file, and the application
/// itself (docs/architecture/apply-flow.md). Anonymous, rate limited per candidate address, and called by the careers
/// server, which forwards the candidate's address in <c>X-Forwarded-For</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each route makes the organization in its slug the request's tenant, like <see cref="PublicJobEndpoints"/>, and then
/// finds the job by id under that tenant. That is safe for these writes, because each one only adds a candidate's own
/// application, or an upload for it, to the organization the route names, which any anonymous caller may do. The job id
/// of another organization is not found (404), so nothing is written across tenants.
/// </para>
/// <para>
/// Integrity signals never refuse a submission (CLAUDE.md non-negotiable 2): the honeypot and the bot challenge are
/// recorded on the application, and knockout answers are stored like any other.
/// </para>
/// </remarks>
internal static class PublicApplicationEndpoints
{
    public static IEndpointRouteBuilder MapPublicApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/public/organizations/{organizationSlug}/jobs/{jobId:guid}")
            .WithTags("Public careers")
            .AllowAnonymous()
            .RequireRateLimiting(ApplyOptions.RateLimitPolicy);

        group.MapPost("/questions/{questionId}/uploads", CreateUpload).WithName("CreatePublicUpload").ProducesApplyErrors();
        group.MapPost("/questions/{questionId}/uploads/{uploadId:guid}/verify", VerifyUpload).WithName("VerifyPublicUpload")
            .ProducesApplyErrors();
        group.MapPost("/applications", SubmitApplication).WithName("SubmitPublicApplication").ProducesApplyErrors()
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<Results<Ok<UploadTicket>, NotFound, ValidationProblem, ProblemHttpResult>> CreateUpload(
        string organizationSlug,
        Guid jobId,
        string questionId,
        UploadRequest request,
        IValidator<UploadRequest> validator,
        ScreenHireDbContext db,
        TenantContext tenant,
        ApplicantUploads uploads,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var (job, gone) = await FindOpenJobAsync(organizationSlug, jobId, db, tenant, cancellationToken);
        if (gone is not null)
        {
            return gone;
        }

        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (FileQuestion(job, questionId) is not { } question)
        {
            return TypedResults.NotFound();
        }

        var fileName = FileTypes.SafeFileName(request.FileName);
        var accepted = FileTypes.AcceptedBy(question.AcceptedFileTypes);
        if (fileName is null || !accepted.Contains(Path.GetExtension(fileName)))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "fileName", $"Upload a {FileTypes.Describe(accepted)} file."));
        }

        if (request.SizeBytes > uploads.MaxBytes)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "sizeBytes", FileTypes.TooLarge(uploads.MaxBytes)));
        }

        return TypedResults.Ok(await uploads.IssueAsync(job.OrganizationId, job.Id, question.Id, fileName, cancellationToken));
    }

    // Checks an upload as soon as it finishes, so the candidate hears about a wrong or oversized file at the step where
    // they chose it rather than at the end. Submission checks every file again.
    private static async Task<Results<Ok<UploadedFile>, NotFound, ValidationProblem, ProblemHttpResult>> VerifyUpload(
        string organizationSlug,
        Guid jobId,
        string questionId,
        Guid uploadId,
        ScreenHireDbContext db,
        TenantContext tenant,
        ApplicantUploads uploads,
        CancellationToken cancellationToken)
    {
        var (job, gone) = await FindOpenJobAsync(organizationSlug, jobId, db, tenant, cancellationToken);
        if (gone is not null)
        {
            return gone;
        }

        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (FileQuestion(job, questionId) is not { } question)
        {
            return TypedResults.NotFound();
        }

        var check = await uploads.CheckAsync(job.OrganizationId, job.Id, question, uploadId, cancellationToken);
        return check switch
        {
            { Upload: { } upload } => TypedResults.Ok(new UploadedFile(upload.UploadId, upload.FileName, upload.ContentType, upload.SizeBytes)),
            { Problem: { } message } => TypedResults.ValidationProblem(ValidationExtensions.Problem("file", message)),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<Created<PublicApplicationReceipt>, NotFound, ValidationProblem, ProblemHttpResult>> SubmitApplication(
        string organizationSlug,
        Guid jobId,
        PublicApplicationRequest request,
        IValidator<PublicApplicationRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        ApplicantUploads uploads,
        TurnstileVerifier turnstile,
        TelemetrySessions telemetry,
        IValidator<TelemetrySnapshotInput> telemetryValidator,
        IOptions<IntegrityOptions> integrity,
        HttpContext http,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var (job, gone) = await FindOpenJobAsync(organizationSlug, jobId, outbox.DbContext, tenant, cancellationToken);
        if (gone is not null)
        {
            return gone;
        }

        if (job is null)
        {
            return TypedResults.NotFound();
        }

        var questions = ApplicationForm.QuestionsFor(job.Workflow);
        var answers = request.Answers ?? [];
        var errors = ApplicationForm.Validate(questions, answers)
            .GroupBy(e => $"answers.{e.QuestionId}")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());

        if (job.Workflow.IntegrityPolicy.AiUse.RequiresDeclaration() && request.AiUseDeclaration == AiUseDeclaration.NotProvided)
        {
            errors["aiUseDeclaration"] = ["Tell us whether you used AI tools for this application."];
        }

        // Every file answer must name an upload for this job and question that passes the checks.
        var files = new List<VerifiedUpload>();
        foreach (var answer in ApplicationForm.Normalize(answers))
        {
            var question = questions.FirstOrDefault(q => q.Id == answer.QuestionId);
            if (question?.Type != QuestionType.File || !Guid.TryParse(answer.Value, out var uploadId) || errors.ContainsKey($"answers.{question.Id}"))
            {
                continue;
            }

            var check = await uploads.CheckAsync(job.OrganizationId, job.Id, question, uploadId, cancellationToken);
            if (check.Upload is { } upload)
            {
                files.Add(upload);
            }
            else
            {
                errors[$"answers.{question.Id}"] = [check.Problem ?? "We could not find this file. Upload it again."];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // The device check's footing where the job is (integrity-engine.md §3): the page is not trusted to have asked.
        var fingerprint = TelemetrySessions.DeviceCheckFor(job, integrity.Value) switch
        {
            DeviceCheckBasis.Notice => request.DeviceFingerprint,
            DeviceCheckBasis.Consent when request.DeviceCheckConsent => request.DeviceFingerprint,
            _ => null,
        };
        var binding = await TelemetryBindingAsync(request, job, telemetry, telemetryValidator, cancellationToken);

        var remoteAddress = http.Connection.RemoteIpAddress?.ToString();
        var now = time.GetUtcNow();
        // The candidate's own permission for future roles, for the period in force now, which the form showed them.
        var futureRoles = request.KeepForFutureRoles
            ? new FutureRolesConsent(now, await TalentPoolMembership.MonthsAsync(outbox.DbContext, cancellationToken))
            : null;
        var intake = new ApplicationIntake(
            ApplicationSource.CareersSite,
            answers,
            request.PrivacyNoticeVersion.Trim(),
            request.HoneypotFilled,
            await turnstile.VerifyAsync(request.BotCheckToken, remoteAddress, cancellationToken),
            futureRoles);

        var submitted = await ApplicationSubmission.SubmitAsync(
            outbox, job, ApplicationSubmission.CandidateDetails.From(request.Candidate),
            new ApplicationDeclarations(request.AccommodationRequested, request.AiUseDeclaration, request.AiUseDetails),
            intake, files, now, actorUserId: null, cancellationToken,
            new SubmissionSession(remoteAddress, request.UserAgent, fingerprint, request.PageTimes, request.AutomationMarkers),
            integrity.Value,
            binding);
        if (submitted is not { } created)
        {
            return TypedResults.Problem(
                title: "Already applied", detail: "An application with this email address is already in for this job.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Created(
            (string?)null, new PublicApplicationReceipt(created.Application.Id, created.Application.SubmittedAt));
    }

    // The session the submission names, with its final snapshot when that is valid for this job. Telemetry never refuses
    // an application: a bad token binds nothing and a bad snapshot is dropped.
    private static async Task<TelemetryBinding?> TelemetryBindingAsync(
        PublicApplicationRequest request, Job job, TelemetrySessions sessions, IValidator<TelemetrySnapshotInput> validator,
        CancellationToken cancellationToken)
    {
        if (sessions.Read(request.TelemetrySessionToken, job.OrganizationId, job.Id) is not { } sessionId)
        {
            return null;
        }

        var final = request.Telemetry;
        var known = TelemetrySessions.FieldIdsFor(job);
        var valid = final is not null
            && sessions.Read(final.SessionToken, job.OrganizationId, job.Id) == sessionId
            && (await validator.ValidateAsync(final, cancellationToken)).IsValid
            && final.Fields.All(f => known.Contains(f.FieldId));
        return new TelemetryBinding(sessionId, valid ? final : null);
    }

    private static RouteHandlerBuilder ProducesApplyErrors(this RouteHandlerBuilder builder) =>
        builder.ProducesProblem(StatusCodes.Status410Gone).Produces(StatusCodes.Status429TooManyRequests);

    private static Question? FileQuestion(Job job, string questionId) =>
        ApplicationForm.QuestionsFor(job.Workflow).FirstOrDefault(q => q.Id == questionId && q.Type == QuestionType.File);

    // The open job with this id in the slug's organization, or the 410 for one that closed. Neither (a draft, a missing
    // job, another organization's job) is the caller's 404.
    internal static async Task<(Job? Job, ProblemHttpResult? Gone)> FindOpenJobAsync(
        string organizationSlug, Guid jobId, ScreenHireDbContext db, TenantContext tenant, CancellationToken cancellationToken)
    {
        if (await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is null)
        {
            return (null, null);
        }

        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        return job switch
        {
            { Status: JobStatus.Open } => (job, null),
            { Status: JobStatus.Closed, PublishedAt: not null } => (null, PublicJobEndpoints.Gone()),
            _ => (null, null),
        };
    }
}
