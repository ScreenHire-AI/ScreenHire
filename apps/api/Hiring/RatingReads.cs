using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// The candidate's rating on each application, as the person reading may see it (ADR-0013 decision 13,
/// workflow-engine.md section 8). It averages people's rating answers only (<see cref="RatingRule"/>), never
/// integrity results, AI signals or the stage, and no automation reads it. A reviewer who still has their own scorecard
/// to give (<see cref="ReviewerGate"/>) reads "after your scorecard" instead, and the other scorecards are left out of
/// what they read, so neither can anchor them. Everyone else, an API key included, sees it. Worked out on each read:
/// nothing derived is stored.
/// </summary>
internal static class RatingReads
{
    /// <summary>The rating of each of <paramref name="applicationIds"/> for the person <paramref name="userId"/> (null for an API key).</summary>
    public static async Task<Dictionary<Guid, ApplicationRating>> ForAsync(
        ScreenHireDbContext db, IReadOnlyCollection<Guid> applicationIds, Guid? userId, CancellationToken cancellationToken)
    {
        if (applicationIds.Count == 0)
        {
            return [];
        }

        var scorecards = await db.ApplicationScorecards.AsNoTracking()
            .Where(s => applicationIds.Contains(s.ApplicationId))
            .Select(s => new { s.ApplicationId, s.ReviewerUserId, s.StageId, s.SubmittedAt, s.Answers })
            .ToListAsync(cancellationToken);
        var given = scorecards
            .Where(s => s.ReviewerUserId == userId)
            .Select(s => (s.ApplicationId, Scorecard: new GivenScorecard(s.StageId, s.SubmittedAt)));
        var withheld = await WithheldAsync(db, applicationIds, userId, given, cancellationToken);
        var byApplication = scorecards.ToLookup(s => s.ApplicationId, s => RatingRule.Of(s.Answers));
        return applicationIds.Distinct().ToDictionary(id => id, id => withheld.Contains(id)
            ? ApplicationRating.AfterYourScorecard
            : ApplicationRating.From(RatingRule.Candidate(byApplication[id])));
    }

    /// <summary>The rating of one application for the person <paramref name="userId"/>.</summary>
    public static async Task<ApplicationRating> ForAsync(ScreenHireDbContext db, Guid applicationId, Guid? userId, CancellationToken cancellationToken) =>
        (await ForAsync(db, [applicationId], userId, cancellationToken))[applicationId];

    /// <summary>Whether <paramref name="userId"/> still has their own scorecard to give on the application.</summary>
    public static async Task<bool> WithheldAsync(ScreenHireDbContext db, Guid applicationId, Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is not { } id)
        {
            return false;
        }

        var given = await db.ApplicationScorecards.AsNoTracking()
            .Where(s => s.ApplicationId == applicationId && s.ReviewerUserId == id)
            .Select(s => new { s.StageId, s.SubmittedAt })
            .ToListAsync(cancellationToken);
        return (await WithheldAsync(
            db, [applicationId], userId, given.Select(s => (applicationId, new GivenScorecard(s.StageId, s.SubmittedAt))), cancellationToken))
            .Contains(applicationId);
    }

    // The applications among applicationIds where the person has an open Review task assigned to them that their
    // scorecards have not answered.
    private static async Task<HashSet<Guid>> WithheldAsync(
        ScreenHireDbContext db,
        IReadOnlyCollection<Guid> applicationIds,
        Guid? userId,
        IEnumerable<(Guid ApplicationId, GivenScorecard Scorecard)> given,
        CancellationToken cancellationToken)
    {
        if (userId is not { } id)
        {
            return [];
        }

        var reviews = await db.ApplicationTasks.AsNoTracking()
            .Where(t => applicationIds.Contains(t.ApplicationId) && t.Kind == ApplicationTaskKind.Review
                && t.AssigneeUserId == id && t.CompletedAt == null)
            .Select(t => new { t.ApplicationId, t.StageId, t.CreatedAt })
            .ToListAsync(cancellationToken);
        if (reviews.Count == 0)
        {
            return [];
        }

        var mine = given.ToLookup(g => g.ApplicationId, g => g.Scorecard);
        return
        [
            .. reviews.GroupBy(r => r.ApplicationId)
                .Where(g => ReviewerGate.Withholds(g.Select(r => new OpenReview(r.StageId, r.CreatedAt)), mine[g.Key]))
                .Select(g => g.Key),
        ];
    }
}
