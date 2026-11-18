namespace ScreenHire.Api.Configuration;

/// <summary>
/// Where ScreenHire's own legal documents live (<c>ScreenHire:Legal</c>): the sign-in page's agreement line links to each
/// one that is set and leaves out the rest. The documents are the owner's to write, so nothing is configured in the
/// repository. An address is an absolute http or https URL; anything else is treated as not set.
/// </summary>
public sealed class LegalOptions
{
    public const string Section = "ScreenHire:Legal";

    /// <summary>The Terms of Service.</summary>
    public string? TermsUrl { get; set; }

    /// <summary>The Privacy Policy.</summary>
    public string? PrivacyUrl { get; set; }

    /// <summary>The Data Processing Agreement, with its sub-processor list.</summary>
    public string? DpaUrl { get; set; }

    /// <summary>The address as the page may link it: absolute and http or https, never a <c>javascript:</c> or relative one.</summary>
    public static string? Link(string? address) =>
        Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;
}
