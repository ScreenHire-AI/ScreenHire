using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Automations;

/// <summary>
/// A person or an API key gave the hiring team a task about an application. Open dashboards refetch My tasks and the
/// application's tasks and Activity. (An automation's tasks arrive with its run, <c>AutomationRunFinished</c>.)
/// <see cref="ActorUserId"/> is the member who did it (null for an API key), and <see cref="AssigneeUserId"/> the
/// person the task is for (null for anyone on the hiring team).
/// </summary>
public sealed record ApplicationTaskCreated(
    Guid OrganizationId, Guid TaskId, Guid ApplicationId, Guid JobId, DateTimeOffset CreatedAt, Guid? ActorUserId = null, Guid? AssigneeUserId = null)
    : IJobEvent;
