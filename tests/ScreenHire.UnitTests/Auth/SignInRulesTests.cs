using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Auth;

public sealed class SignInRulesTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/jobs?view=open#top", "/jobs?view=open#top")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("jobs", "/")]
    [InlineData("https://evil.example.org/", "/")]
    [InlineData("//evil.example.org", "/")]
    [InlineData("/\\evil.example.org", "/")]
    [InlineData("/jobs\r\nSet-Cookie: x=1", "/")]
    public void Return_url_is_kept_only_when_it_is_a_local_path(string? returnUrl, string expected) =>
        ReturnUrls.Sanitize(returnUrl).Should().Be(expected);

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void Google_and_generic_providers_vouch_with_email_verified(string? emailVerified, bool expected)
    {
        var principal = Principal(emailVerified is null ? [] : [new Claim("email_verified", emailVerified)]);

        EmailVerification.IsVerified(ProviderKind.Google, principal).Should().Be(expected);
        EmailVerification.IsVerified(ProviderKind.Generic, principal).Should().Be(expected);
    }

    [Fact]
    public void Microsoft_email_verified_claim_alone_is_not_trusted()
    {
        var principal = Principal([new Claim("email_verified", "true"), new Claim("tid", Guid.NewGuid().ToString())]);

        EmailVerification.IsVerified(ProviderKind.Microsoft, principal).Should().BeFalse();
    }

    [Fact]
    public void Microsoft_work_account_is_verified_by_its_domain_owner_claim() =>
        EmailVerification.IsVerified(ProviderKind.Microsoft, Principal([new Claim("xms_edov", "true")])).Should().BeTrue();

    [Fact]
    public void Microsoft_personal_account_is_verified() =>
        EmailVerification.IsVerified(ProviderKind.Microsoft, Principal([new Claim("tid", "9188040d-6c67-4c5b-b112-36a304b66dad")]))
            .Should().BeTrue();

    [Fact]
    public void Microsoft_issuer_must_match_the_tokens_tenant()
    {
        var tenant = Guid.NewGuid().ToString();
        var token = new JsonWebToken(new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { ["tid"] = tenant },
        }));

        MicrosoftIssuer.Validate(MicrosoftIssuer.Expected(tenant), token, new TokenValidationParameters())
            .Should().Be(MicrosoftIssuer.Expected(tenant));
        var otherTenant = () => MicrosoftIssuer.Validate(MicrosoftIssuer.Expected(Guid.NewGuid().ToString()), token, new TokenValidationParameters());
        otherTenant.Should().Throw<SecurityTokenInvalidIssuerException>();
    }

    [Fact]
    public void Providers_without_a_client_id_and_secret_are_left_out()
    {
        var options = new AuthOptions
        {
            Google = new OidcClientOptions { ClientId = "google-id" }, // no secret
            Microsoft = new OidcClientOptions { ClientId = "ms-id", ClientSecret = "ms-secret" },
            Oidc =
            {
                ["acme-sso"] = new OidcClientOptions { ClientId = "id", ClientSecret = "secret", Authority = "https://sso.example.com", DisplayName = "Acme SSO" },
                ["half"] = new OidcClientOptions { ClientSecret = "secret", Authority = "https://sso.example.org" },
            },
        };

        var providers = ExternalProviders.FromOptions(options, allowPlainHttp: false).All;

        providers.Select(p => (p.Id, p.DisplayName, p.Kind)).Should().Equal(
            ("microsoft", "Microsoft", ProviderKind.Microsoft),
            ("acme-sso", "Acme SSO", ProviderKind.Generic));
        providers[0].Authority.Should().Be(ExternalProviders.MicrosoftAuthority);
        providers[1].CallbackPath.Should().Be("/v1/auth/signin-acme-sso");
    }

    [Theory]
    [InlineData("google")]
    [InlineData("Not_Valid")]
    public void Generic_provider_id_must_be_a_lowercase_slug_that_is_not_built_in(string id)
    {
        var options = new AuthOptions
        {
            Oidc = { [id] = new OidcClientOptions { ClientId = "id", ClientSecret = "secret", Authority = "https://sso.example.com" } },
        };

        var build = () => ExternalProviders.FromOptions(options, allowPlainHttp: false);

        build.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Plain_http_provider_is_refused_at_startup_outside_development()
    {
        var options = new AuthOptions
        {
            Oidc = { ["dev-sso"] = new OidcClientOptions { ClientId = "id", ClientSecret = "secret", Authority = "http://localhost:8080/dev" } },
        };

        var production = () => ExternalProviders.FromOptions(options, allowPlainHttp: false);

        production.Should().Throw<InvalidOperationException>().WithMessage("*https*");
        ExternalProviders.FromOptions(options, allowPlainHttp: true).All.Should().ContainSingle(p => p.Id == "dev-sso");
    }

    [Fact]
    public void Magic_link_tokens_are_random_url_safe_and_stored_as_a_sha256_hash()
    {
        var token = MagicLinks.NewToken();

        token.Should().MatchRegex("^[A-Za-z0-9_-]{43}$", "256 bits in unpadded base64url");
        MagicLinks.NewToken().Should().NotBe(token);
        MagicLinks.Hash(token).Should().MatchRegex("^[0-9a-f]{64}$").And.Be(MagicLinks.Hash(token));
    }

    [Fact]
    public void Magic_link_email_has_the_link_and_says_how_long_it_works()
    {
        var link = new Uri("https://app.example.com/sign-in/verify#token=abc");

        var message = MagicLinks.Compose("avery@example.com", link, TimeSpan.FromMinutes(15));

        message.To.Should().Be("avery@example.com");
        message.TextBody.Should().Contain(link.ToString()).And.Contain("15 minutes").And.Contain("works once");
        message.HtmlBody.Should().Contain("href=\"https://app.example.com/sign-in/verify#token=abc\"");
    }

    [Theory]
    [InlineData(" Avery@Example.COM ", "avery@example.com")]
    public void Magic_link_addresses_are_trimmed_and_lower_cased(string input, string expected) =>
        MagicLinks.NormalizeEmail(input).Should().Be(expected);

    [Theory]
    [InlineData("smtp://localhost:1025")]
    [InlineData("Endpoint=smtp://localhost:1025")]
    public void Smtp_server_accepts_both_aspire_connection_string_forms(string connectionString) =>
        EmailRegistration.ParseServer(connectionString).Should().Be(new Uri("smtp://localhost:1025"));

    [Fact]
    public void Dashboard_urls_come_from_the_configured_app_host()
    {
        var domains = new DomainOptions { Scheme = "https", AppHost = "app.example.com" };

        domains.AppUrl("/sign-in?error=provider").Should().Be(new Uri("https://app.example.com/sign-in?error=provider"));
    }

    private static ClaimsPrincipal Principal(IEnumerable<Claim> claims) => new(new ClaimsIdentity(claims, "test"));
}
