using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace ScreenHire.E2E;

/// <summary>
/// Access for programs in Settings, in a real browser: an API key is shown once, works, and stops working when revoked;
/// an AI assistant that registered itself is approved on the consent page, works with its token and is disconnected in
/// Settings. Each test signs in as a new owner of a new organization.
/// </summary>
public sealed class SettingsAccessTests(E2EFixture fixture) : SettingsArea(fixture)
{
    private static readonly string[] GrantTypes = ["authorization_code", "refresh_token"];

    private static readonly string[] ResponseTypes = ["code"];

    [Fact]
    public async Task An_API_key_is_shown_once_works_and_stops_working_when_revoked()
    {
        var (owner, _) = await OwnerAsync();
        await SignInAsync(owner);
        await GoToAsync("dashboard", "/settings/api-keys");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("API keys");
        var keys = Page.GetByRole(AriaRole.Region, new() { Name = "Keys" });
        await Expect(keys).ToContainTextAsync("No API keys yet");

        await keys.GetByRole(AriaRole.Button, new() { Name = "Create key" }).ClickAsync();
        var create = Page.GetByRole(AriaRole.Dialog, new() { Name = "Create an API key" });
        await create.GetByLabel("Name").FillAsync("Payroll sync");
        await create.GetByLabel("Read and write").CheckAsync();
        await create.GetByRole(AriaRole.Button, new() { Name = "Create key" }).ClickAsync();

        var shown = Page.GetByRole(AriaRole.Dialog, new() { Name = "Your new API key" });
        await Expect(shown).ToContainTextAsync("You will not see it again.");
        var token = await shown.GetByLabel("API key").InputValueAsync();
        token.Should().StartWith("shk_");
        await ExpectNoAccessibilityViolationsAsync();
        await shown.GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();
        await Expect(shown).ToBeHiddenAsync();

        // Once closed, the key is gone from the page: the list shows its first characters only.
        (await Page.ContentAsync()).Should().NotContain(token);
        var row = keys.GetByRole(AriaRole.Row).Filter(new() { HasText = "Payroll sync" });
        await Expect(row).ToContainTextAsync("Active");
        await Expect(row).ToContainTextAsync("Read and write");
        await Expect(row).ToContainTextAsync("shk_");
        await ExpectNoAccessibilityViolationsAsync();
        (await JobsWithKeyAsync(token)).Should().Be(HttpStatusCode.OK);

        await row.GetByRole(AriaRole.Button, new() { Name = "Actions for Payroll sync" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Revoke key" }).ClickAsync();
        await Page.GetByRole(AriaRole.Dialog, new() { Name = "Revoke Payroll sync?" }).GetByRole(AriaRole.Button, new() { Name = "Revoke key" }).ClickAsync();
        await Expect(row).ToContainTextAsync("Revoked");
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = "Actions for Payroll sync" })).ToHaveCountAsync(0);
        (await JobsWithKeyAsync(token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_assistant_signs_in_through_the_consent_page_works_with_its_token_and_is_disconnected_in_settings()
    {
        // A person who owns an organization, and an assistant that registered itself, as claude.ai does.
        const string callback = "https://claude.ai/api/mcp/auth_callback";
        const string clientName = "Briefing assistant";
        var email = ApiSession.NewEmail("e2e-owner");
        string organizationName;
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(email, Ct);
            await session.OnboardAsync(Ct);
            organizationName = (await session.CurrentUserAsync(Ct))!.Value.GetProperty("organization").GetProperty("name").GetString()!;
        }

        using var api = Fixture.CreateHttpClient("api");
        // The OAuth limits are per address; this assistant has its own.
        api.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}");
        var resource = (await GetJsonAsync(api, "/.well-known/oauth-protected-resource/v1/mcp")).GetProperty("resource").GetString()!;
        var issuer = (await GetJsonAsync(api, "/.well-known/oauth-authorization-server")).GetProperty("issuer").GetString()!;
        using var registered = await api.PostAsJsonAsync(
            "/v1/oauth/register",
            new
            {
                client_name = clientName,
                redirect_uris = new[] { callback },
                token_endpoint_auth_method = "none",
                grant_types = GrantTypes,
                response_types = ResponseTypes,
            },
            Ct);
        registered.StatusCode.Should().Be(HttpStatusCode.Created);
        var clientId = (await registered.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("client_id").GetString()!;

        var verifier = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorize = DashboardUrl(
            $"/oauth/authorize?client_id={clientId}&redirect_uri={Uri.EscapeDataString(callback)}&response_type=code&scope=read%20write"
            + $"&resource={Uri.EscapeDataString(resource)}&code_challenge={challenge}&code_challenge_method=S256&state=xyz");

        // Signed out, the dashboard sends the person to sign in and remembers where they were going.
        await Page.GotoAsync(authorize);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/sign-in\?returnUrl="));
        HttpUtility.ParseQueryString(new Uri(Page.Url).Query)["returnUrl"].Should().Be(new Uri(authorize).PathAndQuery);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();

        // Back on the request, which names the assistant and the organization.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync($"Connect {clientName} to {organizationName}");
        await Expect(Page.GetByText("claude.ai", new() { Exact = true })).ToBeVisibleAsync();
        var write = Page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new("^Write") });
        await Expect(Page.GetByText("Always on.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new("^Read") })).ToHaveCountAsync(0);
        await Expect(write).Not.ToBeCheckedAsync();
        await Expect(Page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new("^Verification read") })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();

        // Approving sends the browser to the assistant's own address. Nothing may leave the machine, so the route answers it.
        await write.CheckAsync();
        await Page.RouteAsync("https://claude.ai/**", route => route.FulfillAsync(new() { Status = 200, ContentType = "text/plain", Body = "connected" }));
        var redirect = await Page.RunAndWaitForRequestAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync(), new Regex(@"^https://claude\.ai/"));
        var back = new Uri(redirect.Url);
        $"{back.Scheme}://{back.Host}{back.AbsolutePath}".Should().Be(callback);
        var query = HttpUtility.ParseQueryString(back.Query);
        query["state"].Should().Be("xyz");
        query["iss"].Should().Be(issuer);
        query["code"].Should().NotBeNullOrEmpty();

