using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ScreenHire.Api.Auth;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Realtime;

/// <summary>What the hub sends to a connected client. Server to client only: changes go through the REST endpoints.</summary>
public interface IEventsClient
{
    /// <summary>One tenant event, as <see cref="RealtimeEvent"/>. The client method is <c>ReceiveEvent</c>.</summary>
    Task ReceiveEvent(RealtimeEvent realtimeEvent);
}

/// <summary>
/// The real-time hub at <see cref="RealtimeSetup.HubPath"/>: the dashboard's signed-in session cookie authenticates it,
/// each connection joins its organization's group, and a client may also subscribe to one record (a <c>job</c>) for
/// events only that record's screen needs. Events reach it from the real-time queue (<see cref="TenantEventPusher{TEvent}"/>).
/// </summary>
/// <remarks>
/// Every hub call runs in its own DI scope, which the request middleware never sees, so each call resolves its own
/// tenant from the connection's user (<see cref="Memberships"/>). A connection keeps the groups it joined until it
/// closes. Removing someone from an organization closes their connections into it (<see cref="RealtimeConnections"/>),
/// and every connection closes when the session cookie expires (<c>CloseOnAuthenticationExpiration</c>).
/// </remarks>
[Authorize]
internal sealed class EventsHub(ScreenHireDbContext db, TenantContext tenant, RealtimeConnections connections) : Hub<IEventsClient>
{
    public override async Task OnConnectedAsync()
    {
        var organizationId = await ResolveOrganizationAsync()
            ?? throw new HubException("Set up your organization before connecting to live updates.");
        connections.Add(Context, tenant.UserId!.Value, organizationId);
        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Organization(organizationId), Context.ConnectionAborted);

        // A removal that committed after the first check and closed this user's connections before this one was listed
        // would miss it. Checking again once it is listed closes that gap.
        if (await ResolveOrganizationAsync() != organizationId)
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Adds this connection to <paramref name="topic"/> <paramref name="id"/>'s group, if that record belongs to the
    /// caller's organization. Another organization's record fails exactly like one that does not exist.
    /// </summary>
    public async Task Subscribe(string topic, Guid id)
    {
        var organizationId = await ResolveOrganizationAsync();
        if (organizationId is null
            || !RealtimeTopics.Exists.TryGetValue(topic, out var exists)
            || !await exists(db, id, Context.ConnectionAborted))
        {
            throw new HubException($"There is no {topic} {id} to subscribe to.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Topic(organizationId.Value, topic, id), Context.ConnectionAborted);
    }

    /// <summary>Leaves a record's group. Leaving one the connection never joined does nothing.</summary>
    public async Task Unsubscribe(string topic, Guid id)
    {
        if (await ResolveOrganizationAsync() is { } organizationId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.Topic(organizationId, topic, id), Context.ConnectionAborted);
        }
    }

    private Task<Guid?> ResolveOrganizationAsync() =>
        Memberships.ResolveTenantAsync(Context.User, tenant, db, Context.ConnectionAborted);
}
