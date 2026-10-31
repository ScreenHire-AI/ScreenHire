using FluentValidation;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;

namespace ScreenHire.Api.Branding;

internal static class BrandingSetup
{
    /// <summary>Settings, Branding: brand image uploads and their request validator (docs/architecture/branding.md).</summary>
    public static IHostApplicationBuilder AddScreenHireBranding(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<BrandingOptions>().BindConfiguration(BrandingOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton<BrandImageUploads>();
        builder.Services.AddSingleton<IValidator<BrandImageUploadRequest>, BrandImageUploadRequestValidator>();
        return builder;
    }
}
