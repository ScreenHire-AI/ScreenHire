using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Configuration;

namespace ScreenHire.Api.OAuth;

/// <summary>
/// Options, rate limits and CORS for the OAuth authorization server (docs/architecture/oauth-server.md, ADR-0028). The
/// sign-in handler and the policies that use it are in <see cref="Auth.ApiKeyAuthentication"/>, beside the key handler.
/// </summary>
internal static class OAuthSetup
{
    /// <summary>The per-address limit on registrations, per hour.</summary>
    public const string RegisterLimit = "oauth-register";

    /// <summary>The per-address limit on the token and revocation endpoints, per minute.</summary>
    public const string TokenLimit = "oauth-token";

    /// <summary>
    /// Discovery, registration, token and revocation answer any origin, without credentials, so a browser-based client
    /// can sign in. Every route here is anonymous and returns nothing that is not public or issued to the caller.
    /// </summary>
    public const string CorsPolicy = "oauth-protocol";

    public static WebApplicationBuilder AddScreenHireOAuth(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var options = services.AddOptions<OAuthOptions>().BindConfiguration(OAuthOptions.Section).ValidateDataAnnotations();
        if (!Auth.AuthenticationSetup.GeneratingOpenApiDocument)
        {
            options.ValidateOnStart();
        }

        services.AddScoped<OAuthServer>();

        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(RegisterLimit, context => PerAddress(context, o => o.RegistrationsPerHour, TimeSpan.FromHours(1)));
            limiter.AddPolicy(TokenLimit, context => PerAddress(context, o => o.TokenRequestsPerMinute, TimeSpan.FromMinutes(1)));
        });

        services.AddCors(cors => cors.AddPolicy(CorsPolicy, policy => policy
            .AllowAnyOrigin()
            .WithMethods("GET", "POST")
            .WithHeaders("Content-Type", "Authorization", "MCP-Protocol-Version")));
        return builder;
    }

    private static RateLimitPartition<string> PerAddress(HttpContext context, Func<OAuthOptions, int> permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits(context.RequestServices.GetRequiredService<IOptions<OAuthOptions>>().Value),
                Window = window,
            });
}
