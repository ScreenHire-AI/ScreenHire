using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Verification;

/// <summary>
/// The verification pipeline for one application (verification-engine.md §5): run every analyzer version that has not run
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
/// Analyzers that later tasks add (content forensics) plug in as more <see cref="IVerificationAnalyzer"/> registrations, or
/// publish <see cref="AnalyzeApplicationVerification"/> again when their own asynchronous work finishes. An analyzer whose
/// input is not ready yet records no run. A reassessment is announced (timeline, live update, workflow trigger) only
/// when a level changes.
/// </para>
/// </remarks>
public sealed class VerificationPipeline(ScreenHireDbContext db, IEnumerable<IVerificationAnalyzer> analyzers, TimeProvider time)
{
    /// <summary>Runs what has not run and reassesses. Returns the messages to publish when an assessment changed.</summary>
    public async Task<OutgoingMessages> RunAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        // An application erased since the message was sent has nothing left to analyze: the work is done, not failed, so
        // a redelivery is not retried into the dead-letter table.
        if (await LoadContextAsync(applicationId, cancellationToken) is not { } context)
        {
            return [];
        }

        var application = context.Application;
        var job = context.Job;
        var now = context.Now;

        var done = await db.VerificationAnalyzerRuns.AsNoTracking()
            .Where(r => r.ApplicationId == applicationId)
            .Select(r => new { r.Analyzer, r.AnalyzerVersion, r.Revision })
            .ToListAsync(cancellationToken);
        var signals = await db.VerificationSignals.AsNoTracking().Where(s => s.ApplicationId == applicationId).ToListAsync(cancellationToken);
        var related = new List<Guid>();

        foreach (var analyzer in analyzers)
        {
            if (done.Any(r => r.Analyzer == analyzer.Name && r.AnalyzerVersion == analyzer.Version))
            {
                continue;
            }

            var result = await analyzer.AnalyzeAsync(context, cancellationToken);
            if (!result.Ready)
            {
                continue; // its input is still being prepared; the stage preparing it publishes this message again
            }

            var runId = Guid.CreateVersion7();
            var written = ToSignals(result.Signals, application, runId, analyzer, now);
            db.VerificationAnalyzerRuns.Add(new VerificationAnalyzerRun
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
            db.VerificationSignals.AddRange(written);
            signals.AddRange(written);
            done.Add(new { Analyzer = analyzer.Name, AnalyzerVersion = analyzer.Version, Revision = 1 });
            related.AddRange(result.RelatedApplicationIds);
        }

        var assessed = await AssessAsync(
            application, job, Current(signals, done.Select(r => (r.Analyzer, r.AnalyzerVersion, r.Revision))), now, cancellationToken);
        // Applications that share a detail with this one are checked again, now that this one exists (a message each, once).
        foreach (var earlier in related.Distinct().Take(MaxRechecks))
        {
            assessed.Messages.Add(new RecheckApplicationDuplicates(application.OrganizationId, earlier, applicationId));
        }

        return assessed.Messages;
    }

    /// <summary>The most earlier applications one application's duplicate matches send a re-check to.</summary>
    public const int MaxRechecks = 25;

    /// <summary>
    /// Runs one analyzer again over an application that has already been analyzed, because something it reads changed
    /// outside the application (another application now shares a detail with it, or one that did is gone). Idempotent:
    /// when the analyzer reports what its latest run reported, nothing is written. Otherwise it writes the next revision of
    /// the run and its signals, reassesses, and adds a review-log entry that says why, so a level never moves without one.
    /// </summary>
    public async Task<OutgoingMessages> RerunAsync(Guid applicationId, string analyzerName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerName);
        if (await LoadContextAsync(applicationId, cancellationToken) is not { } context)
        {
            return [];
        }

        var analyzer = analyzers.Single(a => a.Name == analyzerName);
        var runs = await db.VerificationAnalyzerRuns.AsNoTracking()
            .Where(r => r.ApplicationId == applicationId && r.Analyzer == analyzerName)
            .Select(r => new { r.Id, r.AnalyzerVersion, r.Revision })
            .ToListAsync(cancellationToken);
        var latest = runs.Where(r => r.AnalyzerVersion == analyzer.Version).OrderByDescending(r => r.Revision).FirstOrDefault();
        if (latest is null)
        {
            return []; // never analyzed: the normal pipeline will run it and see everything there is
        }

        var result = await analyzer.AnalyzeAsync(context, cancellationToken);
        if (!result.Ready)
        {
            return [];
        }

        var now = context.Now;
        var runId = Guid.CreateVersion7();
        var written = ToSignals(result.Signals, context.Application, runId, analyzer, now, latest.Revision + 1);
        var before = await db.VerificationSignals.AsNoTracking().Where(s => s.AnalyzerRunId == latest.Id).ToListAsync(cancellationToken);
        if (SameFindings(before, written))
        {
            return [];
        }

