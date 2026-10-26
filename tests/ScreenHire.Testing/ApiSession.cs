using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.Testing;

/// <summary>
/// One browser-like client of the API: its own cookie jar (so sessions never leak between tests through a pooled
/// handler), the antiforgery header on every POST, and sign-in through the real flows, the emailed link or Dev SSO.
/// There is no test-only sign-in shortcut (docs/process/testing.md).
/// </summary>
public sealed class ApiSession : IDisposable
{
    private const string AntiforgeryCookie = "XSRF-TOKEN";
    private const string AntiforgeryHeader = "X-XSRF-TOKEN";

    private readonly CookieContainer cookies = new();
    private readonly AppHostFixture fixture;

    public ApiSession(AppHostFixture fixture)
    {
        this.fixture = fixture;
        BaseAddress = fixture.GetEndpoint("api");
        // Redirects are followed by hand, so tests can see where each one goes.
        Client = new HttpClient(new SocketsHttpHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { BaseAddress = BaseAddress };
    }

    public Uri BaseAddress { get; }

    public HttpClient Client { get; }

    /// <summary>This session's cookies, for a client that is not an <see cref="HttpClient"/>, such as a SignalR connection.</summary>
    public CookieContainer Cookies => cookies;

    /// <summary>Sends a JSON POST with the antiforgery header, fetching a token first if this session has none.</summary>
    public Task<HttpResponseMessage> PostAsync<T>(string path, T body, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, path, body, cancellationToken);

    /// <summary>A JSON POST like <see cref="PostAsync{T}(string, T, CancellationToken)"/>, with extra request headers.</summary>
    public Task<HttpResponseMessage> PostAsync<T>(
        string path, T body, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, path, body, cancellationToken, headers);

    /// <summary>Sends a JSON PUT with the antiforgery header, fetching a token first if this session has none.</summary>
    public Task<HttpResponseMessage> PutAsync<T>(string path, T body, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, path, body, cancellationToken);

    /// <summary>Sends a DELETE with the antiforgery header, fetching a token first if this session has none.</summary>
    public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken cancellationToken) =>
        SendAsync<object?>(HttpMethod.Delete, path, null, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync<T>(
        HttpMethod method, string path, T body, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? headers = null)
    {
        if (AntiforgeryToken() is null)
        {
            (await Client.GetAsync("/v1/auth/providers", cancellationToken)).EnsureSuccessStatusCode();
        }

        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add(AntiforgeryHeader, AntiforgeryToken());
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.Add(name, value);
        }

        return await Client.SendAsync(request, cancellationToken);
    }

    /// <summary>Asks for a sign-in link for <paramref name="email"/>, reads it from Mailpit and redeems it.</summary>
    public async Task SignInWithMagicLinkAsync(string email, CancellationToken cancellationToken)
    {
        using var requested = await PostAsync("/v1/auth/magic-link", new { email }, cancellationToken);
        requested.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var token = await fixture.Mailpit.WaitForSignInTokenAsync(email, cancellationToken);
        using var verified = await PostAsync("/v1/auth/magic-link/verify", new { token }, cancellationToken);
        verified.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Signs in through Dev SSO as <paramref name="username"/> (a seeded user from apps/apphost/DevSso/config.json, or
    /// any email address): the challenge, the mock provider's login form, its redirect back with a code, and the API's
    /// callback. Returns where the API finally sends the browser.
    /// </summary>
    public async Task<Uri> SignInWithDevSsoAsync(string username, CancellationToken cancellationToken, string returnUrl = "/")
    {
        using var challenge = await Client.GetAsync(
            $"/v1/auth/challenge/dev-sso?returnUrl={Uri.EscapeDataString(returnUrl)}", cancellationToken);
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var authorize = challenge.Headers.Location!;

        using var loginPage = await Client.GetAsync(authorize, cancellationToken);
        loginPage.StatusCode.Should().Be(HttpStatusCode.OK, "the mock provider shows its login page");
        using var login = await Client.PostAsync(
            authorize, new FormUrlEncodedContent([new("username", username)]), cancellationToken);

        // The provider redirects to the handler's callback, which redirects to the API's own callback, which redirects
        // to the dashboard. Follow only the API's own hops.
        var location = login.Headers.Location ?? throw new InvalidOperationException($"The mock provider answered {login.StatusCode}.");
        while (true)
        {
            var next = location.IsAbsoluteUri ? location : new Uri(BaseAddress, location);
            if (!next.AbsolutePath.StartsWith("/v1/auth/", StringComparison.Ordinal))
            {
                return next;
            }

            using var hop = await Client.GetAsync(next, cancellationToken);
            location = hop.Headers.Location ?? throw new InvalidOperationException($"{next.AbsolutePath} answered {hop.StatusCode}.");
        }
    }

    /// <summary><c>GET /v1/auth/me</c>, or null when signed out.</summary>
    public async Task<JsonElement?> CurrentUserAsync(CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync("/v1/auth/me", cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    /// <summary>Onboards the signed-in user into a new organization with a unique slug and returns its id.</summary>
    public async Task<Guid> OnboardAsync(CancellationToken cancellationToken)
    {
        var slug = $"test-{Guid.NewGuid():N}";
        using var response = await PostAsync("/v1/auth/onboarding", new { organizationName = $"Org {slug}", slug }, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return user.GetProperty("organization").GetProperty("id").GetGuid();
    }

    /// <summary>A unique address at a reserved example domain, so tests never share an inbox or an account.</summary>
    public static string NewEmail(string label = "user") => $"{label}-{Guid.NewGuid():N}@example.com";

    public void Dispose() => Client.Dispose();

    private string? AntiforgeryToken() => cookies.GetCookies(BaseAddress)[AntiforgeryCookie]?.Value;
}
