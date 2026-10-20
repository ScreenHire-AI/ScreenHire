using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Integrity;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Applications;

/// <summary>
/// Telemetry sessions for the careers apply form (integrity-engine.md §10.2). The application form carries a session
/// token, protected with ASP.NET Core Data Protection and valid for <see cref="Lifetime"/>, that names the organization,
/// the job and a new session id. The page sends its snapshots with it, and the submission names it, which binds the
/// session's snapshots to the application.
/// </summary>
/// <remarks>
/// A token that is missing, expired, forged or for another job never refuses anything: the application is sent without
/// telemetry.
/// </remarks>
internal sealed class TelemetrySessions(IDataProtectionProvider dataProtection)
{
    /// <summary>How long a form may stay open and still send telemetry.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    /// <summary>How long a snapshot is kept when its application is never sent.</summary>
    public static readonly TimeSpan UnsentLifetime = TimeSpan.FromDays(1);

    private readonly ITimeLimitedDataProtector protector =
        dataProtection.CreateProtector("ScreenHire.Telemetry.Session.v1").ToTimeLimitedDataProtector();

    public string Issue(Guid organizationId, Guid jobId, DateTimeOffset now) =>
        protector.Protect(
            string.Create(CultureInfo.InvariantCulture, $"{organizationId:N}.{jobId:N}.{Guid.CreateVersion7():N}"),
            now + Lifetime);

    /// <summary>The session id in <paramref name="token"/>, or null unless it is valid, unexpired and for this job.</summary>
    public Guid? Read(string? token, Guid organizationId, Guid jobId)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        string payload;
        try
        {
            payload = protector.Unprotect(token, out _);
        }
        catch (CryptographicException)
        {
            return null; // forged, damaged or expired
        }

        var parts = payload.Split('.');
        return parts.Length == 3
            && Guid.TryParseExact(parts[0], "N", out var organization) && organization == organizationId
            && Guid.TryParseExact(parts[1], "N", out var job) && job == jobId
            && Guid.TryParseExact(parts[2], "N", out var session)
                ? session
                : null;
    }

    /// <summary>
    /// The field ids a snapshot for <paramref name="job"/> may name: the form's own fields and the job's question ids.
    /// Anything else is refused, so a field id never carries anything but the name of a field.
    /// </summary>
    public static IReadOnlySet<string> FieldIdsFor(Job job) =>
        new HashSet<string>(
            [.. FixedFields, .. Domain.Applications.ApplicationForm.QuestionsFor(job.Workflow).Select(q => q.Id)],
            StringComparer.Ordinal);

    private static readonly string[] FixedFields = ["fullName", "email", "phone", "aiUseDetails"];

    /// <summary>The device-check basis where <paramref name="job"/> is: its locations and the countries it hires from.</summary>
    public static DeviceCheckBasis DeviceCheckFor(Job job, IntegrityOptions options) =>
        options.DeviceCheck.BasisFor(job.Locations.Select(l => l.Country).Concat(job.ApplicantLocationRequirements));

    /// <summary>
    /// Stores a snapshot, replacing the page view's earlier one only when <see cref="TelemetrySnapshotInput.Sequence"/> is
    /// higher and the session has not been bound to an application yet. One statement, so two snapshots of one page view
    /// arriving at once never clash.
    /// </summary>
    public static Task StoreAsync(
        ScreenHireDbContext db, Job job, Guid sessionId, TelemetrySnapshotInput snapshot, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var fields = JsonColumns.Serialize(snapshot.Fields.Select(f => f.ToDomain()).ToList());
        var page = JsonColumns.Serialize(snapshot.Page.ToDomain());
        var expiresAt = now + UnsentLifetime;
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO telemetry_snapshots (id, organization_id, job_id, session_id, page_view_id, sequence, fields, page, received_at, expires_at)
            SELECT {Guid.CreateVersion7()}, {job.OrganizationId}, {job.Id}, {sessionId}, {snapshot.PageViewId}, {snapshot.Sequence},
                   {fields}::jsonb, {page}::jsonb, {now}, {expiresAt}
            WHERE NOT EXISTS (
                SELECT 1 FROM telemetry_snapshots WHERE session_id = {sessionId} AND application_id IS NOT NULL)
            ON CONFLICT (organization_id, session_id, page_view_id) DO UPDATE
                SET sequence = excluded.sequence, fields = excluded.fields, page = excluded.page, received_at = excluded.received_at
                WHERE telemetry_snapshots.application_id IS NULL AND excluded.sequence > telemetry_snapshots.sequence
            """,
            cancellationToken);
    }

    /// <summary>
    /// Binds the session's snapshots to a new application in the submission's own transaction (the caller saves). With
    /// the assistive-technology switch on, the session's snapshots are deleted instead: the module should have sent none,
    /// and none are kept. A session that already belongs to an application binds nothing to a second one.
    /// </summary>
    /// <remarks>
    /// The final snapshot is stored first with the beacons' own upsert (<see cref="StoreAsync"/>), never added to the
    /// context. A beacon for the same page view can land while the form post is in flight, and an insert would then break
    /// the unique index and lose the application. If the submission fails after this, the snapshot stays unbound and
    /// expires like any beacon's.
    /// </remarks>
    public static async Task BindToApplicationAsync(
        ScreenHireDbContext db,
        Guid applicationId,
        Job job,
        TelemetryBinding binding,
        bool accommodationRequested,
        DateTimeOffset now,
        IntegrityOptions options,
        CancellationToken cancellationToken)
    {
        if (await db.TelemetrySnapshots.AnyAsync(s => s.SessionId == binding.SessionId && s.ApplicationId != null, cancellationToken))
        {
            return;
        }

        if (!accommodationRequested && binding.FinalSnapshot is { } final)
        {
            await StoreAsync(db, job, binding.SessionId, final, now, cancellationToken);
        }

        var snapshots = await db.TelemetrySnapshots
            .Where(s => s.SessionId == binding.SessionId && s.JobId == job.Id && s.ApplicationId == null)
            .ToListAsync(cancellationToken);
        if (accommodationRequested)
        {
            db.TelemetrySnapshots.RemoveRange(snapshots);
            return;
        }

        foreach (var snapshot in snapshots)
        {
            snapshot.ApplicationId = applicationId;
            snapshot.ExpiresAt = now + TimeSpan.FromDays(options.Retention.RawNetworkDays);
        }
    }
}

/// <summary>A submission's telemetry: its session, and the last page's snapshot, which travels with the form post.</summary>
internal sealed record TelemetryBinding(Guid SessionId, TelemetrySnapshotInput? FinalSnapshot);
