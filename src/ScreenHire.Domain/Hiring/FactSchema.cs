using System.Globalization;

namespace ScreenHire.Domain.Hiring;

/// <summary>
/// The typed facts schema that automation conditions are checked against (docs/architecture/workflow-engine.md §3):
/// every fact a condition may name, its type, and which operators make sense for that type. The validator refuses a
/// condition the schema does not allow, and the evaluator reads values the same way, so a saved condition always means
/// one thing.
/// </summary>
/// <remarks>
/// Accommodation status and the AI-use declaration are not facts on purpose: a condition on them would hold a request
/// for accommodation or an honest disclosure against a candidate (CLAUDE.md non-negotiable 4).
/// </remarks>
public static class FactSchema
{
    public const string AnswersPrefix = "answers.";
    public const string Tags = "application.tags";
    public const string Source = "application.source";
    public const string HoursInStage = "application.hoursInStage";
    public const string IntegrityAuthenticity = "integrity.authenticity";
    public const string IntegrityAiAssistance = "integrity.aiAssistance";
    public const string VerificationResult = "verification.result";

    /// <summary>
    /// The overall recommendation of the scorecard whose submission fired a <see cref="TriggerType.ReviewerDecision"/>
    /// automation. Only that trigger has it: on any other trigger "the latest reviewer's view" would depend on which
    /// scorecard happened to come last, on whichever stage.
    /// </summary>
    public const string ReviewerRecommendation = "reviewer.recommendation";

    // Telemetry facts arrive with the integrity engine. The prefix is reserved now so the accommodation rule already
    // covers them (workflow-engine.md §2, guardrail 4).
    private const string TelemetryPrefix = "telemetry.";

    /// <summary>Facts every workflow has. Answers add one fact per question, <c>answers.{questionId}</c>.</summary>
    public static IReadOnlyList<FactDefinition> ApplicationFacts { get; } =
    [
        new(Tags, FactType.TextList, FactCategory.Application),
        new(Source, FactType.Text, FactCategory.Application),
        new(HoursInStage, FactType.Number, FactCategory.Timing),
        new(IntegrityAuthenticity, FactType.Level, FactCategory.Signal),
        new(IntegrityAiAssistance, FactType.Level, FactCategory.Signal),
        new(VerificationResult, FactType.Level, FactCategory.Signal),
        new(ReviewerRecommendation, FactType.Recommendation, FactCategory.Reviewer),
    ];

    /// <summary>The fact <paramref name="fact"/> names in a workflow with these questions, or null if there is none.</summary>
    public static FactDefinition? Find(string? fact, IReadOnlyDictionary<string, Question> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (fact is null)
        {
            return null;
        }

        if (fact.StartsWith(AnswersPrefix, StringComparison.Ordinal))
        {
            return questions.TryGetValue(fact[AnswersPrefix.Length..], out var question) && TypeOf(question) is { } type
                ? new FactDefinition(fact, type, FactCategory.Answer)
                : null;
        }

        return ApplicationFacts.FirstOrDefault(f => f.Name == fact);
    }

    /// <summary>The operators a condition may use on a fact of <paramref name="type"/>.</summary>
    public static IReadOnlySet<ConditionOperator> OperatorsFor(FactType type) => type switch
    {
        FactType.Text => TextOperators,
        FactType.TextList or FactType.MultiChoice => ListOperators,
        FactType.Number => NumberOperators,
        FactType.YesNo => YesNoOperators,
        FactType.Choice or FactType.Level or FactType.Recommendation => ChoiceOperators,
        FactType.File => ExistsOnly,
        _ => ExistsOnly,
    };

    /// <summary>An integrity or verification signal: an automation that reads one may only route (ADR-0004).</summary>
    public static bool IsSignal(string? fact) =>
        fact is not null && (fact.StartsWith("integrity.", StringComparison.Ordinal) || fact.StartsWith("verification.", StringComparison.Ordinal));

    /// <summary>A timing or telemetry fact, which the engine never evaluates for an application with an accommodation.</summary>
    public static bool IsTimingOrTelemetry(string? fact) =>
        fact is not null && (fact == HoursInStage || fact.StartsWith(TelemetryPrefix, StringComparison.Ordinal));

    /// <summary>Whether a number fact's operand parses as a number, invariantly.</summary>
    public static bool TryParseNumber(string? value, out decimal number) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out number);

    private static FactType? TypeOf(Question question) => question.Type switch
    {
        QuestionType.ShortText or QuestionType.LongText or QuestionType.Url => FactType.Text,
        QuestionType.SingleChoice => FactType.Choice,
        QuestionType.MultiChoice => FactType.MultiChoice,
        QuestionType.YesNo => FactType.YesNo,
        QuestionType.Number => FactType.Number,
        QuestionType.File => FactType.File,
        _ => null,
    };

    private static readonly HashSet<ConditionOperator> TextOperators =
        [ConditionOperator.EqualTo, ConditionOperator.In, ConditionOperator.Contains, ConditionOperator.Exists];

    private static readonly HashSet<ConditionOperator> ListOperators =
        [ConditionOperator.Contains, ConditionOperator.In, ConditionOperator.Exists];

    private static readonly HashSet<ConditionOperator> NumberOperators =
    [
        ConditionOperator.EqualTo, ConditionOperator.GreaterThan, ConditionOperator.GreaterThanOrEqual,
        ConditionOperator.LessThan, ConditionOperator.LessThanOrEqual, ConditionOperator.Exists,
    ];

    private static readonly HashSet<ConditionOperator> YesNoOperators = [ConditionOperator.EqualTo, ConditionOperator.Exists];

    private static readonly HashSet<ConditionOperator> ChoiceOperators =
        [ConditionOperator.EqualTo, ConditionOperator.In, ConditionOperator.Exists];

    private static readonly HashSet<ConditionOperator> ExistsOnly = [ConditionOperator.Exists];
}

/// <summary>One fact a condition can test.</summary>
public sealed record FactDefinition(string Name, FactType Type, FactCategory Category);

/// <summary>
/// How a fact's values read. Level facts are categorical (ADR-0004): equality only, never "greater than", until the
/// integrity engine publishes its levels.
/// </summary>
public enum FactType
{
    Text,
    TextList,
    Number,
    YesNo,
    Choice,
    MultiChoice,
    File,
    Level,

    /// <summary>A <see cref="Hiring.Recommendation"/> name, such as <c>StrongYes</c>: equality and In only.</summary>
    Recommendation,
}

public enum FactCategory
{
    Answer,
    Application,
    Timing,
    Signal,

    /// <summary>What a person on the hiring team recorded: a scorecard's recommendation.</summary>
    Reviewer,
}
