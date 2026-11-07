using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.OAuth;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Auth;

public sealed class OAuthTokensTests
{
    private static readonly Guid Organization = Guid.Parse("0192a5c4-7e1f-7b3a-9c2d-4e5f6a7b8c9d");

    [Theory]
    [InlineData(OAuthTokens.CodeMarker)]
    [InlineData(OAuthTokens.AccessMarker)]
    [InlineData(OAuthTokens.RefreshMarker)]
    public void A_new_token_names_its_organization_and_is_stored_as_its_hash(string marker)
    {
        var (token, hash) = OAuthTokens.Create(marker, Organization);

        token.Should().MatchRegex($"^{marker}_[0-9a-f]{{32}}_[0-9a-f]{{64}}$");
        OAuthTokens.TryReadOrganization(marker, token, out var organizationId).Should().BeTrue();
        organizationId.Should().Be(Organization);
        hash.Should().Be(OAuthTokens.Hash(token)).And.MatchRegex("^[0-9a-f]{64}$").And.NotContain(token);
    }

    [Fact]
    public void A_token_is_only_read_as_the_kind_it_is()
    {
        var (access, _) = OAuthTokens.Create(OAuthTokens.AccessMarker, Organization);

        OAuthTokens.TryReadOrganization(OAuthTokens.RefreshMarker, access, out _).Should().BeFalse();
        OAuthTokens.TryReadOrganization(OAuthTokens.CodeMarker, access, out _).Should().BeFalse();
    }

    [Fact]
    public void A_token_is_never_an_api_key_and_a_key_is_never_a_token()
    {
        var (access, _) = OAuthTokens.Create(OAuthTokens.AccessMarker, Organization);
        var (key, _, _) = ApiKeyTokens.Create(Organization);

        ApiKeyTokens.TryReadOrganization(access, out _).Should().BeFalse();
        OAuthTokens.TryReadOrganization(OAuthTokens.AccessMarker, key, out _).Should().BeFalse();
        OAuthTokens.LooksLikeAccessToken(key).Should().BeFalse();
        OAuthTokens.LooksLikeAccessToken(access).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sho_")]
    [InlineData("sho_0192a5c47e1f7b3a9c2d4e5f6a7b8c9d")]
    [InlineData("sho_0192a5c47e1f7b3a9c2d4e5f6a7b8c9d_aa")]
    [InlineData("sho_0192A5C47E1F7B3A9C2D4E5F6A7B8C9D_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sho_0192a5c47e1f7b3a9c2d4e5f6a7b8c9d_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_extra")]
    public void Anything_but_a_well_formed_token_names_no_organization(string? token)
    {
        OAuthTokens.TryReadOrganization(OAuthTokens.AccessMarker, token, out var organizationId).Should().BeFalse();
        organizationId.Should().BeNull();
    }
}

public sealed class PkceTests
{
    // RFC 7636 appendix B.
    private const string RfcVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string RfcChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    [Fact]
    public void The_verifier_of_the_RFC_example_matches_its_challenge() =>
        Pkce.Verify(RfcVerifier, RfcChallenge).Should().BeTrue();

    [Fact]
    public void Another_verifier_does_not_match()
    {
        var other = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        Pkce.Verify(other, RfcChallenge).Should().BeFalse();
    }

    [Fact]
    public void The_challenge_itself_is_not_accepted_as_the_verifier() =>
        Pkce.Verify(RfcChallenge, RfcChallenge).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjX")] // 42 characters
    [InlineData("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjX!")] // not unreserved
    public void A_verifier_must_be_43_to_128_unreserved_characters(string? verifier) =>
        Pkce.IsVerifier(verifier).Should().BeFalse();

    [Fact]
    public void A_128_character_verifier_is_valid() =>
        Pkce.IsVerifier(new string('a', 128)).Should().BeTrue();

    [Fact]
    public void A_129_character_verifier_is_not() =>
        Pkce.IsVerifier(new string('a', 129)).Should().BeFalse();

