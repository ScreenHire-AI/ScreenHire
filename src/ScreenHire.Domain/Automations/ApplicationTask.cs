using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Automations;

/// <summary>
/// Something a person on the hiring team should do about one application: a review, a to-do, or a decision on a
/// rejection an automation suggested. Automations create them (AssignReviewer, CreateTask, QueueForRejectionReview), and so
/// can a person or an API key (<c>JobApplication.AddTask</c>, <c>JobApplication.RequestScorecard</c>); a
/// task never changes the application by itself, which is how QueueForRejectionReview keeps a person in charge of every
/// rejection that is not an opted-in knockout (workflow-engine.md §2, guardrail 2).
/// </summary>
public sealed class ApplicationTask : IApplicationScoped
{
    /// <summary>The longest note a task holds, from a person, an API key or an automation (the column's length).</summary>
    public const int MaxNoteLength = 1000;

    /// <summary>The longest note a person adds when asking a teammate for a scorecard; the task's text leads with the scorecard.</summary>
    public const int MaxRequestNoteLength = 500;

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

    /// <summary>The library scorecard a <see cref="ApplicationTaskKind.Scorecard"/> task asks for; null on every other kind.</summary>
    public string? ScorecardTemplateId { get; init; }

    /// <summary>The automation run that created it.</summary>
    public Guid? AutomationRunId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>The person who marked it done.</summary>
    public Guid? CompletedByUserId { get; private set; }

    /// <summary>
    /// Whether a person ticks it off by hand: a to-do and a rejection review. A review and a requested scorecard are done
    /// where their work is done, when the scorecard is submitted (ADR-0017 decision 11), so nobody marks them done
    /// without doing it.
    /// </summary>
    public bool CompletesByHand => Kind is ApplicationTaskKind.Task or ApplicationTaskKind.RejectionReview;

    /// <summary>
    /// Whether <paramref name="reviewerUserId"/> submitting a scorecard from the library scorecard
    /// <paramref name="scorecardTemplateId"/> on this task's application finishes it. A review is finished by any
    /// scorecard of its assignee's, or anyone's when it is for the whole team; a requested scorecard only by its
    /// assignee's scorecard of that kind. Someone else's task never is, and nor is a task done by hand.
    /// </summary>
    public bool CompletesWith(Guid reviewerUserId, string? scorecardTemplateId) =>
        CompletedAt is null && Kind switch
        {
            ApplicationTaskKind.Review => AssigneeUserId is null || AssigneeUserId == reviewerUserId,
            ApplicationTaskKind.Scorecard => AssigneeUserId == reviewerUserId
                && scorecardTemplateId is not null
                && string.Equals(ScorecardTemplateId, scorecardTemplateId, StringComparison.Ordinal),
            _ => false,
        };

    /// <summary>
    /// What a requested scorecard's task says: "Fill in the Interview scorecard.", then the asker's note when there is
    /// one. A name that already ends in "scorecard" is not followed by a second one.
    /// </summary>
    public static string ScorecardRequestNote(string scorecardName, string? note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scorecardName);
        var name = scorecardName.Trim();
        var what = name.EndsWith("scorecard", StringComparison.OrdinalIgnoreCase) ? $"Fill in the {name}." : $"Fill in the {name} scorecard.";
        return string.IsNullOrWhiteSpace(note) ? what : $"{what} {note.Trim()}";
    }

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

    /// <summary>
    /// Opens a done task again: the Undo after ticking it. An open task stays as it is, and this returns false. Like
    /// completing, reopening changes nothing about the application.
    /// </summary>
    public bool Reopen()
    {
        if (CompletedAt is null)
        {
            return false;
        }

        CompletedAt = null;
        CompletedByUserId = null;
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

    /// <summary>Fill in a library scorecard a teammate asked for; done when the assignee submits it.</summary>
    Scorecard,
}
