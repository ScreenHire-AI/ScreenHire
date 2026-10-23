using System.Net;
using System.Security.Cryptography;
using System.Text;
using ScreenHire.Api.Endpoints;
using ScreenHire.Application.Automations;

namespace ScreenHire.UnitTests.Automations;

public sealed class TransactionalEmailTests
{
    [Fact]
    public void A_webhook_signature_covers_the_time_and_the_exact_body()
    {
        var sentAt = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        const string body = "{\"a\":1}";
        var signature = WebhookSignature.Create("key", sentAt, body);

        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes($"{sentAt.ToUnixTimeSeconds()}.{body}")));
        signature.Should().Be($"t={sentAt.ToUnixTimeSeconds()},v1={expected}");
        WebhookSignature.Create("key", sentAt, "{\"a\":2}").Should().NotBe(signature);
        WebhookSignature.Create("key", sentAt.AddSeconds(1), body).Should().NotBe(signature);
    }

    // Each organization's receivers get that organization's key, so one organization cannot sign for another.
    [Fact]
    public void Each_organization_signs_webhooks_with_its_own_key_derived_from_the_platform_key()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        var key = WebhookSignature.OrganizationKey("platform", first);

        key.Should().Be(WebhookSignature.OrganizationKey("platform", first)).And.NotBe("platform").And.HaveLength(64);
        WebhookSignature.OrganizationKey("platform", second).Should().NotBe(key);
        WebhookSignature.OrganizationKey("other-platform", first).Should().NotBe(key);
    }

    [Fact]
    public void A_manual_run_key_gives_the_same_event_id_for_the_same_application_and_automation_only()
    {
        var application = Guid.CreateVersion7();
        var id = AutomationEndpoints.ManualEventId(application, "flag", "click-1");

        AutomationEndpoints.ManualEventId(application, "flag", "click-1").Should().Be(id);
        AutomationEndpoints.ManualEventId(Guid.CreateVersion7(), "flag", "click-1").Should().NotBe(id);
        AutomationEndpoints.ManualEventId(application, "other", "click-1").Should().NotBe(id);
        id.Version.Should().Be(8);
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("64:ff9b::a9fe:a9fe", false)] // NAT64 of 169.254.169.254
    [InlineData("64:ff9b::808:808", true)] // NAT64 of 8.8.8.8
    [InlineData("64:ff9b:1::a00:1", false)]
    [InlineData("2002:a00:1::1", false)] // 6to4 of 10.0.0.1
    [InlineData("2002:808:808::1", true)] // 6to4 of 8.8.8.8
    [InlineData("2001:0:4136:e378::1", false)] // Teredo
    [InlineData("2001:db8::1", false)]
    public void Webhooks_reach_public_addresses_only(string address, bool allowed) =>
        WebhookAddresses.IsPublic(IPAddress.Parse(address)).Should().Be(allowed);
}
