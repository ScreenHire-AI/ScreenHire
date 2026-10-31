using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Integrity;
using ScreenHire.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Applications;

/// <summary>
/// Creates an application, for a recruiter adding one by hand and for a candidate applying on the careers page. The
/// candidate is found by email within the job's organization, or created. The application starts in the job's first
/// stage; its Submitted timeline event, its files, the careers form's raw network and session data
/// (<see cref="SubmissionObservation"/>), its form telemetry (<see cref="TelemetrySessions.BindToApplicationAsync"/>),
/// <see cref="ApplicationSubmitted"/> and one <see cref="ScanApplicationFile"/> per file commit together through the
/// outbox.
/// </summary>
internal static class ApplicationSubmission
{
    /// <summary>The candidate as a submission names them: the name in two parts, the address and the phone.</summary>
    public sealed record CandidateDetails(string FirstName, string? LastName, string Email, string? Phone)
    {
        public static CandidateDetails From(CandidateInput input) => new(input.Name.First, input.Name.Last, input.Email, input.Phone);

        public static CandidateDetails From(PublicCandidateInput input) => new(input.FirstName, input.LastName, input.Email, input.Phone);
    }

    /// <summary>
    /// Whether <paramref name="email"/> is a candidate the signed-in member cannot see: one whose every application is on
    /// a job outside their hiring teams (ADR-0015). Adding them to a job would hand over their stored name and phone, so
    /// <c>CreateApplication</c> refuses it, naming nobody. Always false for anyone who sees all jobs.
    /// </summary>
    public static async Task<bool> IsOutOfSightAsync(ScreenHireDbContext db, string email, CancellationToken cancellationToken)
    {
        if (db.Tenant.SeesAllJobs)
        {
            return false;
        }

        var normalized = Candidate.Normalize(email);
        return await db.Candidates.IgnoreQueryFilters([ScreenHireDbContext.JobScopeFilter]).AnyAsync(c => c.NormalizedEmail == normalized, cancellationToken)
            && !await db.Candidates.AnyAsync(c => c.NormalizedEmail == normalized, cancellationToken);
    }

    /// <summary>The new application and its candidate, or null when the candidate has already applied to the job.</summary>
    /// <remarks>
    /// An existing candidate's name and phone are left as they are. Anyone can type any email address into a careers
    /// form, so a submission never changes the details of a candidate who already exists.
    /// </remarks>
    public static async Task<(JobApplication Application, Candidate Candidate)?> SubmitAsync(
        IDbContextOutbox<ScreenHireDbContext> outbox,
        Job job,
        CandidateDetails input,
        ApplicationDeclarations declarations,
        ApplicationIntake intake,
        IReadOnlyList<VerifiedUpload> files,
        DateTimeOffset now,
        Guid? actorUserId,
        CancellationToken cancellationToken,
        SubmissionSession? session = null,
        IntegrityOptions? integrity = null,
        TelemetryBinding? telemetry = null,
        Guid? actorApiKeyId = null)
    {
        var db = outbox.DbContext;
        var email = Candidate.Normalize(input.Email);
        // One person is one candidate in an organization, however many jobs they apply to, so the lookup by email sees
        // past the job scope: a member on Only jobs they're on adding someone who applied to another job attaches the new
        // application to that candidate. The tenant filter stays, and candidates keep tenant-only row-level security for
        // this (ADR-0015). Once added, the candidate is visible to them through this job, with this job's application only.
        var candidate = await db.Candidates.IgnoreQueryFilters([ScreenHireDbContext.JobScopeFilter])
            .SingleOrDefaultAsync(c => c.NormalizedEmail == email, cancellationToken);
        if (candidate is null)
        {
            candidate = Candidate.Create(job.OrganizationId, input.FirstName, input.LastName, input.Email, input.Phone, now);
            db.Candidates.Add(candidate);
        }
        else if (await db.Applications.AnyAsync(a => a.JobId == job.Id && a.CandidateId == candidate.Id, cancellationToken))
        {
            return null;
        }

        var (application, submitted) = JobApplication.Submit(
            job, candidate, declarations.AccommodationRequested, declarations.AiUseDeclaration, declarations.AiUseDetails,
            now, actorUserId, intake, actorApiKeyId);
        db.Applications.Add(application);
        db.ApplicationEvents.Add(submitted);
        if (session is not null && application.Source == ApplicationSource.CareersSite)
        {
            db.SubmissionObservations.Add(session.ToObservation(application, integrity ?? new IntegrityOptions(), now));
        }

        // Before the outbox flush publishes ApplicationSubmitted: the behavioural analyzer runs once and must find them.
        if (telemetry is not null && application.Source == ApplicationSource.CareersSite)
        {
            await TelemetrySessions.BindToApplicationAsync(
                db, application.Id, job, telemetry, application.AccommodationRequested, now, integrity ?? new IntegrityOptions(), cancellationToken);
        }

        await outbox.PublishAsync(new ApplicationSubmitted(
            application.OrganizationId, application.Id, job.Id, application.CurrentStageId, submitted.Id, now, actorUserId));

        foreach (var upload in files)
        {
            var file = new ApplicationFile
            {
                OrganizationId = application.OrganizationId,
                ApplicationId = application.Id,
                QuestionId = upload.QuestionId,
                UploadId = upload.UploadId,
                BlobName = upload.BlobName,
                FileName = upload.FileName,
                ContentType = upload.ContentType,
                SizeBytes = upload.SizeBytes,
                Sha256 = upload.Sha256,
                UploadedAt = now,
            };
            db.ApplicationFiles.Add(file);
            await outbox.PublishAsync(new ScanApplicationFile(file.OrganizationId, application.Id, file.Id));
        }

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        // Only the applications index means "already applied". A clash on the candidates index is a new email applying to
        // two jobs at once; it stays an error the candidate can retry, because the careers page shows "already applied"
        // as the normal confirmation and the application would be lost without a word.
        catch (DbUpdateException exception) when (exception.IsUniqueViolation()
            && exception.InnerException is Npgsql.PostgresException { TableName: "applications" })
        {
            return null; // the same candidate applied to the same job at the same moment
        }

        return (application, candidate);
    }
}

