using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Integrity;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Applications;

/// <summary>
/// Development only, and only when <c>ScreenHire:Demo:SyntheticApplicants</c> is on (the AppHost sets it for
/// <c>pnpm dev</c>): once the API has started, the synthetic applicants (<see cref="SyntheticApplicants"/>) apply to
/// Brightwater's "Senior .NET Engineer" job through the same submission path as the careers form, with their network
/// and session details. The worker then assesses them, so the local demo shows every integrity level. Idempotent: an
/// applicant whose email already applied is skipped, so it costs nothing on later starts.
/// </summary>
/// <remarks>
/// A one-shot startup step, not a loop. It runs after startup rather than in <see cref="DevelopmentDataSeeder"/>
/// because the submission publishes through Wolverine's outbox, which needs the started host.
/// </remarks>
internal sealed partial class SyntheticApplicantsDemo(
    IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, TimeProvider time, ILogger<SyntheticApplicantsDemo> logger) : BackgroundService
{
    public const string JobSlug = "senior-dotnet-engineer";

    /// <summary>The careers notice version the synthetic applicants "saw" (NOTICE_VERSION in apps/careers/src/lib/apply.ts).</summary>
    public const string PrivacyNoticeVersion = "2026-10-20.2";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider")
        {
            return; // the build's OpenAPI generation starts the host without a database
        }

        var started = new TaskCompletionSource();
        await using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        {
            await started.Task.WaitAsync(stoppingToken);
        }

        try
        {
            var submitted = await SeedAsync(stoppingToken);
            LogSeeded(logger, submitted);
        }
        // Demo data is a convenience: a failure is logged and never stops the API.
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            LogFailed(logger, exception);
        }
    }

    private async Task<int> SeedAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var applicant in SyntheticApplicants.All)
        {
            // A scope per applicant: each submission is its own transaction and outbox flush, like a request.
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var outbox = services.GetRequiredService<IDbContextOutbox<ScreenHireDbContext>>();
            var db = outbox.DbContext;
            var organization = await db.Organizations.AsNoTracking()
                .SingleOrDefaultAsync(o => o.Slug == DevelopmentDataSeeder.DemoOrganizationSlug, cancellationToken);
            if (organization is null)
            {
                return count;
            }

            services.GetRequiredService<TenantContext>().SetOrganization(organization.Id);
            var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Slug == JobSlug, cancellationToken);
            var resumeQuestion = job is null ? null : ApplicationForm.QuestionsFor(job.Workflow).FirstOrDefault(q => q.Id == ApplicationForm.ResumeQuestionId);
            if (job is null || resumeQuestion is null)
            {
                return count;
            }

            var email = Domain.Candidates.Candidate.Normalize(applicant.Email);
            if (await db.Candidates.AnyAsync(c => c.NormalizedEmail == email, cancellationToken))
            {
                continue;
            }

            var upload = await services.GetRequiredService<ApplicantUploads>().StoreDevelopmentFileAsync(
                organization.Id, job.Id, resumeQuestion, applicant.ResumeFileName, applicant.ResumeBytes(), cancellationToken);
            var now = time.GetUtcNow();
            var intake = new ApplicationIntake(
                ApplicationSource.CareersSite,
                [new ApplicationAnswer(resumeQuestion.Id, upload.UploadId.ToString()), .. applicant.Answers],
                PrivacyNoticeVersion,
                applicant.HoneypotFilled,
                BotCheckResult.NotChecked);
            var created = await ApplicationSubmission.SubmitAsync(
                outbox, job, new Contracts.CandidateInput(applicant.FullName, applicant.Email, applicant.Phone),
                new ApplicationDeclarations(false, applicant.AiUseDeclaration, applicant.AiUseDetails),
                intake, [upload], now, actorUserId: null, cancellationToken,
                new SubmissionSession(applicant.IpAddress, applicant.UserAgent, applicant.DeviceFingerprint, now - applicant.TimeToComplete, applicant.AutomationMarkers),
                services.GetRequiredService<IOptions<IntegrityOptions>>().Value,
                Telemetry(applicant));
            if (created is not null)
            {
                count++;
            }
        }

        return count;
    }

    // What the applicant's browser would have sent, as the last page's snapshot of a session of its own.
    private static TelemetryBinding? Telemetry(SyntheticApplicant applicant) => applicant.Telemetry is not { } telemetry
        ? null
        : new TelemetryBinding(
            Guid.CreateVersion7(),
            new Contracts.TelemetrySnapshotInput(
                SessionToken: "",
                PageViewId: Guid.CreateVersion7(),
                Sequence: 1,
                [.. telemetry.Fields.Select(f => new Contracts.FieldTelemetryInput(
                    f.FieldId, f.PasteCount, f.PastedCharacters, f.TypedCharacters, f.FocusCount, f.BlurCount, f.ActiveMilliseconds, f.Autofilled))],
                new Contracts.PageTelemetryInput(telemetry.Page.VisibilityChanges, telemetry.Page.AutomationMarkers)));

    [LoggerMessage(Level = LogLevel.Information, Message = "Development demo: {Count} synthetic applicants applied to Brightwater")]
    private static partial void LogSeeded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Development demo: the synthetic applicants could not be seeded")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
