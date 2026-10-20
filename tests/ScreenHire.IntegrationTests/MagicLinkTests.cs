using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace ScreenHire.IntegrationTests;

/// <summary>Emailed sign-in links, read back from the AppHost's Mailpit.</summary>
public sealed class MagicLinkTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Link_signs_in_and_creates_an_account_without_an_organization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        var email = ApiSession.NewEmail();

        await session.SignInWithMagicLinkAsync(email, cancellationToken);

        var user = (await session.CurrentUserAsync(cancellationToken))!.Value;
        user.GetProperty("email").GetString().Should().Be(email);
        user.GetProperty("displayName").GetString().Should().Be(email);
        user.GetProperty("organization").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Link_works_only_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        var email = ApiSession.NewEmail();
        (await session.PostAsync("/v1/auth/magic-link", new { email }, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var token = await fixture.Mailpit.WaitForSignInTokenAsync(email, cancellationToken);

        using var first = await session.PostAsync("/v1/auth/magic-link/verify", new { token }, cancellationToken);
        using var other = new ApiSession(fixture);
        using var second = await other.PostAsync("/v1/auth/magic-link/verify", new { token }, cancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ExpectTokenErrorAsync(second, cancellationToken);
        (await other.CurrentUserAsync(cancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Expired_link_does_not_sign_in()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        var email = ApiSession.NewEmail();
        (await session.PostAsync("/v1/auth/magic-link", new { email }, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var token = await fixture.Mailpit.WaitForSignInTokenAsync(email, cancellationToken);

        // Fifteen minutes pass: no endpoint can do this, so move the expiry as the owner. The table is not tenant-owned.
        await using (var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(cancellationToken)))
        {
            await connection.OpenAsync(cancellationToken);
            await using var expire = new NpgsqlCommand(
                "UPDATE magic_link_tokens SET expires_at = now() - interval '1 second' WHERE email = @email", connection);
            expire.Parameters.AddWithValue("email", email);
            (await expire.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1);
        }

        using var verify = await session.PostAsync("/v1/auth/magic-link/verify", new { token }, cancellationToken);

        verify.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ExpectTokenErrorAsync(verify, cancellationToken);
        (await session.CurrentUserAsync(cancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Links_expire_fifteen_minutes_after_they_are_sent_and_only_their_hash_is_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        var email = ApiSession.NewEmail();
        (await session.PostAsync("/v1/auth/magic-link", new { email }, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var token = await fixture.Mailpit.WaitForSignInTokenAsync(email, cancellationToken);

        await using var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);
        await using var read = new NpgsqlCommand(
            "SELECT token_hash, extract(epoch FROM expires_at - created_at) FROM magic_link_tokens WHERE email = @email", connection);
        read.Parameters.AddWithValue("email", email);
        await using var row = await read.ExecuteReaderAsync(cancellationToken);
        (await row.ReadAsync(cancellationToken)).Should().BeTrue();

        row.GetString(0).Should().NotContain(token).And.HaveLength(64, "a hex SHA-256 of the token");
        row.GetDecimal(1).Should().Be(15 * 60);
    }

    [Fact]
    public async Task Reply_is_the_same_for_known_and_unknown_addresses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var known = ApiSession.NewEmail("known");
        using (var setup = new ApiSession(fixture))
        {
            await setup.SignInWithMagicLinkAsync(known, cancellationToken);
        }

        using var session = new ApiSession(fixture);
        using var forKnown = await session.PostAsync("/v1/auth/magic-link", new { email = known }, cancellationToken);
        using var forUnknown = await session.PostAsync("/v1/auth/magic-link", new { email = ApiSession.NewEmail("unknown") }, cancellationToken);

        forKnown.StatusCode.Should().Be(HttpStatusCode.Accepted);
        forUnknown.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await forKnown.Content.ReadAsStringAsync(cancellationToken)).Should().Be(await forUnknown.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Links_to_one_address_are_capped_without_changing_the_reply()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        var email = ApiSession.NewEmail();

        for (var i = 0; i < 7; i++)
        {
            using var response = await session.PostAsync("/v1/auth/magic-link", new { email }, cancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        (await fixture.Mailpit.WaitForSignInTokensAsync(email, 5, cancellationToken)).Should().HaveCount(5, "five links per address per 15 minutes");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task Request_needs_an_email_address(string email)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);

        using var response = await session.PostAsync("/v1/auth/magic-link", new { email }, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        problem.GetProperty("errors").TryGetProperty("email", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Post_without_the_antiforgery_header_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        (await session.Client.GetAsync("/v1/auth/providers", cancellationToken)).EnsureSuccessStatusCode(); // has the cookies

        using var response = await session.Client.PostAsJsonAsync("/v1/auth/magic-link", new { email = ApiSession.NewEmail() }, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task ExpectTokenErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        problem.GetProperty("errors").GetProperty("token")[0].GetString().Should().Contain("expired or has already been used");
    }
}