/// <summary>What the candidate told us about themselves: the assistive-technology switch and their AI-use answer.</summary>
internal sealed record ApplicationDeclarations(bool AccommodationRequested, AiUseDeclaration AiUseDeclaration, string? AiUseDetails);

/// <summary>
/// What the careers form's submission arrived with, for the integrity engine: the candidate's address and browser, the
/// device fingerprint, when the form was first shown and the automation markers the page reported. Only for careers-site
/// applications, collected after the notice (CLAUDE.md non-negotiable 4).
/// </summary>
internal sealed record SubmissionSession(
    string? IpAddress, string? UserAgent, string? DeviceFingerprint, DateTimeOffset? FormStartedAt, IReadOnlyList<string>? AutomationMarkers)
{
    /// <summary>
    /// The row to store. The fingerprint is hashed with the organization's salt and never stored as sent; without a
    /// device key it is dropped. Automation markers are kept only from the telemetry module's fixed list. With an
    /// accommodation the fingerprint, the markers and the start time are all dropped: the assistive-technology switch
    /// turns the whole telemetry module off, and the API does not trust the page to have done so. The start time is
    /// also dropped when it is in the future or more than 30 days old.
    /// </summary>
    public SubmissionObservation ToObservation(JobApplication application, IntegrityOptions options, DateTimeOffset now)
    {
        var key = KeyedHashing.ParseKey(options.DeviceHashKey);
        var measured = !application.AccommodationRequested;
        var markers = (measured ? AutomationMarkers ?? [] : [])
            .Select(m => m.Trim().ToLowerInvariant())
            .Where(TelemetrySnapshotInputValidator.KnownMarkers.Contains)
            .Distinct(StringComparer.Ordinal)
            .Take(SubmissionObservation.MaxMarkers)
            .ToList();
        return new SubmissionObservation
        {
            OrganizationId = application.OrganizationId,
            ApplicationId = application.Id,
            IpAddress = IpAddress,
            UserAgent = UserAgent is { Length: > 0 } agent ? agent[..Math.Min(agent.Length, SubmissionObservation.MaxUserAgentLength)] : null,
            DeviceHash = key is null || !measured ? null : KeyedHashing.DeviceHash(key, application.OrganizationId, DeviceFingerprint),
            // Timing is never collected with the assistive-technology switch on (CLAUDE.md non-negotiable 4).
            FormStartedAt = measured && FormStartedAt is { } started && started <= now && started > now - TimeSpan.FromDays(30)
                ? started.ToUniversalTime().ToDatabasePrecision()
                : null,
            AutomationMarkers = markers,
            ObservedAt = now,
            ExpiresAt = now + TimeSpan.FromDays(options.Retention.RawNetworkDays), // 1 to 30, checked at startup (RetentionLimit)
        };
    }
}
