namespace ScreenHire.Application.Messaging;

/// <summary>
/// An integration event that belongs to one organization and that its signed-in users may see. The real-time work
/// fans every <see cref="ITenantEvent"/> out to that organization's clients, so a feature only defines the event.
/// Carry ids and the facts a client needs to refresh, never personal data (names, emails, answers).
/// </summary>
public interface ITenantEvent
{
    Guid OrganizationId { get; }
}
