using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Verification.Content;

/// <summary>
/// What the content analyzers read and keep: the files' extracted documents, the consistency check, and the
/// organization's answer fingerprints. The database version runs as the application's organization, so the tenant
/// filter and row-level security scope every query; the unit-test harness has an in-memory one.
/// </summary>
public interface IContentRecords
{
    Task<IReadOnlyList<ApplicationDocument>> GetDocumentsAsync(Guid applicationId, CancellationToken cancellationToken);

    Task<ConsistencyCheck?> GetConsistencyCheckAsync(Guid applicationId, string promptVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Fingerprints of other candidates' answers to this organization, newest first, at most
    /// <see cref="DatabaseContentRecords.MaxFingerprints"/> from the last <see cref="DatabaseContentRecords.FingerprintWindow"/>.
    /// </summary>
    Task<IReadOnlyList<AnswerFingerprint>> GetOtherCandidatesFingerprintsAsync(VerificationContext context, CancellationToken cancellationToken);

    /// <summary>The answers, job title and time of earlier applications, for the comparison a reviewer reads.</summary>
    Task<IReadOnlyList<EarlierApplication>> GetApplicationsAsync(IReadOnlyCollection<Guid> applicationIds, CancellationToken cancellationToken);

    /// <summary>Keeps this application's fingerprints for later applications to compare with. Ones already kept are skipped.</summary>
    Task KeepFingerprintsAsync(IReadOnlyList<AnswerFingerprint> fingerprints, CancellationToken cancellationToken);
}

/// <summary>An earlier application, as a near-duplicate comparison shows it.</summary>
public sealed record EarlierApplication(Guid ApplicationId, string JobTitle, DateTimeOffset SubmittedAt, IReadOnlyList<ApplicationAnswer> Answers);

public sealed class DatabaseContentRecords(ScreenHireDbContext db) : IContentRecords
{
    /// <summary>How far back answers are compared.</summary>
    public static readonly TimeSpan FingerprintWindow = TimeSpan.FromDays(180);

    public const int MaxFingerprints = 20_000;

    public async Task<IReadOnlyList<ApplicationDocument>> GetDocumentsAsync(Guid applicationId, CancellationToken cancellationToken) =>
        await db.ApplicationDocuments.AsNoTracking().Where(d => d.ApplicationId == applicationId).ToListAsync(cancellationToken);

    public Task<ConsistencyCheck?> GetConsistencyCheckAsync(Guid applicationId, string promptVersion, CancellationToken cancellationToken) =>
        db.ConsistencyChecks.AsNoTracking().SingleOrDefaultAsync(c => c.ApplicationId == applicationId && c.PromptVersion == promptVersion, cancellationToken);

    public async Task<IReadOnlyList<AnswerFingerprint>> GetOtherCandidatesFingerprintsAsync(VerificationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var since = context.Now - FingerprintWindow;
        var candidateId = context.Candidate.Id;
        return await db.AnswerFingerprints.AsNoTracking()
            .Where(f => f.CandidateId != candidateId && f.CreatedAt >= since)
            .OrderByDescending(f => f.CreatedAt)
            .Take(MaxFingerprints)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EarlierApplication>> GetApplicationsAsync(IReadOnlyCollection<Guid> applicationIds, CancellationToken cancellationToken)
    {
        var rows = await db.Applications.AsNoTracking()
            .Where(a => applicationIds.Contains(a.Id))
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { a.Id, j.Title, a.SubmittedAt, a.Answers })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new EarlierApplication(r.Id, r.Title, r.SubmittedAt, r.Answers))];
    }

    public async Task KeepFingerprintsAsync(IReadOnlyList<AnswerFingerprint> fingerprints, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);
        if (fingerprints.Count == 0)
        {
            return;
        }

        var applicationId = fingerprints[0].ApplicationId;
        var kept = await db.AnswerFingerprints.AsNoTracking()
            .Where(f => f.ApplicationId == applicationId)
            .Select(f => f.QuestionId)
            .ToListAsync(cancellationToken);
        // Saved with the pipeline's transaction; the unique index on (application, question) catches a race.
        db.AnswerFingerprints.AddRange(fingerprints.Where(f => !kept.Contains(f.QuestionId)));
    }
}
