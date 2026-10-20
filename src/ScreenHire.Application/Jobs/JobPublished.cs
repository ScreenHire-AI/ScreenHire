using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Jobs;

/// <summary>A job went live on the organization's careers page. Published with the change, through the outbox.</summary>
public sealed record JobPublished(Guid OrganizationId, Guid JobId, DateTimeOffset PublishedAt) : ITenantEvent;
