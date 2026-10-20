namespace ScreenHire.Api.Auth;

/// <summary>Where to send the browser after sign-in: only ever a path on the dashboard, never another site.</summary>
internal static class ReturnUrls
{
    public const string Default = "/";

    /// <summary>
    /// <paramref name="returnUrl"/> when it is a local path, otherwise <see cref="Default"/>. Rejects absolute URLs,
    /// scheme-relative <c>//host</c> and <c>/\host</c> (browsers treat a backslash as a slash), and control characters.
    /// </summary>
    public static string Sanitize(string? returnUrl) =>
        returnUrl is { Length: > 0 and <= 2048 }
        && returnUrl[0] == '/'
        && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'))
        && !returnUrl.Any(char.IsControl)
            ? returnUrl
            : Default;
}
