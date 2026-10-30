using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Reviewers' scorecards on an application (workflow-engine.md §8). Tenant-scoped like every hiring route: another
/// organization's application or scorecard is a 404. Submitting raises the ReviewerDecision trigger once, through the
/// outbox, in the transaction that stores the scorecard. A scorecard holds a person's judgement of the job's questions;
/// it never shows or takes integrity output, and no automation it fires may reject (DefinitionValidator). A reviewer who
/// still has their own scorecard to give reads only their own here (ADR-0013, <c>RatingReads</c>). Asking a teammate for a
/// scorecard is <c>ApplicationChanges.RequestScorecardAsync</c>; submitting it finishes their task.
/// </summary>
internal static class ScorecardEndpoints
{
    public static IEndpointRouteBuilder MapScorecardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/applications/{applicationId:guid}/scorecards").WithTags("Applications").AllowApiKeys();
        group.MapGet("/", ListApplicationScorecards).WithName("ListApplicationScorecards").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/", SubmitScorecard).WithName("SubmitScorecard")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.FillInScorecards)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        var requests = app.MapGroup("/v1/applications/{applicationId:guid}/scorecard-requests").WithTags("Applications").AllowApiKeys();
        requests.MapPost("/", RequestScorecard).WithName("RequestScorecard")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.CreateTasks)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // Every scorecard on the application, oldest first, with who gave it. A key reads them too: they are the hiring
    // team's judgements, not integrity evidence. A reviewer with their own scorecard still to give reads only their own,
    // so the others cannot anchor them (ADR-0013); the application's rating says AfterYourScorecard meanwhile.
    private static async Task<Results<Ok<List<ScorecardEntry>>, NotFound>> ListApplicationScorecards(
        Guid applicationId, ITenantContext tenant, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var withheld = await RatingReads.WithheldAsync(db, applicationId, tenant.UserId, cancellationToken);
        var scorecards = await db.ApplicationScorecards.AsNoTracking()
            .Where(s => s.ApplicationId == applicationId && (!withheld || s.ReviewerUserId == tenant.UserId))
            .OrderBy(s => s.SubmittedAt).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(scorecards.Select(s => s.ToEntry()).ToList());
    }

    // Ask a teammate for a scorecard: a Scorecard task for them, done when they submit it (ApplicationChanges).
    private static Task<Results<Created<ApplicationTaskEntry>, NotFound, ValidationProblem, ProblemHttpResult>> RequestScorecard(
        Guid applicationId,
        RequestScorecardRequest request,
        IValidator<RequestScorecardRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        ClaimsPrincipal user,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ApplicationChanges.RequestScorecardAsync(applicationId, request, validator, outbox, tenant, user, time, cancellationToken);

    // The signed-in reviewer's scorecard: a stage's own (one the application has been in) or a library scorecard, given
    // at the stage the application is in now. One per reviewer, stage and scorecard: the same submission again (a
    // retried request) returns the stored scorecard and raises nothing; a different one is a 409. In the same save it
    // finishes the reviewer's tasks it answers (ApplicationTask.CompletesWith), never anyone else's.
    private static async Task<Results<Created<ScorecardEntry>, Ok<ScorecardEntry>, NotFound, ValidationProblem, ProblemHttpResult>> SubmitScorecard(
        Guid applicationId,
        SubmitScorecardRequest request,
        IValidator<SubmitScorecardRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        ClaimsPrincipal user,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        // A scorecard is one person's judgement, so an API key cannot give one.
        if (user.IsApiKey() || tenant.UserId is not { } reviewerUserId)
        {
            return TypedResults.Problem(
                title: "People only", detail: "A scorecard records a person's judgement; sign in to submit one.", statusCode: StatusCodes.Status403Forbidden);
        }

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

        // A retry is answered from the stored scorecard before the checks below, which read the job's current workflow:
        // an edit since the first request must not turn the same submission into an error.
        var answers = request.ToAnswers();
        var key = request.ScorecardTemplateId;
        var stageId = key is null ? request.StageId! : application.CurrentStageId;
        var given = await db.ApplicationScorecards.AsNoTracking()
            .Where(s => s.ApplicationId == application.Id && s.StageId == stageId && s.ReviewerUserId == reviewerUserId)
            .ToListAsync(cancellationToken);
        if (given.Find(s => (key is null || s.ScorecardTemplateId == key) && s.SameAs(answers, request.Recommendation, request.Note)) is { } same)
        {
            return TypedResults.Ok(same.ToEntry());
        }

        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        Scorecard card;
        if (key is null)
        {
            if (job.Workflow.FindStage(stageId) is not { Scorecard: { } copy } stage)
            {
                return TypedResults.ValidationProblem(ValidationExtensions.Problem("stageId", "The job's workflow has no scorecard for this stage."));
            }

            // A stage the application has been in: its first stage or one it was moved to. Scoring a stage it never
            // reached would record a decision about an interview that did not happen.
            var entries = await db.ApplicationEvents.AsNoTracking()
                .Where(e => e.ApplicationId == application.Id && (e.Type == ApplicationEventType.Submitted || e.Type == ApplicationEventType.StageChanged))
                .Select(e => e.Data)
                .ToListAsync(cancellationToken);
            if (application.CurrentStageId != stage.Id && !entries.Exists(d => d.ToStageId == stage.Id))
            {
                return TypedResults.ValidationProblem(ValidationExtensions.Problem("stageId", $"The application has not been in {stage.Name} yet."));
            }

            card = copy;
            key = stage.ScorecardTemplateId;
        }
        else
        {
            // The current stage's own copy when it came from this scorecard; archived ones may still be given, so a
            // teammate's open request never becomes impossible.
            if (await ScorecardReferences.ForJobAsync(db, job.Workflow, application.CurrentStageId, key, cancellationToken) is not { } found)
            {
                return TypedResults.ValidationProblem(ValidationExtensions.Problem("scorecardTemplateId", "This scorecard is not in the library."));
            }

            card = found.Card;
        }

        // One per reviewer, stage and scorecard. A scorecard given before submissions kept their scorecard (no key) was
        // the stage's own.
        if (given.Exists(s => s.ScorecardTemplateId == key || (request.ScorecardTemplateId is null && s.ScorecardTemplateId is null)))
        {
            return AlreadyGiven();
        }

        if (ScorecardAnswers.Check(card, answers) is { Count: > 0 } errors)
        {
            // Each problem at its answer, or at the answers when the question was left out.
            var index = request.Answers.Select((a, i) => (a.QuestionId, i)).ToDictionary(x => x.QuestionId, x => x.i, StringComparer.Ordinal);
            return TypedResults.ValidationProblem(errors
                .GroupBy(e => index.TryGetValue(e.QuestionId, out var i) ? $"answers[{i}]" : $"answers.{e.QuestionId}")
                .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()));
        }

        var now = time.GetUtcNow();
        var (scorecard, entry) = application.SubmitScorecard(
            stageId, card, key, reviewerUserId, answers, request.Recommendation, request.Note, job.WorkflowVersion, now);
        db.ApplicationScorecards.Add(scorecard);
        db.ApplicationEvents.Add(entry);
        await outbox.PublishAsync(new RunAutomations(
            application.OrganizationId, application.Id, TriggerType.ReviewerDecision, stageId, entry.Id, now, ActorUserId: reviewerUserId));
        await outbox.PublishAsync(new ScorecardSubmitted(application.OrganizationId, application.Id, application.JobId, scorecard.Id, entry.Id, now));

        // The tasks this scorecard is the work of: the reviewer's review, a review for the whole team, and a request for
        // this scorecard (ADR-0017 decision 11). Read under the same filters, so only this application's.
        var open = await db.ApplicationTasks
            .Where(t => t.ApplicationId == application.Id && t.CompletedAt == null
                && (t.Kind == ApplicationTaskKind.Review || t.Kind == ApplicationTaskKind.Scorecard))
            .ToListAsync(cancellationToken);
        foreach (var task in open.Where(t => t.CompletesWith(reviewerUserId, key)))
        {
            task.Complete(reviewerUserId, now);
            await outbox.PublishAsync(new ApplicationTaskCompleted(application.OrganizationId, task.Id, application.Id, application.JobId, now));
        }

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // The same reviewer submitted twice at once (a double click, a retry racing the first request): the unique
            // index kept one, and nothing of this request committed.
            db.ChangeTracker.Clear();
            var winner = await db.ApplicationScorecards.AsNoTracking()
                .Where(s => s.ApplicationId == application.Id && s.StageId == stageId && s.ReviewerUserId == reviewerUserId && s.ScorecardTemplateId == key)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("A scorecard conflicted on its unique index but none is stored.", exception);
            return winner.SameAs(answers, request.Recommendation, request.Note) ? TypedResults.Ok(winner.ToEntry()) : AlreadyGiven();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else's scorecard finished a review for the whole team between this request's read and its save, so
            // nothing of this request committed. Saying so lets the reviewer send it again, answers kept.
            return TypedResults.Problem(
                title: "Saved at the same moment",
                detail: "Someone else just saved a scorecard for this candidate. Submit yours again.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Created($"/v1/applications/{application.Id}/scorecards", scorecard.ToEntry());
    }

    private static ProblemHttpResult AlreadyGiven() =>
        TypedResults.Problem(
            "You already submitted this scorecard for this stage. A scorecard is final; add a note to say more.",
            statusCode: StatusCodes.Status409Conflict);
}
