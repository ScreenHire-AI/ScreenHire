using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace ScreenHire.Api.Realtime;

/// <summary>
/// The hub connections open in this process, by user and organization, so removing someone from an organization can
/// close their live updates at once instead of when their session cookie expires, and a change to the jobs they see can
/// close them or add a job's group (ADR-0015). SignalR has no way to find or abort another caller's connections, which
/// is why this list exists.
/// </summary>
/// <remarks>
/// In-process, like the connections themselves: one API instance holds every connection
/// (<see cref="RealtimeSetup.AddScreenHireRealtime"/>). Moving connections to Azure SignalR Service replaces this with
/// the service's own close-connections-of-a-user call.
/// </remarks>
internal sealed class RealtimeConnections(IHubContext<EventsHub, IEventsClient> hub)
{
    private readonly ConcurrentDictionary<string, Entry> connections = new(StringComparer.Ordinal);

    /// <summary>Lists a connection, and whether its member saw all jobs when it opened (so it is in the all-jobs group).</summary>
    public void Add(HubCallerContext context, Guid userId, Guid organizationId, bool seesAllJobs) =>
        connections[context.ConnectionId] = new Entry(context, userId, organizationId, seesAllJobs);

    public void Remove(string connectionId) => connections.TryRemove(connectionId, out _);

    /// <summary>Closes every connection <paramref name="userId"/> opened into <paramref name="organizationId"/>.</summary>
    public void Close(Guid userId, Guid organizationId)
    {
        foreach (var entry in connections.Values.Where(e => e.UserId == userId && e.OrganizationId == organizationId))
        {
            entry.Context.Abort();
        }
    }

    /// <summary>
    /// Adds <paramref name="jobId"/>'s team group to every connection <paramref name="userId"/> opened into
    /// <paramref name="organizationId"/> on Only jobs they're on, after they joined its hiring team, so its events reach
    /// them without a reconnect. A connection in the all-jobs group has them already.
    /// </summary>
    public async Task JoinTeamAsync(Guid userId, Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        foreach (var entry in connections.Values.Where(e => e.UserId == userId && e.OrganizationId == organizationId && !e.SeesAllJobs))
        {
            await hub.Groups.AddToGroupAsync(entry.Context.ConnectionId, RealtimeGroups.Team(organizationId, jobId), cancellationToken);
        }
    }

    private sealed record Entry(HubCallerContext Context, Guid UserId, Guid OrganizationId, bool SeesAllJobs);
}
