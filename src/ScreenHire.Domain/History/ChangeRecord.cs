using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.History;

/// <summary>
/// The kinds of thing whose changes are recorded (design system T6): configuration people edit, never candidate data,
/// so the history has no retention limit (docs/architecture/change-history.md). Stored as text.
/// </summary>
public enum ChangeSubjectKind
{
    HiringTemplate,
    EmailTemplate,
    Job,
    ScorecardTemplate,

    /// <summary>A custom role (<see cref="CustomRole"/>); built-in roles never change.</summary>
    Role,
}

/// <summary>
/// One save's changes to one tracked object: who made them, when, and what changed, in the plain lines the History
/// sheet shows. Written by the persistence layer in the same transaction as the change, and never changed afterwards
/// (the application role has no UPDATE or DELETE on the table).
/// </summary>
public sealed class ChangeRecord : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The database's insertion order: the history's order and its paging cursor.</summary>
    public long Sequence { get; init; }

    public ChangeSubjectKind SubjectKind { get; init; }

    /// <summary>
    /// The object's id: a Guid in "D" form, or a built-in email template's id, which stays the same however often its
    /// row is restored and added again.
    /// </summary>
    public required string SubjectId { get; init; }

    /// <summary>The member who made the change, by id, so the screen shows their current name.</summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>The organization API key that made the change. With no user and no key, ScreenHire made it.</summary>
    public Guid? ActorApiKeyId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public required IReadOnlyList<ChangeLine> Lines { get; init; }
}
