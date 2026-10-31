using FluentValidation;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// An application's verification: two separate assessments, each a categorical level with reason codes, the signals and
/// their evidence and counter-evidence, a plain-language summary and a suggested next step (ADR-0004). There is no score
/// or percentage anywhere in it, and nothing here decides anything: every action is a person's, and every action is in
/// <c>reviewLog</c>.
/// </summary>
/// <remarks>
/// <c>status</c> is <c>Pending</c> until the pipeline has assessed the application; both assessments are null then.
/// <c>requestVerification</c> says whether a reviewer can ask the candidate to verify their identity (phase 2).
/// <c>consistencyCheck</c> is the LLM consistency check's state: null until it is decided, then whether it ran and, if
/// not, why. A check that could not run is unavailable, never a mark against the application.
/// </remarks>
public sealed record ApplicationVerification(
    Guid ApplicationId,
    VerificationStatus Status,
    AuthenticityAssessmentView? Authenticity,
    AiAssistanceAssessmentView? AiAssistance,
    IReadOnlyList<VerificationReviewEntry> ReviewLog,
    VerificationActionAvailability RequestVerification,
    ConsistencyCheckView? ConsistencyCheck);

/// <summary>
/// The LLM consistency check for an application: its status, why it did not run (<c>note</c>), and the prompt version and
/// models it ran with. Contradictions appear as <c>CNT_INCONSISTENCY</c> signals, each citing both spans.
/// </summary>
public sealed record ConsistencyCheckView(
    ConsistencyCheckStatus Status,
    string? Note,
    string PromptVersion,
    string? ExtractionModel,
    string? ConsistencyModel,
    int Contradictions,
    DateTimeOffset? CompletedAt);

public enum VerificationStatus
{
    /// <summary>Not assessed yet: the pipeline runs shortly after the application arrives.</summary>
    Pending,
    Assessed,
}

/// <summary>Is this a real, unique, present person who is who they say they are?</summary>
/// <remarks>
/// <c>reasonCodes</c> are the codes of the signals that raised the level (empty at <c>NoConcerns</c>). <c>signals</c> are
/// every signal considered, including context that did not count. <c>dismissal</c> is set when a reviewer set this
/// assessment aside.
/// </remarks>
public sealed record AuthenticityAssessmentView(
    Guid Id,
    AuthenticityLevel Level,
    IReadOnlyList<string> ReasonCodes,
    string Summary,
    string NextStep,
    IReadOnlyList<VerificationSignalView> Signals,
    string RulesetVersion,
    DateTimeOffset AssessedAt,
    VerificationDismissal? Dismissal);

/// <summary>How much of the content appears generated or templated? Informational, and relative to the job's AI policy.</summary>
/// <remarks>
/// <c>policy</c> is the job's AI-use policy, <c>declaration</c> what the candidate said, and <c>policyContext</c> where
/// the two leave the candidate. A declaration never raises a level.
/// </remarks>
public sealed record AiAssistanceAssessmentView(
    Guid Id,
    AiAssistanceLevel Level,
    IReadOnlyList<string> ReasonCodes,
    string Summary,
    string NextStep,
    IReadOnlyList<VerificationSignalView> Signals,
    string RulesetVersion,
    DateTimeOffset AssessedAt,
    VerificationDismissal? Dismissal,
    AiUsePolicy Policy,
    AiUseDeclaration Declaration,
    AiPolicyContext PolicyContext);

/// <summary>
/// One signal, renderable by its evidence kinds alone (<c>evidence[].kind</c>). Weight and confidence are categories,
/// not numbers.
/// </summary>
/// <remarks>
/// <c>countsTowardLevel</c>: the signal is in a category another independent category backs up, so it is one of the
/// level's reasons. <c>contextOnly</c>: shown for context and never a reason on its own (a VPN, a failed bot challenge).
/// <c>source</c> and <c>sourceVersion</c>: the analyzer that observed it. <c>informational</c>: shown and never counted.
/// <c>llm</c>: for a signal a language model helped derive, the prompt version and the models that answered.
/// <c>explained</c>: a reviewer marked this reason explained, and nobody undid it. It decides nothing: the level and
/// everything that reads it stay as assessed.
/// </remarks>
public sealed record VerificationSignalView(
    Guid Id,
    string Code,
    string Title,
    SignalCategory Category,
    SignalWeight Weight,
    SignalConfidence Confidence,
    bool CountsTowardLevel,
    bool ContextOnly,
    bool AccessibilitySensitive,
    string Explanation,
    IReadOnlyList<SignalEvidence> Evidence,
    IReadOnlyList<CounterEvidence> CounterEvidence,
    string Source,
    int SourceVersion,
    DateTimeOffset ObservedAt,
    bool Informational,
    LlmProvenance? Llm,
    VerificationReasonExplanation? Explained);

