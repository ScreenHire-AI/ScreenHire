using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Realtime;

/// <summary>
/// Asks the worker to raise <see cref="LiveUpdatesChecked"/> for one organization. It checks the whole live-update path
/// a worker-raised event takes (worker, real-time queue, API, SignalR, browser), and is how the integration tests prove
/// that path; an operator can send it the same way.
/// </summary>
public sealed record CheckLiveUpdates(Guid OrganizationId, Guid CheckId) : ITenantMessage;

/// <summary>The worker's answer to <see cref="CheckLiveUpdates"/>, pushed to the organization's connected clients.</summary>
public sealed record LiveUpdatesChecked(Guid OrganizationId, Guid CheckId) : ITenantEvent;

public static class CheckLiveUpdatesHandler
{
    // Returned, not published: the event commits with the handler's inbox entry, so a redelivered check raises nothing.
    public static LiveUpdatesChecked Handle(CheckLiveUpdates message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new LiveUpdatesChecked(message.OrganizationId, message.CheckId);
    }
}
