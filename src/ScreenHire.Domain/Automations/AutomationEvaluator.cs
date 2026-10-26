using ScreenHire.Domain.Hiring;

namespace ScreenHire.Domain.Automations;

/// <summary>
/// Decides which automations a trigger fires and whether their conditions hold (docs/architecture/workflow-engine.md
/// §3). A pure function of the workflow, the facts snapshot, the accommodation flag and the causal chain: no clock, no
/// database, invariant and ordinal comparisons. Replaying an event therefore gives the same result, and the simulate
/// endpoint shows exactly what the worker would do.
/// </summary>
public static class AutomationEvaluator
{
    /// <summary>
    /// How many automations may cause one another in a row. An automation's action can raise an event that fires another
    /// automation (a move fires StageEntered); after this many links the engine stops and reports an issue.
    /// </summary>
    public const int MaxDepth = 10;

    /// <summary>
    /// The enabled automations of <paramref name="workflow"/> that fire on <paramref name="trigger"/>, in definition
    /// order. A stage trigger matches only its own stage. A <see cref="TriggerType.ReviewerDecision"/> automation with no
    /// stage matches a scorecard on any stage; one with a stage matches that stage's scorecards. An
    /// <see cref="TriggerType.SlaBreached"/> automation with no stage matches any stage's time limit. A
    /// <see cref="TriggerType.QuestionnaireCompleted"/> automation matches the follow-up questionnaire it names
    /// (<paramref name="questionnaireId"/>), or any one when it names none.
    /// </summary>
    public static IReadOnlyList<Automation> Triggered(HiringWorkflow workflow, TriggerType trigger, string? stageId, string? questionnaireId = null)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        return [.. workflow.Automations.Where(a => a.Enabled && a.Trigger.Type == trigger
            && (a.Trigger.StageId == stageId || (trigger is (TriggerType.ReviewerDecision or TriggerType.SlaBreached) && a.Trigger.StageId is null))
            && (trigger != TriggerType.QuestionnaireCompleted || a.Trigger.QuestionnaireId is null || a.Trigger.QuestionnaireId == questionnaireId))];
    }

    /// <summary>
    /// Evaluates one automation against a facts snapshot. <paramref name="chain"/> holds the ids of the automations whose
    /// actions led to this trigger, nearest first: empty for an event a person or the candidate caused.
    /// </summary>
    public static AutomationEvaluation Evaluate(
        Automation automation, AutomationFacts facts, bool accommodationRequested, IReadOnlyList<string> chain)
    {
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(chain);

        if (chain.Contains(automation.Id, StringComparer.Ordinal))
        {
            return new(EvaluationOutcome.Blocked, [],
                $"Stopped a loop: this automation's own actions led back to it ({string.Join(" <- ", chain.Prepend(automation.Id))}).");
        }

        if (chain.Count >= MaxDepth)
        {
            return new(EvaluationOutcome.Blocked, [],
                $"Stopped after {MaxDepth} automations in a row caused one another ({string.Join(" <- ", chain)}).");
        }

        var rules = automation.Conditions?.Rules ?? [];

        // Guardrail 4 (workflow-engine.md §2): timing and telemetry facts are switched off for an application with an
        // accommodation, so an automation that depends on them does not run for it at all. Skipping is not held against
        // anyone: nothing else happens.
        if (accommodationRequested
            && (automation.Trigger.Type == TriggerType.TimeElapsed || rules.Any(r => FactSchema.IsTimingOrTelemetry(r.Fact))))
        {
            return new(EvaluationOutcome.Skipped, [], "Not evaluated for this application.");
        }

        var results = rules.Select(rule => new RuleResult(rule.Fact, rule.Operator, Holds(rule, facts))).ToList();
        var matched = results.Count == 0
            || (automation.Conditions!.Match == ConditionMatch.All ? results.TrueForAll(r => r.Held) : results.Exists(r => r.Held));
        return new(matched ? EvaluationOutcome.Matched : EvaluationOutcome.NotMatched, results, null);
    }

    /// <summary>
    /// One rule against the snapshot. An absent fact fails every test except a negated one. Text compares ignoring case;
    /// <see cref="ConditionOperator.Contains"/> finds the operand inside any value (a tag, an option id, a text answer);
    /// numbers compare as invariant decimals.
    /// </summary>
    public static bool Holds(AutomationCondition rule, AutomationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(facts);

        var values = facts.ValuesOf(rule.Fact);
        var held = rule.Operator switch
        {
            ConditionOperator.Exists => values.Count > 0,
            ConditionOperator.EqualTo => values.Any(v => SameValue(v, rule.Value)),
            ConditionOperator.In => values.Any(v => rule.Values?.Any(o => SameValue(v, o)) == true),
            ConditionOperator.Contains => rule.Value is { } part && values.Any(v => v.Contains(part, StringComparison.OrdinalIgnoreCase)),
            ConditionOperator.GreaterThan => Compare(values, rule.Value, c => c > 0),
            ConditionOperator.GreaterThanOrEqual => Compare(values, rule.Value, c => c >= 0),
            ConditionOperator.LessThan => Compare(values, rule.Value, c => c < 0),
            ConditionOperator.LessThanOrEqual => Compare(values, rule.Value, c => c <= 0),
            _ => false,
        };
        return held != rule.Negate;
    }

    private static bool SameValue(string value, string? operand) =>
        operand is not null
        && (FactSchema.TryParseNumber(value, out var left) && FactSchema.TryParseNumber(operand, out var right)
            ? left == right
            : string.Equals(value, operand, StringComparison.OrdinalIgnoreCase));

    private static bool Compare(IReadOnlyList<string> values, string? operand, Func<int, bool> test) =>
        FactSchema.TryParseNumber(operand, out var right)
        && values.Any(v => FactSchema.TryParseNumber(v, out var left) && test(left.CompareTo(right)));
}

/// <summary>What evaluating one automation found.</summary>
/// <remarks><c>reason</c>: Why it was skipped or blocked, in plain language; null when it matched or did not.</remarks>
public sealed record AutomationEvaluation(EvaluationOutcome Outcome, IReadOnlyList<RuleResult> Rules, string? Reason);

/// <summary>One rule's result, so an audit or a simulation shows which conditions held.</summary>
public sealed record RuleResult(string Fact, ConditionOperator Operator, bool Held);

public enum EvaluationOutcome
{
    /// <summary>The conditions held: the actions run.</summary>
    Matched,
    NotMatched,

    /// <summary>Not evaluated for this application (accommodation).</summary>
    Skipped,

    /// <summary>Refused: a loop, the depth limit, or a workflow that no longer validates.</summary>
    Blocked,
}
