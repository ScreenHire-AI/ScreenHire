using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Jobs;

/// <summary>
/// A job's own workflow changed: someone edited it, or applied a template update (workflow-engine.md §7). Published with
/// the change, through the outbox, so open dashboards refetch the job's stages and "update available". Nothing in the
/// worker handles it.
/// </summary>
public sealed record JobWorkflowChanged(Guid OrganizationId, Guid JobId, int WorkflowVersion, DateTimeOffset ChangedAt) : IJobEvent;
