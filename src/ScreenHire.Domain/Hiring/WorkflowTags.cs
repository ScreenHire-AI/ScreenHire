namespace ScreenHire.Domain.Hiring;

/// <summary>
/// The tags a workflow's automations name, and renaming one of them. An automation names a tag in an Add a tag step
/// (<see cref="AutomationAction.Tag"/>) and in a condition on <c>application.tags</c> whose operand is the whole tag:
/// <see cref="ConditionOperator.EqualTo"/> or <see cref="ConditionOperator.Contains"/> with that value, or one of
/// <see cref="ConditionOperator.In"/>'s values. Tags compare ignoring case, as on applications. A rename replaces an
/// operand only when it is the old tag in full: a Contains on part of a tag ("Sen") matches whatever contains it by
/// design, so it stays as written.
/// </summary>
public static class WorkflowTags
{
    /// <summary>Every tag the workflow's automations name, each once (ignoring case), as first written.</summary>
    public static IReadOnlyList<string> Named(HiringWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var named = new List<string>();
        foreach (var automation in workflow.Automations ?? [])
        {
            foreach (var tag in TagsOf(automation))
            {
                if (!string.IsNullOrWhiteSpace(tag) && !named.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    named.Add(tag.Trim());
                }
            }
        }

        return named;
    }

    /// <summary>Whether the workflow's automations name <paramref name="tag"/>, ignoring case.</summary>
    public static bool Names(HiringWorkflow workflow, string tag) =>
        Named(workflow).Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The workflow with every place it names <paramref name="from"/> naming <paramref name="to"/> instead, or null when
    /// it names <paramref name="from"/> nowhere. An In condition that held both keeps one.
    /// </summary>
    public static HiringWorkflow? Rename(HiringWorkflow workflow, string from, string to)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        var target = to.Trim();
        if (!Names(workflow, from))
        {
            return null;
        }

        return workflow with { Automations = [.. workflow.Automations.Select(a => Rename(a, from.Trim(), target))] };
    }

    private static Automation Rename(Automation automation, string from, string to)
    {
        var actions = automation.Actions.Select(action => action.Type == ActionType.AddTag && Same(action.Tag, from) ? action with { Tag = to } : action);
        var conditions = automation.Conditions is { } c
            ? c with { Rules = [.. c.Rules.Select(rule => IsTagRule(rule) ? Rename(rule, from, to) : rule)] }
            : null;
        return automation with { Actions = [.. actions], Conditions = conditions };
    }

    private static AutomationCondition Rename(AutomationCondition rule, string from, string to) => rule.Operator switch
    {
        ConditionOperator.In when rule.Values is { } values && values.Any(v => Same(v, from)) =>
            rule with { Values = [.. values.Select(v => Same(v, from) ? to : v).Distinct(StringComparer.OrdinalIgnoreCase)] },
        ConditionOperator.EqualTo or ConditionOperator.Contains when Same(rule.Value, from) => rule with { Value = to },
        _ => rule,
    };

    private static IEnumerable<string?> TagsOf(Automation automation)
    {
        foreach (var action in automation.Actions ?? [])
        {
            if (action?.Type == ActionType.AddTag)
            {
                yield return action.Tag;
            }
        }

        foreach (var rule in automation.Conditions?.Rules ?? [])
        {
            if (rule is null || !IsTagRule(rule))
            {
                continue;
            }

            if (rule.Operator == ConditionOperator.In)
            {
                foreach (var value in rule.Values ?? [])
                {
                    yield return value;
                }
            }
            else if (rule.Operator is ConditionOperator.EqualTo or ConditionOperator.Contains)
            {
                yield return rule.Value;
            }
        }
    }

    private static bool IsTagRule(AutomationCondition rule) => rule.Fact == FactSchema.Tags;

    private static bool Same(string? a, string b) => a is not null && string.Equals(a.Trim(), b, StringComparison.OrdinalIgnoreCase);
}
