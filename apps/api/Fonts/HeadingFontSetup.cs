using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace ScreenHire.Api.Fonts;

internal static class HeadingFontSetup
{
    /// <summary>The picker's previews: per person, because each first one makes the server download a file.</summary>
    public const string PreviewRateLimitPolicy = "heading-font-previews";

    /// <summary>
    /// Heading fonts (docs/architecture/branding.md section 3): the sandbox catalogue when
    /// <c>ScreenHire:Fonts:Sandbox:Enabled</c> is on, Fontsource otherwise; the in-memory catalogue, storage, hosting and
    /// previews.
    /// </summary>
    public static IHostApplicationBuilder AddScreenHireHeadingFonts(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddOptions<FontsOptions>().BindConfiguration(FontsOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        if (builder.Configuration.GetValue<bool>($"{FontsOptions.Section}:Sandbox:Enabled"))
        {
            services.AddSingleton<IFontSource, SandboxFontSource>();
        }
        else
        {
            services.AddHttpClient<IFontSource, FontsourceSource>();
        }

        services.AddMemoryCache();
        services.AddSingleton<HeadingFontStore>();
        services.AddScoped<HeadingFontCatalogue>();
        services.AddScoped<HeadingFontHosting>();
        services.AddScoped<HeadingFontPreviews>();
        services.AddRateLimiter(options => options.AddPolicy(PreviewRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = context.RequestServices.GetRequiredService<IOptions<FontsOptions>>().Value.PreviewsPerMinute,
                Window = TimeSpan.FromMinutes(1),
            })));
        return builder;
    }
}
