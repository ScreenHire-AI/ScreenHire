using System.Net;

namespace ScreenHire.Api.OAuth;

/// <summary>
/// The redirect URIs a client may register and be sent back to (MCP authorization, RFC 8252 section 7.3): HTTPS, or HTTP
/// on a loopback address, which a desktop client such as Claude Code listens on with a port it picks each time. A request
/// must name a registered URI exactly; only a loopback URI's port may differ.
/// </summary>
internal static class OAuthRedirects
{
    public const int MaxLength = 2048;
    public const int MaxPerClient = 10;

    public static bool IsAllowed(string? value) =>
        value is { Length: > 0 and <= MaxLength }
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.UserInfo.Length == 0
        && uri.Fragment.Length == 0
        && ((uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0) || (uri.Scheme == Uri.UriSchemeHttp && IsLoopback(uri)));

    /// <summary>Whether <paramref name="requested"/> is <paramref name="registered"/>, or the same loopback URI on another port.</summary>
    public static bool Matches(string registered, string requested)
    {
        if (string.Equals(registered, requested, StringComparison.Ordinal))
        {
            return true;
        }

        return Uri.TryCreate(registered, UriKind.Absolute, out var a)
            && Uri.TryCreate(requested, UriKind.Absolute, out var b)
            && a.Scheme == Uri.UriSchemeHttp && b.Scheme == Uri.UriSchemeHttp
            && IsLoopback(a) && IsLoopback(b)
            && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
            && a.PathAndQuery == b.PathAndQuery;
    }

    /// <summary>Whether the host is the local machine: <c>localhost</c> or a loopback address.</summary>
    public static bool IsLoopback(Uri uri) =>
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
}
