using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Privacy;

namespace ScreenHire.Application.Privacy;

/// <summary>
/// A candidate asked for a copy of their data or for it to be erased (docs/architecture/data-requests.md). Published with
/// the request, through the outbox. It carries the request's id, kind and time only: the dashboard learns who asked by
/// reading the request through the REST API, which applies its own authorization.
/// </summary>
public sealed record DataRequestReceived(Guid OrganizationId, Guid RequestId, DataRequestKind Kind, DateTimeOffset ReceivedAt) : ITenantEvent;

/// <summary>
/// A team member answered a data request (sent the copy, erased, declined), or the copy was built or expired. The
/// Data requests list refetches.
/// </summary>
public sealed record DataRequestChanged(Guid OrganizationId, Guid RequestId) : ITenantEvent;
