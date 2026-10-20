using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Domain.Automations;

/// <summary>
/// What an automation's conditions can see about one application at the moment its trigger fired: the facts snapshot
/// (docs/architecture/workflow-engine.md §3). The engine stores it with every run, so a replay or an audit sees exactly
/// what the conditions saw. A fact that is null or empty here is absent: <see cref="ConditionOperator.Exists"/> is false
/// and every other test fails.
/// </summary>
/// <remarks>
/// <c>answers</c>: The candidate's answers by question id; yes/no answers are <c>yes</c> or <c>no</c>, choice answers
/// are option ids, numbers use invariant digits. Empty until the careers apply flow stores answers.
/// <c>hoursInStage</c>: Whole hours between entering the current stage and the trigger.
/// <c>integrityAuthenticity</c>, <c>integrityAiAssistance</c>, <c>verificationResult</c>: Levels from the integrity
/// engine and the verification step, once they exist.
/// </remarks>
public sealed record AutomationFacts(
    IReadOnlyList<AnswerFact> Answers,
    IReadOnlyList<string> Tags,
    string? Source = null,
    int? HoursInStage = null,
    string? IntegrityAuthenticity = null,
    string? IntegrityAiAssistance = null,
    string? VerificationResult = null)
{
    public static AutomationFacts None { get; } = new([], []);

    /// <summary>
    /// The snapshot for a trigger, rebuilt from the application's append-only timeline as it stood when the trigger
    /// happened, so an evaluation that runs late (the worker was down) or again (a replay) sees the same facts.
    /// <paramref name="timeline"/> is the application's events in order; the snapshot reads those up to
    /// <paramref name="triggerEventId"/> when the timeline holds it, otherwise those at or before
    /// <paramref name="occurredAt"/> (a manual run). A <see cref="TriggerType.TimeElapsed"/> timer is keyed on its stage
    /// entry but fires later, so it reads everything at or before its due time (<paramref name="occurredAt"/>): a tag added
    /// while it waited counts. A <see cref="TriggerType.StageExited"/> trigger measures time in the stage that was left.
    /// </summary>
    public static AutomationFacts FromTimeline(
        IReadOnlyList<ApplicationEvent> timeline, Guid triggerEventId, TriggerType trigger, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        var triggerIndex = -1;
        for (var i = 0; i < timeline.Count; i++)
        {
            if (timeline[i].Id == triggerEventId)
            {
                triggerIndex = i;
            }
        }

        var seen = triggerIndex >= 0 && trigger != TriggerType.TimeElapsed
            ? timeline.Take(triggerIndex + 1).ToList()
            : [.. timeline.Where(e => e.OccurredAt <= occurredAt)];
        var entries = seen.Where(e => e.Type is ApplicationEventType.Submitted or ApplicationEventType.StageChanged).ToList();
        if (trigger == TriggerType.StageExited && triggerIndex >= 0 && entries.Count > 0 && entries[^1].Id == triggerEventId)
        {
            // The move itself starts the next stage; the stage that was exited began at the entry before it.
            entries.RemoveAt(entries.Count - 1);
        }

        var tags = new List<string>();
        foreach (var tag in seen.Where(e => e.Type == ApplicationEventType.TagAdded).Select(e => e.Data.Tag))
        {
            if (tag is not null && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(tag);
            }
        }

        int? hoursInStage = entries.Count == 0 ? null : (int)Math.Max(0, Math.Floor((occurredAt - entries[^1].OccurredAt).TotalHours));

        // Answers, the source and integrity levels join the snapshot when the apply flow and the integrity engine store
        // them; until then those facts are absent.
        return new AutomationFacts([], tags, HoursInStage: hoursInStage);
    }

    /// <summary>The values of <paramref name="fact"/>, or an empty list when the application has none.</summary>
    public IReadOnlyList<string> ValuesOf(string fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.StartsWith(FactSchema.AnswersPrefix, StringComparison.Ordinal))
        {
            var questionId = fact[FactSchema.AnswersPrefix.Length..];
            return Answers?.FirstOrDefault(a => a?.QuestionId == questionId)?.Values ?? [];
        }

        return fact switch
        {
            FactSchema.Tags => Tags ?? [],
            FactSchema.Source => One(Source),
            FactSchema.HoursInStage => HoursInStage is { } hours ? [hours.ToString(System.Globalization.CultureInfo.InvariantCulture)] : [],
            FactSchema.IntegrityAuthenticity => One(IntegrityAuthenticity),
            FactSchema.IntegrityAiAssistance => One(IntegrityAiAssistance),
            FactSchema.VerificationResult => One(VerificationResult),
            _ => [],
        };
    }

    private static IReadOnlyList<string> One(string? value) => string.IsNullOrEmpty(value) ? [] : [value];
}

/// <summary>A candidate's answer to one question. A multiple-choice answer has one value per chosen option.</summary>
public sealed record AnswerFact(string QuestionId, IReadOnlyList<string> Values);
