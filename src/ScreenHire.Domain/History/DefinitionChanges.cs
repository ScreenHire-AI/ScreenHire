using System.Globalization;
using ScreenHire.Domain.Hiring;
using static ScreenHire.Domain.History.ChangeLabels;

namespace ScreenHire.Domain.History;

/// <summary>
/// A hiring definition's changes in words. Stages, questions, scorecard criteria, automations and follow-up
/// questionnaires are matched by their stable ids (HiringWorkflow.cs), so a renamed question is one rename rather than a
/// removal and an addition. Question ids are unique across the apply questionnaire and every follow-up, so a question is
/// found wherever it sits. Parts without ids (an automation's actions and conditions) are compared inside their
/// automation and show in its details. Raw JSON never reaches a line.
/// </summary>
public static class DefinitionChanges
{
    public static IReadOnlyList<ChangeLine> Describe(HiringTemplateDefinition before, HiringTemplateDefinition after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var lines = Describe(before.Workflow, after.Workflow).ToList();
        if (PostingDefaultsDetails(before.PostingDefaults, after.PostingDefaults) is { Count: > 0 } details)
        {
            lines.Add(ChangeLine.Plain("changed the job defaults", details));
        }

        return lines;
    }

    public static IReadOnlyList<ChangeLine> Describe(HiringWorkflow before, HiringWorkflow after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var lines = new List<ChangeLine>();
        Stages(before, after, lines);
        Questions(before, after, lines);
        Automations(before, after, lines);
        if (before.IntegrityPolicy.AiUse != after.IntegrityPolicy.AiUse)
        {
            lines.Add(ChangeLine.Of($"changed AI use from {AiUse(before.IntegrityPolicy.AiUse)} to ", AiUse(after.IntegrityPolicy.AiUse)));
        }

        return lines;
    }

    private static void Stages(HiringWorkflow before, HiringWorkflow after, List<ChangeLine> lines)
    {
        Match(
            before.Stages, after.Stages, s => s.Id,
            added: s => lines.Add(ChangeLine.Of("added the stage ", s.Name, details: StageDetails(s))),
            removed: s => lines.Add(ChangeLine.Of("removed the stage ", s.Name)),
            kept: (a, b) => Stage(a, b, lines));
        if (Reordered(before.Stages, after.Stages, s => s.Id))
        {
            lines.Add(ChangeLine.Plain("reordered the stages"));
        }
    }

    private static void Stage(WorkflowStage a, WorkflowStage b, List<ChangeLine> lines)
    {
        if (a.Name != b.Name)
        {
            lines.Add(ChangeLine.Of($"renamed the stage from {a.Name} to ", b.Name));
        }

        if (a.Kind != b.Kind)
        {
            lines.Add(ChangeLine.Of("changed the kind of ", b.Name, $" from {ChangeLabels.Stage(a.Kind)} to {ChangeLabels.Stage(b.Kind)}"));
        }

        switch (a.SlaDays, b.SlaDays)
        {
            case (null, { } days):
                lines.Add(ChangeLine.Of($"set a time limit of {Days(days)} on ", b.Name));
                break;
            case ({ }, null):
                lines.Add(ChangeLine.Of("removed the time limit from ", b.Name));
                break;
            case ({ } from, { } to) when from != to:
                lines.Add(ChangeLine.Of("changed the time limit of ", b.Name, $" from {Days(from)} to {Days(to)}"));
                break;
        }

        switch (a.Scorecard, b.Scorecard)
        {
            case (null, { } added):
                lines.Add(ChangeLine.Of("added a scorecard to ", b.Name, details: [new("Criteria", null, Join(added.Criteria.Select(c => c.Label)))]));
                break;
            case ({ } removed, null):
                lines.Add(ChangeLine.Of("removed the scorecard from ", b.Name, details: [new("Criteria", Join(removed.Criteria.Select(c => c.Label)), null)]));
                break;
            case ({ } left, { } right) when CriteriaDetails(left, right) is { Count: > 0 } details:
                lines.Add(ChangeLine.Of("changed the scorecard of ", b.Name, details: details));
                break;
        }
    }

    private static List<ChangeDetail> StageDetails(WorkflowStage stage)
    {
        var details = new List<ChangeDetail> { new("Kind", null, ChangeLabels.Stage(stage.Kind)) };
        if (stage.SlaDays is { } days)
        {
            details.Add(new("Time limit", null, Days(days)));
        }

        if (stage.Scorecard is { } scorecard)
        {
            details.Add(new("Scorecard", null, Join(scorecard.Criteria.Select(c => c.Label))));
        }

        return details;
    }

