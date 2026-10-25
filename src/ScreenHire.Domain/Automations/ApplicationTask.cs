using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Automations;

/// <summary>
/// Something a person on the hiring team should do about one application: a review, a to-do, or a decision on a
/// rejection an automation suggested. Automations create them (AssignReviewer, CreateTask, QueueForRejectionReview); a
/// task never changes the application by itself, which is how QueueForRejectionReview keeps a person in charge of every
/// rejection that is not an opted-in knockout (workflow-engine.md §2, guardrail 2).
/// </summary>
public sealed class ApplicationTask : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    public ApplicationTaskKind Kind { get; init; }

    /// <summary>What to do: the action's note, or a rejection's reason.</summary>
    public required string Note { get; init; }

    /// <summary>Who should do it, or null for anyone on the hiring team.</summary>
    public Guid? AssigneeUserId { get; init; }

    /// <summary>
    /// The stage the application was in when the task was made, for a review: a scorecard for that stage answers it
    /// (<c>ReviewerGate</c>). Null on tasks made before tasks kept it.
    /// </summary>
    public string? StageId { get; init; }

    /// <summary>The automation run that created it.</summary>
    public Guid? AutomationRunId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>The person who marked it done.</summary>
    public Guid? CompletedByUserId { get; private set; }

    /// <summary>
    /// Marks the task done by <paramref name="userId"/>. A task that is done already keeps who finished it and when, and
    /// this returns false. Completing a task changes nothing about the application: a rejection review is decided by
    /// moving the candidate, not by this.
    /// </summary>
    public bool Complete(Guid userId, DateTimeOffset now)
    {
        if (CompletedAt is not null)
        {
            return false;
        }

        CompletedAt = now;
        CompletedByUserId = userId;
        return true;
    }
}

public enum ApplicationTaskKind
{
    /// <summary>Review the application (AssignReviewer).</summary>
    Review,

    /// <summary>A to-do from a CreateTask action.</summary>
    Task,

    /// <summary>Decide whether to reject: a person must, the automation could not (QueueForRejectionReview).</summary>
    RejectionReview,
}