    [Fact]
    public void A_challenge_is_43_base64url_characters()
    {
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(RfcVerifier)));

        Pkce.IsChallenge(challenge).Should().BeTrue();
        Pkce.IsChallenge(challenge + "=").Should().BeFalse();
        Pkce.IsChallenge(challenge[..42]).Should().BeFalse();
        Pkce.IsChallenge(null).Should().BeFalse();
    }

    [Fact]
    public void Only_S256_is_offered() => Pkce.Method.Should().Be("S256");
}

public sealed class OAuthRedirectsTests
{
    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback")]
    [InlineData("https://example.com/callback?x=1")]
    [InlineData("http://localhost:6274/oauth/callback")]
    [InlineData("http://127.0.0.1:51234/callback")]
    [InlineData("http://[::1]:8080/callback")]
    public void A_registered_address_is_https_or_http_on_this_computer(string uri) =>
        OAuthRedirects.IsAllowed(uri).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://example.com/callback")] // http off this computer
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("myapp://callback")]
    [InlineData("/relative/path")]
    [InlineData("https://example.com/callback#fragment")]
    [InlineData("https://user:pass@example.com/callback")]
    [InlineData("http://localhost.example.com/callback")]
    public void Nothing_else_is_registered(string? uri) =>
        OAuthRedirects.IsAllowed(uri).Should().BeFalse();

    [Fact]
    public void An_address_longer_than_the_limit_is_refused() =>
        OAuthRedirects.IsAllowed("https://example.com/" + new string('a', OAuthRedirects.MaxLength)).Should().BeFalse();

    [Fact]
    public void A_request_names_a_registered_address_exactly()
    {
        OAuthRedirects.Matches("https://claude.ai/api/mcp/auth_callback", "https://claude.ai/api/mcp/auth_callback").Should().BeTrue();
        OAuthRedirects.Matches("https://claude.ai/api/mcp/auth_callback", "https://claude.ai/api/mcp/auth_callback/").Should().BeFalse();
        OAuthRedirects.Matches("https://claude.ai/api/mcp/auth_callback", "https://claude.ai/api/mcp/auth_callback?x=1").Should().BeFalse();
        OAuthRedirects.Matches("https://claude.ai/api/mcp/auth_callback", "https://evil.example/api/mcp/auth_callback").Should().BeFalse();
        OAuthRedirects.Matches("https://claude.ai/cb", "https://claude.ai:8443/cb").Should().BeFalse();
    }

    [Fact]
    public void Only_the_port_of_a_local_address_may_differ()
    {
        OAuthRedirects.Matches("http://127.0.0.1:1111/callback", "http://127.0.0.1:2222/callback").Should().BeTrue();
        OAuthRedirects.Matches("http://localhost/callback", "http://localhost:3333/callback").Should().BeTrue();
        OAuthRedirects.Matches("http://127.0.0.1:1111/callback", "http://localhost:1111/callback").Should().BeFalse();
        OAuthRedirects.Matches("http://127.0.0.1:1111/callback", "http://127.0.0.1:1111/other").Should().BeFalse();
        OAuthRedirects.Matches("https://example.com:1111/cb", "https://example.com:2222/cb").Should().BeFalse();
    }
}

public sealed class OAuthClientNameTests
{
    [Theory]
    [InlineData("Claude", "Claude")]
    [InlineData("  Claude  ", "Claude")]
    [InlineData("Cl\u202Eaude", "Claude")] // A right-to-left override
    [InlineData("Cla\u200Bude", "Claude")] // A zero-width space
    [InlineData("Claude\u2028Code", "ClaudeCode")] // A line separator
    [InlineData("Cla\nude\t", "Claude")]
    [InlineData("Assistant \U0001F916", "Assistant \U0001F916")] // An emoji is two surrogates, and stays
    [InlineData("\u200B", "")]
    public void A_client_name_keeps_only_what_a_person_can_see(string sent, string kept) =>
        OAuthEndpoints.CleanName(sent).Should().Be(kept);