    private static List<ChangeDetail> CriteriaDetails(Scorecard before, Scorecard after)
    {
        var details = new List<ChangeDetail>();
        Match(
            before.Criteria, after.Criteria, c => c.Id,
            added: c => details.Add(new(c.Label, null, Criterion(c))),
            removed: c => details.Add(new(c.Label, Criterion(c), null)),
            kept: (a, b) =>
            {
                if (a != b)
                {
                    details.Add(new(b.Label, Criterion(a), Criterion(b)));
                }
            });
        if (Reordered(before.Criteria, after.Criteria, c => c.Id))
        {
            details.Add(new("Order", Join(before.Criteria.Select(c => c.Label)), Join(after.Criteria.Select(c => c.Label))));
        }

        return details;
    }

    private static string Criterion(ScorecardCriterion criterion) =>
        criterion.Guidance is { } guidance ? $"{criterion.Label}. {guidance}" : criterion.Label;

    private static void Questions(HiringWorkflow before, HiringWorkflow after, List<ChangeLine> lines)
    {
        // A follow-up questionnaire added or removed whole is one line, with its questions in the details.
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        Match(
            before.FollowUpQuestionnaires ?? [], after.FollowUpQuestionnaires ?? [], f => f.Id,
            added: f =>
            {
                skipped.UnionWith(f.Questions.Select(q => q.Id));
                lines.Add(ChangeLine.Of("added the follow-up questionnaire ", f.Name, details: [new("Questions", null, Join(f.Questions.Select(q => q.Label)))]));
            },
            removed: f =>
            {
                skipped.UnionWith(f.Questions.Select(q => q.Id));
                lines.Add(ChangeLine.Of("removed the follow-up questionnaire ", f.Name));
            },
            kept: (a, b) =>
            {
                if (a.Name != b.Name)
                {
                    lines.Add(ChangeLine.Of($"renamed the follow-up questionnaire from {a.Name} to ", b.Name));
                }

                if (Reordered(a.Questions, b.Questions, q => q.Id))
                {
                    lines.Add(ChangeLine.Of("reordered the questions of ", b.Name));
                }
            });

        Match(
            [.. AllQuestions(before).Where(q => !skipped.Contains(q.Id))],
            [.. AllQuestions(after).Where(q => !skipped.Contains(q.Id))],
            q => q.Id,
            added: q => lines.Add(ChangeLine.Of("added ", q.Label, details: [.. QuestionFields(q).Select(f => new ChangeDetail(f.Label, null, f.Value))])),
            removed: q => lines.Add(ChangeLine.Of("removed ", q.Label, details: [.. QuestionFields(q).Select(f => new ChangeDetail(f.Label, f.Value, null))])),
            kept: (a, b) => Question(a, b, lines));
        if (Reordered(before.Questionnaire.Questions, after.Questionnaire.Questions, q => q.Id))
        {
            lines.Add(ChangeLine.Plain("reordered the questions"));
        }
    }

    private static IEnumerable<Question> AllQuestions(HiringWorkflow workflow) =>
        workflow.Questionnaire.Questions.Concat((workflow.FollowUpQuestionnaires ?? []).SelectMany(f => f.Questions));

    private static void Question(Question a, Question b, List<ChangeLine> lines)
    {
        if (a.Label != b.Label)
        {
            lines.Add(ChangeLine.Of($"renamed the question from {a.Label} to ", b.Label));
        }

        if (a.Type != b.Type)
        {
            lines.Add(ChangeLine.Of(
                "changed ", b.Label, $" from {ChangeLabels.Question(a.Type)} to {ChangeLabels.Question(b.Type)}",
                [new("Answer", ChangeLabels.Question(a.Type), ChangeLabels.Question(b.Type)), new("Required", OnOff(a.Required), OnOff(b.Required))]));
        }

        if (a.Required != b.Required)
        {
            lines.Add(ChangeLine.Of("made ", b.Label, b.Required ? " required" : " optional"));
        }

        // Everything else about the question: its help text, options and limits.
        var left = QuestionFields(a).Skip(2).ToDictionary(f => f.Label, f => f.Value, StringComparer.Ordinal);
        var right = QuestionFields(b).Skip(2).ToDictionary(f => f.Label, f => f.Value, StringComparer.Ordinal);
        var details = left.Keys.Union(right.Keys)
            .Select(label => new ChangeDetail(label, left.GetValueOrDefault(label), right.GetValueOrDefault(label)))
            .Where(d => d.Before != d.After)
            .ToList();
        if (details.Count > 0)
        {
            lines.Add(ChangeLine.Of("changed ", b.Label, details: details));
        }
    }

