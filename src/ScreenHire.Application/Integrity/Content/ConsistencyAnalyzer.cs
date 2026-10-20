using ScreenHire.Domain.Integrity;
using ScreenHire.Infrastructure.Integrity.Llm;

namespace ScreenHire.Application.Integrity.Content;

/// <summary>
/// <c>CNT_INCONSISTENCY</c> from the LLM consistency check (<see cref="CheckApplicationConsistency"/>): one signal per
/// contradiction, each citing the quoted span from both sources side by side, with the prompt version and the models
/// that answered (integrity-engine.md §10.10). The explanation is ours, built from a fixed topic; no model prose reaches
/// a reviewer.
/// </summary>
/// <remarks>
/// Not ready until the check has an outcome. A check that could not run (no model, the organization's switch off, the
/// budget spent, fewer than two sources) leaves no signal and a run note saying why: unavailable, never failed.
/// </remarks>
public sealed class ConsistencyAnalyzer(IContentRecords records) : IIntegrityAnalyzer
{
    public const string PendingNote = "Waiting for the consistency check.";

    public string Name => "consistency";

    public int Version => 1;

    public async Task<AnalyzerResult> AnalyzeAsync(IntegrityContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var check = await records.GetConsistencyCheckAsync(context.Application.Id, ConsistencyPrompts.Version, cancellationToken);
        if (check is null || check.Status == ConsistencyCheckStatus.Running)
        {
            return AnalyzerResult.NotReady(PendingNote);
        }

        if (check.Status != ConsistencyCheckStatus.Completed)
        {
            return AnalyzerResult.Nothing($"Consistency check {Describe(check.Status)}: {check.Note}");
        }

        var provenance = new LlmProvenance(check.PromptVersion, check.ExtractionModel ?? "", check.ConsistencyModel ?? "");
        return new AnalyzerResult([.. check.Contradictions.Select(c => Signal(c, provenance))]);
    }

    /// <summary>The signal for one contradiction. Public for the contract test that every such signal cites both spans.</summary>
    public static ObservedSignal Signal(Contradiction contradiction, LlmProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(contradiction);
        var left = ApplicationText.Describe(contradiction.Left.Source);
        var right = ApplicationText.Describe(contradiction.Right.Source);
        return new ObservedSignal(
            SignalCatalogue.Inconsistency,
            SignalConfidence.Medium,
            [
                SignalEvidence.Comparison(
                    Capitalize(Topic(contradiction.Topic)),
                    new ComparedText(Capitalize(left), contradiction.Left.Quote),
                    new ComparedText(Capitalize(right), contradiction.Right.Quote)),
                SignalEvidence.Fact("Checked by", $"Language model ({provenance.ConsistencyModel}), prompt {provenance.PromptVersion}"),
            ],
            new Dictionary<string, string> { ["left"] = left, ["right"] = right, ["topic"] = Topic(contradiction.Topic) },
            Llm: provenance);
    }

    private static string Topic(ContradictionTopic topic) => topic switch
    {
        ContradictionTopic.Employer => "employers",
        ContradictionTopic.JobTitle => "job titles",
        ContradictionTopic.EmploymentDates => "employment dates",
        ContradictionTopic.YearsOfExperience => "years of experience",
        ContradictionTopic.Education => "education",
        _ => "certifications",
    };

    private static string Describe(ConsistencyCheckStatus status) => status switch
    {
        ConsistencyCheckStatus.Disabled => "turned off",
        ConsistencyCheckStatus.OverBudget => "skipped",
        ConsistencyCheckStatus.NotEnoughSources => "skipped",
        _ => "unavailable",
    };

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
