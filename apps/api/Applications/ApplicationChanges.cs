using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Slack;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Applications;

/// <summary>
/// Changing an application: moving it, notes, tags and tasks. The hiring API's routes and the MCP tools both call these, so
/// a change follows the same rules whichever way it arrives. The caller has already checked the permission
/// (<see cref="HiringAuthorization"/>). Each returns the result its route answers with; the tenant and job-scope
/// filters make another organization's application, or one on a job the reader cannot see, a 404.
/// </summary>
internal static class ApplicationChanges
{
    /// <summary>
    /// Moves an application to any stage of its job's workflow. The timeline event and ApplicationMoved commit with the
    /// move; a move to the current stage commits nothing. Two people moving one application at once: the second gets
    /// 409 (optimistic concurrency on the row), never a lost update.
    /// </summary>
    public static async Task<Results<Ok<ApplicationDetail>, NotFound, ValidationProblem, ProblemHttpResult>> MoveAsync(
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

        return TypedResults.Ok(await ApplicationReads.DetailOfAsync(tenant, db, application, job, candidate, cancellationToken));
    }

    /// <summary>
    /// A note from the hiring team: the note, a timeline entry naming it with its author, and ApplicationNoteAdded so
    /// open dashboards show it. Each teammate it mentions, other than its author, gets TeammateMentioned: a card on
    /// their open dashboards and an email from the worker. It changes nothing else about the application, so it cannot
    /// race a move.
    /// </summary>
    public static async Task<Results<Created<ApplicationTimelineEntry>, NotFound, ValidationProblem>> AddNoteAsync(
        Guid applicationId,
        AddApplicationNoteRequest request,
        IValidator<AddApplicationNoteRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        ClaimsPrincipal user,
        SlackAccess slack,
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

        if (await MentionProblemsAsync(db, tenant, application.JobId, request.Note, cancellationToken) is { } mentionProblems)
        {
            return TypedResults.ValidationProblem(mentionProblems);
        }

        var now = time.GetUtcNow();
        var (note, entry) = application.AddNote(request.Note, now, tenant.UserId, user.ApiKeyId());
        db.ApplicationNotes.Add(note);
        db.ApplicationEvents.Add(entry);
        await outbox.PublishAsync(new ApplicationNoteAdded(application.OrganizationId, application.Id, application.JobId, entry.Id, now));
        await PublishMentionsAsync(outbox, slack, application, note, note.Mentions, now);
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Created(
            $"/v1/applications/{application.Id}/timeline", entry.ToTimelineEntry(note, await ApiKeyNameAsync(db, user, cancellationToken)));
    }

