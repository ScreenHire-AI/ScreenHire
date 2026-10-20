using Microsoft.AspNetCore.Http.HttpResults;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Check and test a workflow before saving it (workflow-engine.md §4, §5). Both run the code the save and the worker run
/// (<see cref="DefinitionValidator"/>, <see cref="AutomationEvaluator"/>), so what the builder shows is what will
/// happen. Neither reads or writes organization data.
/// </summary>
internal static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/workflows").WithTags("Workflows").RequireAuthorization();
        group.MapPost("/validate", ValidateWorkflow).WithName("ValidateWorkflow")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/simulate", SimulateWorkflow).WithName("SimulateWorkflow")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static Ok<WorkflowValidationResult> ValidateWorkflow(ValidateWorkflowRequest request)
    {
        var errors = DefinitionValidator.Validate(request.Workflow);
        return TypedResults.Ok(new WorkflowValidationResult(errors.Count == 0, errors));
    }

    // The workflow must be valid first: simulating a definition the save would refuse would show actions that can
    // never run, such as an integrity-driven rejection.
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
        var results = AutomationEvaluator.Triggered(request.Workflow, trigger.Type, trigger.StageId)
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
}