    // The answer kind and Required first: Question() compares the rest.
    private static IEnumerable<(string Label, string Value)> QuestionFields(Question q)
    {
        yield return ("Answer", ChangeLabels.Question(q.Type));
        yield return ("Required", OnOff(q.Required));
        if (q.HelpText is { Length: > 0 } help)
        {
            yield return ("Help text", help);
        }

        if (q.Options is { Count: > 0 } options)
        {
            yield return ("Options", Join(options.Select(o => o.Label)));
        }

        if (q.Knockout is { } knockout)
        {
            var rule = $"Answering {(knockout.DisqualifyingAnswer ? "yes" : "no")} makes the candidate ineligible";
            yield return ("Eligibility check", knockout.Reason is { Length: > 0 } reason ? $"{rule}. {reason}" : rule);
        }

        if (q.Min is { } min)
        {
            yield return ("Lowest", Number(min));
        }

        if (q.Max is { } max)
        {
            yield return ("Highest", Number(max));
        }

        if (q.MaxLength is { } length)
        {
            yield return ("Longest answer", $"{length.ToString(CultureInfo.InvariantCulture)} characters");
        }

        if (q.AcceptedFileTypes is { Count: > 0 } types)
        {
            yield return ("File types", Join(types));
        }
    }

    private static void Automations(HiringWorkflow before, HiringWorkflow after, List<ChangeLine> lines)
    {
        Match(
            before.Automations, after.Automations, a => a.Id,
            added: a => lines.Add(ChangeLine.Of("added the automation ", a.Name, details: [.. AutomationFields(a, after).Select(f => new ChangeDetail(f.Label, null, f.Value))])),
            removed: a => lines.Add(ChangeLine.Of("removed the automation ", a.Name)),
            kept: (a, b) =>
            {
                if (a.Name != b.Name)
                {
                    lines.Add(ChangeLine.Of($"renamed the automation from {a.Name} to ", b.Name));
                }

                if (a.Enabled != b.Enabled)
                {
                    lines.Add(ChangeLine.Of(b.Enabled ? "turned on " : "turned off ", b.Name));
                }

                var left = AutomationFields(a, before).ToDictionary(f => f.Label, f => f.Value, StringComparer.Ordinal);
                var right = AutomationFields(b, after).ToDictionary(f => f.Label, f => f.Value, StringComparer.Ordinal);
                var details = left.Keys.Union(right.Keys)
                    .Select(label => new ChangeDetail(label, left.GetValueOrDefault(label), right.GetValueOrDefault(label)))
                    .Where(d => d.Before != d.After)
                    .ToList();
                if (details.Count > 0)
                {
                    lines.Add(ChangeLine.Of("changed the automation ", b.Name, details: details));
                }
            });
        if (Reordered(before.Automations, after.Automations, a => a.Id))
        {
            lines.Add(ChangeLine.Plain("reordered the automations"));
        }
    }

    private static IEnumerable<(string Label, string Value)> AutomationFields(Automation automation, HiringWorkflow workflow)
    {
        yield return ("When", Trigger(automation.Trigger, workflow));
        if (automation.Conditions is { Rules.Count: > 0 } conditions)
        {
            var rules = Join(conditions.Rules.Select(r => Condition(r, workflow)), "; ");
            yield return ("If", conditions.Rules.Count == 1 ? rules : $"{(conditions.Match == ConditionMatch.All ? "All" : "Any")} of {rules}");
        }

        yield return ("Then", Join(automation.Actions.Select(a => Action(a, workflow)), "; "));
        if (automation.AllowAutoReject)
        {
            yield return ("Rejects without a person", OnOff(true));
        }
    }

    private static string Trigger(AutomationTrigger trigger, HiringWorkflow workflow)
    {
        var parts = new List<string>();
        if (trigger.StageId is { } stageId)
        {
            parts.Add(workflow.FindStage(stageId)?.Name ?? stageId);
        }

        if (trigger.AfterHours is { } hours)
        {
            parts.Add(hours == 1 ? "after 1 hour" : $"after {hours.ToString(CultureInfo.InvariantCulture)} hours");
        }

        if (trigger.QuestionnaireId is { } questionnaireId)
        {
            parts.Add(workflow.FindFollowUp(questionnaireId)?.Name ?? questionnaireId);
        }

        var label = ChangeLabels.Trigger(trigger.Type);
        return parts.Count == 0 ? label : $"{label}: {Join(parts)}";
    }

