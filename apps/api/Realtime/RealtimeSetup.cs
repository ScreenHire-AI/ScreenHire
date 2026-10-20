using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenHire.Api.Realtime;

/// <summary>
/// Live updates for the dashboard (docs/architecture/realtime.md): SignalR with the same JSON rules as the REST API,
/// and the hub at <see cref="HubPath"/>. Wolverine delivers the events (<c>AddScreenHireApiMessaging</c>).
/// </summary>
internal static class RealtimeSetup
{
    public const string HubPath = "/v1/hubs/events";

    /// <summary>
    /// The JSON rules for event payloads, applied on top of SignalR's defaults (camelCase): enums as strings and strict
    /// numbers, like the REST API, so packages/realtime's Zod schemas match what the generated client sees.
    /// </summary>
    public static void ConfigureJson(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.NumberHandling = JsonNumberHandling.Strict;
        options.Converters.Add(new JsonStringEnumConverter());
    }

    public static WebApplicationBuilder AddScreenHireRealtime(this WebApplicationBuilder builder)
    {
        // Self-hosted SignalR keeps connections in this process, and one API instance consumes the real-time queue. To
        // run more than one API instance, move connections to Azure SignalR Service (docs/architecture/realtime.md).
        builder.Services.AddSignalR().AddJsonProtocol(options => ConfigureJson(options.PayloadSerializerOptions));
        return builder;
    }

    public static IEndpointRouteBuilder MapRealtimeEndpoints(this IEndpointRouteBuilder app)
    {
        // The fallback policy already requires a signed-in user; said again here for the reader. An anonymous negotiate
        // or WebSocket request gets 401 from the cookie scheme.
        app.MapHub<EventsHub>(HubPath, options => options.CloseOnAuthenticationExpiration = true)
            .RequireAuthorization();
        return app;
    }
}
