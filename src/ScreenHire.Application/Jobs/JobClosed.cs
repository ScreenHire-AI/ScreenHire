using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Jobs;

/// <summary>
/// A job stopped taking applications. Published with the change, through the outbox. Job distribution will take its
/// board postings down from here (docs/architecture/job-distribution.md); today the dashboard's jobs list refreshes on it.
/// </summary>
public sealed record JobClosed(Guid OrganizationId, Guid JobId, DateTimeOffset ClosedAt) : ITenantEvent;
