using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Domain.Integrity;

/// <summary>
/// The versioned rules that turn signals into levels (integrity-engine.md §2 and "Ruleset"). Documented rules, not a
/// model, so every level can be explained, audited and bias-tested. ML may produce signals; rules produce levels.
/// </summary>
/// <remarks>
/// <para>Rules, in the order they apply to each assessment:</para>
/// <list type="number">
/// <item><c>accommodation-suppression</c>: with an accommodation, accessibility-sensitive signals are dropped. The
/// accommodation itself is never a signal and never appears in the output.</item>
/// <item><c>telemetry-only-alongside-another-category</c>: behavioural (telemetry) signals are shown only next to a
/// signal of another category on the same assessment; alone they are dropped (docs/research/legal.md: never flag on
/// telemetry alone).</item>
/// <item>A category <em>corroborates</em> when its signals' weights add up to at least
/// <see cref="CorroboratingWeight"/> (Low 1, Medium 2, High 3). Context-only signals add their weight only next to a
/// signal of the same category that is not context-only. Informational signals (<c>CNT_TEMPLATED_ANSWER</c>) add
/// nothing.</item>
/// <item><c>telemetry-never-alone</c>: if no category other than <see cref="SignalCategory.Behavioural"/>
/// corroborates, the level is the lowest.</item>
/// <item><c>two-independent-categories</c>: if fewer than two categories corroborate, the level is the lowest.</item>
/// <item><c>top-level-needs-two-non-telemetry-categories</c>: the highest level needs two corroborating categories
/// besides telemetry and a combined weight of at least <see cref="TopLevelWeight"/>; otherwise the middle level.</item>
/// <item><c>ai-within-policy-cap</c>: AI assistance that fits the job's policy (allowed, or disclosed where the job
/// asks for disclosure) is capped at the middle level. A declaration never raises a level.</item>
/// </list>
/// </remarks>
public static class IntegrityRuleset
{
    /// <summary>Stored with every assessment. Change it whenever a rule, weight or threshold changes.</summary>
    public const string Version = "2026-10-20.4";

    public const int CorroboratingWeight = 2;

    public const int TopLevelWeight = 6;

    public const string AccommodationSuppression = "accommodation-suppression";
    public const string TelemetryOnlyAlongsideAnotherCategory = "telemetry-only-alongside-another-category";
    public const string TelemetryNeverAlone = "telemetry-never-alone";
    public const string TwoIndependentCategories = "two-independent-categories";
    public const string TopLevelNeedsTwoNonTelemetryCategories = "top-level-needs-two-non-telemetry-categories";
    public const string AiWithinPolicyCap = "ai-within-policy-cap";

    /// <summary>Both assessments for one application.</summary>
    public static IReadOnlyList<RulesetResult> Assess(IntegrityInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return [AssessKind(IntegrityAssessmentKind.Authenticity, inputs), AssessKind(IntegrityAssessmentKind.AiAssistance, inputs)];
    }

    /// <summary>Where AI use stands against the job's policy. Depends on the policy and the declaration only.</summary>
    public static AiPolicyContext ContextFor(AiUsePolicy policy, AiUseDeclaration declaration) => policy switch
    {
        AiUsePolicy.Allowed => AiPolicyContext.AllowedByPolicy,
        AiUsePolicy.AllowedWithDisclosure => declaration == AiUseDeclaration.Used ? AiPolicyContext.Disclosed : AiPolicyContext.NotDisclosed,
        _ => declaration == AiUseDeclaration.Used ? AiPolicyContext.DeclaredUseNotAllowed : AiPolicyContext.NotAllowed,
    };

