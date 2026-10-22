using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenHire.Api.Configuration;

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
        builder.Services.AddSingleton<RealtimeConnections>();

        // A WebSocket upgrade is not subject to CORS, and the Lax session cookie rides along from any same-site page,
        // such as a tenant careers page under the same apex. Only the dashboard's origin may open the hub's WebSocket.
        // MapHub runs UseWebSockets with these options; a request without an Origin (not a browser) is let through.
        // Read now, like the CORS policy (AuthenticationSetup), not through IOptions, which validates DomainOptions and
        // would fail while the build generates the OpenAPI document with no domains configured.
        var domains = builder.Configuration.GetSection(DomainOptions.Section).Get<DomainOptions>() ?? new DomainOptions();
        if (domains.AppHost.Length > 0)
        {
            builder.Services.Configure<WebSocketOptions>(webSockets =>
                webSockets.AllowedOrigins.Add(domains.GetAppOrigin().GetLeftPart(UriPartial.Authority)));
        }
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
