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
        applications.MapPost("/{applicationId:guid}/notes", AddApplicationNote).WithName("AddApplicationNote")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapPut("/{applicationId:guid}/notes/{noteId:guid}", EditApplicationNote).WithName("EditApplicationNote")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapDelete("/{applicationId:guid}/notes/{noteId:guid}", DeleteApplicationNote).WithName("DeleteApplicationNote")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapPost("/{applicationId:guid}/tags", AddApplicationTag).WithName("AddApplicationTag")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        applications.MapDelete("/{applicationId:guid}/tags", RemoveApplicationTag).WithName("RemoveApplicationTag")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .ProducesProblem(StatusCodes.Status409Conflict)
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
            return TypedResults.Problem("A closed job takes no applications.", statusCode: StatusCodes.Status409Conflict);
        }

        var submitted = await ApplicationSubmission.SubmitAsync(
            outbox, job, request.Candidate,
            new ApplicationDeclarations(request.AccommodationRequested, request.AiUseDeclaration, request.AiUseDetails),
            ApplicationIntake.Recruiter, [], time.GetUtcNow(), tenant.UserId, cancellationToken, actorApiKeyId: user.ApiKeyId());
        if (submitted is not { } created)
        {
            return TypedResults.Problem("This candidate already applied to this job.", statusCode: StatusCodes.Status409Conflict);
        }

        var (application, candidate) = created;
        return TypedResults.Created($"/v1/applications/{application.Id}", application.ToDetail(job, candidate, [], []));
    }

    private static async Task<Results<Ok<ApplicationDetail>, NotFound>> GetApplication(
        Guid applicationId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var row = await LoadAsync(db, applicationId, cancellationToken);
        if (row is not { } found)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(found.Application.ToDetail(
            found.Job, found.Candidate, await FilesOfAsync(db, applicationId, cancellationToken), await FollowUpsOfAsync(db, applicationId, cancellationToken)));
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
        ClaimsPrincipal user,
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
        if (application.MoveTo(target, now, tenant.UserId, request.Note, actorApiKeyId: user.ApiKeyId()) is { } moved)
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

        return TypedResults.Ok(application.ToDetail(
            job, candidate, await FilesOfAsync(db, application.Id, cancellationToken), await FollowUpsOfAsync(db, application.Id, cancellationToken)));
    }

    // A note from the hiring team: the note, a timeline entry naming it with its author, and ApplicationNoteAdded so open
    // dashboards show it. Each teammate it mentions, other than its author, gets TeammateMentioned: a card on their open
    // dashboards and an email from the worker. It changes nothing else about the application, so it cannot race a move.
    private static async Task<Results<Created<ApplicationTimelineEntry>, NotFound, ValidationProblem>> AddApplicationNote(
        Guid applicationId,
        AddApplicationNoteRequest request,
        IValidator<AddApplicationNoteRequest> validator,
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
        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        if (application is null)
        {
            return TypedResults.NotFound();
        }

        if (await MentionProblemsAsync(db, tenant, request.Note, cancellationToken) is { } mentionProblems)
        {
            return TypedResults.ValidationProblem(mentionProblems);
        }

        var now = time.GetUtcNow();
        var (note, entry) = application.AddNote(request.Note, now, tenant.UserId, user.ApiKeyId());
        db.ApplicationNotes.Add(note);
        db.ApplicationEvents.Add(entry);
        await outbox.PublishAsync(new ApplicationNoteAdded(application.OrganizationId, application.Id, application.JobId, entry.Id, now));
        await PublishMentionsAsync(outbox, application, note, note.Mentions, now);
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Created(
            $"/v1/applications/{application.Id}/timeline", entry.ToTimelineEntry(note, await ApiKeyNameAsync(db, user, cancellationToken)));
    }

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

        if (await MentionProblemsAsync(db, tenant, request.Note, cancellationToken) is { } mentionProblems)
        {
            return TypedResults.ValidationProblem(mentionProblems);
        }

        var now = time.GetUtcNow();
        if (application.EditNote(note, request.Note, now, tenant.UserId, user.ApiKeyId()) is var (entry, added))
        {
            db.ApplicationEvents.Add(entry);
            await outbox.PublishAsync(new ApplicationNoteEdited(application.OrganizationId, application.Id, application.JobId, note.Id, now));
            await PublishMentionsAsync(outbox, application, note, added, now);
            try
            {
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return NoteConflict();
            }
        }

        return TypedResults.Ok(note.ToDetail());
    }

    // The author, an Owner or an Admin deletes a note (NotePolicy). Its row goes, and its text and mentions with it; the
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

        // The role in this organization: the members filter also shows the person's memberships in others.
        var role = tenant.UserId is { } userId
            ? await db.OrganizationMembers.AsNoTracking()
                .Where(m => m.OrganizationId == tenant.OrganizationId && m.UserId == userId)
                .Select(m => (OrganizationRole?)m.Role)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        if (!NotePolicy.MayDelete(note, tenant.UserId, user.ApiKeyId(), role))
        {
            return TypedResults.Problem(
                title: "Author, owners and admins only",
                detail: "Only the note's author, an owner or an admin can delete a note.",
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

    // Every mention names a member of this organization. Narrowed to it explicitly: the members filter also shows the
    // signed-in person's memberships in other organizations.
    private static async Task<Dictionary<string, string[]>?> MentionProblemsAsync(
        ScreenHireDbContext db, ITenantContext tenant, string text, CancellationToken cancellationToken)
    {
        var mentioned = NoteMentions.Parse(text);
        if (mentioned.Count == 0)
        {
            return null;
        }

        var members = await db.OrganizationMembers.AsNoTracking()
            .CountAsync(m => m.OrganizationId == tenant.OrganizationId && mentioned.Contains(m.UserId), cancellationToken);
        return members == mentioned.Count ? null : ValidationExtensions.Problem("note", "A mention must name a member of this organization.");
    }

    // A card and an email for each teammate newly mentioned, never for the note's own author.
    private static async Task PublishMentionsAsync(
        IDbContextOutbox<ScreenHireDbContext> outbox, JobApplication application, ApplicationNote note,
        IEnumerable<ApplicationNoteMention> mentions, DateTimeOffset now)
    {
        foreach (var mention in mentions.Where(m => m.UserId != note.AuthorUserId))
        {
            await outbox.PublishAsync(new TeammateMentioned(
                application.OrganizationId, application.Id, application.JobId, note.Id, mention.Id, mention.UserId, note.AuthorUserId, now));
        }
    }

    private static async Task<string?> ApiKeyNameAsync(ScreenHireDbContext db, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        user.ApiKeyId() is { } keyId
            ? await db.ApiKeys.AsNoTracking().Where(k => k.Id == keyId).Select(k => k.Name).SingleOrDefaultAsync(cancellationToken)
            : null;

    // A person or an API key tags an application. A tag it already has (ignoring case) changes nothing. The worker's
    // AddTag action writes the same row, so a clash is a 409, as a move is.
    private static async Task<Results<Ok<ApplicationDetail>, NotFound, ValidationProblem, ProblemHttpResult>> AddApplicationTag(
        Guid applicationId,
        AddApplicationTagRequest request,
        IValidator<AddApplicationTagRequest> validator,
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

        return await ChangeTagsAsync(
            applicationId, outbox, time, (application, now) => application.AddTag(request.Tag, now, tenant.UserId, actorApiKeyId: user.ApiKeyId()),
            cancellationToken);
    }

    // Takes a tag off. A tag the application does not have changes nothing. Removing is undone by adding the tag again.
    private static async Task<Results<Ok<ApplicationDetail>, NotFound, ValidationProblem, ProblemHttpResult>> RemoveApplicationTag(
        Guid applicationId,
        string? tag,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        ClaimsPrincipal user,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Trim().Length > JobApplication.MaxTagLength)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("tag", $"Name the tag to remove, in at most {JobApplication.MaxTagLength} characters."));
        }

        return await ChangeTagsAsync(
            applicationId, outbox, time, (application, now) => application.RemoveTag(tag, now, tenant.UserId, user.ApiKeyId()), cancellationToken);
    }

    private static async Task<Results<Ok<ApplicationDetail>, NotFound, ValidationProblem, ProblemHttpResult>> ChangeTagsAsync(
        Guid applicationId,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TimeProvider time,
        Func<JobApplication, DateTimeOffset, ApplicationEvent?> change,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var application = await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        if (application is null)
        {
            return TypedResults.NotFound();
        }

        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == application.CandidateId, cancellationToken);
        var now = time.GetUtcNow();
        if (change(application, now) is { } changed)
        {
            db.ApplicationEvents.Add(changed);
            await outbox.PublishAsync(new ApplicationTagsChanged(application.OrganizationId, application.Id, application.JobId, changed.Id, now));
            try
            {
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return TypedResults.Problem("Someone else changed this application at the same moment; refresh and try again.", statusCode: StatusCodes.Status409Conflict);
            }
        }

        return TypedResults.Ok(application.ToDetail(
            job, candidate, await FilesOfAsync(db, application.Id, cancellationToken), await FollowUpsOfAsync(db, application.Id, cancellationToken)));
    }

    private static async Task<Results<Ok<List<ApplicationTimelineEntry>>, NotFound>> GetApplicationTimeline(
        Guid applicationId, ClaimsPrincipal user, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        // A key sees no integrity assessments here either: not even that one ran, or when (IntegritySummariesAsync).
        var withIntegrity = !user.IsApiKey();
        var events = await db.ApplicationEvents.AsNoTracking()
            .Where(e => e.ApplicationId == applicationId)
            .Where(e => withIntegrity || e.Type != ApplicationEventType.IntegrityAssessed)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
        // A NoteAdded entry shows its note's latest text, or none once it was deleted; a key's change shows the key's name
        // (a revoked key keeps its name).
        var notes = await db.ApplicationNotes.AsNoTracking().IgnoreAutoIncludes()
            .Where(n => n.ApplicationId == applicationId)
            .ToDictionaryAsync(n => n.Id, cancellationToken);
        var keyIds = events.Select(e => e.Data.ApiKeyId).OfType<Guid>().Distinct().ToList();
        var keyNames = keyIds.Count == 0
            ? []
            : await db.ApiKeys.AsNoTracking().Where(k => keyIds.Contains(k.Id)).ToDictionaryAsync(k => k.Id, k => k.Name, cancellationToken);
        return TypedResults.Ok(events.Select(e => e.ToTimelineEntry(
            e.Data.NoteId is { } noteId ? notes.GetValueOrDefault(noteId) : null,
            e.Data.ApiKeyId is { } keyId ? keyNames.GetValueOrDefault(keyId) : null)).ToList());
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

    private static async Task<List<QuestionnaireRequest>> FollowUpsOfAsync(ScreenHireDbContext db, Guid applicationId, CancellationToken cancellationToken) =>
        await db.QuestionnaireRequests.AsNoTracking()
            .Where(r => r.ApplicationId == applicationId)
            .OrderBy(r => r.FirstSentAt)
            .ThenBy(r => r.Id)
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
