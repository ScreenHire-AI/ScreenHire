using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Integrity;

/// <summary>
/// One observation about one application, from one analyzer run (docs/architecture/integrity-engine.md §4). Signals are
/// evidence, not verdicts (ADR-0004): the ruleset (<see cref="IntegrityRuleset"/>) turns them into categorical levels,
/// and every signal carries its evidence, any counter-evidence and a plain-language explanation. Written once, never
/// changed; a re-run under a new analyzer version writes new rows.
/// </summary>
public sealed class IntegritySignal : IApplicationScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    /// <summary>The analyzer run that observed it.</summary>
    public Guid AnalyzerRunId { get; init; }

    /// <summary>A code from <see cref="SignalCatalogue"/>, such as <c>NET_DATACENTER_IP</c>.</summary>
    public required string Code { get; init; }

    public SignalCategory Category { get; init; }

    /// <summary>The assessment the signal feeds.</summary>
    public IntegrityAssessmentKind Assessment { get; init; }

    public SignalWeight Weight { get; init; }

    public SignalConfidence Confidence { get; init; }

    /// <summary>The analyzer that observed it, such as <c>network</c>.</summary>
    public required string Source { get; init; }

    public int SourceVersion { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    /// <summary>
    /// Assistive technology can produce this signal (timing, typing, paste). It is never collected for an application
    /// with an accommodation, and the ruleset drops it if one slips through.
    /// </summary>
    public bool AccessibilitySensitive { get; init; }

    /// <summary>Context only: it adds weight to its category but never makes a category count on its own (a VPN, say).</summary>
    public bool ContextOnly { get; init; }

    /// <summary>
    /// Informational: shown to the reviewer and never counted, not even next to other signals of its category
    /// (<c>CNT_TEMPLATED_ANSWER</c>).
    /// </summary>
    public bool Informational { get; init; }

    /// <summary>
    /// For a signal a language model helped derive (<c>CNT_INCONSISTENCY</c>): the prompt version and the models that
    /// actually answered. Null for every other signal. Such a signal always cites its spans from both sources
    /// (<see cref="LlmProvenance"/>).
    /// </summary>
    public LlmProvenance? Llm { get; init; }

    /// <summary>The catalogue's explanation template with this observation's values filled in.</summary>
    public required string Explanation { get; init; }

    public IReadOnlyList<SignalEvidence> Evidence { get; init; } = [];

    /// <summary>What argues against the signal, such as a VPN exit in the country the candidate gave.</summary>
    public IReadOnlyList<CounterEvidence> CounterEvidence { get; init; } = [];
}

/// <summary>The two separate questions the engine answers (integrity-engine.md §2).</summary>
public enum IntegrityAssessmentKind
{
    /// <summary>Is this a real, unique, present person who is who they say they are?</summary>
    Authenticity,

    /// <summary>How much of the content appears generated or templated? Informational and relative to the job's AI policy.</summary>
    AiAssistance,
}

/// <summary>
/// Independent kinds of evidence. A level above the lowest needs two of them (integrity-engine.md §2), so that one noisy
/// source, such as a corporate VPN, cannot raise a level by itself.
/// </summary>
public enum SignalCategory
{
    Network,
    Identity,
    Automation,

    /// <summary>Form telemetry aggregates. Never raises a level on its own (ADR-0004).</summary>
    Behavioural,

    /// <summary>The submitted files themselves: file metadata and hidden text.</summary>
    Document,

    /// <summary>The text of the answers and resume: duplicates, templates, contradictions.</summary>
    Content,
}

/// <summary>How much a signal counts towards its category. Categorical on purpose: there is no score (ADR-0004).</summary>
public enum SignalWeight
{
    Low,
    Medium,
    High,
}

/// <summary>How sure the analyzer is of the observation itself (a lookup that matched exactly, or a heuristic).</summary>
public enum SignalConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>How a piece of evidence renders, so the dashboard can show any signal without code of its own per signal.</summary>
public enum EvidenceKind
{
    /// <summary>A labelled value: <c>label</c> and <c>value</c>.</summary>
    Fact,

    /// <summary>Quoted text: <c>text</c>, and <c>source</c> for where it came from.</summary>
    Quote,

    /// <summary>Two texts side by side: <c>left</c> and <c>right</c>.</summary>
    TextComparison,

    /// <summary>A measurement: <c>amount</c> in <c>unit</c>, with an optional <c>reference</c> such as a threshold.</summary>
    Metric,

    /// <summary>Several values: <c>items</c>.</summary>
    List,
}

/// <summary>
/// One piece of evidence. One flat record for every kind, members that do not apply left null, like timeline data, so
/// the JSON column, the API and the generated client stay one simple shape. <see cref="Kind"/> says which members apply.
/// A metric is a measurement (seconds, a count), never a likelihood or a score.
/// </summary>
/// <remarks>
/// <c>value</c>: For a fact. <c>text</c> and <c>source</c>: For a quote. <c>left</c> and <c>right</c>: For a text
/// comparison. <c>amount</c>, <c>unit</c>, <c>reference</c> and <c>referenceLabel</c>: For a metric. <c>items</c>: For a list.
/// </remarks>
public sealed record SignalEvidence(
    EvidenceKind Kind,
    string Label,
    string? Value = null,
    string? Text = null,
    string? Source = null,
    ComparedText? Left = null,
    ComparedText? Right = null,
    decimal? Amount = null,
    string? Unit = null,
    decimal? Reference = null,
    string? ReferenceLabel = null,
    IReadOnlyList<string>? Items = null)
{
    public static SignalEvidence Fact(string label, string value) => new(EvidenceKind.Fact, label, Value: value);

    public static SignalEvidence Quote(string label, string text, string source) => new(EvidenceKind.Quote, label, Text: text, Source: source);

    public static SignalEvidence Comparison(string label, ComparedText left, ComparedText right) =>
        new(EvidenceKind.TextComparison, label, Left: left, Right: right);

    public static SignalEvidence Metric(string label, decimal amount, string unit, decimal? reference = null, string? referenceLabel = null) =>
        new(EvidenceKind.Metric, label, Amount: amount, Unit: unit, Reference: reference, ReferenceLabel: referenceLabel);

    public static SignalEvidence List(string label, IReadOnlyList<string> items) => new(EvidenceKind.List, label, Items: items);
}

/// <summary>One side of a text comparison.</summary>
public sealed record ComparedText(string Label, string Text);

/// <summary>Something that argues against a signal, in plain language, with optional evidence of its own.</summary>
public sealed record CounterEvidence(string Explanation, SignalEvidence? Evidence = null);

/// <summary>
/// Which prompt and models produced an LLM-derived signal, for audit (integrity-engine.md §5). The model ids are the
/// ones the provider reported serving, not the configured names.
/// </summary>
public sealed record LlmProvenance(string PromptVersion, string ExtractionModel, string ConsistencyModel);
