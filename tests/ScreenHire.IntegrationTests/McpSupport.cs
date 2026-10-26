using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ScreenHire.IntegrationTests;

/// <summary>An AI assistant's side of <c>/v1/mcp</c>: an MCP client over Streamable HTTP, signed in with an API key.</summary>
internal static class McpSupport
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The endpoint, on the API's address.</summary>
    public static Uri Endpoint(AppHostFixture fixture) => new(fixture.GetEndpoint("api"), "/v1/mcp");

    /// <summary>Connects as an assistant would with <paramref name="token"/> as its bearer key. Dispose the client.</summary>
    public static async Task<McpClient> ConnectAsync(AppHostFixture fixture, string token)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = Endpoint(fixture),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            },
            new HttpClient(new SocketsHttpHandler { UseCookies = false }),
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: Ct);
    }

    /// <summary>The names of the tools the client is offered.</summary>
    public static async Task<List<string>> ToolNamesAsync(this McpClient client) =>
        [.. (await client.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name).Order(StringComparer.Ordinal)];

    /// <summary>Calls a tool that should succeed and returns its structured result.</summary>
    public static async Task<JsonElement> CallAsync(this McpClient client, string tool, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: Ct);
        result.IsError.Should().NotBe(true, $"{tool} succeeds, but said: {Text(result)}");
        return JsonSerializer.SerializeToElement(result.StructuredContent);
    }

    /// <summary>Calls a tool that should fail as a tool error (the assistant reads it) and returns the error's text.</summary>
    public static async Task<string> CallFailingAsync(this McpClient client, string tool, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: Ct);
        result.IsError.Should().BeTrue($"{tool} fails, but returned {Text(result)}");
        return Text(result);
    }

    /// <summary>
    /// Sends an MCP <c>initialize</c> request by hand, with whatever credentials <paramref name="client"/> carries, and
    /// returns the status: what a client without a key, or with only a session cookie, gets.
    /// </summary>
    public static async Task<HttpStatusCode> InitializeStatusAsync(HttpClient client, Uri endpoint)
    {
        const string initialize = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(initialize, Encoding.UTF8, "application/json") };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }

    /// <summary>Every property name anywhere in <paramref name="element"/>.</summary>
    public static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };

    private static string Text(CallToolResult result) =>
        string.Join(" ", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
