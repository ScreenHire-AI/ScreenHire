using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Content;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Endpoints;
using ScreenHire.Api.Hiring;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Applications;

/// <summary>
/// Which applications a list reads: one job's, one candidate's or every job's, optionally narrowed to candidates whose
/// name or email contains <paramref name="Search"/>, and capped at <paramref name="Limit"/>. The HTTP lists pass no
/// search and no limit.
/// </summary>
internal sealed record ApplicationFilter(Guid? JobId = null, string? Search = null, int? Limit = null, Guid? CandidateId = null);

/// <summary>
/// Reading applications, for the hiring API's routes and the MCP tools alike, so both apply the same rules: the tenant
/// and job-scope filters, no integrity output for an API key (ADR-0011), and the rating as the reader may see it
/// (<see cref="RatingReads"/>).
/// </summary>
internal static class ApplicationReads
{
    /// <summary>The applications the filter names, newest first.</summary>
    /// <remarks>
    /// One statement reads the rows and each one's last activity (<see cref="ApplicationActivity"/>): the latest counted
    /// timeline entry and the latest finished task are correlated subqueries, served by the
    /// <c>(organization_id, application_id, occurred_at)</c> index. Integrity and ratings follow in one query each.
    /// </remarks>
    public static async Task<List<ApplicationSummary>> ListAsync(
        ClaimsPrincipal user, ITenantContext tenant, ScreenHireDbContext db, ApplicationFilter filter, CancellationToken cancellationToken)
    {
        var applications = db.Applications.AsNoTracking();
        if (filter.JobId is { } jobId)
        {
            applications = applications.Where(a => a.JobId == jobId);
        }

        if (filter.CandidateId is { } candidateId)
        {
            applications = applications.Where(a => a.CandidateId == candidateId);
        }

        var rows = applications
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .Join(db.Candidates, x => x.Application.CandidateId, c => c.Id, (x, c) => new { x.Application, x.Job, Candidate = c });
        if (Like.Contains(filter.Search) is { } pattern)
        {
            rows = rows.Where(r => EF.Functions.ILike(r.Candidate.FullName, pattern, Like.Escape)
                || EF.Functions.ILike(r.Candidate.Email, pattern, Like.Escape));
        }

        var ordered = rows.OrderByDescending(r => r.Application.SubmittedAt).ThenBy(r => r.Application.Id);
        var counted = ApplicationActivity.CountedTypes;
        var found = await (filter.Limit is { } limit ? ordered.Take(limit) : ordered)
            .Select(r => new
            {
                r.Application,
                r.Job,
                r.Candidate,
                LastEventAt = db.ApplicationEvents
                    .Where(e => e.ApplicationId == r.Application.Id && counted.Contains(e.Type))
                    .Max(e => (DateTimeOffset?)e.OccurredAt),
                LastTaskCompletedAt = db.ApplicationTasks
                    .Where(t => t.ApplicationId == r.Application.Id)
                    .Max(t => t.CompletedAt),
            })
            .ToListAsync(cancellationToken);
        var integrity = await IntegritySummariesAsync(user, db, [.. found.Select(r => r.Application.Id)], cancellationToken);
        var ratings = await RatingReads.ForAsync(db, [.. found.Select(r => r.Application.Id)], tenant.UserId, cancellationToken);
        return [.. found.Select(r => r.Application.ToSummary(
            r.Job, r.Candidate, ApplicationActivity.LastAt(r.Application.SubmittedAt, r.LastEventAt, r.LastTaskCompletedAt),
            integrity.GetValueOrDefault(r.Application.Id), ratings.GetValueOrDefault(r.Application.Id)))];
    }

    /// <summary>One application with its candidate, files and follow-ups, or null when the reader cannot see it.</summary>
    public static async Task<ApplicationDetail?> DetailAsync(
        ITenantContext tenant, ScreenHireDbContext db, Guid applicationId, CancellationToken cancellationToken)
    {
        var row = await LoadAsync(db, applicationId, cancellationToken);
        return row is { } found ? await DetailOfAsync(tenant, db, found.Application, found.Job, found.Candidate, cancellationToken) : null;
    }