/// <summary>A reviewer marked one reason explained. <c>actorName</c> is the reviewer's display name, when they have one.</summary>
public sealed record VerificationReasonExplanation(
    Guid ActionId, ExplainReason Reason, string? Note, Guid ActorUserId, string? ActorName, DateTimeOffset ExplainedAt);

/// <summary>A reviewer set an assessment aside. <c>actorName</c> is the reviewer's display name, when they have one.</summary>
public sealed record VerificationDismissal(Guid ActionId, DismissReason Reason, string? Note, Guid ActorUserId, string? ActorName, DateTimeOffset DismissedAt);

/// <summary>One entry of the verification audit log, oldest first.</summary>
/// <remarks>
/// <c>assessmentKind</c> and <c>assessmentId</c> are null for a note on the application's verification as a whole.
/// <c>signalId</c> and <c>signalTitle</c> name the reason an explanation or its undo is about; <c>explainReason</c> is why
/// it was explained.
/// </remarks>
public sealed record VerificationReviewEntry(
    Guid Id,
    VerificationReviewActionType Type,
    VerificationAssessmentKind? AssessmentKind,
    Guid? AssessmentId,
    DismissReason? DismissReason,
    string? Note,
    Guid ActorUserId,
    string? ActorName,
    DateTimeOffset OccurredAt,
    Guid? SignalId,
    string? SignalTitle,
    ExplainReason? ExplainReason);

/// <summary>Whether an action is offered, and why not when it is not.</summary>
public sealed record VerificationActionAvailability(bool Available, string? Reason);

/// <summary>
/// The verification chips for an application in a list: each current level and its reason codes (shown as "3 reasons"), and
/// whether a reviewer dismissed it. Null on the summary until the application is assessed.
/// </summary>
public sealed record VerificationSummary(
    AuthenticityLevel Authenticity,
    IReadOnlyList<string> AuthenticityReasons,
    bool AuthenticityDismissed,
    AiAssistanceLevel AiAssistance,
    IReadOnlyList<string> AiAssistanceReasons,
    bool AiAssistanceDismissed);

/// <summary>Sets an assessment aside. A reason is required; <c>Other</c> also needs a note.</summary>
public sealed record DismissVerificationAssessmentRequest(DismissReason Reason, string? Note = null);

/// <summary>Marks one reason explained. A reason is required; <c>Other</c> also needs a note.</summary>
public sealed record ExplainVerificationReasonRequest(ExplainReason Reason, string? Note = null);

/// <summary>Adds a note to the verification audit log, about one assessment (<c>assessmentId</c>) or the application as a whole.</summary>
public sealed record AddVerificationNoteRequest(string Note, Guid? AssessmentId = null);

internal sealed class DismissVerificationAssessmentRequestValidator : AbstractValidator<DismissVerificationAssessmentRequest>
{
    public DismissVerificationAssessmentRequestValidator()
    {
        RuleFor(r => r.Reason).IsInEnum();
        RuleFor(r => r.Note).MaximumLength(VerificationReviewAction.MaxNoteLength);
        RuleFor(r => r.Note).NotEmpty().WithMessage("Say why you are dismissing it.").When(r => r.Reason == DismissReason.Other);
    }
}

internal sealed class ExplainVerificationReasonRequestValidator : AbstractValidator<ExplainVerificationReasonRequest>
{
    public ExplainVerificationReasonRequestValidator()
    {
        RuleFor(r => r.Reason).IsInEnum();
        RuleFor(r => r.Note).MaximumLength(VerificationReviewAction.MaxNoteLength);
        RuleFor(r => r.Note).NotEmpty().WithMessage("Say why it is explained.").When(r => r.Reason == ExplainReason.Other);
    }
}

internal sealed class AddVerificationNoteRequestValidator : AbstractValidator<AddVerificationNoteRequest>
{
    public AddVerificationNoteRequestValidator()
    {
        RuleFor(r => r.Note).NotEmpty().WithMessage("Write a note.").MaximumLength(VerificationReviewAction.MaxNoteLength);
    }
}
