using System.Net;

namespace ScreenHire.Domain.Organizations;

/// <summary>
/// The sites allowed to show an organization's careers pages in a frame (docs/architecture/careers-widget.md). Each is an
/// origin, written the way a browser writes <c>location.origin</c>: <c>https://www.example.com</c>, lowercase, with a
/// port only when it is not the scheme's default. The careers pages send them in <c>frame-ancestors</c>, so each must be
/// something a CSP host source can say: a name, never an IP address.
/// </summary>
public static class EmbedOrigins
{
    /// <summary>How many sites one organization may list.</summary>
    public const int MaxCount = 20;

    /// <summary>The longest origin we keep, in characters.</summary>
    public const int MaxLength = 200;

    /// <summary>
    /// The origin <paramref name="input"/> names, or the problem with it in words. A bare host gets <c>https://</c>, and a
    /// page's address keeps only its origin, so a pasted link works. Only https, except plain http to this machine
    /// (<c>localhost</c>, <c>*.localhost</c>, <c>127.0.0.1</c>) when <paramref name="allowLoopback"/> is on, for trying
    /// the widget on a local copy of a site.
    /// </summary>
    public static (string? Origin, string? Problem) Normalize(string? input, bool allowLoopback)
    {
        var text = input?.Trim() ?? "";
        if (text.Length == 0)
        {
            return (null, "Enter the site's address.");
        }

        if (text.Contains('*', StringComparison.Ordinal))
        {
            return (null, "Name each site in full, without a *.");
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = $"https://{text}";
        }

        if (text.Length > MaxLength * 4 || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
            || uri.Host.Length == 0)
        {
            return (null, "Enter a site's address, such as https://www.example.com.");
        }

        if (uri.UserInfo.Length > 0)
        {
            return (null, "A site's address has no user name or password.");
        }

        var host = uri.IdnHost.ToLowerInvariant();
        var loopback = host is "localhost" or "127.0.0.1" || host.EndsWith(".localhost", StringComparison.Ordinal);
        if (uri.HostNameType is UriHostNameType.IPv6 || (IPAddress.TryParse(host, out _) && !(loopback && allowLoopback)))
        {
            return (null, "Use the site's name, not its IP address.");
        }

        if (uri.Scheme == "http" && !(loopback && allowLoopback))
        {
            return (null, "A site that embeds the careers page uses https.");
        }

        if (loopback && !allowLoopback)
        {
            return (null, "A site that embeds the careers page is on the internet, not this computer.");
        }

        var origin = uri.IsDefaultPort ? $"{uri.Scheme}://{host}" : $"{uri.Scheme}://{host}:{uri.Port}";
        return origin.Length > MaxLength
            ? (null, $"A site's address is at most {MaxLength} characters.")
            : (origin, null);
    }

    /// <summary>
    /// The list as it is kept: each origin normalized, duplicates dropped, order kept. Throws
    /// <see cref="ArgumentException"/> for an entry that does not normalize or for more than <see cref="MaxCount"/>; the
    /// API checks both first and says why.
    /// </summary>
    public static List<string> NormalizeAll(IEnumerable<string> inputs, bool allowLoopback)
    {
        var origins = new List<string>();
        foreach (var input in inputs)
        {
            var (origin, problem) = Normalize(input, allowLoopback);
            if (origin is null)
            {
                throw new ArgumentException(problem, nameof(inputs));
            }

            if (!origins.Contains(origin, StringComparer.Ordinal))
            {
                origins.Add(origin);
            }
        }

        return origins.Count > MaxCount
            ? throw new ArgumentException($"At most {MaxCount} sites can embed the careers page.", nameof(inputs))
            : origins;
    }
}
