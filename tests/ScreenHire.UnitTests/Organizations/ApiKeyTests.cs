using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Organizations;

public sealed class ApiKeyTests
{
    [Theory]
    [InlineData(new[] { ApiKeyScope.Read }, ApiKeyScope.Read, true)]
    [InlineData(new[] { ApiKeyScope.Read }, ApiKeyScope.Write, false)]
    [InlineData(new[] { ApiKeyScope.Write }, ApiKeyScope.Read, true)] // Write includes reading
    [InlineData(new[] { ApiKeyScope.Read, ApiKeyScope.Write }, ApiKeyScope.Write, true)]
    [InlineData(new ApiKeyScope[0], ApiKeyScope.Read, false)]
    public void A_key_allows_what_its_scopes_grant(ApiKeyScope[] scopes, ApiKeyScope wanted, bool allowed) =>
        ApiKey.Allows(scopes, wanted).Should().Be(allowed);

    [Fact]
    public void Revoking_again_keeps_the_first_revocation_time()
    {
        var first = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
        var key = new ApiKey
        {
            Name = "ATS sync", Prefix = "shk_1a2b3c4d", SecretHash = new string('a', 64), Scopes = [ApiKeyScope.Read], CreatedAt = first,
        };

        key.Revoke(first);
        key.Revoke(first.AddHours(1));

        key.RevokedAt.Should().Be(first);
    }
}
