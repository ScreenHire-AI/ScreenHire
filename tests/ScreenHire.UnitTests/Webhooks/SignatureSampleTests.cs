using System.Text.Json;
using ScreenHire.Application.Automations;
using ScreenHire.Webhooks;

namespace ScreenHire.UnitTests.Webhooks;

/// <summary>
/// The C# signature check the developer docs show (<c>apps/marketing/src/developers/samples/ScreenHireSignature.cs</c>,
/// compiled into this project as it is shown) accepts what the API signs and refuses anything changed. The vector is
/// <c>signature-vector.json</c> beside the samples, which the Node sample's Vitest (<c>developers.test.ts</c>) and the
/// webhooks page read too, so the samples, the page and the signer agree. Its secret is fictional and says so.
/// </summary>
public sealed class SignatureSampleTests
{
    private static readonly JsonElement Vector = JsonDocument.Parse(File.ReadAllText(
        RepositoryFiles.PathTo("apps", "marketing", "src", "developers", "samples", "signature-vector.json"))).RootElement;

    private static readonly string Secret = Vector.GetProperty("secret").GetString()!;
    private static readonly string Body = Vector.GetProperty("body").GetString()!;
    private static readonly string Header = Vector.GetProperty("header").GetString()!;
    private static readonly DateTimeOffset SentAt = DateTimeOffset.FromUnixTimeSeconds(Vector.GetProperty("sentAt").GetInt64());

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

    public static TheoryData<string, string, string, int> Changed => new()
    {
        { Body + " ", Header, Secret, 0 },
        { Body, Header, Secret + "0", 0 },
        { Body, Header, Secret, 301 },
        { Body, Header[..Header.IndexOf(",v1=", StringComparison.Ordinal)] + ",v1=nothex", Secret, 0 },
        { Body, Header[(Header.IndexOf(',', StringComparison.Ordinal) + 1)..], Secret, 0 },
    };

    [Theory]
    [MemberData(nameof(Changed))]
    public void The_sample_refuses_a_changed_body_a_wrong_secret_an_old_time_or_a_broken_header(string body, string header, string secret, int ageSeconds) =>
        ScreenHireSignature.Verify(body, header, secret, now: SentAt.AddSeconds(ageSeconds)).Should().BeFalse();
}
