namespace ScreenHire.Application.Messaging;

/// <summary>
/// A message about one organization's data. The worker makes <see cref="OrganizationId"/> the tenant before the handler
/// runs (<see cref="TenantMessageMiddleware"/>), so the tenant query filter and row-level security scope everything the
/// handler reads or writes. A message that touches tenant data implements this, or its handler sees no tenant rows.
/// </summary>
public interface ITenantMessage
{
    Guid OrganizationId { get; }
}

/// <summary>
/// An integration event that belongs to one organization and that its signed-in users may see. The API pushes every
/// <see cref="ITenantEvent"/> to that organization's connected clients (docs/architecture/realtime.md), so a feature
/// only defines the event. Carry ids and the facts a client needs to refresh, never personal data (names, emails,
/// answers): the payload goes to every signed-in member's browser.
/// </summary>
public interface ITenantEvent : ITenantMessage;

/// <summary>
/// A tenant event that only screens showing one record need, such as progress on one application. The API pushes it
/// to the clients that subscribed to <see cref="Topic"/> and <see cref="TopicId"/> (validated against their
/// organization when they subscribe), instead of to the whole organization.
/// </summary>
public interface ITopicEvent : ITenantEvent
{
    /// <summary>The kind of record, such as <c>job</c>: a topic the hub accepts subscriptions for.</summary>
    string Topic { get; }

    /// <summary>The record's id.</summary>
    Guid TopicId { get; }
}

/// <summary>
/// A tenant event about one job, or something under it such as an application. The API pushes it only to the members
/// who see that job: everyone who sees all jobs, and the members on its hiring team (ADR-0015,
/// docs/architecture/realtime.md). Every tenant event that carries a job id implements it.
/// </summary>
public interface IJobEvent : ITenantEvent
{
    Guid JobId { get; }
}