        db.VerificationAnalyzerRuns.Add(new VerificationAnalyzerRun
        {
            Id = runId,
            OrganizationId = context.Application.OrganizationId,
            ApplicationId = applicationId,
            Analyzer = analyzer.Name,
            AnalyzerVersion = analyzer.Version,
            Revision = latest.Revision + 1,
            SignalCount = written.Count,
            Note = result.Note,
            CompletedAt = now,
        });
        db.VerificationSignals.AddRange(written);

        var all = await db.VerificationSignals.AsNoTracking().Where(s => s.ApplicationId == applicationId).ToListAsync(cancellationToken);
        all.AddRange(written);
        var allRuns = await db.VerificationAnalyzerRuns.AsNoTracking()
            .Where(r => r.ApplicationId == applicationId)
            .Select(r => new { r.Analyzer, r.AnalyzerVersion, r.Revision })
            .ToListAsync(cancellationToken);
        var runKeys = allRuns.Select(r => (r.Analyzer, r.AnalyzerVersion, r.Revision)).Append((analyzer.Name, analyzer.Version, latest.Revision + 1));
        var assessed = await AssessAsync(context.Application, context.Job, Current(all, runKeys), now, cancellationToken);

        var assessmentId = assessed.Added.FirstOrDefault(a => a.Kind == VerificationAssessmentKind.Authenticity)?.Id
            ?? await db.VerificationAssessments.AsNoTracking()
                .Where(a => a.ApplicationId == applicationId && a.Kind == VerificationAssessmentKind.Authenticity)
                .OrderByDescending(a => a.AssessedAt).ThenByDescending(a => a.Id)
                .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);
        // An earlier run exists, so the application has an assessment (AssessAsync adds the first one otherwise). If it
        // somehow does not, fail so the message retries: the new evidence must never move a level without its reason.
        var id = assessmentId ?? throw new InvalidOperationException($"Application {applicationId} has a verification run but no authenticity assessment.");
        var why = written.Count > 0
            ? $"New evidence: {written[0].Explanation}"
            : "A detail this application shared with another application no longer applies.";
        db.VerificationReviewActions.Add(VerificationReviewAction.EvidenceChanged(
            context.Application.OrganizationId, applicationId, id, why, now));

        return assessed.Messages;
    }

    // What an analyzer found, compared without ids and times: the same codes, strengths and explanations.
    private static bool SameFindings(IEnumerable<VerificationSignal> before, IEnumerable<VerificationSignal> after)
    {
        static List<string> Key(IEnumerable<VerificationSignal> signals) =>
            [.. signals.Select(s => $"{s.Code}|{s.Weight}|{s.Confidence}|{s.Explanation}").Order(StringComparer.Ordinal)];
        return Key(before).SequenceEqual(Key(after), StringComparer.Ordinal);
    }

    private async Task<VerificationContext?> LoadContextAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        if (application is null)
        {
            return null;
        }

        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == application.CandidateId, cancellationToken);
        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        var observation = await db.SubmissionObservations.AsNoTracking().SingleOrDefaultAsync(o => o.ApplicationId == applicationId, cancellationToken);
        var files = await db.ApplicationFiles.AsNoTracking().Where(f => f.ApplicationId == applicationId).ToListAsync(cancellationToken);
        return new VerificationContext(application, candidate, job, observation, files, time.GetUtcNow());
    }

    /// <summary>
    /// The signals the assessment counts: those of each analyzer's latest run (its newest version, then its newest
    /// revision). A re-run under a new version (its logic or its input changed), or a later revision of the same version,
    /// speaks for the analyzer from then on, so an application analyzed before and after never counts one observation
    /// twice; the superseded rows stay stored for the audit trail.
    /// </summary>
    public static List<VerificationSignal> Current(IEnumerable<VerificationSignal> signals, IEnumerable<(string Analyzer, int Version, int Revision)> runs)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(runs);
        var latest = runs.GroupBy(r => r.Analyzer, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(r => (r.Version, r.Revision)), StringComparer.Ordinal);
        return [.. signals.Where(s => !latest.TryGetValue(s.Source, out var run) || (s.SourceVersion, s.SourceRevision) == run)];
    }

    /// <summary>
    /// The rows for what one analyzer run observed. The catalogue fills in what an analyzer does not decide. With an
    /// accommodation, accessibility-sensitive signals are not stored at all (CLAUDE.md non-negotiable 4); the ruleset
    /// drops any that were stored before.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// An LLM-derived signal does not cite a span from each of two sources (verification-engine.md §5: evidence, not
    /// verdicts). A model's say-so is never stored as a signal.
    /// </exception>
    public static List<VerificationSignal> ToSignals(
        IReadOnlyList<ObservedSignal> observed, JobApplication application, Guid runId, IVerificationAnalyzer analyzer, DateTimeOffset now,
        int revision = 1)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(analyzer);
        var signals = new List<VerificationSignal>();
        foreach (var signal in observed)
        {
            var definition = SignalCatalogue.Get(signal.Code);
            if (definition.AccessibilitySensitive && application.AccommodationRequested)
            {
                continue;
            }

            if (signal.Llm is not null && !CitesBothSources(signal))
            {
                throw new InvalidOperationException(
                    $"Analyzer {analyzer.Name} reported an LLM-derived {signal.Code} without a quoted span from each source.");
            }

            signals.Add(new VerificationSignal
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
                SourceRevision = revision,
                ObservedAt = now,
                AccessibilitySensitive = definition.AccessibilitySensitive,
                ContextOnly = definition.ContextOnly,
                Informational = definition.Informational,
                Llm = signal.Llm,
                Explanation = definition.Explain(signal.Values),
                Evidence = signal.Evidence,
                CounterEvidence = signal.CounterEvidence ?? [],
            });
        }

        return signals;
    }

    /// <summary>
    /// Whether an LLM-derived signal cites its evidence: at least one text comparison with a quoted span on both sides,
    /// each labelled with its source.
    /// </summary>
    public static bool CitesBothSources(ObservedSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return signal.Evidence.Any(e => e.Kind == EvidenceKind.TextComparison
            && e.Left is { } left && !string.IsNullOrWhiteSpace(left.Text) && !string.IsNullOrWhiteSpace(left.Label)
            && e.Right is { } right && !string.IsNullOrWhiteSpace(right.Text) && !string.IsNullOrWhiteSpace(right.Label)
            && !string.Equals(left.Label, right.Label, StringComparison.Ordinal));
    }

    private async Task<Assessed> AssessAsync(
        JobApplication application, Domain.Jobs.Job job, List<VerificationSignal> signals, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var inputs = new VerificationInputs(signals, job.Workflow.VerificationPolicy.AiUse, application.AiUseDeclaration, application.AccommodationRequested);
        var results = VerificationRuleset.Assess(inputs);
        var fingerprints = results.Select(r => r.Fingerprint).ToList();
        var existing = await db.VerificationAssessments.AsNoTracking()
            .Where(a => a.ApplicationId == application.Id && fingerprints.Contains(a.Fingerprint))
            .Select(a => a.Kind)
            .ToListAsync(cancellationToken);
        var changed = results.Where(r => !existing.Contains(r.Kind)).ToList();
        if (changed.Count == 0)
        {
            return new([], []);
        }

        // The levels until now: the newest assessment of each kind. Read before the new rows are added.
        var previous = (await db.VerificationAssessments.AsNoTracking()
                .Where(a => a.ApplicationId == application.Id)
                .Select(a => new { a.Kind, a.Level, a.AssessedAt, a.Id })
                .ToListAsync(cancellationToken))
            .GroupBy(a => a.Kind)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AssessedAt).ThenByDescending(a => a.Id).First().Level);
        var added = changed.Select(r => r.ToAssessment(application.OrganizationId, application.Id, inputs, now)).ToList();
        db.VerificationAssessments.AddRange(added);

        // Content forensics reassess an application as each of their stages lands. New evidence at the same levels is
        // stored (the panel shows it) but not announced: the timeline, the live update and the workflow engine's
        // VerificationAssessed trigger fire for a first assessment and for a change of level, so an automation that assigns
        // a reviewer runs once, not once per stage.
        if (results.All(r => previous.TryGetValue(r.Kind, out var level) && level == r.Level))
        {
            return new([], added);
        }

        var authenticity = results.Single(r => r.Kind == VerificationAssessmentKind.Authenticity).Level;
        var aiAssistance = results.Single(r => r.Kind == VerificationAssessmentKind.AiAssistance).Level;
        var entry = new ApplicationEvent
        {
            OrganizationId = application.OrganizationId,
            ApplicationId = application.Id,
            Type = ApplicationEventType.VerificationAssessed,
            Data = new ApplicationEventData(AuthenticityLevel: authenticity, AiAssistanceLevel: aiAssistance, RulesetVersion: VerificationRuleset.Version),
            OccurredAt = now,
        };
        db.ApplicationEvents.Add(entry);

        return new(
        [
            new VerificationAssessed(
                application.OrganizationId, application.Id, application.JobId,
                Enum.Parse<AuthenticityLevel>(authenticity), Enum.Parse<AiAssistanceLevel>(aiAssistance),
                VerificationRuleset.Version, entry.Id, now),
            // The workflow engine's VerificationAssessed trigger. Automations it runs may only route and step up (ADR-0004).
            new RunAutomations(application.OrganizationId, application.Id, TriggerType.VerificationAssessed, null, entry.Id, now),
        ], added);
    }

    /// <summary>The messages an assessment announces, and the assessment rows it added.</summary>
    private sealed record Assessed(OutgoingMessages Messages, List<VerificationAssessment> Added);
}