    [Fact]
    public void A_client_name_is_cut_to_the_longest_a_name_may_be() =>
        OAuthEndpoints.CleanName(new string('a', 500)).Length.Should().Be(100);
}

public sealed class OAuthScopesTests
{
    [Theory]
    [InlineData(null, "Read")]
    [InlineData("", "Read")]
    [InlineData("read", "Read")]
    [InlineData("write", "Read,Write")]
    [InlineData("read write", "Read,Write")]
    [InlineData("verification_read", "Read,VerificationRead")]
    [InlineData("write verification_read read offline_access", "Read,Write,VerificationRead")]
    [InlineData("admin delete everything", "Read")]
    [InlineData("  write   write ", "Read,Write")]
    public void A_request_always_reads_and_names_only_scopes_the_server_knows(string? scope, string expected) =>
        string.Join(',', OAuthScopes.Parse(scope)).Should().Be(expected);

    [Fact]
    public void Scopes_are_written_in_a_fixed_order() =>
        OAuthScopes.Format([ApiKeyScope.VerificationRead, ApiKeyScope.Read, ApiKeyScope.Write]).Should().Be("read write verification_read");

    [Fact]
    public void Every_key_scope_has_a_word_and_the_words_round_trip()
    {
        foreach (var scope in Enum.GetValues<ApiKeyScope>())
        {
            OAuthScopes.Parse(OAuthScopes.NameOf(scope)).Should().Contain(scope);
        }

        OAuthScopes.Supported.Should().BeEquivalentTo(Enum.GetValues<ApiKeyScope>().Select(OAuthScopes.NameOf));
    }
}

public sealed class OAuthMetadataTests
{
    private static readonly DomainOptions Domains = new()
    {
        Scheme = "https",
        BaseDomain = "example.com",
        AppHost = "app.example.com",
        ApiHost = "api.example.com",
        CareersDomain = "careers.example.com",
        CareersHostPattern = "{tenant}.careers.example.com",
    };

    [Fact]
    public void The_resource_is_the_MCP_server_and_the_issuer_is_the_API_origin()
    {
        var resource = OAuthMetadata.ProtectedResource(Domains);

        resource.Resource.Should().Be("https://api.example.com/v1/mcp");
        resource.AuthorizationServers.Should().Equal("https://api.example.com");
        resource.ScopesSupported.Should().Equal("read", "write", "verification_read");
    }

    [Fact]
    public void The_consent_page_is_on_the_dashboard_and_everything_else_on_the_API()
    {
        var server = OAuthMetadata.AuthorizationServer(Domains);

        server.Issuer.Should().Be("https://api.example.com");
        server.AuthorizationEndpoint.Should().Be("https://app.example.com/oauth/authorize");
        server.TokenEndpoint.Should().Be("https://api.example.com/v1/oauth/token");
        server.RegistrationEndpoint.Should().Be("https://api.example.com/v1/oauth/register");
        server.RevocationEndpoint.Should().Be("https://api.example.com/v1/oauth/revoke");
    }

    [Fact]
    public void Only_public_clients_with_S256_are_offered()
    {
        var server = OAuthMetadata.AuthorizationServer(Domains);

        server.CodeChallengeMethodsSupported.Should().Equal("S256");
        server.TokenEndpointAuthMethodsSupported.Should().Equal("none");
        server.ResponseTypesSupported.Should().Equal("code");
        server.GrantTypesSupported.Should().Equal("authorization_code", "refresh_token");
        server.AuthorizationResponseIssParameterSupported.Should().BeTrue();
    }

    [Fact]
    public void The_challenge_points_at_the_protected_resource_document()
    {
        ApiKeyAuthentication.McpChallenge(Domains, sentCredentials: false)
            .Should().Be("Bearer resource_metadata=\"https://api.example.com/.well-known/oauth-protected-resource/v1/mcp\"");
        ApiKeyAuthentication.McpChallenge(Domains, sentCredentials: true)
            .Should().StartWith("Bearer error=\"invalid_token\", resource_metadata=");
    }
}
