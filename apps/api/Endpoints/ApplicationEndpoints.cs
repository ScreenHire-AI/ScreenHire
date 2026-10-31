using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
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
            .RequirePermission(Permission.AddCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        jobs.MapPost("/resume-uploads", CreateResumeUpload).WithName("CreateResumeUpload")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.AddCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        var applications = app.MapGroup("/v1/applications").WithTags("Applications").AllowApiKeys();
        applications.MapGet("/", ListApplications).WithName("ListApplications").Produces(StatusCodes.Status401Unauthorized);
        applications.MapGet("/{applicationId:guid}", GetApplication).WithName("GetApplication").Produces(StatusCodes.Status401Unauthorized);
        applications.MapPost("/{applicationId:guid}/move", MoveApplication).WithName("MoveApplication")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.MoveCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapGet("/{applicationId:guid}/timeline", GetApplicationTimeline).WithName("GetApplicationTimeline")
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapPost("/{applicationId:guid}/notes", AddApplicationNote).WithName("AddApplicationNote")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.AddNotesAndTags)
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapPut("/{applicationId:guid}/notes/{noteId:guid}", EditApplicationNote).WithName("EditApplicationNote")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.AddNotesAndTags)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapDelete("/{applicationId:guid}/notes/{noteId:guid}", DeleteApplicationNote).WithName("DeleteApplicationNote")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.AddNotesAndTags)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapPost("/{applicationId:guid}/tags", AddApplicationTag).WithName("AddApplicationTag")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.AddNotesAndTags)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapDelete("/{applicationId:guid}/tags", RemoveApplicationTag).WithName("RemoveApplicationTag")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.AddNotesAndTags)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapPost("/{applicationId:guid}/tasks", CreateApplicationTask).WithName("CreateApplicationTask")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.CreateTasks)
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
        Guid jobId, ClaimsPrincipal user, ITenantContext tenant, ScreenHireDbContext db, CancellationToken cancellationToken) =>
        await db.Jobs.AnyAsync(j => j.Id == jobId, cancellationToken)
            ? TypedResults.Ok(await ApplicationReads.ListAsync(user, tenant, db, new(JobId: jobId), cancellationToken))
            : TypedResults.NotFound();

    // Every application in the organization, newest first: the dashboard's candidates table across jobs.
    private static Task<List<ApplicationSummary>> ListApplications(
        ClaimsPrincipal user, ITenantContext tenant, ScreenHireDbContext db, CancellationToken cancellationToken) =>
        ApplicationReads.ListAsync(user, tenant, db, new(), cancellationToken);

    // A recruiter adds an application by hand (ApplicationSubmission): the candidate is found by email within the
    // organization, or created, and the application starts in the job's first stage. A resume uploaded through
    // CreateResumeUpload is checked as a careers upload is and stored as the answer to the resume question. A hand-added
    // application has no careers form behind it, so no notice, network or session data or telemetry is collected.
    private static async Task<Results<Created<ApplicationDetail>, NotFound, ValidationProblem, ProblemHttpResult>> CreateApplication(
        Guid jobId,
        CreateApplicationRequest request,
        IValidator<CreateApplicationRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ApplicantUploads uploads,
        ITenantContext tenant,
        ClaimsPrincipal user,
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
            return ClosedJob();
        }

        var details = ApplicationSubmission.CandidateDetails.From(request.Candidate);
        if (await ApplicationSubmission.IsOutOfSightAsync(db, details.Email, cancellationToken))
        {
            return TypedResults.Problem(
                "Someone with this email is already a candidate on a job you don't see. Ask someone who sees all jobs to add them.",
                statusCode: StatusCodes.Status409Conflict);
        }

        List<VerifiedUpload> files = [];
        if (request.ResumeUploadId is { } uploadId)
        {
            var check = await uploads.CheckAsync(job.OrganizationId, job.Id, ResumeQuestion(job), uploadId, cancellationToken);
            if (check.Upload is not { } resume)
            {
                return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                    "resumeUploadId", check.Problem ?? "We could not find this file. Upload it again."));
            }

            files.Add(resume);
        }

        var submitted = await ApplicationSubmission.SubmitAsync(
            outbox, job, details,
            new ApplicationDeclarations(request.AccommodationRequested, request.AiUseDeclaration, request.AiUseDetails),
            ApplicationIntake.Recruiter, files, time.GetUtcNow(), tenant.UserId, cancellationToken, actorApiKeyId: user.ApiKeyId());
        if (submitted is not { } created)
        {
            return TypedResults.Problem("This candidate already applied to this job.", statusCode: StatusCodes.Status409Conflict);
        }

        var (application, candidate) = created;
        return TypedResults.Created(
            $"/v1/applications/{application.Id}",
            await ApplicationReads.DetailOfAsync(tenant, db, application, job, candidate, cancellationToken));
    }

    // Where to upload a resume for a candidate a recruiter is adding: a create-only link straight to storage, as the
    // careers form's uploads get (ApplicantUploads), so the file's bytes never pass through the API. The job's resume
    // question says which types it takes. CreateApplication checks the file and claims it.
    private static async Task<Results<Ok<UploadTicket>, NotFound, ValidationProblem, ProblemHttpResult>> CreateResumeUpload(
        Guid jobId,
        UploadRequest request,
        IValidator<UploadRequest> validator,
        ScreenHireDbContext db,
        ApplicantUploads uploads,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (job.Status == JobStatus.Closed)
        {
            return ClosedJob();
        }

        var question = ResumeQuestion(job);
        var fileName = FileTypes.SafeFileName(request.FileName);
        var accepted = FileTypes.AcceptedBy(question.AcceptedFileTypes);
        if (fileName is null || !accepted.Contains(Path.GetExtension(fileName)))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("fileName", $"Upload a {FileTypes.Describe(accepted)} file."));
        }

        if (request.SizeBytes > uploads.MaxBytes)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("sizeBytes", FileTypes.TooLarge(uploads.MaxBytes)));
        }

        return TypedResults.Ok(await uploads.IssueAsync(job.OrganizationId, job.Id, question.Id, fileName, cancellationToken));
    }

    private static ProblemHttpResult ClosedJob() =>
        TypedResults.Problem("A closed job takes no applications.", statusCode: StatusCodes.Status409Conflict);

    // Every form has a resume question: the job's own, or the default one (ApplicationForm.QuestionsFor).
    private static Question ResumeQuestion(Job job) =>
        ApplicationForm.QuestionsFor(job.Workflow).First(q => q.Id == ApplicationForm.ResumeQuestionId);

    private static async Task<Results<Ok<ApplicationDetail>, NotFound>> GetApplication(
        Guid applicationId, ITenantContext tenant, ScreenHireDbContext db, CancellationToken cancellationToken) =>
        await ApplicationReads.DetailAsync(tenant, db, applicationId, cancellationToken) is { } detail
            ? TypedResults.Ok(detail)
            : TypedResults.NotFound();

    private static Task<Results<Ok<ApplicationDetail>, NotFound, ValidationProblem, ProblemHttpResult>> MoveApplication(
        Guid applicationId,
        MoveApplicationRequest request,
        IValidator<MoveApplicationRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        ClaimsPrincipal user,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ApplicationChanges.MoveAsync(applicationId, request, validator, outbox, tenant, user, time, cancellationToken);

    private static Task<Results<Created<ApplicationTimelineEntry>, NotFound, ValidationProblem>> AddApplicationNote(
        Guid applicationId,
        AddApplicationNoteRequest request,
        IValidator<AddApplicationNoteRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        ClaimsPrincipal user,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ApplicationChanges.AddNoteAsync(applicationId, request, validator, outbox, tenant, user, time, cancellationToken);

    // Only the author edits a note (NotePolicy), at any time. The note keeps its latest text only, the timeline gains a
    // NoteEdited entry, and a mention the edit adds notifies only that person. The same text again changes nothing.
    private static async Task<Results<Ok<ApplicationNoteDetail>, NotFound, ValidationProblem, ProblemHttpResult>> EditApplicationNote(
        Guid applicationId,
        Guid noteId,
        EditApplicationNoteRequest request,
        IValidator<EditApplicationNoteRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        ClaimsPrincipal user,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var db = outbox.DbContext;
        if (await LoadNoteAsync(db, applicationId, noteId, cancellationToken) is not var (application, note))
        {
            return TypedResults.NotFound();
        }

        if (!NotePolicy.MayEdit(note, tenant.UserId, user.ApiKeyId()))
        {
            return TypedResults.Problem(title: "Author only", detail: "Only the author can edit a note.", statusCode: StatusCodes.Status403Forbidden);
        }

        if (await ApplicationChanges.MentionProblemsAsync(db, tenant, application.JobId, request.Note, cancellationToken, note) is { } mentionProblems)
        {
            return TypedResults.ValidationProblem(mentionProblems);
        }

        var now = time.GetUtcNow();
        if (application.EditNote(note, request.Note, now, tenant.UserId, user.ApiKeyId()) is var (entry, added))
        {
            db.ApplicationEvents.Add(entry);
            // New rows, said so: a child with a key already set, added to a tracked note, would otherwise be taken for an
            // existing row and updated.
            db.ApplicationNoteMentions.AddRange(added);
            await outbox.PublishAsync(new ApplicationNoteEdited(application.OrganizationId, application.Id, application.JobId, note.Id, now));
            await ApplicationChanges.PublishMentionsAsync(outbox, application, note, added, now);
            try
            {
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return NoteConflict();
            }
            catch (DbUpdateException exception) when (exception.IsForeignKeyViolation() || exception.IsUniqueViolation())
            {
                // A mention added to a note deleted, or given the same mention, by a request that committed first.
                return NoteConflict();
            }
        }

        return TypedResults.Ok(note.ToDetail());
    }

    // The author, or a role that deletes anyone's notes, deletes a note (NotePolicy). Its row goes, and its text and mentions with it; the
    // timeline keeps a NoteDeleted entry saying who deleted it and when, never what it said.
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteApplicationNote(
        Guid applicationId,
        Guid noteId,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        ClaimsPrincipal user,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        if (await LoadNoteAsync(db, applicationId, noteId, cancellationToken) is not var (application, note))
        {
            return TypedResults.NotFound();
        }

        // What the person's role allows in this organization; a key has no role.
        var permissions = tenant.UserId is { } userId
            ? (await RoleAccess.ForMemberAsync(db, tenant.OrganizationId!.Value, userId, cancellationToken))?.Permissions
            : null;
        if (!NotePolicy.MayDelete(note, tenant.UserId, user.ApiKeyId(), permissions))
        {
            return TypedResults.Problem(
                title: "Not your note",
                detail: "Only the note's author, or a role that deletes anyone's notes, can delete it.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var now = time.GetUtcNow();
        db.ApplicationEvents.Add(application.DeleteNote(note, now, tenant.UserId, user.ApiKeyId()));
        db.ApplicationNotes.Remove(note);
        await outbox.PublishAsync(new ApplicationNoteDeleted(application.OrganizationId, application.Id, application.JobId, note.Id, now));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return NoteConflict();
        }

        return TypedResults.NoContent();
    }

    private static ProblemHttpResult NoteConflict() =>
        TypedResults.Problem("Someone changed this note at the same moment; refresh and try again.", statusCode: StatusCodes.Status409Conflict);

    // The application and one of its notes, the note tracked for a change; null for another application's note, a deleted
    // one or another organization's.
    private static async Task<(JobApplication Application, ApplicationNote Note)?> LoadNoteAsync(
        ScreenHireDbContext db, Guid applicationId, Guid noteId, CancellationToken cancellationToken)
    {
        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        var note = application is null
            ? null
            : await db.ApplicationNotes.SingleOrDefaultAsync(n => n.Id == noteId && n.ApplicationId == applicationId, cancellationToken);
        return application is null || note is null ? null : (application, note);
    }

    private static Task<Results<Ok<ApplicationDetail>, NotFound, ValidationProblem, ProblemHttpResult>> AddApplicationTag(
        Guid applicationId,
        AddApplicationTagRequest request,
        IValidator<AddApplicationTagRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        ClaimsPrincipal user,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ApplicationChanges.AddTagAsync(applicationId, request, validator, outbox, tenant, user, time, cancellationToken);

    private static Task<Results<Ok<ApplicationDetail>, NotFound, ValidationProblem, ProblemHttpResult>> RemoveApplicationTag(
        Guid applicationId,
        string? tag,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        ClaimsPrincipal user,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ApplicationChanges.RemoveTagAsync(applicationId, tag, outbox, tenant, user, time, cancellationToken);

    private static Task<Results<Created<ApplicationTaskEntry>, NotFound, ValidationProblem>> CreateApplicationTask(
        Guid applicationId,
        CreateApplicationTaskRequest request,
        IValidator<CreateApplicationTaskRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        ClaimsPrincipal user,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ApplicationChanges.CreateTaskAsync(applicationId, request, validator, outbox, tenant, user, time, cancellationToken);

    private static async Task<Results<Ok<List<ApplicationTimelineEntry>>, NotFound>> GetApplicationTimeline(
        Guid applicationId, ClaimsPrincipal user, ITenantContext tenant, ScreenHireDbContext db, CancellationToken cancellationToken) =>
        await ApplicationReads.TimelineAsync(user, tenant, db, applicationId, cancellationToken) is { } timeline
            ? TypedResults.Ok(timeline)
            : TypedResults.NotFound();

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
        Guid candidateId, ClaimsPrincipal user, ITenantContext tenant, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.AsNoTracking().SingleOrDefaultAsync(c => c.Id == candidateId, cancellationToken);
        if (candidate is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new CandidateDetail(
            candidate.Id, candidate.FullName, candidate.Email, candidate.Phone, candidate.CreatedAt,
            await ApplicationReads.ListAsync(user, tenant, db, new(CandidateId: candidateId), cancellationToken),
            candidate.FirstName, candidate.LastName));
    }
}
