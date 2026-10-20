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
/// An integration event that belongs to one organization and that its signed-in users may see. The real-time work
/// fans every <see cref="ITenantEvent"/> out to that organization's clients, so a feature only defines the event.
/// Carry ids and the facts a client needs to refresh, never personal data (names, emails, answers).
/// </summary>
public interface ITenantEvent : ITenantMessage;