    private static string Condition(AutomationCondition condition, HiringWorkflow workflow)
    {
        var fact = condition.Fact.StartsWith("answers.", StringComparison.Ordinal)
            && AllQuestions(workflow).FirstOrDefault(q => q.Id == condition.Fact["answers.".Length..]) is { } question
                ? question.Label
                : condition.Fact;
        var test = condition.Operator switch
        {
            ConditionOperator.EqualTo => "is",
            ConditionOperator.In => "is one of",
            ConditionOperator.Contains => "contains",
            ConditionOperator.GreaterThan => "is more than",
            ConditionOperator.GreaterThanOrEqual => "is at least",
            ConditionOperator.LessThan => "is less than",
            ConditionOperator.LessThanOrEqual => "is at most",
            ConditionOperator.Exists => "is given",
            _ => condition.Operator.ToString(),
        };
        var operand = condition.Operator switch
        {
            ConditionOperator.Exists => "",
            ConditionOperator.In => $" {Join(condition.Values ?? [])}",
            _ => condition.Value is { } value ? $" {value}" : "",
        };
        return $"{(condition.Negate ? "not " : "")}{fact} {test}{operand}";
    }

    private static string Action(AutomationAction action, HiringWorkflow workflow)
    {
        var label = ChangeLabels.Action(action.Type);
        var value = action.Type switch
        {
            ActionType.SendEmail or ActionType.Reject when action.EmailTemplate is { } template =>
                EmailTemplateIds.Names.GetValueOrDefault(template) ?? "one of your templates",
            ActionType.SendQuestionnaire => workflow.FindFollowUp(action.QuestionnaireId)?.Name ?? action.QuestionnaireId,
            ActionType.MoveToStage => action.StageId is { } stageId ? workflow.FindStage(stageId)?.Name ?? stageId : null,
            ActionType.AddTag => action.Tag,
            ActionType.NotifyTeam or ActionType.CreateTask => action.Note,
            ActionType.CallWebhook or ActionType.ScheduleInterview => action.Url,
            ActionType.QueueForRejectionReview => action.Reason,
            _ => null,
        };
        if (action.Type == ActionType.Reject && action.Reason is { Length: > 0 } reason)
        {
            value = value is null ? reason : $"{value}, {reason}";
        }

        return value is { Length: > 0 } ? $"{label}: {value}" : label;
    }

    private static List<ChangeDetail> PostingDefaultsDetails(PostingDefaults before, PostingDefaults after)
    {
        var details = new List<ChangeDetail>
        {
            new("Employment type", before.EmploymentType is { } e1 ? Employment(e1) : null, after.EmploymentType is { } e2 ? Employment(e2) : null),
            new("Workplace", before.WorkplaceType is { } w1 ? Workplace(w1) : null, after.WorkplaceType is { } w2 ? Workplace(w2) : null),
            new("Department", before.Department, after.Department),
            new("Salary currency", before.SalaryCurrency, after.SalaryCurrency),
            new("Salary per", before.SalaryPeriod is { } p1 ? Period(p1) : null, after.SalaryPeriod is { } p2 ? Period(p2) : null),
            new("Open for", before.ValidForDays is { } d1 ? Days(d1) : null, after.ValidForDays is { } d2 ? Days(d2) : null),
            new("Description", RichTextToPlain.Convert(before.Description), RichTextToPlain.Convert(after.Description), ChangeValueKind.Paragraphs),
        };
        return [.. details.Where(d => d.Before != d.After)];
    }

    /// <summary>
    /// Walks <paramref name="after"/> in its order, matching each element to <paramref name="before"/>'s by id, then the
    /// elements that went. Duplicate ids, which the validator refuses, count once.
    /// </summary>
    private static void Match<T>(
        IReadOnlyList<T> before, IReadOnlyList<T> after, Func<T, string> id, Action<T> added, Action<T> removed, Action<T, T> kept)
    {
        var left = before.DistinctBy(id).ToDictionary(id, StringComparer.Ordinal);
        var right = after.DistinctBy(id).ToDictionary(id, StringComparer.Ordinal);
        foreach (var item in right.Values)
        {
            if (left.TryGetValue(id(item), out var old))
            {
                kept(old, item);
            }
            else
            {
                added(item);
            }
        }

        foreach (var item in left.Values.Where(i => !right.ContainsKey(id(i))))
        {
            removed(item);
        }
    }

    // Whether the elements both sides have are in a different order.
    private static bool Reordered<T>(IReadOnlyList<T> before, IReadOnlyList<T> after, Func<T, string> id)
    {
        var right = after.Select(id).ToHashSet(StringComparer.Ordinal);
        var left = before.Select(id).ToHashSet(StringComparer.Ordinal);
        return !before.Select(id).Where(right.Contains).SequenceEqual(after.Select(id).Where(left.Contains), StringComparer.Ordinal);
    }

    private static string Join(IEnumerable<string> values, string separator = ", ") => string.Join(separator, values);
}
