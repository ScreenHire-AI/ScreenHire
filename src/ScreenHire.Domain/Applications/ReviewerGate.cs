namespace ScreenHire.Domain.Applications;

/// <summary>
/// Whether a person still has a review to give on an application, so the candidate's rating and the other scorecards
/// are left out of what they read until they give it (ADR-0013: a reviewer sees the rating only after submitting their
/// own, so it cannot anchor them). Everyone else, an API key included, sees them.
/// </summary>
/// <remarks>
/// A review is an open Review task assigned to the person (AssignReviewer). A task for anyone on the team holds nothing
/// back: it would hide the rating from the whole team. A review is answered by the person's scorecard for the stage it
/// was assigned in, or by any scorecard of theirs on the application submitted since it was assigned, so a review asked
/// for in a stage without a scorecard is answered by their next one, and a reminder after they scored the stage hides
/// nothing. Marking the task done answers it too.
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
            (review.StageId is not null && s.StageId == review.StageId) || s.SubmittedAt >= review.AssignedAt));
    }
}

/// <summary>An open Review task assigned to the person: the stage it was assigned in (null before tasks kept it) and when.</summary>
public sealed record OpenReview(string? StageId, DateTimeOffset AssignedAt);

/// <summary>A scorecard the person submitted on the application.</summary>
public sealed record GivenScorecard(string StageId, DateTimeOffset SubmittedAt);
