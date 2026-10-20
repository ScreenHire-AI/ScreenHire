using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// One entry of an application's timeline. Append-only: the database lets the application role insert and read these
/// rows but never change or delete one, so the timeline is a record of what happened (workflow-engine.md §6).
/// </summary>
public sealed class ApplicationEvent : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    public ApplicationEventType Type { get; init; }

    public required ApplicationEventData Data { get; init; }

    /// <summary>The signed-in user who caused it, or null for the candidate or the system.</summary>
    public Guid? ActorUserId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}

public enum ApplicationEventType
{
    Submitted,
    StageChanged,
}

/// <summary>
/// An event's details. One flat record for every event type, with what does not apply left null, so the timeline API
/// and the JSONB column stay one simple shape. Add members as optional.
/// </summary>
public sealed record ApplicationEventData(string? FromStageId = null, string? ToStageId = null, string? Note = null);
