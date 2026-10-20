using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Integrity;

/// <summary>
/// A reviewer's action on an application's integrity assessments: the audit log (integrity-engine.md §7). Append-only:
/// the database lets the application role insert and read these rows but never change or delete one. There is no
/// reject action here, on purpose (CLAUDE.md non-negotiable 2).
/// </summary>
public sealed class IntegrityReviewAction : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    /// <summary>The assessment the action is about; null for a note on the application's integrity as a whole.</summary>
    public Guid? AssessmentId { get; init; }

    public IntegrityReviewActionType Type { get; init; }

    /// <summary>Why a reviewer dismissed an assessment. Required for a dismissal, null otherwise.</summary>
    public DismissReason? DismissReason { get; init; }

    /// <summary>The reviewer's words: required for a note and for a dismissal with <see cref="Integrity.DismissReason.Other"/>.</summary>
    public string? Note { get; init; }

    public Guid ActorUserId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public const int MaxNoteLength = 2000;

    public static IntegrityReviewAction Dismiss(
        IntegrityAssessment assessment, DismissReason reason, string? note, Guid actorUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (!assessment.IsRaised)
        {
            throw new InvalidOperationException("Only an assessment above the lowest level can be dismissed.");
        }

        if (reason == Integrity.DismissReason.Other && string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("A dismissal for another reason needs a note.", nameof(note));
        }

        return new()
        {
            OrganizationId = assessment.OrganizationId,
            ApplicationId = assessment.ApplicationId,
            AssessmentId = assessment.Id,
            Type = IntegrityReviewActionType.Dismissed,
            DismissReason = reason,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ActorUserId = actorUserId,
            OccurredAt = now,
        };
    }

    public static IntegrityReviewAction AddNote(
        Guid organizationId, Guid applicationId, Guid? assessmentId, string note, Guid actorUserId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        return new()
        {
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            AssessmentId = assessmentId,
            Type = IntegrityReviewActionType.NoteAdded,
            Note = note.Trim(),
            ActorUserId = actorUserId,
            OccurredAt = now,
        };
    }
}

public enum IntegrityReviewActionType
{
    /// <summary>A reviewer set an assessment aside, with a reason.</summary>
    Dismissed,

    NoteAdded,
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
