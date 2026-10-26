using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Automations;

/// <summary>
/// A person marked an application task done (a review, a to-do or a rejection review an automation made). Open
/// dashboards refetch My tasks and the application's tasks.
/// </summary>
public sealed record ApplicationTaskCompleted(Guid OrganizationId, Guid TaskId, Guid ApplicationId, Guid JobId, DateTimeOffset CompletedAt)
    : IJobEvent;
