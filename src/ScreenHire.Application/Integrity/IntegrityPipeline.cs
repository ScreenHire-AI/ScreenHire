using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Integrity;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Integrity;

/// <summary>
/// The integrity pipeline for one application (integrity-engine.md §5): run every analyzer version that has not run
/// yet, store what each reports, then derive both assessments with the ruleset. Runs as the application's organization
/// inside the handler's transaction, so the runs, signals, assessments and the messages it returns commit together.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent at every step. An analyzer version runs once per application (unique index on the run). An assessment is
/// written only when its inputs changed (unique index on its fingerprint). Two deliveries racing hit one of those indexes;
/// the loser's transaction rolls back and its retry finds the work done.
/// </para>
/// <para>
/// Analyzers that later tasks add (content forensics) plug in as more <see cref="IIntegrityAnalyzer"/> registrations, or
/// publish <see cref="AnalyzeApplicationIntegrity"/> again when their own asynchronous work finishes.
/// </para>
/// </remarks>
public sealed class IntegrityPipeline(ScreenHireDbContext db, IEnumerable<IIntegrityAnalyzer> analyzers, TimeProvider time)
{
    /// <summary>Runs what has not run and reassesses. Returns the messages to publish when an assessment changed.</summary>
    public async Task<OutgoingMessages> RunAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken)
            ?? throw new InvalidOperationException($"Application {applicationId} is not visible to the current organization.");
        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == application.CandidateId, cancellationToken);
        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        var observation = await db.SubmissionObservations.AsNoTracking().SingleOrDefaultAsync(o => o.ApplicationId == applicationId, cancellationToken);
        var files = await db.ApplicationFiles.AsNoTracking().Where(f => f.ApplicationId == applicationId).ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        var context = new IntegrityContext(application, candidate, job, observation, files, now);

        var done = await db.IntegrityAnalyzerRuns.AsNoTracking()
            .Where(r => r.ApplicationId == applicationId)
            .Select(r => new { r.Analyzer, r.AnalyzerVersion })
            .ToListAsync(cancellationToken);
        var signals = await db.IntegritySignals.AsNoTracking().Where(s => s.ApplicationId == applicationId).ToListAsync(cancellationToken);

        foreach (var analyzer in analyzers)
        {
            if (done.Any(r => r.Analyzer == analyzer.Name && r.AnalyzerVersion == analyzer.Version))
            {
                continue;
            }

            var result = await analyzer.AnalyzeAsync(context, cancellationToken);
            var runId = Guid.CreateVersion7();
            var written = ToSignals(result.Signals, application, runId, analyzer, now);
            db.IntegrityAnalyzerRuns.Add(new IntegrityAnalyzerRun
            {
                Id = runId,
                OrganizationId = application.OrganizationId,
                ApplicationId = applicationId,
                Analyzer = analyzer.Name,
                AnalyzerVersion = analyzer.Version,
                SignalCount = written.Count,
                Note = result.Note,
                CompletedAt = now,
            });
            db.IntegritySignals.AddRange(written);
            signals.AddRange(written);
        }

        return await AssessAsync(application, job, signals, now, cancellationToken);
    }

    /// <summary>
    /// The rows for what one analyzer run observed. The catalogue fills in what an analyzer does not decide. With an
    /// accommodation, accessibility-sensitive signals are not stored at all (CLAUDE.md non-negotiable 4); the ruleset
    /// drops any that were stored before.
    /// </summary>
    public static List<IntegritySignal> ToSignals(
        IReadOnlyList<ObservedSignal> observed, JobApplication application, Guid runId, IIntegrityAnalyzer analyzer, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(analyzer);
        var signals = new List<IntegritySignal>();
        foreach (var signal in observed)
        {
            var definition = SignalCatalogue.Get(signal.Code);
            if (definition.AccessibilitySensitive && application.AccommodationRequested)
            {
                continue;
            }

            signals.Add(new IntegritySignal
            {
                OrganizationId = application.OrganizationId,
                ApplicationId = application.Id,
                AnalyzerRunId = runId,
                Code = definition.Code,
                Category = definition.Category,
                Assessment = definition.Assessment,
                Weight = signal.Weight ?? definition.Weight,
                Confidence = signal.Confidence,
                Source = analyzer.Name,
                SourceVersion = analyzer.Version,
                ObservedAt = now,
                AccessibilitySensitive = definition.AccessibilitySensitive,
                ContextOnly = definition.ContextOnly,
                Explanation = definition.Explain(signal.Values),
                Evidence = signal.Evidence,
                CounterEvidence = signal.CounterEvidence ?? [],
            });
        }

        return signals;
    }

    private async Task<OutgoingMessages> AssessAsync(
        JobApplication application, Domain.Jobs.Job job, List<IntegritySignal> signals, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var inputs = new IntegrityInputs(signals, job.Workflow.IntegrityPolicy.AiUse, application.AiUseDeclaration, application.AccommodationRequested);
        var results = IntegrityRuleset.Assess(inputs);
        var fingerprints = results.Select(r => r.Fingerprint).ToList();
        var existing = await db.IntegrityAssessments.AsNoTracking()
            .Where(a => a.ApplicationId == application.Id && fingerprints.Contains(a.Fingerprint))
            .Select(a => a.Kind)
            .ToListAsync(cancellationToken);
        var changed = results.Where(r => !existing.Contains(r.Kind)).ToList();
        if (changed.Count == 0)
        {
            return [];
        }

        db.IntegrityAssessments.AddRange(changed.Select(r => r.ToAssessment(application.OrganizationId, application.Id, inputs, now)));

        var authenticity = results.Single(r => r.Kind == IntegrityAssessmentKind.Authenticity).Level;
        var aiAssistance = results.Single(r => r.Kind == IntegrityAssessmentKind.AiAssistance).Level;
        var entry = new ApplicationEvent
        {
            OrganizationId = application.OrganizationId,
            ApplicationId = application.Id,
            Type = ApplicationEventType.IntegrityAssessed,
            Data = new ApplicationEventData(AuthenticityLevel: authenticity, AiAssistanceLevel: aiAssistance, RulesetVersion: IntegrityRuleset.Version),
            OccurredAt = now,
        };
        db.ApplicationEvents.Add(entry);

        return
        [
            new IntegrityAssessed(
                application.OrganizationId, application.Id, application.JobId,
                Enum.Parse<AuthenticityLevel>(authenticity), Enum.Parse<AiAssistanceLevel>(aiAssistance),
                IntegrityRuleset.Version, entry.Id, now),
            // The workflow engine's IntegrityAssessed trigger. Automations it runs may only route and step up (ADR-0004).
            new RunAutomations(application.OrganizationId, application.Id, TriggerType.IntegrityAssessed, null, entry.Id, now),
        ];
    }
}
