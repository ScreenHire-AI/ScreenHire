using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace ScreenHire.Api.Realtime;

/// <summary>
/// The hub connections open in this process, by user and organization, so removing someone from an organization can
/// close their live updates at once instead of when their session cookie expires. SignalR has no way to find or abort
/// another caller's connections, which is why this list exists.
/// </summary>
/// <remarks>
/// In-process, like the connections themselves: one API instance holds every connection
/// (<see cref="RealtimeSetup.AddScreenHireRealtime"/>). Moving connections to Azure SignalR Service replaces this with
/// the service's own close-connections-of-a-user call.
/// </remarks>
internal sealed class RealtimeConnections
{
    private readonly ConcurrentDictionary<string, Entry> connections = new(StringComparer.Ordinal);

    public void Add(HubCallerContext context, Guid userId, Guid organizationId) =>
        connections[context.ConnectionId] = new Entry(context, userId, organizationId);

    public void Remove(string connectionId) => connections.TryRemove(connectionId, out _);

    /// <summary>Closes every connection <paramref name="userId"/> opened into <paramref name="organizationId"/>.</summary>
    public int Close(Guid userId, Guid organizationId)
    {
        var closed = 0;
        foreach (var entry in connections.Values.Where(e => e.UserId == userId && e.OrganizationId == organizationId))
        {
            entry.Context.Abort();
            closed++;
        }

        return closed;
    }

    private sealed record Entry(HubCallerContext Context, Guid UserId, Guid OrganizationId);
}
