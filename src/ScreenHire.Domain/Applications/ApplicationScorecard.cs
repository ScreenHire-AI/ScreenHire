using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// One reviewer's scorecard for one stage of one application: a labelled rating per criterion of the stage's
/// <see cref="Scorecard"/> and an overall recommendation (workflow-engine.md §8). Final once submitted: a reviewer gives
/// one judgement per stage, and the <c>ScorecardSubmitted</c> timeline entry it comes with is what raises the
/// ReviewerDecision trigger, once. Nothing here reads integrity output, and no rating is summed into a score.
/// </summary>
public sealed class ApplicationScorecard : ITenantOwned
{
    public const int MaxNoteLength = 2000;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    public Guid JobId { get; init; }

    /// <summary>The stage whose scorecard this is.</summary>
    public required string StageId { get; init; }

    /// <summary>The person who gave the ratings. Always a person: an API key has no judgement to record.</summary>
    public Guid ReviewerUserId { get; init; }

    /// <summary>
    /// One rating per criterion, in the scorecard's order, with each criterion's label as it was: a later workflow change
    /// cannot rewrite what the reviewer was asked.
    /// </summary>
    public required IReadOnlyList<CriterionScore> Ratings { get; init; }

    public Recommendation Recommendation { get; init; }

    public string? Note { get; init; }

    /// <summary>The job's workflow version the scorecard was filled under.</summary>
    public int WorkflowVersion { get; init; }

    /// <summary>The <c>ScorecardSubmitted</c> timeline entry: the ReviewerDecision trigger's event.</summary>
    public Guid TimelineEventId { get; init; }

    public DateTimeOffset SubmittedAt { get; init; }

    /// <summary>
    /// Whether a submission says exactly what this scorecard says, so a retried request returns it rather than
    /// conflicting. Ratings compare by criterion, the note after trimming.
    /// </summary>
    public bool SameAs(IReadOnlyDictionary<string, CriterionRating?> ratings, Recommendation recommendation, string? note)
    {
        ArgumentNullException.ThrowIfNull(ratings);
        return Recommendation == recommendation
            && string.Equals(Note, Normalize(note), StringComparison.Ordinal)
            && Ratings.All(r => ratings.GetValueOrDefault(r.CriterionId) == r.Rating)
            && ratings.Keys.All(id => Ratings.Any(r => r.CriterionId == id));
    }

    internal static string? Normalize(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}

/// <summary>A criterion's rating on a submitted scorecard; <c>rating</c> is null when the reviewer did not rate it.</summary>
public sealed record CriterionScore(string CriterionId, string Label, CriterionRating? Rating);
