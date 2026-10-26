using ScreenHire.Api.Auth;

namespace ScreenHire.UnitTests.Auth;

public sealed class ApiKeyTokensTests
{
    private static readonly Guid Organization = Guid.Parse("0192a5c4-7e1f-7b3a-9c2d-4e5f6a7b8c9d");

    [Fact]
    public void A_new_key_names_its_organization_starts_with_its_prefix_and_is_stored_as_its_hash()
    {
        var (token, prefix, hash) = ApiKeyTokens.Create(Organization);

        token.Should().StartWith(prefix + "_").And.MatchRegex("^shk_[0-9a-f]{8}_[0-9a-f]{32}_[0-9a-f]{64}$");
        ApiKeyTokens.TryReadOrganization(token, out var organizationId).Should().BeTrue();
        organizationId.Should().Be(Organization);
        hash.Should().Be(ApiKeyTokens.Hash(token)).And.MatchRegex("^[0-9a-f]{64}$").And.NotContain(token);
    }

    [Fact]
    public void Two_keys_for_one_organization_share_nothing_secret() =>
        ApiKeyTokens.Create(Organization).Token.Split('_')[3].Should().NotBe(ApiKeyTokens.Create(Organization).Token.Split('_')[3]);

    [Theory]
    [InlineData("")]
    [InlineData("not-a-key")]
    [InlineData("shk_1a2b3c4d_0192a5c47e1f7b3a9c2d4e5f6a7b8c9d")] // no secret
    [InlineData("shk_1a2b3c4d_0192a5c47e1f7b3a9c2d4e5f6a7b8c9d_aa")] // short secret
    [InlineData("shk_1a2b3c_0192a5c47e1f7b3a9c2d4e5f6a7b8c9d_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // short prefix
    [InlineData("shk_1A2B3C4D_0192a5c47e1f7b3a9c2d4e5f6a7b8c9d_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // upper case
    [InlineData("shk_1a2b3c4d_0192a5c4-7e1f-7b3a-9c2d-4e5f6a7b8c9d_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // dashed id
    [InlineData("shk_1a2b3c4d_0192a5c47e1f7b3a9c2d4e5f6a7b8c9z_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // not hex
    [InlineData("xyz_1a2b3c4d_0192a5c47e1f7b3a9c2d4e5f6a7b8c9d_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // wrong marker
    [InlineData("shk_1a2b3c4d_0192a5c47e1f7b3a9c2d4e5f6a7b8c9d_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_extra")]
    public void Anything_but_a_well_formed_key_names_no_organization(string token)
    {
        ApiKeyTokens.TryReadOrganization(token, out var organizationId).Should().BeFalse();
        organizationId.Should().BeNull();
    }
}
