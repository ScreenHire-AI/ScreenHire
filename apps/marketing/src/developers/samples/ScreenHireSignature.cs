using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ScreenHire.Webhooks;

/// <summary>Checks a ScreenHire webhook before you trust it.</summary>
public static class ScreenHireSignature
{
    /// <summary>
    /// Whether <paramref name="header"/> (the ScreenHire-Signature header) signs <paramref name="rawBody"/>, the request
    /// body exactly as it arrived, with <paramref name="secret"/>, your endpoint's signing secret (shws_...). A request
    /// more than <paramref name="tolerance"/> old (five minutes unless you say) is refused, so a captured delivery cannot
    /// be sent again later.
    /// </summary>
    public static bool Verify(string rawBody, string header, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(secret);
        var parts = header.Split(',').Select(part => part.Split('=', 2)).Where(part => part.Length == 2).ToDictionary(part => part[0], part => part[1]);
        if (!parts.TryGetValue("t", out var timestamp) || !parts.TryGetValue("v1", out var hex)
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var age = (now ?? DateTimeOffset.UtcNow) - DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (age.Duration() > (tolerance ?? TimeSpan.FromMinutes(5)) || hex.Length != 64 || !hex.All(Uri.IsHexDigit))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}"));
        return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(hex));
    }
}
