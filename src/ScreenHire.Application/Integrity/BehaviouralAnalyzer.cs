namespace ScreenHire.Application.Integrity;

/// <summary>
/// Behavioural signals from form telemetry (integrity-engine.md §4, <c>BEH_*</c>). Registered with no data source: the
/// telemetry module supplies <see cref="ITelemetrySource"/> and the rules. Every signal it reports is accessibility
/// sensitive and never raises a level on its own (<c>telemetry-never-alone</c> in the ruleset).
/// </summary>
/// <remarks>
/// An application with an accommodation has no telemetry at all (the careers page never starts it), and the analyzer
/// does not even ask the source for one. Its note is the same either way, so the run record never reveals the switch.
/// </remarks>
public sealed class BehaviouralAnalyzer(ITelemetrySource telemetry) : IIntegrityAnalyzer
{
    public const string NoTelemetryNote = "No telemetry for this application.";

    public string Name => "behavioural";

    public int Version => 1;

    public async Task<AnalyzerResult> AnalyzeAsync(IntegrityContext context, CancellationToken cancellationToken)
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
/// <c>fields</c> are per-field aggregates keyed by the form's field id (a question id, <c>fullName</c>, ...).
/// <c>page</c> holds page-level counts and the automation markers the page reported.
/// </remarks>
public sealed record TelemetrySessionAggregates(
    Guid ApplicationId, IReadOnlyList<FieldTelemetry> Fields, PageTelemetry Page, DateTimeOffset CollectedAt);

/// <summary>Aggregates for one form field.</summary>
/// <remarks>
/// <c>pasteCount</c>, <c>pastedCharacters</c>, <c>typedCharacters</c>: Counts, not content. <c>focusCount</c> and
/// <c>blurCount</c>: Times the field gained and lost focus. <c>activeMilliseconds</c>: Total time focused.
/// <c>autofilled</c>: The browser or a password manager filled it. There is deliberately no typing-rhythm field: timing
/// between keys is keystroke dynamics, a behavioural biometric, and v1 has no biometrics (CLAUDE.md non-negotiable 3).
/// The telemetry task adds one only if its own legal review allows it.
/// </remarks>
public sealed record FieldTelemetry(
    string FieldId,
    int PasteCount,
    int PastedCharacters,
    int TypedCharacters,
    int FocusCount,
    int BlurCount,
    int ActiveMilliseconds,
    bool Autofilled);

/// <summary>Page-level aggregates: how often the page was hidden (tab switches) and automation markers such as <c>webdriver</c>.</summary>
public sealed record PageTelemetry(int VisibilityChanges, IReadOnlyList<string> AutomationMarkers);

/// <summary>
/// Where telemetry aggregates come from, and the rules that turn them into <c>BEH_*</c> signals. The telemetry module
/// registers its own; until then <see cref="NoTelemetrySource"/> has none.
/// </summary>
/// <remarks>
/// The pipeline runs each analyzer version once per application, and today every application's <c>behavioural</c>
/// version 1 run records "no telemetry". A source that lands later must bump <see cref="BehaviouralAnalyzer.Version"/>,
/// and it must have the aggregates stored before it publishes <see cref="AnalyzeApplicationIntegrity"/>, or its run
/// records nothing and is never repeated.
/// </remarks>
public interface ITelemetrySource
{
    /// <summary>The application's telemetry aggregates, or null when there are none.</summary>
    Task<TelemetrySessionAggregates?> GetAsync(Guid applicationId, CancellationToken cancellationToken);

    /// <summary>The behavioural signals the aggregates show. Codes from <c>SignalCatalogue</c>'s <c>BEH_*</c> set only.</summary>
    IReadOnlyList<ObservedSignal> Evaluate(TelemetrySessionAggregates session, IntegrityContext context);

    /// <summary>Deletes the current organization's telemetry whose retention ended before <paramref name="cutoff"/>; returns how many.</summary>
    Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

/// <summary>The local adapter until the telemetry module lands: no telemetry, nothing to purge.</summary>
public sealed class NoTelemetrySource : ITelemetrySource
{
    public Task<TelemetrySessionAggregates?> GetAsync(Guid applicationId, CancellationToken cancellationToken) =>
        Task.FromResult<TelemetrySessionAggregates?>(null);

    public IReadOnlyList<ObservedSignal> Evaluate(TelemetrySessionAggregates session, IntegrityContext context) => [];

    public Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => Task.FromResult(0);
}