    private static RulesetResult AssessKind(IntegrityAssessmentKind kind, IntegrityInputs inputs)
    {
        var rules = new List<string>();
        var ofKind = inputs.Signals.Where(s => s.Assessment == kind).ToList();
        var signals = ofKind;
        if (inputs.AccommodationRequested && ofKind.Any(s => s.AccessibilitySensitive))
        {
            signals = [.. ofKind.Where(s => !s.AccessibilitySensitive)];
            rules.Add(AccommodationSuppression);
        }

        // Dictation, screen readers and autofill look like pasting and switching away, so telemetry is never shown alone.
        if (signals.Count > 0 && signals.All(s => s.Category == SignalCategory.Behavioural))
        {
            signals = [];
            rules.Add(TelemetryOnlyAlongsideAnotherCategory);
        }

        var corroborating = signals
            .GroupBy(s => s.Category)
            .Select(g => (Category: g.Key, Weight: CategoryWeight(g.ToList()), Signals: g.ToList()))
            .Where(c => c.Weight >= CorroboratingWeight)
            .OrderBy(c => c.Category)
            .ToList();
        var nonTelemetry = corroborating.Where(c => c.Category != SignalCategory.Behavioural).ToList();

        var step = 0;
        if (nonTelemetry.Count == 0)
        {
            if (signals.Any(s => s.Category == SignalCategory.Behavioural))
            {
                rules.Add(TelemetryNeverAlone);
            }
        }
        else if (corroborating.Count < 2)
        {
            rules.Add(TwoIndependentCategories);
        }
        else
        {
            var combined = corroborating.Sum(c => c.Weight);
            step = nonTelemetry.Count >= 2 && combined >= TopLevelWeight ? 2 : 1;
            if (step == 1)
            {
                rules.Add(TopLevelNeedsTwoNonTelemetryCategories);
            }
        }

        AiPolicyContext? aiContext = null;
        if (kind == IntegrityAssessmentKind.AiAssistance)
        {
            aiContext = ContextFor(inputs.AiUsePolicy, inputs.AiUseDeclaration);
            if (step == 2 && aiContext is AiPolicyContext.AllowedByPolicy or AiPolicyContext.Disclosed)
            {
                step = 1;
                rules.Add(AiWithinPolicyCap);
            }
        }

        var counted = step == 0 ? [] : corroborating.SelectMany(c => c.Signals).OrderBy(s => s.ObservedAt).ThenBy(s => s.Code, StringComparer.Ordinal).ToList();
        var level = LevelName(kind, step);
        return new RulesetResult(
            kind,
            level,
            [.. counted.Select(s => s.Code).Distinct(StringComparer.Ordinal)],
            [.. Ordered(signals).Select(s => s.Id)],
            [.. counted.Select(s => s.Id)],
            rules,
            Summaries.Summary(kind, step, signals, counted, aiContext, inputs.AiUseDeclaration),
            Summaries.NextStep(kind, step, aiContext),
            aiContext,
            Fingerprint(kind, ofKind, inputs));
    }

    private static int CategoryWeight(List<IntegritySignal> signals)
    {
        var direct = signals.Where(s => !s.ContextOnly && !s.Informational).Sum(s => WeightOf(s.Weight));
        return direct == 0 ? 0 : direct + signals.Where(s => s.ContextOnly && !s.Informational).Sum(s => WeightOf(s.Weight));
    }

    private static int WeightOf(SignalWeight weight) => weight switch
    {
        SignalWeight.High => 3,
        SignalWeight.Medium => 2,
        _ => 1,
    };

    private static string LevelName(IntegrityAssessmentKind kind, int step) => kind == IntegrityAssessmentKind.Authenticity
        ? ((AuthenticityLevel)step).ToString()
        : ((AiAssistanceLevel)step).ToString();

    private static IEnumerable<IntegritySignal> Ordered(IEnumerable<IntegritySignal> signals) =>
        signals.OrderBy(s => s.ObservedAt).ThenBy(s => s.Code, StringComparer.Ordinal).ThenBy(s => s.Id);

