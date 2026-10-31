using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Verification;

/// <summary>
/// A reviewer's action on an application's verification assessments: the audit log (verification-engine.md §7). Append-only:
/// the database lets the application role insert and read these rows but never change or delete one. There is no
/// reject action here, on purpose (CLAUDE.md non-negotiable 2).
/// </summary>
/// <remarks>
/// Marking a reason explained records a person's reading of one signal and changes nothing else: the level, the list
/// chips, filters, saved views, automations and webhooks never read it (ADR-0004). Undo is a new row, because rows never
/// change, so the latest explain or undo row for a reason decides (<see cref="CurrentExplanations"/>).
/// </remarks>
public sealed class VerificationReviewAction : IApplicationScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    /// <summary>The assessment the action is about; null for a note on the application's verification as a whole.</summary>
    public Guid? AssessmentId { get; init; }

    public VerificationReviewActionType Type { get; init; }

    /// <summary>Why a reviewer dismissed an assessment. Required for a dismissal, null otherwise.</summary>
    public DismissReason? DismissReason { get; init; }

    /// <summary>The reason (signal) an explanation or its undo is about; null for every other action.</summary>
    public Guid? SignalId { get; init; }

    /// <summary>Why a reviewer marked a reason explained. Required for an explanation, null otherwise.</summary>
    public ExplainReason? ExplainReason { get; init; }

    /// <summary>
    /// The reviewer's words: required for a note, and for a dismissal or an explanation for another reason (<c>Other</c>).
    /// </summary>
    public string? Note { get; init; }

    public Guid ActorUserId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public const int MaxNoteLength = 2000;

    public static VerificationReviewAction Dismiss(
        VerificationAssessment assessment, DismissReason reason, string? note, Guid actorUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (!assessment.IsRaised)
        {
            throw new InvalidOperationException("Only an assessment above the lowest level can be dismissed.");
        }

        if (reason == Verification.DismissReason.Other && string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("A dismissal for another reason needs a note.", nameof(note));
        }

        return new()
        {
            OrganizationId = assessment.OrganizationId,
            ApplicationId = assessment.ApplicationId,
            AssessmentId = assessment.Id,
            Type = VerificationReviewActionType.Dismissed,
            DismissReason = reason,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ActorUserId = actorUserId,
            OccurredAt = now,
        };
    }

    /// <summary>Marks one reason of a raised assessment explained. It changes nothing but the review log.</summary>
    public static VerificationReviewAction Explain(
        VerificationAssessment assessment, Guid signalId, ExplainReason reason, string? note, Guid actorUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (!assessment.IsRaised)
        {
            throw new InvalidOperationException("Only the reasons of an assessment above the lowest level can be explained.");
        }

        if (!assessment.SignalIds.Contains(signalId))
        {
            throw new ArgumentException("The signal is not one of this assessment's reasons.", nameof(signalId));
        }

        if (reason == Verification.ExplainReason.Other && string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("An explanation for another reason needs a note.", nameof(note));
        }

        return new()
        {
            OrganizationId = assessment.OrganizationId,
            ApplicationId = assessment.ApplicationId,
            AssessmentId = assessment.Id,
            SignalId = signalId,
            Type = VerificationReviewActionType.ReasonExplained,
            ExplainReason = reason,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ActorUserId = actorUserId,
            OccurredAt = now,
        };
    }

    /// <summary>Takes back an explanation. Only its author can.</summary>
    public static VerificationReviewAction UndoExplanation(VerificationReviewAction explanation, Guid actorUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(explanation);
        if (explanation.Type != VerificationReviewActionType.ReasonExplained)
        {
            throw new ArgumentException("Only an explanation can be undone.", nameof(explanation));
        }

        if (explanation.ActorUserId != actorUserId)
        {
            throw new InvalidOperationException("Only the author of an explanation can undo it.");
        }

        return new()
        {
            OrganizationId = explanation.OrganizationId,
            ApplicationId = explanation.ApplicationId,
            AssessmentId = explanation.AssessmentId,
            SignalId = explanation.SignalId,
            Type = VerificationReviewActionType.ExplanationUndone,
            ActorUserId = actorUserId,
            OccurredAt = now,
        };
    }

    /// <summary>
    /// The explanations that stand, by assessment and signal: for each reason, its latest explain or undo row, kept when it
    /// is an explanation.
    /// </summary>
    public static Dictionary<(Guid AssessmentId, Guid SignalId), VerificationReviewAction> CurrentExplanations(IEnumerable<VerificationReviewAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return actions
            .Where(a => a.Type is VerificationReviewActionType.ReasonExplained or VerificationReviewActionType.ExplanationUndone
                && a.AssessmentId is not null && a.SignalId is not null)
            .GroupBy(a => (AssessmentId: a.AssessmentId!.Value, SignalId: a.SignalId!.Value))
            .Select(g => g.OrderBy(a => a.OccurredAt).ThenBy(a => a.Id).Last())
            .Where(a => a.Type == VerificationReviewActionType.ReasonExplained)
            .ToDictionary(a => (a.AssessmentId!.Value, a.SignalId!.Value));
    }

    public static VerificationReviewAction AddNote(
        Guid organizationId, Guid applicationId, Guid? assessmentId, string note, Guid actorUserId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        return new()
        {
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            AssessmentId = assessmentId,
            Type = VerificationReviewActionType.NoteAdded,
            Note = note.Trim(),
            ActorUserId = actorUserId,
            OccurredAt = now,
        };
    }
}

public enum VerificationReviewActionType
{
    /// <summary>A reviewer set an assessment aside, with a reason.</summary>
    Dismissed,

    NoteAdded,

    /// <summary>A reviewer marked one reason explained, with a reason. It decides nothing.</summary>
    ReasonExplained,

    /// <summary>The author of an explanation took it back.</summary>
    ExplanationUndone,
}

/// <summary>Why a reviewer dismissed an assessment.</summary>
public enum DismissReason
{
    /// <summary>The reviewer confirmed the candidate's identity another way (a call, a reference).</summary>
    VerifiedOutsideScreenHire,

    /// <summary>The candidate explained what the signals show (a work VPN, a shared family phone).</summary>
    ExplainedByCandidate,

    /// <summary>The reviewer judged the signals to be wrong for this application.</summary>
    FalsePositive,

    /// <summary>Another reason, which the note gives.</summary>
    Other,
}

/// <summary>Why a reviewer marked a reason explained.</summary>
public enum ExplainReason
{
    /// <summary>The reviewer confirmed it with the candidate.</summary>
    ConfirmedWithCandidate,

    /// <summary>It is expected for this role (a remote role and a VPN, for example).</summary>
    ExpectedForRole,

    /// <summary>A false positive the team already knows about.</summary>
    KnownFalsePositive,

    /// <summary>Another reason, which the note gives.</summary>
    Other,
}
