using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// "Send a form" on a candidate's page (workflow-engine.md section 8): a person emails the candidate one of the job's forms
/// by hand, whatever its automations do. It is the same send a <c>SendQuestionnaire</c> action makes, one request per
/// application and form, so a form sent by hand and by an automation is one form with one count. Signed-in people who may
/// email candidates only: the email goes in their name, as an email from the composer does.
/// </summary>
internal static class ApplicationFormEndpoints
{
    public static IEndpointRouteBuilder MapApplicationFormEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/applications").WithTags("Applications").RequireAuthorization();
        group.MapPost("/{applicationId:guid}/forms/{questionnaireId}/send", SendApplicationForm).WithName("SendApplicationForm")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EmailCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // With an Idempotency-Key header, the send's timeline entries take ids from the key, so a retried request finds the
    // first try's entry and sends nothing twice; without one, every request is a send.
    private static async Task<Results<Accepted<ApplicationFollowUp>, NotFound, ValidationProblem, ProblemHttpResult>> SendApplicationForm(
        Guid applicationId,
        string questionnaireId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        QuestionnaireLinks links,
        IServiceProvider services,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is { Length: 0 or > 200 })
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("Idempotency-Key", "An idempotency key is 1 to 200 characters."));
        }

        var db = outbox.DbContext;
        if (await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken) is not { } application)
        {
            return TypedResults.NotFound();
        }

        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        if (job.Workflow.FindFollowUp(questionnaireId) is not { } followUp)
        {
            return TypedResults.NotFound();
        }

        if (services.GetService<IEmailSender>() is null || !links.IsConfigured)
        {
            return TypedResults.Problem(
                "Email or the careers site is not set up here, so no form can be sent.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (job.Status == Domain.Jobs.JobStatus.Closed || application.HasEnded(job.Workflow.FindStage(application.CurrentStageId)))
        {
            return TypedResults.Problem("The application has ended, so no form can be sent.", statusCode: StatusCodes.Status409Conflict);
        }

        var sentId = idempotencyKey is null ? Guid.CreateVersion7() : AutomationEndpoints.ManualEventId(application.Id, $"send-form:{followUp.Id}", idempotencyKey);
        if (await db.ApplicationEvents.AnyAsync(e => e.Id == sentId, cancellationToken))
        {
            var sent = await db.QuestionnaireRequests.AsNoTracking()
                .SingleAsync(r => r.ApplicationId == application.Id && r.QuestionnaireId == followUp.Id, cancellationToken);
            return TypedResults.Accepted($"/v1/applications/{application.Id}", sent.ToFollowUp());
        }

        var now = time.GetUtcNow();
        if (await QuestionnaireSending.PrepareAsync(db, application, followUp, now, cancellationToken) is not { } request)
        {
            return TypedResults.Problem($"The candidate has answered \"{followUp.Name}\" already.", statusCode: StatusCodes.Status409Conflict);
        }

        db.ApplicationEvents.Add(request.SentEvent(now, automationRunId: null, tenant.UserId, sentId));
        var emailId = idempotencyKey is null ? Guid.CreateVersion7() : AutomationEndpoints.ManualEventId(application.Id, $"send-form-email:{followUp.Id}", idempotencyKey);
        await outbox.PublishAsync(new SendQuestionnaireLink(application.OrganizationId, application.Id, request.Id, emailId, tenant.UserId));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || exception.IsUniqueViolation())
        {
            // A send or an answer at the same moment: one of the two stands, and the page reads it again.
            return TypedResults.Problem("The form was sent or answered at the same moment. Look again.", statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Accepted($"/v1/applications/{application.Id}", request.ToFollowUp());
    }
}
