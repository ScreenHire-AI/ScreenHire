using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// A follow-up questionnaire's careers page reads and answers it through these routes (workflow-engine.md §8). The token
/// in the path is the signed, time-limited link a SendQuestionnaire action emailed (<see cref="QuestionnaireLinks"/>).
/// Anonymous, rate limited per candidate address like the apply flow, and called by the careers server.
/// </summary>
/// <remarks>
/// The slug's organization becomes the tenant, as on every public route, and the token must name that organization: a
/// token for another organization, or one that is forged, damaged or long past its expiry, is a 404. An answered link is
/// a 409, an expired one or one whose questionnaire is no longer open a 410, and the GET says which, so the page can say
/// so plainly. Only the answers are collected: no device check, telemetry or file (CLAUDE.md non-negotiable 4).
/// </remarks>
internal static class PublicQuestionnaireEndpoints
{
    public static IEndpointRouteBuilder MapPublicQuestionnaireEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/public/organizations/{organizationSlug}/questionnaires/{token}")
            .WithTags("Public careers")
            .AllowAnonymous()
            .RequireRateLimiting(ApplyOptions.RateLimitPolicy);

        group.MapGet("", GetPublicQuestionnaire).WithName("GetPublicQuestionnaire")
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPost("", AnswerPublicQuestionnaire).WithName("AnswerPublicQuestionnaire")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .Produces(StatusCodes.Status429TooManyRequests);
        return app;
    }

    private static async Task<Results<Ok<PublicQuestionnaire>, NotFound>> GetPublicQuestionnaire(
        string organizationSlug,
        string token,
        ScreenHireDbContext db,
        TenantContext tenant,
        QuestionnaireLinks links,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(organizationSlug, token, db, tenant, links, tracked: false, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        var status = found.StatusAt(time.GetUtcNow());
        return TypedResults.Ok(new PublicQuestionnaire(
            found.Organization.ToPublic(),
            found.Job.Title,
            found.Questionnaire?.Name ?? found.Request.QuestionnaireName,
            status,
            status == PublicQuestionnaireStatus.Open ? [.. found.Questionnaire!.Questions.Select(q => q.ToPublic(_ => []))] : []));
    }

    // One transaction: the answers on the request, its QuestionnaireCompleted timeline entry, the RunAutomations that fires
    // the trigger (keyed on that entry, and deduplicated in the worker) and the event that refreshes open dashboards.
    private static async Task<Results<NoContent, NotFound, ValidationProblem, ProblemHttpResult>> AnswerPublicQuestionnaire(
        string organizationSlug,
        string token,
        PublicQuestionnaireAnswers request,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        QuestionnaireLinks links,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        if (await FindAsync(organizationSlug, token, db, tenant, links, tracked: true, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        switch (found.StatusAt(now))
        {
            case PublicQuestionnaireStatus.Answered:
                return Answered();
            case PublicQuestionnaireStatus.Expired:
                return TypedResults.Problem(
                    title: "Link expired", detail: "This link has expired. Ask the hiring team to send it again.", statusCode: StatusCodes.Status410Gone);
            case PublicQuestionnaireStatus.Closed:
                return TypedResults.Problem(
                    title: "No longer open", detail: "These questions are no longer open.", statusCode: StatusCodes.Status410Gone);
        }

        var questionnaire = found.Questionnaire!;
        var answers = request.Answers?.Where(a => a is not null).ToList() ?? [];
        var errors = ApplicationForm.Validate(questionnaire.Questions, answers)
            .GroupBy(e => $"answers.{e.QuestionId}")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var entry = found.Request.Complete(questionnaire, answers, now);
        db.ApplicationEvents.Add(entry);
        var application = found.Application;
        await outbox.PublishAsync(new RunAutomations(
            application.OrganizationId, application.Id, TriggerType.QuestionnaireCompleted, StageId: null, entry.Id, now,
            QuestionnaireId: questionnaire.Id));
        await outbox.PublishAsync(new QuestionnaireAnswered(application.OrganizationId, application.Id, application.JobId, found.Request.Id, entry.Id, now));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Answered(); // the same link answered at the same moment: the first answers stand
        }

        return TypedResults.NoContent();
    }

    private static ProblemHttpResult Answered() =>
        TypedResults.Problem(title: "Already answered", detail: "These questions have been answered.", statusCode: StatusCodes.Status409Conflict);

    private static async Task<Found?> FindAsync(
        string organizationSlug,
        string token,
        ScreenHireDbContext db,
        TenantContext tenant,
        QuestionnaireLinks links,
        bool tracked,
        CancellationToken cancellationToken)
    {
        if (await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is not { } organization
            || links.Read(token) is not { } link
            || link.OrganizationId != organization.Id)
        {
            return null;
        }

        // The request on its own query: an answer changes it, and AsNoTracking anywhere in a query applies to all of it.
        var requests = tracked ? db.QuestionnaireRequests : db.QuestionnaireRequests.AsNoTracking();
        var request = await requests.SingleOrDefaultAsync(r => r.Id == link.RequestId, cancellationToken);
        if (request is null)
        {
            return null;
        }

        var row = await db.Applications.AsNoTracking()
            .Where(a => a.Id == request.ApplicationId)
            .Join(db.Jobs.AsNoTracking(), a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .SingleAsync(cancellationToken);
        return new Found(organization, request, row.Application, row.Job, row.Job.Workflow.FindFollowUp(request.QuestionnaireId));
    }

    private sealed record Found(
        Domain.Organizations.Organization Organization,
        QuestionnaireRequest Request,
        JobApplication Application,
        Job Job,
        FollowUpQuestionnaire? Questionnaire)
    {
        // Answered first, so a candidate who answered never reads "expired". Closed: the questionnaire left the workflow,
        // or the job or the application ended, so the answers would go nowhere.
        public PublicQuestionnaireStatus StatusAt(DateTimeOffset now) =>
            Request.IsCompleted ? PublicQuestionnaireStatus.Answered
            : Questionnaire is null || Job.Status == JobStatus.Closed || Job.Workflow.FindStage(Application.CurrentStageId)?.Kind.IsTerminal() == true
                ? PublicQuestionnaireStatus.Closed
            : Request.IsExpired(now) ? PublicQuestionnaireStatus.Expired
            : PublicQuestionnaireStatus.Open;
    }
}