        // The assistant exchanges the code, within its minute, for a token that the MCP endpoint accepts.
        using var exchanged = await api.PostAsync(
            "/v1/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["code"] = query["code"]!, ["client_id"] = clientId, ["redirect_uri"] = callback,
                ["code_verifier"] = verifier, ["resource"] = resource,
            }),
            Ct);
        exchanged.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await exchanged.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        tokens.GetProperty("scope").GetString()!.Split(' ').Should().BeEquivalentTo("read", "write");
        (await McpInitializeAsync(api, accessToken)).Should().Be(HttpStatusCode.OK);

        // Settings lists the connection with the access the person gave.
        await GoToAsync("dashboard", "/settings/mcp-server");
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = clientName });
        await Expect(row).ToContainTextAsync("Active");
        await Expect(row).ToContainTextAsync("Read and write");
        await Expect(row).ToContainTextAsync(ApiSession.OwnerName);

        // Disconnecting is destructive: it is in the row's menu and confirmed in a dialog.
        await row.GetByRole(AriaRole.Button, new() { Name = $"Actions for {clientName}" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Disconnect assistant" }).ClickAsync();
        await Page.GetByRole(AriaRole.Dialog, new() { Name = $"Disconnect {clientName}?" }).GetByRole(AriaRole.Button, new() { Name = "Disconnect assistant" }).ClickAsync();
        await Expect(row).ToContainTextAsync("Revoked");
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = $"Actions for {clientName}" })).ToHaveCountAsync(0);
        (await McpInitializeAsync(api, accessToken)).Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    // The MCP endpoint's initialize call with a bearer token: 200 when the token works, 401 when it does not.
    private static async Task<HttpStatusCode> McpInitializeAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        using var response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }

    private async Task<HttpStatusCode> JobsWithKeyAsync(string token)
    {
        using var client = Fixture.CreateHttpClient("api");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/jobs");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }
}
