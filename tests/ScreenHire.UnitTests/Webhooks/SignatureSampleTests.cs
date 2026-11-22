using ScreenHire.Application.Automations;
using ScreenHire.Webhooks;

namespace ScreenHire.UnitTests.Webhooks;

/// <summary>
/// The C# signature check the developer docs show (<c>apps/marketing/src/developers/samples/ScreenHireSignature.cs</c>,
/// compiled into this project as it is shown) accepts what the API signs and refuses anything changed. The vector is the
/// one the Node sample's Vitest checks too (<c>developers.test.ts</c>), so the samples and the signer agree.
/// </summary>
public sealed class SignatureSampleTests
{
    private const string Secret = "whsec_6f1d2c3b4a59687706f1d2c3b4a59687706f1d2c3b4a59687706f1d2c3b4a5";
    private const string Body = """{"id":"0193a1b2-0000-8000-8000-000000000001","type":"job.closed","occurredAt":"2026-11-22T09:30:00+00:00","organizationId":"0193a1b2-1a2b-7c3d-8e4f-5a6b7c8d9e0f","data":{"jobId":"0193a1b2-6b3c-7d4e-8f5a-6b7c8d9e0f1a"}}""";
    private const string Header = "t=1795685400,v1=2bed3e1e1acabdfbb4a8b529ba76f2920439761e8d495fde69392a6f0e5e833d";
    private static readonly DateTimeOffset SentAt = DateTimeOffset.FromUnixTimeSeconds(1795685400);

    [Fact]
    public void The_pinned_vector_is_what_the_api_signs() =>
        WebhookSignature.Create(Secret, SentAt, Body).Should().Be(Header, "the Node and Python samples are checked against this header");

    [Fact]
    public void The_sample_accepts_a_delivery_the_api_signed()
    {
        var now = SentAt.AddSeconds(30);
        var header = WebhookSignature.Create(Secret, SentAt, Body);

        ScreenHireSignature.Verify(Body, header, Secret, now: now).Should().BeTrue();
    }

    [Theory]
    [InlineData(Body + " ", Header, Secret, 0)]
    [InlineData(Body, Header, Secret + "0", 0)]
    [InlineData(Body, Header, Secret, 301)]
    [InlineData(Body, "t=1795685400,v1=nothex", Secret, 0)]
    [InlineData(Body, "v1=2bed3e1e1acabdfbb4a8b529ba76f2920439761e8d495fde69392a6f0e5e833d", Secret, 0)]
    public void The_sample_refuses_a_changed_body_a_wrong_secret_an_old_time_or_a_broken_header(string body, string header, string secret, int ageSeconds) =>
        ScreenHireSignature.Verify(body, header, secret, now: SentAt.AddSeconds(ageSeconds)).Should().BeFalse();
}
