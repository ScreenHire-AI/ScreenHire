using System.Net.Http.Json;
using System.Text.Json;
using System.Web;

namespace ScreenHire.IntegrationTests;

/// <summary>Sign-in providers, the Dev SSO flow, the session and sign-out.</summary>
public sealed class SignInTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Only_configured_providers_are_listed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);

        var options = await session.Client.GetFromJsonAsync<JsonElement>("/v1/auth/providers", cancellationToken);

        // Google and Microsoft have no client id or secret here, so they must not appear; Dev SSO is the AppHost's mock.
        options.GetProperty("providers").EnumerateArray()
            .Select(p => (p.GetProperty("id").GetString(), p.GetProperty("displayName").GetString()))
            .Should().Equal(("dev-sso", "Dev SSO"));
        options.GetProperty("magicLinkEnabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Signed_out_requests_are_unauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);

        (await session.Client.GetAsync("/v1/jobs", cancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await session.Client.GetAsync($"/v1/jobs/{Guid.NewGuid()}", cancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await session.Client.GetAsync("/v1/auth/me", cancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Acme", slug = "acme-signed-out" }, cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Challenge_sends_the_browser_to_the_provider_with_PKCE_and_a_dashboard_redirect_uri()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);

        using var response = await session.Client.GetAsync("/v1/auth/challenge/dev-sso?returnUrl=%2Fjobs", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        query["response_type"].Should().Be("code");
        query["code_challenge_method"].Should().Be("S256");
        query["code_challenge"].Should().NotBeNullOrEmpty();
        // Without frontends the API is its own dashboard origin (AppHost.cs); with them it is the dashboard's.
        query["redirect_uri"].Should().Be(new Uri(session.BaseAddress, "/v1/auth/signin-dev-sso").ToString());
    }

    [Fact]
    public async Task Unknown_provider_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);

        (await session.Client.GetAsync("/v1/auth/challenge/google", cancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Seeded_Dev_SSO_user_signs_in_as_Brightwaters_owner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);

        var landing = await session.SignInWithDevSsoAsync("brightwater-owner", cancellationToken);

        landing.Should().Be(new Uri(session.BaseAddress, "/"));
        var user = (await session.CurrentUserAsync(cancellationToken))!.Value;
        user.GetProperty("email").GetString().Should().Be("avery@brightwater.example.com");
        user.GetProperty("displayName").GetString().Should().Be("Avery Quinn");
        var organization = user.GetProperty("organization");
        organization.GetProperty("slug").GetString().Should().Be("brightwater");
        organization.GetProperty("role").GetString().Should().Be("Owner");
        (await session.Client.GetFromJsonAsync<JsonElement>("/v1/jobs", cancellationToken)).GetArrayLength().Should().Be(4);
    }

    [Fact]
    public async Task New_Dev_SSO_identity_with_a_verified_email_creates_an_account()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        var email = ApiSession.NewEmail("sso");

        await session.SignInWithDevSsoAsync(email, cancellationToken);

        var user = (await session.CurrentUserAsync(cancellationToken))!.Value;
        user.GetProperty("email").GetString().Should().Be(email);
        user.GetProperty("organization").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Provider_identity_links_to_the_account_with_the_same_verified_email()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = ApiSession.NewEmail("linked");
        Guid accountId;
        using (var byEmail = new ApiSession(fixture))
        {
            await byEmail.SignInWithMagicLinkAsync(email, cancellationToken);
            accountId = (await byEmail.CurrentUserAsync(cancellationToken))!.Value.GetProperty("id").GetGuid();
        }

        using var bySso = new ApiSession(fixture);
        await bySso.SignInWithDevSsoAsync(email, cancellationToken);

        (await bySso.CurrentUserAsync(cancellationToken))!.Value.GetProperty("id").GetGuid().Should().Be(accountId);
    }

    [Fact]
    public async Task Unverified_email_from_a_provider_neither_signs_in_nor_takes_over_an_account()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);

        // The "unverified" Dev SSO user claims the seeded owner's address with email_verified false.
        var landing = await session.SignInWithDevSsoAsync("unverified", cancellationToken);

        landing.Should().Be(new Uri(session.BaseAddress, "/sign-in?error=email-unverified"));
        (await session.CurrentUserAsync(cancellationToken)).Should().BeNull();
    }

    [Theory]
    [InlineData("/jobs?view=open", "/jobs?view=open")]
    [InlineData("https://evil.example.org/", "/")]
    [InlineData("//evil.example.org", "/")]
    [InlineData("/\\evil.example.org", "/")]
    public async Task Sign_in_returns_only_to_a_local_path(string returnUrl, string expected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);

        var landing = await session.SignInWithDevSsoAsync(ApiSession.NewEmail(), cancellationToken, returnUrl);

        landing.Should().Be(new Uri(session.BaseAddress, expected));
    }

    [Fact]
    public async Task Sign_out_ends_the_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail(), cancellationToken);
        await session.CurrentUserAsync(cancellationToken); // refreshes the antiforgery token for the signed-in user

        using var response = await session.PostAsync("/v1/auth/sign-out", new { }, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await session.CurrentUserAsync(cancellationToken)).Should().BeNull();
    }
}
