using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Automations;

/// <summary>
/// A person marked an application task done (a review, a to-do or a rejection review an automation made). Open
/// dashboards refetch My tasks and the application's tasks.
/// <see cref="ActorUserId"/> is the member who did it (null for an API key), and <see cref="AssigneeUserId"/> the
/// person the task is for (null for anyone on the hiring team).
/// </summary>
public sealed record ApplicationTaskCompleted(
    Guid OrganizationId, Guid TaskId, Guid ApplicationId, Guid JobId, DateTimeOffset CompletedAt, Guid? ActorUserId = null, Guid? AssigneeUserId = null)
    : IJobEvent;
