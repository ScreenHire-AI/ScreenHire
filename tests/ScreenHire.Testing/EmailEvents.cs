using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ScreenHire.Testing;

/// <summary>
/// Signs SendGrid event webhook posts the way SendGrid does (an ECDSA P-256 signature, base64 of DER, over the timestamp and
/// the body), with a key pair made once per test run. The stack under test is given the public half
/// (<c>ScreenHire:Email:SendGrid:EventWebhookPublicKey</c>), so a bounce can be reported with no SendGrid.
/// </summary>
public static class EmailEvents
{
    public const string SignatureHeader = "X-Twilio-Email-Event-Webhook-Signature";
    public const string TimestampHeader = "X-Twilio-Email-Event-Webhook-Timestamp";

    /// <summary>The password of the inbound webhook's HTTP Basic credentials (user <c>sendgrid</c>) in the test stacks.</summary>
    public const string InboundSecret = "test-inbound-secret-0123456789abcdef";

    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The public key, base64 of its SubjectPublicKeyInfo: what SendGrid shows in its settings.</summary>
    public static string PublicKey { get; } = Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());

    /// <summary>The signature and timestamp headers for <paramref name="body"/>, as of <paramref name="now"/>.</summary>
    public static (string Signature, string Timestamp) Sign(string body, DateTimeOffset now)
    {
        var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = Key.SignData(Encoding.UTF8.GetBytes(timestamp + body), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return (Convert.ToBase64String(signature), timestamp);
    }
}