    // Everything the result depends on. Signal ids identify signals, which never change once written.
    private static string Fingerprint(IntegrityAssessmentKind kind, List<IntegritySignal> signals, IntegrityInputs inputs)
    {
        var text = new StringBuilder()
            .Append(Version).Append('|').Append(kind)
            .Append('|').Append(inputs.AiUsePolicy).Append('|').Append(inputs.AiUseDeclaration)
            .Append('|').Append(inputs.AccommodationRequested ? '1' : '0');
        foreach (var id in signals.Select(s => s.Id).Order())
        {
            text.Append('|').Append(id.ToString("N", CultureInfo.InvariantCulture));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    // Plain-language text for each outcome. Candidate- and recruiter-facing voice: docs/design/design-system.md.
    private static class Summaries
    {
        public static string Summary(
            IntegrityAssessmentKind kind, int step, List<IntegritySignal> shown, List<IntegritySignal> counted, AiPolicyContext? context,
            AiUseDeclaration declaration)
        {
            var text = step > 0
                ? $"{Observations(counted.Count)} from {Categories(counted)} checks back each other up: {Titles(counted)}."
                : shown.Count == 0
                    ? kind == IntegrityAssessmentKind.Authenticity
                        ? "Nothing unusual in the checks that ran."
                        : "No signs that the content was generated or templated."
                    : shown.All(s => s.ContextOnly)
                            ? $"Shown for context only: {Titles(shown)}. This is never a reason on its own."
                            : $"{Observations(shown.Count)} from {Categories(shown)} checks, with nothing from a second, independent kind of check to back {(shown.Count == 1 ? "it" : "them")} up. Shown for context only.";
            return context is { } policy ? $"{text} {PolicySentence(policy, declaration)}" : text;
        }

        public static string NextStep(IntegrityAssessmentKind kind, int step, AiPolicyContext? context)
        {
            if (kind == IntegrityAssessmentKind.Authenticity)
            {
                return step switch
                {
                    0 => "No action needed.",
                    1 => "Read the evidence before the first screen. If it still raises questions, ask for a short video call before the technical interview.",
                    _ => "Confirm the candidate is who they say they are before you spend interview time, for example on a short video call. These signals are not a reason to reject on their own.",
                };
            }

            return (step, context) switch
            {
                (0, _) => "No action needed.",
                (_, AiPolicyContext.AllowedByPolicy or AiPolicyContext.Disclosed) => "No action needed. The AI use fits this job's policy.",
                (_, AiPolicyContext.NotDisclosed) => "Read the evidence before deciding whether it matters for this role. This job asks candidates to say when they use AI help, and indicators are not proof.",
                _ => "Read the evidence before deciding whether it matters for this role. This job does not allow AI help, and indicators are not proof.",
            };
        }

        private static string PolicySentence(AiPolicyContext context, AiUseDeclaration declaration) => context switch
        {
            AiPolicyContext.AllowedByPolicy => "This job allows AI help.",
            AiPolicyContext.Disclosed => "The candidate said they used AI tools, which this job allows when disclosed.",
            AiPolicyContext.NotDisclosed => $"This job allows AI help when disclosed. {Declared(declaration)}",
            AiPolicyContext.DeclaredUseNotAllowed => $"This job does not allow AI help. {Declared(declaration)}",
            _ => $"This job does not allow AI help. {Declared(declaration)}",
        };

        private static string Declared(AiUseDeclaration declaration) => declaration switch
        {
            AiUseDeclaration.Used => "Declared AI use: yes.",
            AiUseDeclaration.DidNotUse => "Declared AI use: no.",
            _ => "Declared AI use: not answered.",
        };

        private static string Observations(int count) =>
            count == 1 ? "One observation" : $"{count.ToString(CultureInfo.InvariantCulture)} observations";

        private static string Categories(IEnumerable<IntegritySignal> signals)
        {
            var names = signals.Select(s => s.Category).Distinct().Order().Select(CategoryName).ToList();
            return names.Count <= 1 ? names.FirstOrDefault() ?? "" : $"{string.Join(", ", names[..^1])} and {names[^1]}";
        }

        private static string CategoryName(SignalCategory category) => category switch
        {
            SignalCategory.Network => "network",
            SignalCategory.Identity => "identity",
            SignalCategory.Automation => "automation",
            SignalCategory.Behavioural => "form behaviour",
            SignalCategory.Document => "document",
            _ => "content",
        };

        private static string Titles(IEnumerable<IntegritySignal> signals) =>
            string.Join("; ", signals.Select(s => SignalCatalogue.Get(s.Code).Title).Distinct(StringComparer.Ordinal).Select(LowerFirst));

        private static string LowerFirst(string text) =>
            text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
    }
}

/// <summary>What the ruleset reads: the application's signals, the job's AI policy and the candidate's declarations.</summary>
public sealed record IntegrityInputs(
    IReadOnlyList<IntegritySignal> Signals, AiUsePolicy AiUsePolicy, AiUseDeclaration AiUseDeclaration, bool AccommodationRequested);

/// <summary>One assessment as the ruleset derived it; <see cref="ToAssessment"/> makes the row.</summary>
public sealed record RulesetResult(
    IntegrityAssessmentKind Kind,
    string Level,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<Guid> SignalIds,
    IReadOnlyList<Guid> CountedSignalIds,
    IReadOnlyList<string> AppliedRules,
    string Summary,
    string NextStep,
    AiPolicyContext? AiPolicyContext,
    string Fingerprint)
{
    public IntegrityAssessment ToAssessment(Guid organizationId, Guid applicationId, IntegrityInputs inputs, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var ai = Kind == IntegrityAssessmentKind.AiAssistance;
        return new IntegrityAssessment
        {
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Kind = Kind,
            Level = Level,
            RulesetVersion = IntegrityRuleset.Version,
            ReasonCodes = [.. ReasonCodes],
            SignalIds = [.. SignalIds],
            CountedSignalIds = [.. CountedSignalIds],
            AppliedRules = [.. AppliedRules],
            Summary = Summary,
            NextStep = NextStep,
            AiUsePolicy = ai ? inputs.AiUsePolicy : null,
            AiUseDeclaration = ai ? inputs.AiUseDeclaration : null,
            AiPolicyContext = AiPolicyContext,
            Fingerprint = Fingerprint,
            AssessedAt = now,
        };
    }
}
