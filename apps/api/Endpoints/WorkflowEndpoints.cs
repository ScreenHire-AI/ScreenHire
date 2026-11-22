using System.Security.Claims;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Branding;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Check and test a workflow before saving it (workflow-engine.md §4, §5). Both run the code the save and the worker run
/// (<see cref="DefinitionValidator"/>, <see cref="AutomationEvaluator"/>), so what the builder shows is what will
/// happen. Validation also checks that the organization's email templates the workflow names exist; nothing else reads or
/// writes organization data. Try it's "Send me these emails" sends a stage's templates to the signed-in person alone.
/// </summary>
internal static class WorkflowEndpoints
{
    /// <summary>The rate limiter policy on Try it's test emails: a few sends per person, so the button cannot flood an inbox.</summary>
    public const string TryItEmailsRateLimitPolicy = "try-it-emails";

    /// <summary>Sends one person may make in <see cref="TryItEmailsWindow"/>.</summary>
    public const int TryItEmailsPermits = 5;

    public static readonly TimeSpan TryItEmailsWindow = TimeSpan.FromMinutes(10);

    /// <summary>The test emails' validator and their rate limit, per signed-in person.</summary>
    public static IServiceCollection AddScreenHireTryIt(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<TryItEmailsRequest>, TryItEmailsRequestValidator>();
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(TryItEmailsRateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = TryItEmailsPermits, Window = TryItEmailsWindow })));
        return services;
    }

    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/workflows").WithTags("Workflows").RequireAuthorization();
        group.MapPost("/validate", ValidateWorkflow).WithName("ValidateWorkflow")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/simulate", SimulateWorkflow).WithName("SimulateWorkflow")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        // Every member may: it reaches only their own inbox, and the rate limit keeps it to a few sends.
        group.MapPost("/test-emails", SendTryItEmails).WithName("SendTryItEmails")
            .RequireAntiforgeryToken()
            .RequireRateLimiting(TryItEmailsRateLimitPolicy)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return app;
    }

    private static async Task<Ok<WorkflowValidationResult>> ValidateWorkflow(
        ValidateWorkflowRequest request, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var errors = await TemplateReferences.ValidateAsync(db, request.Workflow, cancellationToken);
        return TypedResults.Ok(new WorkflowValidationResult(errors.Count == 0, errors));
    }

    // The workflow must be valid first: simulating a definition the save would refuse would show actions that can
    // never run, such as a verification-driven rejection.
    private static Results<Ok<WorkflowSimulationResult>, ValidationProblem> SimulateWorkflow(SimulateWorkflowRequest request)
    {
        if (DefinitionValidator.Validate(request.Workflow).ToProblems("workflow") is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (request.Trigger is not { } trigger || !Enum.IsDefined(trigger.Type))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("trigger", "A trigger with a known type is required."));
        }

        var facts = request.Facts ?? AutomationFacts.None;
        var chain = request.Chain?.Where(id => id is not null).ToList() ?? [];
        var results = AutomationEvaluator.Triggered(request.Workflow, trigger.Type, trigger.StageId, trigger.QuestionnaireId)
            .Select(automation =>
            {
                var evaluation = AutomationEvaluator.Evaluate(automation, facts, request.AccommodationRequested, chain);
                return new SimulatedAutomation(
                    automation.Id,
                    automation.Name,
                    evaluation.Outcome,
                    evaluation.Rules,
                    evaluation.Outcome == EvaluationOutcome.Matched ? automation.Actions : [],
                    evaluation.Reason);
            })
            .ToList();
        return TypedResults.Ok(new WorkflowSimulationResult(results));
    }

    // The templates as the organization words them now, with the sample candidate (Jordan Lee), to the signed-in person's
    // own address and nobody else's, each subject marked [Test]. Sent from the API, as the template editor's test is, so the
    // person sees a failure at once. Any template may go, the rejection and form emails included, because Try it shows them.
    private static async Task<Results<Ok<TryItEmailsSent>, ValidationProblem, NotFound>> SendTryItEmails(
        TryItEmailsRequest request,
        IValidator<TryItEmailsRequest> validator,
        ScreenHireDbContext db,
        EmailTemplateStore store,
        EmailTemplates renderer,
        EmailHeaders headers,
        ITenantContext tenant,
        IServiceProvider services,
        IOptions<DomainOptions> domains,
        CancellationToken cancellationToken)
    {
        if (services.GetService<IEmailSender>() is not { } sender)
        {
            return TypedResults.NotFound(); // no email sender is configured, so Try it hides the button
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var templates = new List<OrganizationEmailTemplate>();
        foreach (var id in request.TemplateIds.Distinct(StringComparer.Ordinal))
        {
            if (await store.FindAsync(db, id, cancellationToken) is not { } template)
            {
                return TypedResults.ValidationProblem(ValidationExtensions.Problem("templateIds", "One of these emails no longer exists."));
            }

            templates.Add(template);
        }

        var to = await db.Users.AsNoTracking().Where(u => u.Id == tenant.UserId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken);
        if (to is null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("", "Your account has no email address."));
        }

        var organization = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == tenant.OrganizationId, cancellationToken);
        var header = await headers.ForAsync(db, organization, cancellationToken);
        foreach (var template in templates)
        {
            var message = EmailEndpoints.WithPortalFooter(
                renderer.Render(template.Content, to, EmailTemplateModel.Sample(organization.Name), template.Id, header), domains.Value, organization.Slug);
            await sender.SendAsync(message with { Subject = $"[Test] {message.Subject}" }, cancellationToken);
        }

        return TypedResults.Ok(new TryItEmailsSent(to, templates.Count));
    }
}
