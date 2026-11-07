using System.Security.Cryptography;
using System.Text;
using ScreenHire.Application.Billing;

namespace ScreenHire.UnitTests.Billing;

/// <summary>
/// Stripe's signed webhook (https://docs.stripe.com/webhooks#verify-manually): <c>t=timestamp,v1=hex</c>, where the hex is
/// the HMAC-SHA256 of <c>timestamp.body</c> keyed with the endpoint's signing secret, and a timestamp outside five
/// minutes is refused so a captured post cannot be replayed later.
/// </summary>
public sealed class StripeSignatureTests
{
    private const string Secret = "whsec_fictional_secret";
    private const string Body = """{"id":"evt_fictional","type":"customer.subscription.updated"}""";

    private static readonly DateTimeOffset Now = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    // Written out from the documented algorithm, not from StripeSignature.Sign.
    private static string Hmac(string secret, string timestamp, string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();

    private static string Header(DateTimeOffset at, string? signature = null, string secret = Secret, string body = Body)
    {
        var timestamp = at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"t={timestamp},v1={signature ?? Hmac(secret, timestamp, body)}";
    }

    [Fact]
    public void A_delivery_signed_with_the_secret_at_the_current_time_is_valid()
    {
        StripeSignature.IsValid(Secret, Header(Now), Body, Now).Should().BeTrue();
    }

    [Fact]
    public void The_header_the_helper_builds_is_the_one_the_documented_algorithm_gives()
    {
        StripeSignature.HeaderFor(Secret, Now, Body).Should().Be(Header(Now));
        StripeSignature.IsValid(Secret, StripeSignature.HeaderFor(Secret, Now, Body), Body, Now).Should().BeTrue();
    }

    [Fact]
    public void A_delivery_signed_with_another_secret_is_refused()
    {
        StripeSignature.IsValid(Secret, Header(Now, secret: "whsec_someone_else"), Body, Now).Should().BeFalse();
    }

    [Fact]
    public void A_body_changed_after_signing_is_refused()
    {
        var header = Header(Now);

        StripeSignature.IsValid(Secret, header, Body.Replace("updated", "deleted", StringComparison.Ordinal), Now).Should().BeFalse();
        StripeSignature.IsValid(Secret, header, Body + " ", Now).Should().BeFalse();
    }

    [Fact]
    public void A_timestamp_changed_after_signing_is_refused()
    {
        var signature = Hmac(Secret, Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), Body);

        StripeSignature.IsValid(Secret, $"t={Now.ToUnixTimeSeconds() + 1},v1={signature}", Body, Now).Should().BeFalse();
    }

    [Fact]
    public void A_timestamp_more_than_five_minutes_from_now_is_refused_in_both_directions_and_five_minutes_is_accepted()
    {
        StripeSignature.IsValid(Secret, Header(Now.AddMinutes(-5)), Body, Now).Should().BeTrue();
        StripeSignature.IsValid(Secret, Header(Now.AddMinutes(5)), Body, Now).Should().BeTrue();
        StripeSignature.IsValid(Secret, Header(Now.AddMinutes(-5).AddSeconds(-1)), Body, Now).Should().BeFalse("a captured post replayed later");
        StripeSignature.IsValid(Secret, Header(Now.AddMinutes(5).AddSeconds(1)), Body, Now).Should().BeFalse();
        StripeSignature.IsValid(Secret, Header(Now.AddDays(-1)), Body, Now).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("t=,v1=")]
    [InlineData("t=abc,v1=deadbeef")]
    [InlineData("t=-5,v1=deadbeef")]
    [InlineData("t=99999999999999999999,v1=deadbeef")]
    [InlineData("t=999999999999,v1=deadbeef")]
    [InlineData("v1=deadbeef")]
    [InlineData("t=1772442000")]
    [InlineData("t=1772442000,v0=deadbeef")]
    [InlineData("t=1772442000,v1")]
    public void A_missing_or_malformed_header_is_refused_without_throwing(string? header)
    {
        StripeSignature.IsValid(Secret, header, Body, Now).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_a_signing_secret_nothing_is_valid(string? secret)
    {
        StripeSignature.IsValid(secret, Header(Now), Body, Now).Should().BeFalse();
        StripeSignature.IsValid(secret, Header(Now, secret: secret ?? ""), Body, Now).Should().BeFalse();
    }

    [Fact]
    public void Any_one_matching_v1_is_enough_when_the_header_carries_several()
    {
        var timestamp = Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var good = Hmac(Secret, timestamp, Body);
        var stale = Hmac("whsec_the_rolled_secret", timestamp, Body);

        StripeSignature.IsValid(Secret, $"t={timestamp},v1={stale},v1={good}", Body, Now).Should().BeTrue();
        StripeSignature.IsValid(Secret, $"t={timestamp},v1={good},v1={stale}", Body, Now).Should().BeTrue();
        StripeSignature.IsValid(Secret, $"t={timestamp},v1={stale},v1={stale}", Body, Now).Should().BeFalse();
    }

    [Fact]
    public void Spaces_around_the_header_parts_and_other_schemes_are_tolerated()
    {
        var timestamp = Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var good = Hmac(Secret, timestamp, Body);

        StripeSignature.IsValid(Secret, $"t={timestamp}, v0=ignored, v1={good}", Body, Now).Should().BeTrue();
    }

    [Fact]
    public void A_signature_of_the_wrong_length_or_case_is_refused()
    {
        var timestamp = Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var good = Hmac(Secret, timestamp, Body);

        StripeSignature.IsValid(Secret, $"t={timestamp},v1={good[..^2]}", Body, Now).Should().BeFalse();
        StripeSignature.IsValid(Secret, $"t={timestamp},v1={good}00", Body, Now).Should().BeFalse();
        StripeSignature.IsValid(Secret, $"t={timestamp},v1={good.ToUpperInvariant()}", Body, Now).Should().BeFalse("Stripe sends lowercase hex");
    }
}
