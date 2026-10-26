using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Automations;

/// <summary>
/// A person opened a done task again, the Undo after ticking it. Open dashboards refetch My tasks and the
/// application's tasks.
/// </summary>
public sealed record ApplicationTaskReopened(Guid OrganizationId, Guid TaskId, Guid ApplicationId, Guid JobId, DateTimeOffset ReopenedAt)
    : IJobEvent;
