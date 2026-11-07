using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ScreenHire.Application.Billing;

/// <summary>
/// Checks Stripe's signed webhook (https://docs.stripe.com/webhooks#verify-manually): the <c>Stripe-Signature</c> header is
/// <c>t=timestamp,v1=signature</c>, and each <c>v1</c> is the lowercase hex HMAC-SHA256, keyed with the endpoint's signing
/// secret, of <c>timestamp + "." + body</c>.
/// </summary>
public static class StripeSignature
{
    public const string Header = "Stripe-Signature";

    /// <summary>How far the timestamp may be from now. A captured post replayed later fails.</summary>
    public static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(5);

    /// <summary>True when one <c>v1</c> matches and the timestamp is recent. Never throws.</summary>
    public static bool IsValid(string? secret, string? header, string body, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        string? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(','))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            switch (pair[0].Trim())
            {
                case "t":
                    timestamp = pair[1].Trim();
                    break;
                case "v1":
                    signatures.Add(pair[1].Trim());
                    break;
            }
        }

        if (timestamp is null || signatures.Count == 0 || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        try
        {
            // Inside the try: a timestamp beyond the year 9999 makes FromUnixTimeSeconds throw, and an anonymous post must get a refusal.
            if ((now - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > MaxSkew)
            {
                return false;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var expected = Sign(secret, timestamp, body);
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        return signatures.Any(candidate => CryptographicOperations.FixedTimeEquals(expectedBytes, Encoding.ASCII.GetBytes(candidate)));
    }

    /// <summary>The <c>v1</c> value for <paramref name="body"/>. Tests sign with it.</summary>
    public static string Sign(string secret, string timestamp, string body)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>The whole header for <paramref name="body"/>, as Stripe sends it. Tests build deliveries with it.</summary>
    public static string HeaderFor(string secret, DateTimeOffset at, string body)
    {
        var timestamp = at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return $"t={timestamp},v1={Sign(secret, timestamp, body)}";
    }
}
