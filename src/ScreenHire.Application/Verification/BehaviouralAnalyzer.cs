using ScreenHire.Domain.Verification;

namespace ScreenHire.Application.Verification;

/// <summary>
/// Behavioural signals from form telemetry (verification-engine.md §4, <c>BEH_*</c>): the aggregates come from
/// <see cref="ITelemetrySource"/> (<see cref="DatabaseTelemetrySource"/>, fed by the careers page's telemetry module) and
/// its rules (<see cref="TelemetryRules"/>). Every signal it reports is accessibility sensitive and never raises a level
/// on its own (<c>telemetry-never-alone</c> in the ruleset).
/// </summary>
/// <remarks>
/// An application with an accommodation has no telemetry at all (the careers page never starts it), and the analyzer
/// does not even ask the source for one. Its note is the same either way, so the run record never reveals the switch.
/// </remarks>
public sealed class BehaviouralAnalyzer(ITelemetrySource telemetry) : IVerificationAnalyzer
{
    public const string NoTelemetryNote = "No telemetry for this application.";

    public string Name => "behavioural";

    /// <summary>
    /// Version 1 ran before any telemetry existed and saw none for every application. Version 2 reads the telemetry
    /// module's aggregates, which submission binds to the application before the pipeline starts.
    /// </summary>
    public int Version => 2;

    public async Task<AnalyzerResult> AnalyzeAsync(VerificationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Application.AccommodationRequested
            || await telemetry.GetAsync(context.Application.Id, cancellationToken) is not { } session)
        {
            return AnalyzerResult.Nothing(NoTelemetryNote);
        }

        return new AnalyzerResult(telemetry.Evaluate(session, context));
    }
}

/// <summary>
/// The behavioural analyzer's input: one application's form telemetry, as session aggregates only (CLAUDE.md
/// non-negotiable 4). Never raw keystrokes, key values, timings per key or field content.
/// </summary>
/// <remarks>
/// <c>fields</c> are per-field aggregates keyed by the form's field id (a question id, <c>firstName</c>, ...).
/// <c>page</c> holds page-level counts and the automation markers the page reported.
/// </remarks>
public sealed record TelemetrySessionAggregates(
    Guid ApplicationId, IReadOnlyList<FieldTelemetry> Fields, PageTelemetry Page, DateTimeOffset CollectedAt);

/// <summary>
/// Where telemetry aggregates come from, and the rules that turn them into <c>BEH_*</c> signals. The worker uses
/// <see cref="DatabaseTelemetrySource"/>; <see cref="NoTelemetrySource"/> has none, for tests and hosts without a database.
/// </summary>
/// <remarks>
/// The pipeline runs each analyzer version once per application, so the aggregates must be stored before it publishes
/// <see cref="AnalyzeApplicationVerification"/>, or the run records nothing and is never repeated. Submission binds the
/// session's telemetry in the same transaction as the application for that reason.
/// </remarks>
public interface ITelemetrySource
{
    /// <summary>The application's telemetry aggregates, or null when there are none.</summary>
    Task<TelemetrySessionAggregates?> GetAsync(Guid applicationId, CancellationToken cancellationToken);

    /// <summary>The behavioural signals the aggregates show. Codes from <c>SignalCatalogue</c>'s <c>BEH_*</c> set only.</summary>
    IReadOnlyList<ObservedSignal> Evaluate(TelemetrySessionAggregates session, VerificationContext context);

    /// <summary>Deletes the current organization's telemetry whose retention ended before <paramref name="cutoff"/>; returns how many.</summary>
    Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

/// <summary>No telemetry and nothing to purge.</summary>
public sealed class NoTelemetrySource : ITelemetrySource
{
    public Task<TelemetrySessionAggregates?> GetAsync(Guid applicationId, CancellationToken cancellationToken) =>
        Task.FromResult<TelemetrySessionAggregates?>(null);

    public IReadOnlyList<ObservedSignal> Evaluate(TelemetrySessionAggregates session, VerificationContext context) => [];

    public Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => Task.FromResult(0);
}
