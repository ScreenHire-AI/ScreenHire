using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

/// <summary>Organization API keys for the tests that call the API as an integrator or an AI assistant would.</summary>
internal static class ApiKeySupport
{
    /// <summary>Creates a key in <paramref name="owner"/>'s session; the response holds the key once, as <c>token</c>.</summary>
    public static async Task<ApiResult> CreateKeyAsync(this Tenant owner, string name, string[] scopes, int? permitsPerMinute = null)
    {
        var body = new JsonObject { ["name"] = name, ["scopes"] = new JsonArray([.. scopes.Select(s => JsonValue.Create(s))]) };
        if (permitsPerMinute is { } permits)
        {
            body["permitsPerMinute"] = permits;
        }

        return (await owner.PostAsync("/v1/organization/api-keys", body)).Expect(HttpStatusCode.Created);
    }
}

/// <summary>An integrator's client: the key in the Authorization header, no cookies and no antiforgery header.</summary>
internal sealed class KeyClient : IDisposable
{
    private readonly HttpClient client;

    public KeyClient(AppHostFixture fixture, string token, string? forwardedFor = null)
    {
        client = new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = fixture.GetEndpoint("api"),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (forwardedFor is not null)
        {
            client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body = null) =>
        client.SendAsync(new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) }, Ct);

    public async Task<ApiResult> GetAsync(string path) => await ReadAsync(await SendAsync(HttpMethod.Get, path));

    public async Task<ApiResult> PostAsync(string path, JsonNode? body = null) =>
        await ReadAsync(await SendAsync(HttpMethod.Post, path, body ?? new JsonObject()));

    public async Task<ApiResult> PutAsync(string path, JsonNode body) => await ReadAsync(await SendAsync(HttpMethod.Put, path, body));

    public async Task<ApiResult> DeleteAsync(string path) => await ReadAsync(await SendAsync(HttpMethod.Delete, path));

    public void Dispose() => client.Dispose();

    private static async Task<ApiResult> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(Ct);
            using var document = text.Length == 0 ? null : JsonDocument.Parse(text);
            return new ApiResult(response.StatusCode, document?.RootElement.Clone() ?? default);
        }
    }
}
