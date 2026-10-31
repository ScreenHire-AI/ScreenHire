using System.ComponentModel.DataAnnotations;

namespace ScreenHire.Api.Configuration;

/// <summary>The organization's brand images (<c>ScreenHire:Branding</c>, docs/architecture/branding.md).</summary>
public sealed class BrandingOptions
{
    public const string Section = "ScreenHire:Branding";

    /// <summary>The private blob container brand images go to. The API serves them; storage never does.</summary>
    [Required, RegularExpression("^[a-z0-9](?!.*--)[a-z0-9-]{1,61}[a-z0-9]$")]
    public string Container { get; set; } = "brand-images";

    /// <summary>How long an upload link stays valid.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "01:00:00")]
    public TimeSpan UploadLinkLifetime { get; set; } = TimeSpan.FromMinutes(15);
}
