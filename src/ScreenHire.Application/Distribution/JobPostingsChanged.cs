using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Distribution;

/// <summary>
/// A job's postings changed state or version: raised once by each publish or close transaction in the API, however many
/// boards it touched, and once each time the worker records a board's answer. Open dashboards refetch the job's
/// postings (<c>GET /v1/jobs/{jobId}/postings</c>) on it. One event per change, not one per board, keeps the queues
/// quiet: a publish touches every board at once.
/// </summary>
public sealed record JobPostingsChanged(Guid OrganizationId, Guid JobId, DateTimeOffset ChangedAt) : ITenantEvent;
