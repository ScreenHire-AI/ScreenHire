using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Automations;

/// <summary>
/// A person opened a done task again, the Undo after ticking it. Open dashboards refetch My tasks and the
/// application's tasks.
/// <see cref="ActorUserId"/> is the member who did it (null for an API key), and <see cref="AssigneeUserId"/> the
/// person the task is for (null for anyone on the hiring team).
/// </summary>
public sealed record ApplicationTaskReopened(
    Guid OrganizationId, Guid TaskId, Guid ApplicationId, Guid JobId, DateTimeOffset ReopenedAt, Guid? ActorUserId = null, Guid? AssigneeUserId = null)
    : IJobEvent;
