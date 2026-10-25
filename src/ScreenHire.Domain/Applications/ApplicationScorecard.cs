using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// One reviewer's scorecard for one stage of one application: an answer to each question of the job's copy of the
/// stage's <see cref="Scorecard"/> and an overall recommendation (workflow-engine.md §8). Final once submitted: a reviewer
/// gives one judgement per stage, and the <c>ScorecardSubmitted</c> timeline entry it comes with is what raises the
/// ReviewerDecision trigger, once. Nothing here reads integrity output. Its rating answers make the candidate's rating
/// (<see cref="RatingRule"/>, ADR-0013), worked out when it is read and never stored.
/// </summary>
public sealed class ApplicationScorecard : IJobScoped
{
    public const int MaxNoteLength = 2000;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    public Guid JobId { get; init; }

    /// <summary>The stage whose scorecard this is.</summary>
    public required string StageId { get; init; }

    /// <summary>The person who gave the answers. Always a person: an API key has no judgement to record.</summary>
    public Guid ReviewerUserId { get; init; }

    /// <summary>
    /// A v0.2 scorecard's ratings: one per criterion, with each criterion's label as it was. Empty on a scorecard with
    /// answers. Kept so those scorecards read as they were; they never count toward the candidate's rating.
    /// </summary>
    public IReadOnlyList<CriterionScore> Ratings { get; init; } = [];

    /// <summary>
    /// One answer per question, in the scorecard's order, each with the question as it was asked: a later workflow change
    /// cannot rewrite what the reviewer was asked. A question left unanswered has an answer with no value.
    /// </summary>
    public IReadOnlyList<ScorecardAnswer> Answers { get; init; } = [];

    public Recommendation Recommendation { get; init; }

    public string? Note { get; init; }

    /// <summary>The job's workflow version the scorecard was filled under.</summary>
    public int WorkflowVersion { get; init; }

    /// <summary>The <c>ScorecardSubmitted</c> timeline entry: the ReviewerDecision trigger's event.</summary>
    public Guid TimelineEventId { get; init; }

    public DateTimeOffset SubmittedAt { get; init; }

    /// <summary>
    /// Whether a submission says exactly what this scorecard says, so a retried request returns it rather than
    /// conflicting. Answers compare by question after the same tidying a submission gets, the note after trimming.
    /// </summary>
    public bool SameAs(IReadOnlyDictionary<string, ScorecardAnswerValue> answers, Recommendation recommendation, string? note)
    {
        ArgumentNullException.ThrowIfNull(answers);
        return Recommendation == recommendation
            && string.Equals(Note, Normalize(note), StringComparison.Ordinal)
            && answers.Keys.All(id => Answers.Any(a => a.QuestionId == id))
            && Answers.All(a => a.Same(ScorecardAnswers.Tidy(answers.GetValueOrDefault(a.QuestionId))));
    }

    internal static string? Normalize(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}

/// <summary>A criterion's rating on a v0.2 scorecard; <c>rating</c> is null when the reviewer did not rate it.</summary>
public sealed record CriterionScore(string CriterionId, string Label, CriterionRating? Rating);

/// <summary>
/// A reviewer's answer to one question, with the question as it was asked. Which value applies depends on
/// <see cref="Type"/>: <see cref="Value"/> is "yes" or "no" for the yes-or-no kinds, an option id for a choice and the
/// text for the text kinds; <see cref="Values"/> holds a multi-choice answer's option ids; <see cref="Rating"/> a number
/// on a 1 to 5 or 1 to 10 scale. All empty means the reviewer left it unanswered.
/// </summary>
/// <remarks><c>options</c>: A choice question's options as they were, so the answer keeps its words.</remarks>
public sealed record ScorecardAnswer(
    string QuestionId,
    string Label,
    ScorecardQuestionType Type,
    string? Value = null,
    IReadOnlyList<string>? Values = null,
    int? Rating = null,
    IReadOnlyList<QuestionOption>? Options = null)
{
    /// <summary>Whether the reviewer answered it.</summary>
    public bool IsAnswered() => Value is not null || Values is { Count: > 0 } || Rating is not null;

    internal bool Same(ScorecardAnswerValue given) =>
        Value == given.Value && Rating == given.Rating && (Values ?? []).SequenceEqual(given.Values ?? [], StringComparer.Ordinal);
}

/// <summary>What a reviewer gave for one question, before it is checked against the question.</summary>
public sealed record ScorecardAnswerValue(string? Value = null, IReadOnlyList<string>? Values = null, int? Rating = null)
{
    public static ScorecardAnswerValue None { get; } = new();
}
