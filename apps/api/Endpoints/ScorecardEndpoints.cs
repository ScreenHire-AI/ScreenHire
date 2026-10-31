using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Applications;
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
/// still has their own scorecard to give reads only their own here (ADR-0013, <c>RatingReads</c>).
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

    // The signed-in reviewer's scorecard for a stage the application has been in. One per reviewer and stage: the same
    // submission again (a retried request) returns the stored scorecard and raises nothing; a different one is a 409.
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
        if (await ExistingAsync(db, application.Id, request.StageId, reviewerUserId, cancellationToken) is { } existing)
        {
            return Replay(existing, answers, request);
        }

        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        if (job.Workflow.FindStage(request.StageId) is not { Scorecard: { } card } stage)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("stageId", "The job's workflow has no scorecard for this stage."));
        }

        if (ScorecardAnswers.Check(card, answers) is { Count: > 0 } errors)
        {
            // Each problem at its answer, or at the answers when the question was left out.
            var index = request.Answers.Select((a, i) => (a.QuestionId, i)).ToDictionary(x => x.QuestionId, x => x.i, StringComparer.Ordinal);
            return TypedResults.ValidationProblem(errors
                .GroupBy(e => index.TryGetValue(e.QuestionId, out var i) ? $"answers[{i}]" : $"answers.{e.QuestionId}")
                .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()));
        }

        // A stage the application has been in: its first stage or one it was moved to. Scoring a stage it never reached
        // would record a decision about an interview that did not happen.
        var entries = await db.ApplicationEvents.AsNoTracking()
            .Where(e => e.ApplicationId == application.Id && (e.Type == ApplicationEventType.Submitted || e.Type == ApplicationEventType.StageChanged))
            .Select(e => e.Data)
            .ToListAsync(cancellationToken);
        if (application.CurrentStageId != stage.Id && !entries.Exists(d => d.ToStageId == stage.Id))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("stageId", $"The application has not been in {stage.Name} yet."));
        }

        var now = time.GetUtcNow();
        var (scorecard, entry) = application.SubmitScorecard(stage, reviewerUserId, answers, request.Recommendation, request.Note, job.WorkflowVersion, now);
        db.ApplicationScorecards.Add(scorecard);
        db.ApplicationEvents.Add(entry);
        await outbox.PublishAsync(new RunAutomations(
            application.OrganizationId, application.Id, TriggerType.ReviewerDecision, stage.Id, entry.Id, now, ActorUserId: reviewerUserId));
        await outbox.PublishAsync(new ScorecardSubmitted(
            application.OrganizationId, application.Id, application.JobId, scorecard.Id, entry.Id, now, reviewerUserId));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // The same reviewer submitted twice at once (a double click, a retry racing the first request): the unique
            // index kept one, and nothing of this request committed.
            db.ChangeTracker.Clear();
            var winner = await ExistingAsync(db, application.Id, stage.Id, reviewerUserId, cancellationToken)
                ?? throw new InvalidOperationException("A scorecard conflicted on its unique index but none is stored.", exception);
            return Replay(winner, answers, request);
        }

        return TypedResults.Created($"/v1/applications/{application.Id}/scorecards", scorecard.ToEntry());
    }

    private static Task<ApplicationScorecard?> ExistingAsync(
        ScreenHireDbContext db, Guid applicationId, string stageId, Guid reviewerUserId, CancellationToken cancellationToken) =>
        db.ApplicationScorecards.AsNoTracking()
            .SingleOrDefaultAsync(s => s.ApplicationId == applicationId && s.StageId == stageId && s.ReviewerUserId == reviewerUserId, cancellationToken);

    private static Results<Created<ScorecardEntry>, Ok<ScorecardEntry>, NotFound, ValidationProblem, ProblemHttpResult> Replay(
        ApplicationScorecard existing, IReadOnlyDictionary<string, ScorecardAnswerValue> answers, SubmitScorecardRequest request) =>
        existing.SameAs(answers, request.Recommendation, request.Note)
            ? TypedResults.Ok(existing.ToEntry())
            : TypedResults.Problem(
                "You already submitted a scorecard for this stage. A scorecard is final; add a note to say more.",
                statusCode: StatusCodes.Status409Conflict);
}
