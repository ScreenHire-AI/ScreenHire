using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Privacy;

namespace ScreenHire.Api.Applications;

internal static class DataRequestSetup
{
    /// <summary>
    /// The public data request routes: their request validators, the rate limit, and the API's half of the store of copies
    /// of candidates' data. The signed links and the options come from <c>AddScreenHirePrivacy</c>.
    /// </summary>
    public static IServiceCollection AddScreenHireDataRequests(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<PublicDataRequestLinkRequest>, PublicDataRequestLinkRequestValidator>();
        services.AddSingleton<IValidator<PublicDataRequestChoice>, PublicDataRequestChoiceValidator>();
        services.TryAddSingleton<IDataExportStore, DataExportStore>();

        // Per visitor address, like the apply flow: the careers server forwards it in X-Forwarded-For, which ForwardedHeaders
        // restores only from trusted proxies (ScreenHire:Auth:TrustedProxies).
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(DataRequestOptions.RateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<DataRequestOptions>>().Value.PermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                })));
        return services;
    }
}
