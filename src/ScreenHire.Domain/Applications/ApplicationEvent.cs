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
    TagAdded,

    /// <summary>The integrity engine assessed the application, or reassessed it with a different result.</summary>
    IntegrityAssessed,
}

/// <summary>
/// An event's details. One flat record for every event type, with what does not apply left null, so the timeline API
/// and the JSONB column stay one simple shape. Add members as optional.
/// </summary>
/// <remarks>
/// <c>automationRunId</c>: The automation run that made the change; null when a person or the candidate did. The engine
/// follows it to find which automations led to a move (depth limit and cycle detection).
/// <c>authenticityLevel</c>, <c>aiAssistanceLevel</c>, <c>rulesetVersion</c>: For <see cref="ApplicationEventType.IntegrityAssessed"/>,
/// the levels as they stood after the assessment; automation conditions read them as integrity facts.
/// </remarks>
public sealed record ApplicationEventData(
    string? FromStageId = null,
    string? ToStageId = null,
    string? Note = null,
    string? Tag = null,
    Guid? AutomationRunId = null,
    string? AuthenticityLevel = null,
    string? AiAssistanceLevel = null,
    string? RulesetVersion = null);
