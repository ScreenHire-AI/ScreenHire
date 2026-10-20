using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Integrity;

/// <summary>
/// One assessment of one application: a categorical level with its reason codes, the signals behind it, a
/// plain-language summary and a suggested next step (ADR-0004). Append-only: a reassessment that changes anything
/// writes a new row, and the newest row per <see cref="Kind"/> is the current one, so the history of what reviewers saw
/// stays. Never a number: no score, no percentage.
/// </summary>
public sealed class IntegrityAssessment : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    public IntegrityAssessmentKind Kind { get; init; }

    /// <summary>
    /// The level's name: an <see cref="AuthenticityLevel"/> for <see cref="IntegrityAssessmentKind.Authenticity"/>, an
    /// <see cref="AiAssistanceLevel"/> for <see cref="IntegrityAssessmentKind.AiAssistance"/>. Automation conditions
    /// read it as <c>integrity.authenticity</c> and <c>integrity.aiAssistance</c>.
    /// </summary>
    public required string Level { get; init; }

    /// <summary>The <see cref="IntegrityRuleset.Version"/> that derived the level.</summary>
    public required string RulesetVersion { get; init; }

    /// <summary>The codes of the signals that raised the level; empty at the lowest level.</summary>
    public List<string> ReasonCodes { get; init; } = [];

    /// <summary>Every signal of this kind the assessment considered, shown with it.</summary>
    public List<Guid> SignalIds { get; init; } = [];

    /// <summary>The signals that counted towards the level (in categories that back each other up).</summary>
    public List<Guid> CountedSignalIds { get; init; } = [];

    /// <summary>The ruleset's rules that decided the level, by id (docs/architecture/integrity-engine.md, "Ruleset").</summary>
    public List<string> AppliedRules { get; init; } = [];

    public required string Summary { get; init; }

    public required string NextStep { get; init; }

    /// <summary>For AI assistance: the job's policy and the candidate's declaration, which the level is relative to.</summary>
    public AiUsePolicy? AiUsePolicy { get; init; }

    public AiUseDeclaration? AiUseDeclaration { get; init; }

    public AiPolicyContext? AiPolicyContext { get; init; }

    /// <summary>
    /// A hash of everything the level was derived from (ruleset, signals, policy, declaration). The same inputs give the
    /// same fingerprint, so a redelivered message writes nothing new (a unique index backs it).
    /// </summary>
    public required string Fingerprint { get; init; }

    public DateTimeOffset AssessedAt { get; init; }

    /// <summary>The lowest level for <paramref name="kind"/>.</summary>
    public static string LowestLevel(IntegrityAssessmentKind kind) =>
        kind == IntegrityAssessmentKind.Authenticity ? nameof(AuthenticityLevel.NoConcerns) : nameof(AiAssistanceLevel.NotIndicated);

    /// <summary>Whether this assessment is above the lowest level for its kind.</summary>
    public bool IsRaised => Level != LowestLevel(Kind);
}

/// <summary>Authenticity levels, lowest first (integrity-engine.md §2).</summary>
public enum AuthenticityLevel
{
    NoConcerns,
    ReviewSuggested,
    VerificationRecommended,
}

/// <summary>AI assistance levels, lowest first. Informational and relative to the job's AI policy.</summary>
public enum AiAssistanceLevel
{
    NotIndicated,
    SomeIndicators,
    StrongIndicators,
}

/// <summary>Where the candidate's AI use stands against the job's policy, from the policy and the declaration only.</summary>
public enum AiPolicyContext
{
    /// <summary>The job allows AI help without asking.</summary>
    AllowedByPolicy,

    /// <summary>The job allows AI help with disclosure, and the candidate disclosed it.</summary>
    Disclosed,

    /// <summary>The job allows AI help with disclosure; the candidate said they did not use it, or did not answer.</summary>
    NotDisclosed,

    /// <summary>The job does not allow AI help, and the candidate said they used it.</summary>
    DeclaredUseNotAllowed,

    /// <summary>The job does not allow AI help; the candidate said they did not use it, or did not answer.</summary>
    NotAllowed,
}
