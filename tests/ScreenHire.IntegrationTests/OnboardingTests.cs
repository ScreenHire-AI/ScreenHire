using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.IntegrationTests;

/// <summary>A new user's first organization: created with them as its Owner, once.</summary>
public sealed class OnboardingTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Onboarding_creates_the_organization_with_the_user_as_owner_and_no_jobs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail(), cancellationToken);
        var slug = $"acme-{Guid.NewGuid():N}"[..30];

        using var response = await session.PostAsync(
            "/v1/auth/onboarding", new { organizationName = "  Acme Hiring  ", slug }, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var organization = (await session.CurrentUserAsync(cancellationToken))!.Value.GetProperty("organization");
        organization.GetProperty("name").GetString().Should().Be("Acme Hiring");
        organization.GetProperty("slug").GetString().Should().Be(slug);
        organization.GetProperty("role").GetString().Should().Be("Owner");
        (await session.Client.GetFromJsonAsync<JsonElement>("/v1/jobs", cancellationToken)).GetArrayLength().Should().Be(0);
        // Careers pages find the new organization by its slug.
        (await session.Client.GetAsync($"/v1/public/organizations/{slug}/jobs", cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_user_onboards_only_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail(), cancellationToken);
        await session.OnboardAsync(cancellationToken);

        using var again = await session.PostAsync(
            "/v1/auth/onboarding", new { organizationName = "Second", slug = $"second-{Guid.NewGuid():N}"[..30] }, cancellationToken);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Double_submit_creates_one_organization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        var email = ApiSession.NewEmail();
        await session.SignInWithMagicLinkAsync(email, cancellationToken);
        await session.CurrentUserAsync(cancellationToken); // the antiforgery token both requests share

        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => session.PostAsync(
            "/v1/auth/onboarding", new { organizationName = $"Twice {i}", slug = $"twice-{i}-{Guid.NewGuid():N}" }, cancellationToken)));

        attempts.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        await using var connection = new Npgsql.NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);
        await using var count = new Npgsql.NpgsqlCommand(
            "SELECT count(*) FROM organization_members m JOIN users u ON u.id = m.user_id WHERE u.email = @email", connection);
        count.Parameters.AddWithValue("email", email);
        ((long)(await count.ExecuteScalarAsync(cancellationToken))!).Should().Be(1);
        foreach (var attempt in attempts)
        {
            attempt.Dispose();
        }
    }

    [Fact]
    public async Task Taken_slug_is_refused_and_does_not_join_its_organization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var theirs = await fixture.CreateOrganizationAsync(cancellationToken: cancellationToken);
        using var session = new ApiSession(fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail(), cancellationToken);

        using var response = await session.PostAsync(
            "/v1/auth/onboarding", new { organizationName = "Impostor", slug = theirs.Slug }, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        problem.GetProperty("errors").GetProperty("slug")[0].GetString().Should().Be("That address is taken. Choose another.");
        (await session.CurrentUserAsync(cancellationToken))!.Value.GetProperty("organization").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("Acme", "ab", "slug")]
    [InlineData("Acme", "Acme-Hiring", "slug")]
    [InlineData("Acme", "-acme", "slug")]
    [InlineData("Acme", "www", "slug")]
    [InlineData("   ", "acme-blank-name", "organizationName")]
    public async Task Invalid_details_are_refused(string organizationName, string slug, string field)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail(), cancellationToken);

        using var response = await session.PostAsync("/v1/auth/onboarding", new { organizationName, slug }, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }
}