    /// <summary>The detail of an application already loaded, for a write that answers with it.</summary>
    public static async Task<ApplicationDetail> DetailOfAsync(
        ITenantContext tenant, ScreenHireDbContext db, JobApplication application, Job job, Candidate candidate, CancellationToken cancellationToken) =>
        application.ToDetail(
            job, candidate, await FilesOfAsync(db, application.Id, cancellationToken), await FollowUpsOfAsync(db, application.Id, cancellationToken),
            await RatingReads.ForAsync(db, application.Id, tenant.UserId, cancellationToken));

    /// <summary>
    /// The application's timeline, oldest first, or null when the reader cannot see the application. A key sees no
    /// integrity assessments here either: not even that one ran, or when (<see cref="IntegritySummariesAsync"/>).
    /// </summary>
    public static async Task<List<ApplicationTimelineEntry>?> TimelineAsync(
        ClaimsPrincipal user, ITenantContext tenant, ScreenHireDbContext db, Guid applicationId, CancellationToken cancellationToken)
    {
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId, cancellationToken))
        {
            return null;
        }

        var withIntegrity = !user.IsApiKey();
        var events = await db.ApplicationEvents.AsNoTracking()
            .Where(e => e.ApplicationId == applicationId)
            .Where(e => withIntegrity || e.Type != ApplicationEventType.IntegrityAssessed)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
        // A NoteAdded entry shows its note's latest text, or none once it was deleted; a key's change shows the key's name
        // (a revoked key keeps its name).
        var notes = await db.ApplicationNotes.AsNoTracking().IgnoreAutoIncludes()
            .Where(n => n.ApplicationId == applicationId)
            .ToDictionaryAsync(n => n.Id, cancellationToken);
        var keyIds = events.Select(e => e.Data.ApiKeyId).OfType<Guid>().Distinct().ToList();
        var keyNames = keyIds.Count == 0
            ? []
            : await db.ApiKeys.AsNoTracking().Where(k => keyIds.Contains(k.Id)).ToDictionaryAsync(k => k.Id, k => k.Name, cancellationToken);
        // A reviewer with their own scorecard still to give sees that others submitted theirs, but not what they said
        // (ADR-0013: nothing anchors them).
        var withheld = await RatingReads.WithheldAsync(db, applicationId, tenant.UserId, cancellationToken);
        return [.. events.Select(e => e.ToTimelineEntry(
            e.Data.NoteId is { } noteId ? notes.GetValueOrDefault(noteId) : null,
            e.Data.ApiKeyId is { } keyId ? keyNames.GetValueOrDefault(keyId) : null))
            .Select(entry => withheld && entry.Type == ApplicationEventType.ScorecardSubmitted && entry.ActorUserId != tenant.UserId
                ? entry with { ScorecardId = null, Recommendation = null }
                : entry)];
    }

    /// <summary>
    /// Integrity output is evidence for the person reviewing an application (ADR-0004), so an API key never receives it:
    /// an integrator's system could reject on it, which ScreenHire's own automations may not (ADR-0011).
    /// </summary>
    public static async Task<Dictionary<Guid, IntegritySummary>> IntegritySummariesAsync(
        ClaimsPrincipal user, ScreenHireDbContext db, IReadOnlyCollection<Guid> applicationIds, CancellationToken cancellationToken) =>
        user.IsApiKey() ? [] : await IntegrityReads.SummariesAsync(db, applicationIds, cancellationToken);

    private static async Task<List<ApplicationFile>> FilesOfAsync(ScreenHireDbContext db, Guid applicationId, CancellationToken cancellationToken) =>
        await db.ApplicationFiles.AsNoTracking()
            .Where(f => f.ApplicationId == applicationId)
            .OrderBy(f => f.UploadedAt)
            .ThenBy(f => f.QuestionId)
            .ToListAsync(cancellationToken);

    private static async Task<List<QuestionnaireRequest>> FollowUpsOfAsync(ScreenHireDbContext db, Guid applicationId, CancellationToken cancellationToken) =>
        await db.QuestionnaireRequests.AsNoTracking()
            .Where(r => r.ApplicationId == applicationId)
            .OrderBy(r => r.FirstSentAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

    private static async Task<(JobApplication Application, Job Job, Candidate Candidate)?> LoadAsync(
        ScreenHireDbContext db, Guid applicationId, CancellationToken cancellationToken)
    {
        var row = await db.Applications.AsNoTracking()
            .Where(a => a.Id == applicationId)
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .Join(db.Candidates, x => x.Application.CandidateId, c => c.Id, (x, c) => new { x.Application, x.Job, Candidate = c })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.Application, row.Job, row.Candidate);
    }
}