    /// <summary>
    /// A person or an API key tags an application. A tag it already has (ignoring case) changes nothing. The worker's
    /// AddTag action writes the same row, so a clash is a 409, as a move is.
    /// </summary>
    public static async Task<Results<Ok<ApplicationDetail>, NotFound, ValidationProblem, ProblemHttpResult>> AddTagAsync(
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
            applicationId, outbox, tenant, time, (application, now) => application.AddTag(request.Tag, now, tenant.UserId, actorApiKeyId: user.ApiKeyId()),
            cancellationToken);
    }

    /// <summary>Takes a tag off. A tag the application does not have changes nothing. Removing is undone by adding the tag again.</summary>
    public static async Task<Results<Ok<ApplicationDetail>, NotFound, ValidationProblem, ProblemHttpResult>> RemoveTagAsync(
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
            applicationId, outbox, tenant, time, (application, now) => application.RemoveTag(tag, now, tenant.UserId, user.ApiKeyId()), cancellationToken);
    }

    /// <summary>
    /// A task for the hiring team about an application, from a person or an API key: the task, a timeline entry naming
    /// who created it, and ApplicationTaskCreated so open dashboards show it. An assignee must be a member who can see
    /// the candidate (<see cref="AudienceAsync"/>), as a mention must. It changes nothing about the application.
    /// </summary>
    public static async Task<Results<Created<ApplicationTaskEntry>, NotFound, ValidationProblem>> CreateTaskAsync(
        Guid applicationId,
        CreateApplicationTaskRequest request,
        IValidator<CreateApplicationTaskRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        ClaimsPrincipal user,
        SlackAccess slack,
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

        Guid[] assignees = request.AssigneeUserId is { } assignee ? [assignee] : [];
        switch (await AudienceAsync(db, tenant, application.JobId, assignees, cancellationToken))
        {
            case JobAudience.NotMembers:
                return TypedResults.ValidationProblem(ValidationExtensions.Problem("assigneeUserId", "The assignee must be a member of this organization."));
            case JobAudience.CannotSee:
                return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                    "assigneeUserId", "The assignee must be someone who can see this candidate. Add them to the job's hiring team first."));
        }

        var now = time.GetUtcNow();
        var (task, entry) = application.AddTask(request.Note, request.AssigneeUserId, now, tenant.UserId, user.ApiKeyId());
        db.ApplicationTasks.Add(task);
        db.ApplicationEvents.Add(entry);
        await outbox.PublishAsync(new ApplicationTaskCreated(application.OrganizationId, task.Id, application.Id, application.JobId, now));
        // A task given to someone else reaches them in Slack too, when they linked it (docs/architecture/slack.md).
        if (slack.IsConfigured && task.AssigneeUserId is { } assigneeId && assigneeId != tenant.UserId)
        {
            await outbox.PublishAsync(new SendSlackDirectMessage(
                application.OrganizationId, SlackDirectMessageKind.Task, task.Id, assigneeId, application.Id, application.JobId, tenant.UserId));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Created($"/v1/applications/{application.Id}/tasks", task.ToEntry());
    }

    /// <summary>
    /// Asks a teammate for a scorecard: a Scorecard task assigned to them, a timeline entry naming who asked, and
    /// ApplicationTaskCreated. The teammate must see the job (<see cref="AudienceAsync"/>) and their role must fill in
    /// scorecards, or the task could never be done; the scorecard must be in the library and not archived, or be the
    /// current stage's own. Asking again while the same request is open, or for a scorecard they already gave, is a 409.
    /// </summary>
    public static async Task<Results<Created<ApplicationTaskEntry>, NotFound, ValidationProblem, ProblemHttpResult>> RequestScorecardAsync(
        Guid applicationId,
        RequestScorecardRequest request,
        IValidator<RequestScorecardRequest> validator,
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

        switch (await AudienceAsync(db, tenant, application.JobId, [request.AssigneeUserId], cancellationToken))
        {
            case JobAudience.NotMembers:
                return TypedResults.ValidationProblem(ValidationExtensions.Problem("assigneeUserId", "Ask a member of this organization."));
            case JobAudience.CannotSee:
                return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                    "assigneeUserId", "Ask someone who can see this candidate. Add them to the job's hiring team first."));
        }

        var role = await RoleAccess.ForMemberAsync(db, application.OrganizationId, request.AssigneeUserId, cancellationToken);
        if (role is null || !role.Permissions.Contains(Permission.FillInScorecards))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "assigneeUserId", "Their role does not fill in scorecards. Ask someone whose role does."));
        }

        var key = request.ScorecardTemplateId;
        var workflow = await db.Jobs.AsNoTracking().Where(j => j.Id == application.JobId).Select(j => j.Workflow).SingleAsync(cancellationToken);
        if (await ScorecardReferences.ForJobAsync(db, workflow, application.CurrentStageId, key, cancellationToken) is not { Archived: false } scorecard)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "scorecardTemplateId", "This scorecard is not in the library, or it is archived. Choose another."));
        }

        if (await db.ApplicationTasks.AnyAsync(
            t => t.ApplicationId == application.Id && t.Kind == ApplicationTaskKind.Scorecard && t.AssigneeUserId == request.AssigneeUserId
                && t.ScorecardTemplateId == key && t.CompletedAt == null,
            cancellationToken))
        {
            return TypedResults.Problem(
                title: "Already asked", detail: "They already have this scorecard to fill in for this candidate.", statusCode: StatusCodes.Status409Conflict);
        }

        // One given before scorecards kept their key was its stage's own, so it counts when that stage's came from this key.
        var theirs = await db.ApplicationScorecards.AsNoTracking()
            .Where(s => s.ApplicationId == application.Id && s.ReviewerUserId == request.AssigneeUserId)
            .Select(s => new { s.StageId, s.ScorecardTemplateId })
            .ToListAsync(cancellationToken);
        if (theirs.Exists(s => s.ScorecardTemplateId == key
            || (s.ScorecardTemplateId is null && workflow.FindStage(s.StageId)?.ScorecardTemplateId == key)))
        {
            return TypedResults.Problem(
                title: "Already given", detail: "They already gave this scorecard for this candidate.", statusCode: StatusCodes.Status409Conflict);
        }

        var now = time.GetUtcNow();
        var (task, entry) = application.RequestScorecard(key, scorecard.Card.Name, request.AssigneeUserId, request.Note, now, tenant.UserId, user.ApiKeyId());
        db.ApplicationTasks.Add(task);
        db.ApplicationEvents.Add(entry);
        await outbox.PublishAsync(new ApplicationTaskCreated(application.OrganizationId, task.Id, application.Id, application.JobId, now));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Created($"/v1/applications/{application.Id}/tasks", task.ToEntry());
    }

    /// <summary>
    /// Every mention a note adds names a member of this organization who can see the candidate (<see cref="AudienceAsync"/>),
    /// so a mention never points someone at a candidate they cannot open. One it already had may name someone who has
    /// left since, or lost the job, and stays, so an edit never fails for it.
    /// </summary>
    public static async Task<Dictionary<string, string[]>?> MentionProblemsAsync(
        ScreenHireDbContext db, ITenantContext tenant, Guid jobId, string text, CancellationToken cancellationToken, ApplicationNote? existing = null)
    {
        var mentioned = NoteMentions.Parse(text).Except(existing?.Mentions.Select(m => m.UserId) ?? []).ToList();
        return await AudienceAsync(db, tenant, jobId, mentioned, cancellationToken) switch
        {
            JobAudience.NotMembers => ValidationExtensions.Problem("note", "A mention must name a member of this organization."),
            JobAudience.CannotSee => ValidationExtensions.Problem(
                "note", "A mention must name someone who can see this candidate. Add them to the job's hiring team first."),
            _ => null,
        };
    }

    /// <summary>
    /// A card, an email and a Slack message for each teammate newly mentioned, never for the note's own author. The
    /// Slack message is its own message, so a failing email never holds it up.
    /// </summary>
    public static async Task PublishMentionsAsync(
        IDbContextOutbox<ScreenHireDbContext> outbox, SlackAccess slack, JobApplication application, ApplicationNote note,
        IEnumerable<ApplicationNoteMention> mentions, DateTimeOffset now)
    {
        foreach (var mention in mentions.Where(m => m.UserId != note.AuthorUserId))
        {
            await outbox.PublishAsync(new TeammateMentioned(
                application.OrganizationId, application.Id, application.JobId, note.Id, mention.Id, mention.UserId, note.AuthorUserId, now));
            if (slack.IsConfigured)
            {
                await outbox.PublishAsync(new SendSlackDirectMessage(
                    application.OrganizationId, SlackDirectMessageKind.Mention, mention.Id, mention.UserId, application.Id, application.JobId,
                    note.AuthorUserId));
            }
        }
    }

    /// <summary>Whether a set of people can be pointed at a candidate on a job: by a mention, or as a task's assignee.</summary>
    public enum JobAudience
    {
        /// <summary>Every one of them is a member who sees the job (or there is nobody to check).</summary>
        SeeTheJob,

        /// <summary>Someone is not a member of this organization.</summary>
        NotMembers,

        /// <summary>Everyone is a member, but someone neither sees all jobs nor is on the job's hiring team.</summary>
        CannotSee,
    }

    /// <summary>
    /// Whether every user in <paramref name="userIds"/> is a member of this organization who can see the job: one who
    /// sees all jobs, or one on its hiring team (ADR-0015). Narrowed to this organization explicitly: the members filter
    /// also shows the signed-in person's memberships in other organizations.
    /// </summary>
    public static async Task<JobAudience> AudienceAsync(
        ScreenHireDbContext db, ITenantContext tenant, Guid jobId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return JobAudience.SeeTheJob;
        }

        var members = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == tenant.OrganizationId && userIds.Contains(m.UserId))
            .Select(m => new { m.Id, m.Role, m.JobAccess })
            .ToListAsync(cancellationToken);
        if (members.Count != userIds.Count)
        {
            return JobAudience.NotMembers;
        }

        var team = await db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.HiringTeam).SingleAsync(cancellationToken);
        return members.All(m => JobAccessRules.SeesAllJobs(m.Role, m.JobAccess) || team.Contains(m.Id))
            ? JobAudience.SeeTheJob
            : JobAudience.CannotSee;
    }

    private static async Task<string?> ApiKeyNameAsync(ScreenHireDbContext db, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        user.ApiKeyId() is { } keyId
            ? await db.ApiKeys.AsNoTracking().Where(k => k.Id == keyId).Select(k => k.Name).SingleOrDefaultAsync(cancellationToken)
            : null;

    private static async Task<Results<Ok<ApplicationDetail>, NotFound, ValidationProblem, ProblemHttpResult>> ChangeTagsAsync(
        Guid applicationId,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
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

        return TypedResults.Ok(await ApplicationReads.DetailOfAsync(tenant, db, application, job, candidate, cancellationToken));
    }
}
