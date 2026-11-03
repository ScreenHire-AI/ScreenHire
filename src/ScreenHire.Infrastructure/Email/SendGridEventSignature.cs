using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ScreenHire.Infrastructure.Email;

/// <summary>
/// Checks SendGrid's signed event webhook (https://www.twilio.com/docs/sendgrid/for-developers/tracking-events/getting-started-event-webhook-security-features):
/// an ECDSA P-256 signature, base64 of DER, over the timestamp header followed by the raw request body, made with the
/// private half of the key pair whose public half (base64 SubjectPublicKeyInfo) SendGrid shows in its settings.
/// </summary>
public static class SendGridEventSignature
{
    public const string SignatureHeader = "X-Twilio-Email-Event-Webhook-Signature";
    public const string TimestampHeader = "X-Twilio-Email-Event-Webhook-Timestamp";

    /// <summary>How far the timestamp may be from now. A captured post replayed later fails.</summary>
    public static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(5);

    /// <summary>True when the signature is valid for <paramref name="body"/> and the timestamp is recent. Never throws.</summary>
    public static bool IsValid(string? publicKey, string? signature, string? timestamp, ReadOnlySpan<byte> body, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(timestamp))
        {
            return false;
        }

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        try
        {
            // Inside the try: a timestamp beyond the year 9999 makes FromUnixTimeSeconds throw, and an anonymous post must get a refusal.
            var sent = DateTimeOffset.FromUnixTimeSeconds(seconds);
            if ((now - sent).Duration() > MaxSkew)
            {
                return false;
            }

            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            var stamp = Encoding.UTF8.GetBytes(timestamp);
            var signed = new byte[stamp.Length + body.Length];
            stamp.CopyTo(signed, 0);
            body.CopyTo(signed.AsSpan(stamp.Length));
            return key.VerifyData(signed, Convert.FromBase64String(signature), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }
}
