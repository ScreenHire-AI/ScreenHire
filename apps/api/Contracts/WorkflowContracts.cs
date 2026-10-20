using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Api.Contracts;

/// <summary>A workflow to check, as the builder holds it before saving.</summary>
public sealed record ValidateWorkflowRequest(HiringWorkflow Workflow);

/// <summary>
/// The validator's verdict, guardrails included. 200 either way: a workflow with problems is a normal answer here, and
/// each problem has a JSON path such as <c>automations[0].actions[0]</c> the builder can point at.
/// </summary>
public sealed record WorkflowValidationResult(bool Valid, IReadOnlyList<DefinitionError> Errors);

/// <summary>
/// "Test this workflow" (workflow-engine.md §4): run a sample application through a workflow and see which automations
/// a trigger would fire. Nothing is saved or sent. <c>facts</c> is the sample application's snapshot; leave members out
/// for facts it does not have.
/// </summary>
/// <remarks>
/// <c>trigger</c>: The trigger to simulate; <c>stageId</c> for stage and time triggers.
/// <c>accommodationRequested</c>: Simulate an application with an accommodation, for which timing facts are off.
/// <c>chain</c>: Automation ids that caused this trigger, nearest first, to see where loop detection stops a chain.
/// </remarks>
public sealed record SimulateWorkflowRequest(
    HiringWorkflow Workflow,
    AutomationTrigger Trigger,
    AutomationFacts? Facts = null,
    bool AccommodationRequested = false,
    IReadOnlyList<string>? Chain = null);

/// <summary>What one automation would do. Only automations the trigger fires are listed, in definition order.</summary>
/// <remarks><c>actions</c>: What would run, in order, when the outcome is <c>Matched</c>; empty otherwise.</remarks>
public sealed record SimulatedAutomation(
    string AutomationId,
    string Name,
    EvaluationOutcome Outcome,
    IReadOnlyList<RuleResult> Rules,
    IReadOnlyList<AutomationAction> Actions,
    string? Reason);

public sealed record WorkflowSimulationResult(IReadOnlyList<SimulatedAutomation> Automations);
