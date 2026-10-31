using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// The candidate's rating on each application, as the person reading may see it (ADR-0013 decision 13,
/// workflow-engine.md section 8). It averages people's rating answers only (<see cref="RatingRule"/>), never
/// verification results, AI signals or the stage, and no automation reads it. A reviewer who still has their own scorecard
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
            .Select(s => new { s.ApplicationId, s.ReviewerUserId, s.StageId, s.SubmittedAt, s.ScorecardTemplateId, s.Answers })
            .ToListAsync(cancellationToken);
        var given = scorecards
            .Where(s => s.ReviewerUserId == userId)
            .Select(s => (s.ApplicationId, Scorecard: new GivenScorecard(s.StageId, s.SubmittedAt, s.ScorecardTemplateId)));
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
            .Select(s => new { s.StageId, s.SubmittedAt, s.ScorecardTemplateId })
            .ToListAsync(cancellationToken);
        return (await WithheldAsync(
            db, [applicationId], userId, given.Select(s => (applicationId, new GivenScorecard(s.StageId, s.SubmittedAt, s.ScorecardTemplateId))), cancellationToken))
            .Contains(applicationId);
    }

    // The applications among applicationIds where the person has an open Review task, or a scorecard asked of them, that
    // their scorecards have not answered.
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
            .Where(t => applicationIds.Contains(t.ApplicationId) && (t.Kind == ApplicationTaskKind.Review || t.Kind == ApplicationTaskKind.Scorecard)
                && t.AssigneeUserId == id && t.CompletedAt == null)
            .Select(t => new { t.ApplicationId, t.StageId, t.CreatedAt, t.ScorecardTemplateId })
            .ToListAsync(cancellationToken);
        if (reviews.Count == 0)
        {
            return [];
        }

        // Whether each review's stage has a scorecard decides what answers it, so read the jobs' workflows.
        var reviewed = reviews.Select(r => r.ApplicationId).Distinct().ToList();
        var workflows = await db.Applications.AsNoTracking()
            .Where(a => reviewed.Contains(a.Id))
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { a.Id, j.Workflow })
            .ToDictionaryAsync(x => x.Id, x => x.Workflow, cancellationToken);
        var mine = given.ToLookup(g => g.ApplicationId, g => g.Scorecard);
        return
        [
            .. reviews.GroupBy(r => r.ApplicationId)
                .Where(g => ReviewerGate.Withholds(
                    g.Select(r => new OpenReview(
                        r.StageId, r.CreatedAt, r.StageId is { } stage && workflows[g.Key].FindStage(stage)?.Scorecard is not null, r.ScorecardTemplateId)),
                    mine[g.Key]))
                .Select(g => g.Key),
        ];
    }
}
