using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Views;

/// <summary>
/// A view was saved, renamed, shared, made private, deleted or restored (docs/architecture/saved-views.md). Carries the
/// view's id only, never its name or filters: the dashboards that can see it refetch the list. A person's own hidden
/// built-in views are no event, because nobody else's list changes. <see cref="ActorUserId"/> is who did it, null for an
/// API key.
/// </summary>
public sealed record ViewsChanged(Guid OrganizationId, Guid ViewId, DateTimeOffset ChangedAt, Guid? ActorUserId = null) : ITenantEvent;
