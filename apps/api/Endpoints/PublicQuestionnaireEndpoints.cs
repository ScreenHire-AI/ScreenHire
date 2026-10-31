using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Branding;
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
/// so plainly. Only the answers are collected: no device check, telemetry or file (CLAUDE.md non-negotiable 4). The
/// candidate may save their answers to finish later through the same link (<c>/draft</c>); a saved draft raises no event
/// and runs no automation, so a knockout answer in it never ends anything before the candidate sends the answers.
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
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPut("/draft", SavePublicQuestionnaireDraft).WithName("SavePublicQuestionnaireDraft")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status429TooManyRequests);
        return app;
    }

    private static async Task<Results<Ok<PublicQuestionnaire>, NotFound>> GetPublicQuestionnaire(
        string organizationSlug,
        string token,
        ScreenHireDbContext db,
        TenantContext tenant,
        QuestionnaireLinks links,
        IOptions<DomainOptions> domains,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(organizationSlug, token, db, tenant, links, tracked: false, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        var status = found.StatusAt(time.GetUtcNow());
        var open = status == PublicQuestionnaireStatus.Open;
        return TypedResults.Ok(new PublicQuestionnaire(
            await found.Organization.ToPublicAsync(db, domains.Value, cancellationToken),
            found.Job.Title,
            found.Questionnaire?.Name ?? found.Request.QuestionnaireName,
            status,
            open ? [.. found.Questionnaire!.Questions.Select(q => q.ToPublic(_ => []))] : [],
            open && found.Questionnaire!.Sections is { Count: > 0 } sections ? sections : null,
            found.Organization.QuestionsPerPage,
            open ? found.Request.DraftAnswers : []));
    }

    // Keeps the answers to finish later, through the same link: checked for shape only, since nothing is required yet.
    private static async Task<Results<NoContent, NotFound, ValidationProblem, ProblemHttpResult>> SavePublicQuestionnaireDraft(
        string organizationSlug,
        string token,
        PublicQuestionnaireAnswers request,
        ScreenHireDbContext db,
        TenantContext tenant,
        QuestionnaireLinks links,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(organizationSlug, token, db, tenant, links, tracked: true, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        if (NotOpen(found.StatusAt(now)) is { } ended)
        {
            return ended;
        }

        var answers = request.Answers?.Where(a => a is not null).ToList() ?? [];
        var errors = ApplicationForm.ValidateDraft(found.Questionnaire!.Questions, answers)
            .GroupBy(e => $"answers.{e.QuestionId}")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        found.Request.SaveDraft(answers, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Answered or sent again at the same moment: nothing was saved, and the page asks the candidate to try again.
            return TypedResults.Problem(
                title: "Try again", detail: "The questions changed while you answered. Save your answers again.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return TypedResults.NoContent();
    }

    private static ProblemHttpResult? NotOpen(PublicQuestionnaireStatus status) => status switch
    {
        PublicQuestionnaireStatus.Answered => Answered(),
        PublicQuestionnaireStatus.Expired => TypedResults.Problem(
            title: "Link expired", detail: "This link has expired. Ask the hiring team to send it again.", statusCode: StatusCodes.Status410Gone),
        PublicQuestionnaireStatus.Closed => TypedResults.Problem(
            title: "No longer open", detail: "These questions are no longer open.", statusCode: StatusCodes.Status410Gone),
        _ => null,
    };

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
        if (NotOpen(found.StatusAt(now)) is { } ended)
        {
            return ended;
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
            // Someone else changed the request first. Answered at the same moment: the first answers stand. Sent again
            // at the same moment (a resend moves its expiry): nothing was saved, and the candidate can send again.
            var requestId = found.Request.Id;
            db.ChangeTracker.Clear();
            return await db.QuestionnaireRequests.AsNoTracking().AnyAsync(r => r.Id == requestId && r.CompletedAt != null, cancellationToken)
                ? Answered()
                : TypedResults.Problem(
                    title: "Try again", detail: "The questions changed while you answered. Send your answers again.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
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
        var request = await requests.SingleOrDefaultAsync(r => r.Id == link.RequestId && r.OrganizationId == organization.Id, cancellationToken);
        if (request is null)
        {
            return null;
        }

        var row = await db.Applications.AsNoTracking()
            .Where(a => a.Id == request.ApplicationId)
            .Join(db.Jobs.AsNoTracking(), a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null; // the application went between the two reads
        }

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
        // or the job or the application ended (or the team kept the candidate in mind), so the answers would go nowhere.
        public PublicQuestionnaireStatus StatusAt(DateTimeOffset now) =>
            Request.IsCompleted ? PublicQuestionnaireStatus.Answered
            : Questionnaire is null || Job.Status == JobStatus.Closed || Application.HasEnded(Job.Workflow.FindStage(Application.CurrentStageId))
                ? PublicQuestionnaireStatus.Closed
            : Request.IsExpired(now) ? PublicQuestionnaireStatus.Expired
            : PublicQuestionnaireStatus.Open;
    }
}
