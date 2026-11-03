using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Email;

/// <summary>
/// SendGrid's signed event webhook: ECDSA P-256, base64 of a DER signature over the timestamp and the raw body. The check
/// answers true or false and never throws, because it runs on a post anyone can send.
/// </summary>
public sealed class SendGridEventSignatureTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private const string Body = "[{\"event\":\"bounce\",\"email\":\"casey@example.com\"}]";

    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose() => key.Dispose();

    private string PublicKey(ECDsa? of = null) => Convert.ToBase64String((of ?? key).ExportSubjectPublicKeyInfo());

    private static (string Signature, string Timestamp) Sign(ECDsa signer, string body, DateTimeOffset at)
    {
        var timestamp = at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = signer.SignData(Encoding.UTF8.GetBytes(timestamp + body), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return (Convert.ToBase64String(signature), timestamp);
    }

    private bool Check(string body, string? signature, string? timestamp, string? publicKey = null, DateTimeOffset? now = null) =>
        SendGridEventSignature.IsValid(publicKey ?? PublicKey(), signature, timestamp, Encoding.UTF8.GetBytes(body), now ?? Now);

    [Fact]
    public void A_post_signed_with_the_key_and_sent_just_now_is_valid()
    {
        var (signature, timestamp) = Sign(key, Body, Now);

        Check(Body, signature, timestamp).Should().BeTrue();
    }

    [Fact]
    public void A_body_changed_after_signing_is_refused()
    {
        var (signature, timestamp) = Sign(key, Body, Now);

        Check(Body.Replace("bounce", "delivered", StringComparison.Ordinal), signature, timestamp).Should().BeFalse();
    }

    [Fact]
    public void A_timestamp_changed_after_signing_is_refused()
    {
        var (signature, _) = Sign(key, Body, Now);

        Check(Body, signature, Now.AddSeconds(1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)).Should().BeFalse();
    }

    [Fact]
    public void A_post_signed_with_another_key_is_refused()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (signature, timestamp) = Sign(other, Body, Now);

        Check(Body, signature, timestamp).Should().BeFalse();
    }

    [Theory]
    [InlineData(-4, true)]
    [InlineData(4, true)]
    [InlineData(-6, false)]
    [InlineData(6, false)]
    public void A_timestamp_more_than_five_minutes_from_now_is_refused_so_a_captured_post_cannot_be_replayed(int minutesFromNow, bool valid)
    {
        var (signature, timestamp) = Sign(key, Body, Now.AddMinutes(minutesFromNow));

        Check(Body, signature, timestamp).Should().Be(valid);
    }

    [Theory]
    [InlineData(null, "123")]
    [InlineData("", "123")]
    [InlineData("   ", "123")]
    [InlineData("not base64 !!!", "123")]
    [InlineData("AAAA", "123")]
    public void A_missing_or_malformed_signature_is_refused_without_throwing(string? signature, string timestamp)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(long.Parse(timestamp, CultureInfo.InvariantCulture));

        Check(Body, signature, timestamp, now: now).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999")]
    [InlineData("999999999999")]
    public void A_missing_or_malformed_timestamp_is_refused_without_throwing(string? timestamp)
    {
        var (signature, _) = Sign(key, Body, Now);

        Check(Body, signature, timestamp).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a key")]
    [InlineData("AAAA")]
    public void A_missing_or_malformed_public_key_refuses_everything_without_throwing(string? publicKey)
    {
        var (signature, timestamp) = Sign(key, Body, Now);

        SendGridEventSignature.IsValid(publicKey, signature, timestamp, Encoding.UTF8.GetBytes(Body), Now).Should().BeFalse();
    }
}
