using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using Npgsql;

namespace ScreenHire.IntegrationTests;

/// <summary>A response from the OAuth endpoints: status, JSON body (if it had one) and every header.</summary>
internal sealed record RawResult(HttpStatusCode Status, JsonElement Body, IReadOnlyDictionary<string, string> Headers)
{
    public string Text(string name) => Body.GetProperty(name).GetString()!;

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    public RawResult Expect(HttpStatusCode status)
    {
        Status.Should().Be(status, "the body was {0}", Body.ValueKind == JsonValueKind.Undefined ? "empty" : Body.GetRawText());
        return this;
    }

    /// <summary>An RFC 6749 error response: 400 (or the given status) with this <c>error</c> code.</summary>
    public void ExpectError(string error, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        Expect(status);
        Text("error").Should().Be(error);
    }
}

/// <summary>A PKCE verifier and its S256 challenge (RFC 7636), made the way a client makes them.</summary>
internal sealed record PkcePair(string Verifier, string Challenge)
{
    public static PkcePair New()
    {
        var verifier = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        return new PkcePair(verifier, Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }
}

/// <summary>
/// What an assistant sends to the authorization endpoint, which the consent page forwards to the API. Null members are
/// left out, so a test can drop one with <c>with</c>.
/// </summary>
internal sealed record OAuthRequest(
    string ClientId,
    string RedirectUri,
    string? Scope,
    string? Resource,
    string? CodeChallenge,
    string? CodeChallengeMethod,
    string? State,
    string Verifier,
    string? ResponseType = "code")
{
    /// <summary>The query of <c>GET /v1/oauth/authorization-request</c>: camelCase names.</summary>
    public string Query()
    {
        var parts = new Dictionary<string, string?>
        {
            ["clientId"] = ClientId, ["redirectUri"] = RedirectUri, ["responseType"] = ResponseType, ["scope"] = Scope,
            ["resource"] = Resource, ["codeChallenge"] = CodeChallenge, ["codeChallengeMethod"] = CodeChallengeMethod,
        };
        return string.Join('&', parts.Where(p => p.Value is not null).Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));
    }

    /// <summary>The body of <c>POST /v1/oauth/authorize</c>.</summary>
    public JsonObject Body(bool approve, params string[] granted) => new()
    {
        ["clientId"] = ClientId, ["redirectUri"] = RedirectUri, ["responseType"] = ResponseType, ["scope"] = Scope,
        ["resource"] = Resource, ["codeChallenge"] = CodeChallenge, ["codeChallengeMethod"] = CodeChallengeMethod,
        ["state"] = State, ["approve"] = approve, ["grantedScopes"] = new JsonArray([.. granted.Select(s => JsonValue.Create(s))]),
    };
}

/// <summary>A person's approval: the code the assistant was sent back with, and what came with it.</summary>
internal sealed record OAuthApproval(OAuthRequest Request, string Code, string? State, string? Issuer);

/// <summary>An assistant that holds tokens for a connection a person approved.</summary>
internal sealed record OAuthConnection(OAuthApproval Approval, string ClientName, string AccessToken, string RefreshToken, string Scope, Guid ConnectionId)
{
    public OAuthRequest Request => Approval.Request;
}

/// <summary>
/// An AI assistant's side of the OAuth flow: an anonymous client, as claude.ai or the MCP Inspector is. It has an address of
/// its own (the API believes <c>X-Forwarded-For</c> from loopback), because the registration, token and failed-credential
/// limits are per address and every test in the assembly would otherwise share 127.0.0.1.
/// </summary>
internal sealed class OAuthClientApp : IDisposable
{
    private const string Initialize = """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """;

    private readonly HttpClient http;
    private string? resource;
    private string? issuer;

