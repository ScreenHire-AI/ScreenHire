using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Integrity;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Infrastructure.Integrity;

namespace ScreenHire.Api.Applications;

internal static class ApplySetup
{
    /// <summary>
    /// The careers apply flow: upload links, the bot challenge, telemetry sessions, saved drafts, request validators and
    /// the rate limits.
    /// </summary>
    public static IHostApplicationBuilder AddScreenHireApply(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddOptions<ApplyOptions>().BindConfiguration(ApplyOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<TurnstileOptions>().BindConfiguration(TurnstileOptions.Section).ValidateDataAnnotations().ValidateOnStart();

        // The integrity engine's settings the submission needs: the device-hash key and the raw data's retention.
        services.AddIntegrityOptions();

        services.AddSingleton<ApplicantUploads>();
        services.AddSingleton<TelemetrySessions>();
        services.AddQuestionnaireLinks();
        services.AddSingleton<ApplicationDrafts>();
        services.AddHttpClient<TurnstileVerifier>();
        services.AddSingleton<IValidator<PublicApplicationRequest>, PublicApplicationRequestValidator>();
        services.AddSingleton<IValidator<PublicApplicationDraftRequest>, PublicApplicationDraftRequestValidator>();
        services.AddSingleton<IValidator<UploadRequest>, UploadRequestValidator>();
        services.AddSingleton<IValidator<TelemetrySnapshotInput>, TelemetrySnapshotInputValidator>();

        // Per candidate address. The careers server forwards it in X-Forwarded-For, which ForwardedHeaders restores only
        // from trusted proxies (ScreenHire:Auth:TrustedProxies; loopback by default), so put the careers servers there.
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(ApplyOptions.RateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<ApplyOptions>>().Value.PermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                })));
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(ApplyOptions.TelemetryRateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<ApplyOptions>>().Value.TelemetryPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                })));

        return builder;
    }
}
