using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Privacy;

/// <summary>
/// Which applications record retention deletes (docs/architecture/data-requests.md). One query shared by the dashboard's
/// dry-run count and the daily run, so what the preview promises is what the run removes.
/// </summary>
/// <remarks>
/// A saved candidate (anyone with a saved candidates entry) is skipped entirely, whatever their applications look like. Of the
/// rest, an application is selected when it ended before the cutoff: its stage is Hired, Rejected or Withdrawn and it entered
/// that stage before the cutoff; or the team kept the candidate in mind from it before the cutoff and they have since left the
/// saved list (the entry is gone, the time stays); or its job closed before the cutoff. A job closed with no recorded time
/// (from before the time was kept) is left alone, because the age of the end is unknown.
/// </remarks>
public static class RetentionSelection
{
    /// <summary>
    /// The applications to delete if retention ran with <paramref name="cutoff"/>, oldest first by id so a run in batches
    /// takes the same ones in the same order each time. Needs a tenant that sees every job.
    /// </summary>
    public static async Task<RetentionSelectionResult> FindAsync(ScreenHireDbContext db, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var closedJobs = db.Jobs.AsNoTracking().Where(j => j.Status == JobStatus.Closed && j.ClosedAt < cutoff).Select(j => j.Id);

        // The cheap conditions in SQL, so an organization's live applications are never loaded. The stage kind needs the
        // job's workflow, which is JSON, so it is settled below.
        var candidates = await db.Applications.AsNoTracking()
            .Where(a => a.StageEnteredAt < cutoff || a.KeptInMindAt < cutoff || closedJobs.Contains(a.JobId))
            .Select(a => new { a.Id, a.CandidateId, a.JobId, a.CurrentStageId, a.StageEnteredAt, a.KeptInMindAt })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return RetentionSelectionResult.None;
        }

        var jobIds = candidates.Select(a => a.JobId).Distinct().ToList();
        var jobs = await db.Jobs.AsNoTracking().Where(j => jobIds.Contains(j.Id))
            .Select(j => new { j.Id, j.Status, j.ClosedAt, j.Workflow }).ToDictionaryAsync(j => j.Id, cancellationToken);
        var candidateIds = candidates.Select(a => a.CandidateId).Distinct().ToList();
        var saved = (await db.TalentPoolEntries.AsNoTracking().Where(e => candidateIds.Contains(e.CandidateId)).Select(e => e.CandidateId).Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        return Select(
            candidates.Select(a => new RetentionApplication(
                a.Id, a.CandidateId, jobs[a.JobId].Workflow.FindStage(a.CurrentStageId)?.Kind, a.StageEnteredAt, a.KeptInMindAt,
                jobs[a.JobId].Status, jobs[a.JobId].ClosedAt)),
            saved,
            cutoff);
    }

    /// <summary>The selection rule on plain values, for tests and for <see cref="FindAsync"/>.</summary>
    public static RetentionSelectionResult Select(
        IEnumerable<RetentionApplication> applications, IReadOnlySet<Guid> savedCandidateIds, DateTimeOffset cutoff)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(savedCandidateIds);
        var selected = applications
            .Where(a => !savedCandidateIds.Contains(a.CandidateId) && EndedBefore(a, cutoff))
            .OrderBy(a => a.ApplicationId)
            .ToList();
        return new RetentionSelectionResult([.. selected.Select(a => a.ApplicationId)], selected.Select(a => a.CandidateId).Distinct().Count());
    }

    private static bool EndedBefore(RetentionApplication a, DateTimeOffset cutoff) =>
        (a.StageKind is { } kind && kind.IsTerminal() && a.StageEnteredAt < cutoff)
        || a.KeptInMindAt < cutoff
        || (a.JobStatus == JobStatus.Closed && a.JobClosedAt < cutoff);
}

/// <summary>One application as the selection sees it: where it is and when it ended, with its job's state.</summary>
/// <param name="ApplicationId">The application.</param>
/// <param name="CandidateId">Whose it is.</param>
/// <param name="StageKind">The kind of the application's current stage in its job's workflow; null when the stage is no longer there.</param>
/// <param name="StageEnteredAt">When it entered its current stage: the clock an ended application is measured from.</param>
/// <param name="KeptInMindAt">When the team saved the candidate from this application; stays after they left the saved list.</param>
/// <param name="JobStatus">Its job's status.</param>
/// <param name="JobClosedAt">When the job closed; null while it is open, and for a job closed before the time was kept.</param>
public sealed record RetentionApplication(
    Guid ApplicationId,
    Guid CandidateId,
    StageKind? StageKind,
    DateTimeOffset StageEnteredAt,
    DateTimeOffset? KeptInMindAt,
    JobStatus JobStatus,
    DateTimeOffset? JobClosedAt);

/// <summary>The applications retention would delete, and how many different candidates they belong to.</summary>
public sealed record RetentionSelectionResult(IReadOnlyList<Guid> ApplicationIds, int CandidateCount)
{
    public static RetentionSelectionResult None { get; } = new([], 0);
}
