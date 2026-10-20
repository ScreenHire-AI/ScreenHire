using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Application.Integrity;

/// <summary>
/// One stage of the integrity pipeline (integrity-engine.md §5): reads what is known about an application and reports
/// signals. The pipeline runs each analyzer version once per application, keyed (application, <see cref="Name"/>,
/// <see cref="Version"/>), and stores what it reports. Bump <see cref="Version"/> whenever what the analyzer reports
/// changes, so applications can be re-analyzed and the stored signals say which logic made them.
/// </summary>
/// <remarks>
/// An analyzer never decides a level, never refuses anything, and never reports on the accommodation or the AI-use
/// declaration (CLAUDE.md non-negotiables 2 and 4). A source it cannot reach is not a signal: it returns no signals and
/// a <see cref="AnalyzerResult.Note"/>.
/// </remarks>
public interface IIntegrityAnalyzer
{
    /// <summary>A short, stable name, such as <c>network</c>. Stored with every run and signal.</summary>
    string Name { get; }

    int Version { get; }

    Task<AnalyzerResult> AnalyzeAsync(IntegrityContext context, CancellationToken cancellationToken);
}

/// <summary>Everything the analyzers read about one application.</summary>
/// <remarks>
/// <c>observation</c> is the raw network and session data from a careers-site submission: null for an application a
/// recruiter added, or once the retention job has deleted it. <c>files</c> are the application's uploaded files.
/// </remarks>
public sealed record IntegrityContext(
    JobApplication Application,
    Candidate Candidate,
    Job Job,
    SubmissionObservation? Observation,
    IReadOnlyList<ApplicationFile> Files,
    DateTimeOffset Now)
{
    /// <summary>
    /// The one country the job is in or hires from, or null when it names none or several. Used as the region for a
    /// phone number written without a country code.
    /// </summary>
    public string? JobCountry
    {
        get
        {
            var countries = Job.Locations.Select(l => l.Country)
                .Concat(Job.ApplicantLocationRequirements)
                .Where(c => c is { Length: 2 })
                .Select(c => c!.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return countries.Count == 1 ? countries[0] : null;
        }
    }
}

/// <summary>What an analyzer reported. <see cref="Note"/> says why it saw nothing, when that is because it could not look.</summary>
public sealed record AnalyzerResult(IReadOnlyList<ObservedSignal> Signals, string? Note = null)
{
    public static AnalyzerResult Nothing(string? note = null) => new([], note);
}

/// <summary>
/// One signal as an analyzer observed it. The catalogue (<see cref="SignalCatalogue"/>) supplies its category,
/// assessment, default weight and explanation template; <see cref="Values"/> fill the template's placeholders.
/// </summary>
/// <remarks><c>weight</c> overrides the catalogue's weight, for a code whose strength depends on what matched.</remarks>
public sealed record ObservedSignal(
    string Code,
    SignalConfidence Confidence,
    IReadOnlyList<SignalEvidence> Evidence,
    IReadOnlyDictionary<string, string>? Values = null,
    IReadOnlyList<CounterEvidence>? CounterEvidence = null,
    SignalWeight? Weight = null);
