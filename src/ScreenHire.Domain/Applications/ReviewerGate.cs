namespace ScreenHire.Domain.Applications;

/// <summary>
/// Whether a person still has a review to give on an application, so the candidate's rating and the other scorecards
/// are left out of what they read until they give it (ADR-0013: a reviewer sees the rating only after submitting their
/// own, so it cannot anchor them). Everyone else, an API key included, sees them.
/// </summary>
/// <remarks>
/// A review is an open Review task assigned to the person (AssignReviewer). A task for anyone on the team holds nothing
/// back: it would hide the rating from the whole team. A review in a stage with a scorecard is answered only by the
/// person's scorecard for that stage, whenever they gave it, so a reminder after they scored the stage hides nothing and
/// scoring another stage does not open the one they were asked to review. A review in a stage without a scorecard, or
/// one made before tasks kept their stage, is answered by any scorecard of theirs submitted since it was assigned.
/// Marking the task done answers it too.
/// </remarks>
public static class ReviewerGate
{
    /// <summary>Whether <paramref name="reviews"/>, the person's open reviews on one application, leave anything unanswered.</summary>
    public static bool Withholds(IEnumerable<OpenReview> reviews, IEnumerable<GivenScorecard> theirs)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(theirs);
        var given = theirs.ToList();
        return reviews.Any(review => !given.Exists(s =>
            (review.StageId is not null && s.StageId == review.StageId)
            || (!review.StageHasScorecard && s.SubmittedAt >= review.AssignedAt)));
    }
}

/// <summary>
/// An open Review task assigned to the person: the stage it was assigned in (null before tasks kept it), when, and
/// whether the job's workflow has a scorecard for that stage.
/// </summary>
public sealed record OpenReview(string? StageId, DateTimeOffset AssignedAt, bool StageHasScorecard);

/// <summary>A scorecard the person submitted on the application.</summary>
public sealed record GivenScorecard(string StageId, DateTimeOffset SubmittedAt);