    public OAuthClientApp(AppHostFixture fixture)
    {
        Origin = fixture.GetEndpoint("api");
        http = new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false }) { BaseAddress = Origin };
        http.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public Uri Origin { get; }

    public async Task<RawResult> SendAsync(
        HttpMethod method, string path, HttpContent? content = null, string? token = null, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.Add(name, value);
        }

        using var response = await http.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        JsonElement body = default;
        if (text.Length > 0)
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                body = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Not JSON (an event stream from the MCP endpoint): the status and headers are the result.
            }
        }

        var all = response.Headers.Concat(response.Content.Headers)
            .ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        return new RawResult(response.StatusCode, body, all);
    }

    // ---- Discovery: what a client reads before it does anything else ----

    /// <summary>The resource identifier the protected resource document names, which a client sends as <c>resource</c>.</summary>
    public async Task<string> ResourceAsync() =>
        resource ??= (await SendAsync(HttpMethod.Get, "/.well-known/oauth-protected-resource/v1/mcp")).Expect(HttpStatusCode.OK).Text("resource");

    public async Task<string> IssuerAsync() =>
        issuer ??= (await SendAsync(HttpMethod.Get, "/.well-known/oauth-authorization-server")).Expect(HttpStatusCode.OK).Text("issuer");

    // ---- Registration, token and revocation ----

    public Task<RawResult> RegisterAsync(JsonNode body) =>
        SendAsync(HttpMethod.Post, "/v1/oauth/register", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));

    public Task<RawResult> RegisterRawAsync(string json) =>
        SendAsync(HttpMethod.Post, "/v1/oauth/register", new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>Registers a public client and returns its <c>client_id</c>.</summary>
    public async Task<string> RegisterClientAsync(string name, params string[] redirects)
    {
        var created = (await RegisterAsync(new JsonObject
        {
            ["client_name"] = name,
            ["redirect_uris"] = new JsonArray([.. redirects.Select(r => JsonValue.Create(r))]),
            ["token_endpoint_auth_method"] = "none",
            ["grant_types"] = new JsonArray("authorization_code", "refresh_token"),
            ["response_types"] = new JsonArray("code"),
        })).Expect(HttpStatusCode.Created);
        return created.Text("client_id");
    }

    public Task<RawResult> TokenAsync(IEnumerable<KeyValuePair<string, string>> form) =>
        SendAsync(HttpMethod.Post, "/v1/oauth/token", new FormUrlEncodedContent(form));

    public Task<RawResult> ExchangeCodeAsync(string? clientId, string code, string? redirectUri, string? verifier, string? resource = null) =>
        TokenAsync(Form(
            ("grant_type", "authorization_code"), ("code", code), ("client_id", clientId), ("redirect_uri", redirectUri),
            ("code_verifier", verifier), ("resource", resource)));

    public Task<RawResult> RefreshAsync(string clientId, string refreshToken, string? resource = null) =>
        TokenAsync(Form(("grant_type", "refresh_token"), ("refresh_token", refreshToken), ("client_id", clientId), ("resource", resource)));

    public Task<RawResult> RevokeAsync(string token, string? clientId = null) =>
        SendAsync(HttpMethod.Post, "/v1/oauth/revoke", new FormUrlEncodedContent(Form(("token", token), ("client_id", clientId))));

    // ---- Calling the API with a token ----

    /// <summary>An MCP <c>initialize</c> request with this bearer token (none when null): the status and challenge a client gets.</summary>
    public Task<RawResult> McpAsync(string? token) =>
        SendAsync(
            HttpMethod.Post,
            "/v1/mcp",
            new StringContent(Initialize, Encoding.UTF8, "application/json"),
            token,
            new Dictionary<string, string> { ["Accept"] = "application/json, text/event-stream" });

    public async Task<HttpStatusCode> StatusAsync(HttpMethod method, string path, string? token) =>
        (await SendAsync(method, path, method == HttpMethod.Post ? new StringContent("{}", Encoding.UTF8, "application/json") : null, token)).Status;

    public void Dispose() => http.Dispose();

    private static List<KeyValuePair<string, string>> Form(params (string Name, string? Value)[] fields) =>
        [.. fields.Where(f => f.Value is not null).Select(f => new KeyValuePair<string, string>(f.Name, f.Value!))];
}

internal static class OAuthSupport
{
    public const string ClaudeCallback = "https://claude.ai/api/mcp/auth_callback";

    /// <summary>What claude.ai asks for: every scope the server lists.</summary>
    public const string AllScopes = "read write verification_read";

    /// <summary>Registers a client and builds its authorization request with a fresh PKCE pair.</summary>
    public static async Task<OAuthRequest> NewRequestAsync(
        this OAuthClientApp app, string clientName, string scope = AllScopes, string redirect = ClaudeCallback, string? state = null)
    {
        var clientId = await app.RegisterClientAsync(clientName, redirect);
        return await app.RequestForAsync(clientId, redirect, scope, state);
    }

