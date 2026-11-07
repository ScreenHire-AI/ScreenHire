using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Portal;

namespace ScreenHire.Api.Applications;

internal static class PortalSetup
{
    /// <summary>
    /// The candidate portal's public routes: their request validators and the rate limit. The signed links and the options
    /// come from <c>AddQuestionnaireLinks</c>, which the API and the worker both register.
    /// </summary>
    public static IServiceCollection AddScreenHirePortal(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<PublicPortalLinkRequest>, PublicPortalLinkRequestValidator>();
        services.AddSingleton<IValidator<PublicPortalReplyRequest>, PublicPortalReplyRequestValidator>();

        // Per visitor address, like the apply flow: the careers server forwards it in X-Forwarded-For, which ForwardedHeaders
        // restores only from trusted proxies (ScreenHire:Auth:TrustedProxies).
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(PortalOptions.RateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<PortalOptions>>().Value.PermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                })));
        return services;
    }
}
