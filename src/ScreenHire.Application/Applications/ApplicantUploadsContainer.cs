using Microsoft.Extensions.Configuration;

namespace ScreenHire.Application.Applications;

/// <summary>
/// The blob container candidate uploads go to, as the worker reads it: the API's <c>ScreenHire:Apply:Container</c>
/// setting, with the same default as its <c>ApplyOptions</c>.
/// </summary>
public static class ApplicantUploadsContainer
{
    public static string From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration["ScreenHire:Apply:Container"] is { Length: > 0 } name ? name : "applicant-uploads";
    }
}