    /// <summary>The authorization request of an already registered client.</summary>
    public static async Task<OAuthRequest> RequestForAsync(
        this OAuthClientApp app, string clientId, string redirect, string scope = AllScopes, string? state = null)
    {
        var pkce = PkcePair.New();
        return new OAuthRequest(
            clientId, redirect, scope, await app.ResourceAsync(), pkce.Challenge, "S256", state ?? Guid.NewGuid().ToString("N"), pkce.Verifier);
    }

    public static Task<ApiResult> GetAuthorizationRequestAsync(this Tenant person, OAuthRequest request) =>
        person.GetAsync($"/v1/oauth/authorization-request?{request.Query()}");

    public static Task<ApiResult> DecideAsync(this Tenant person, OAuthRequest request, bool approve, params string[] granted) =>
        person.PostAsync("/v1/oauth/authorize", request.Body(approve, granted));

    /// <summary>The query of a redirect back to the assistant.</summary>
    public static System.Collections.Specialized.NameValueCollection QueryOf(string redirectUrl) =>
        HttpUtility.ParseQueryString(new Uri(redirectUrl).Query);

    /// <summary>Approves <paramref name="request"/> as <paramref name="person"/> and reads the code from the redirect.</summary>
    public static async Task<OAuthApproval> ApproveAsync(this Tenant person, OAuthRequest request, params string[] granted)
    {
        var decided = (await person.DecideAsync(request, approve: true, granted)).Expect(HttpStatusCode.OK);
        var url = decided.Text("redirectUrl");
        url.Should().StartWith(request.RedirectUri);
        var query = QueryOf(url);
        query["code"].Should().NotBeNullOrEmpty();
        return new OAuthApproval(request, query["code"]!, query["state"], query["iss"]);
    }

    /// <summary>
    /// The whole flow as an assistant does it: register, ask the person, approve, exchange the code. Each call registers a
    /// new client, as claude.ai does on every connection.
    /// </summary>
    public static async Task<OAuthConnection> ConnectAsync(
        this Tenant person, OAuthClientApp app, string clientName, string[] granted, string scope = AllScopes, string redirect = ClaudeCallback)
    {
        var request = await app.NewRequestAsync(clientName, scope, redirect);
        var approval = await person.ApproveAsync(request, granted);
        var tokens = (await app.ExchangeCodeAsync(request.ClientId, approval.Code, request.RedirectUri, request.Verifier, request.Resource))
            .Expect(HttpStatusCode.OK);
        var connections = (await person.GetAsync("/v1/organization/oauth-connections")).Expect(HttpStatusCode.OK);
        var connectionId = connections.Body.EnumerateArray()
            .Where(c => c.GetProperty("clientName").GetString() == clientName && c.GetProperty("revokedAt").ValueKind == JsonValueKind.Null)
            .OrderByDescending(c => c.GetProperty("createdAt").GetDateTimeOffset())
            .First().GetProperty("id").GetGuid();
        return new OAuthConnection(approval, clientName, tokens.Text("access_token"), tokens.Text("refresh_token"), tokens.Text("scope"), connectionId);
    }

    /// <summary>
    /// Ends the clock on every authorization code of an organization, as the passing of the code's lifetime would. The
    /// lifetime is a minute and a test never waits for it; this is state no endpoint can reach (docs/process/testing.md).
    /// </summary>
    public static Task ExpireCodesAsync(this AppHostFixture fixture, Guid organizationId) =>
        ExecuteAsync(fixture, "UPDATE oauth_codes SET expires_at = now() - interval '1 minute' WHERE organization_id = @organization", organizationId);

    /// <summary>Ends the clock on every access token of an organization; refresh tokens stay as they are.</summary>
    public static Task ExpireAccessTokensAsync(this AppHostFixture fixture, Guid organizationId) =>
        ExecuteAsync(
            fixture,
            "UPDATE oauth_tokens SET expires_at = now() - interval '1 minute' WHERE organization_id = @organization AND kind = 'Access'",
            organizationId);

    private static async Task ExecuteAsync(AppHostFixture fixture, string sql, Guid organizationId)
    {
        await using var owner = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(TestContext.Current.CancellationToken));
        await owner.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, owner);
        command.Parameters.AddWithValue("organization", organizationId);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
