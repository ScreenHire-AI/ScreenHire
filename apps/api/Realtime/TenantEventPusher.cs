using Microsoft.AspNetCore.SignalR;
using ScreenHire.Application.Messaging;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace ScreenHire.Api.Realtime;

/// <summary>What a client receives for one tenant event: the event type's name and the event itself.</summary>
/// <param name="Type">The event record's name, such as <c>JobPublished</c>; packages/realtime has a Zod schema per name.</param>
/// <param name="Data">The event, serialized with its runtime type (<c>object</c>, so every property is written).</param>
public sealed record RealtimeEvent(string Type, object Data);

/// <summary>
/// The API's Wolverine handler for one <see cref="ITenantEvent"/> type from the real-time queue: pushes the event to its
/// organization's group on <see cref="EventsHub"/>, or to the members who see its job, or to its record's subscribers
/// (<see cref="RealtimeGroups.For"/>).
/// One instance per event type is registered in <c>ScreenHireMessaging.ConfigureApi</c>, so a new event needs no code
/// here.
/// </summary>
internal sealed class TenantEventPusher<TEvent> : MessageHandler<TEvent>
    where TEvent : ITenantEvent
{
    protected override Task HandleAsync(TEvent message, MessageContext context, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        var hub = context.Runtime.Services.GetRequiredService<IHubContext<EventsHub, IEventsClient>>();
        return hub.Clients.Groups(RealtimeGroups.For(message)).ReceiveEvent(new RealtimeEvent(typeof(TEvent).Name, message));
    }
}

internal static class TenantEventPusher
{
    /// <summary>Makes the pusher for <paramref name="eventType"/>, for <c>AddScreenHireApiMessaging</c>.</summary>
    public static IMessageHandler For(Type eventType) =>
        (IMessageHandler)Activator.CreateInstance(typeof(TenantEventPusher<>).MakeGenericType(eventType))!;
}
