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

    /// <summary>The automation run that created it.</summary>
    public Guid? AutomationRunId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; private set; }
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
