using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ScreenHire.Application.Verification;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// OAuth sign-in for the MCP server (ADR-0028, docs/architecture/oauth-server.md), driven as claude.ai or Claude Code drives
/// it: discovery, dynamic client registration, a signed-in person's consent, the token endpoint, and then a real MCP client
/// with the access token. Expected values come from the MCP authorization spec and RFC 6749, 7009, 7591, 7636, 8252,
/// 8414, 8707, 9207 and 9728, and from the plan's decisions, never from what the code returns. Every test makes its own
/// organization and gives the assistant an address of its own, because the OAuth limits are per address.
/// </summary>
public sealed class OAuthServerTests(AppHostFixture fixture)
{
    private static readonly string[] ReadTools = ["find_candidates", "find_jobs", "get_application", "get_hiring_report", "list_saved_candidates"];

    private static readonly string[] WriteTools = ["add_note", "add_tag", "create_task", "move_to_stage", "remove_tag"];

    private static readonly string[] VerificationTools = ["find_candidates_by_verification_level", "get_verification"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] Sorted(IEnumerable<string> names) => [.. names.Order(StringComparer.Ordinal)];

    private Uri Api => fixture.GetEndpoint("api");

    // ---- Discovery ----

    [Fact]
    public async Task The_protected_resource_document_names_the_mcp_endpoint_and_this_server_as_its_authorization_server()
    {
        using var app = new OAuthClientApp(fixture);

        var document = await app.SendAsync(HttpMethod.Get, "/.well-known/oauth-protected-resource/v1/mcp");

        document.Expect(HttpStatusCode.OK);
        document.Text("resource").Should().Be(new Uri(Api, "/v1/mcp").ToString(), "RFC 9728: the resource is the MCP endpoint's URL");
        var issuer = await app.IssuerAsync();
        document.Body.GetProperty("authorization_servers").EnumerateArray().Select(s => s.GetString()).Should().Equal(issuer);
        document.Body.GetProperty("scopes_supported").EnumerateArray().Select(s => s.GetString()).Should().Equal("read", "write", "verification_read");
        document.Body.GetProperty("bearer_methods_supported").EnumerateArray().Select(s => s.GetString()).Should().Equal("header");
        document.Header("Content-Type").Should().StartWith("application/json");
    }

    [Fact]
    public async Task The_authorization_server_document_offers_pkce_s256_only_and_public_clients_and_its_endpoints()
    {
        using var app = new OAuthClientApp(fixture);

        var document = (await app.SendAsync(HttpMethod.Get, "/.well-known/oauth-authorization-server")).Expect(HttpStatusCode.OK);

        var body = document.Body;
        document.Text("issuer").Should().Be(Api.GetLeftPart(UriPartial.Authority), "RFC 8414: the issuer is the API's origin, with no trailing slash");
        document.Text("token_endpoint").Should().Be(new Uri(Api, "/v1/oauth/token").ToString());
        document.Text("registration_endpoint").Should().Be(new Uri(Api, "/v1/oauth/register").ToString());
        document.Text("revocation_endpoint").Should().Be(new Uri(Api, "/v1/oauth/revoke").ToString());
        // The consent page is a dashboard route, on the AppHost origin. This stack has no dashboard (Frontends=None), so its
        // AppHost is the API's own host and this test cannot tell the two origins apart; the path is what it checks.
        new Uri(document.Text("authorization_endpoint")).AbsolutePath.Should().Be("/oauth/authorize");
        document.Text("authorization_endpoint").Should().Be(new Uri(Api, "/oauth/authorize").ToString());
        body.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(s => s.GetString()).Should().Equal("S256");
        body.GetProperty("response_types_supported").EnumerateArray().Select(s => s.GetString()).Should().Equal("code");
        body.GetProperty("grant_types_supported").EnumerateArray().Select(s => s.GetString()).Should().BeEquivalentTo("authorization_code", "refresh_token");
        body.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(s => s.GetString()).Should().Equal("none");
        body.GetProperty("scopes_supported").EnumerateArray().Select(s => s.GetString()).Should().Equal("read", "write", "verification_read");
        body.GetProperty("authorization_response_iss_parameter_supported").GetBoolean().Should().BeTrue("RFC 9207");
        body.TryGetProperty("client_id_metadata_document_supported", out _).Should().BeFalse("only dynamic registration is offered, so clients fall back to it");
        (await app.SendAsync(HttpMethod.Get, "/.well-known/oauth-protected-resource")).Expect(HttpStatusCode.OK)
            .Text("resource").Should().Be(await app.ResourceAsync(), "the root form of the document answers too");
    }

    // ---- The 401 challenge ----

    [Fact]
    public async Task An_unauthenticated_mcp_call_is_401_and_points_at_the_resource_metadata()
    {
        using var app = new OAuthClientApp(fixture);

        var response = await app.McpAsync(token: null);

        response.Status.Should().Be(HttpStatusCode.Unauthorized);
        var challenge = response.Header("WWW-Authenticate")!;
        challenge.Should().StartWith("Bearer");
        challenge.Should().Contain($"resource_metadata=\"{new Uri(Api, "/.well-known/oauth-protected-resource/v1/mcp")}\"");
        challenge.Should().NotContain("error=", "RFC 6750: a request without credentials gets no error code");
        var metadataUrl = challenge[(challenge.IndexOf("resource_metadata=\"", StringComparison.Ordinal) + "resource_metadata=\"".Length)..].Split('"')[0];
        (await app.SendAsync(HttpMethod.Get, new Uri(metadataUrl).PathAndQuery)).Expect(HttpStatusCode.OK)
            .Text("resource").Should().Be(await app.ResourceAsync(), "the URL in the challenge is the document");
    }

    [Fact]
    public async Task A_bad_access_token_is_401_with_invalid_token()
    {
        using var app = new OAuthClientApp(fixture);
        var unknown = $"sho_{Guid.NewGuid():N}_{Convert.ToHexStringLower(Guid.NewGuid().ToByteArray())}{Convert.ToHexStringLower(Guid.NewGuid().ToByteArray())}";

        foreach (var token in new[] { "sho_garbage", unknown })
        {
            var response = await app.McpAsync(token);

            response.Status.Should().Be(HttpStatusCode.Unauthorized, token);
            response.Header("WWW-Authenticate").Should().Contain("error=\"invalid_token\"").And.Contain("resource_metadata=");
        }
    }

    [Fact]
    public async Task An_expired_or_revoked_token_does_not_count_against_the_address()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var expired = await owner.ConnectAsync(app, "Claude", ["Read"]);
        var valid = await owner.ConnectAsync(app, "Another assistant", ["Read"]);
        await fixture.ExpireAccessTokensAsync(owner.OrganizationId);
        var working = (await app.RefreshAsync(valid.Request.ClientId, valid.RefreshToken)).Expect(HttpStatusCode.OK).Text("access_token");
        var revoked = await owner.ConnectAsync(app, "A third assistant", ["Read"]);
        (await owner.PostAsync($"/v1/organization/oauth-connections/{revoked.ConnectionId}/revoke")).Expect(HttpStatusCode.OK);

        // One more than the default ScreenHire:ApiKeys:FailedAttemptsPerMinute: every client does this once an hour, and a
        // hosted assistant's address is shared by everyone who uses it.
        for (var attempt = 0; attempt < 31; attempt++)
        {
            var token = attempt % 2 == 0 ? expired.AccessToken : revoked.AccessToken;
            (await app.McpAsync(token)).Status.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await app.McpAsync(working)).Status.Should().Be(HttpStatusCode.OK, "the address was not locked out");
    }

    [Fact]
    public async Task A_token_with_another_organization_put_in_its_name_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var connection = await owner.ConnectAsync(app, "Claude", ["Read"]);
        string Moved(string token) => token.Replace($"{owner.OrganizationId:N}", $"{other.OrganizationId:N}", StringComparison.Ordinal);

        (await app.McpAsync(Moved(connection.AccessToken))).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await app.RefreshAsync(connection.Request.ClientId, Moved(connection.RefreshToken))).ExpectError("invalid_grant");
        (await app.RevokeAsync(Moved(connection.RefreshToken))).Expect(HttpStatusCode.OK);
        (await app.RevokeAsync(Moved(connection.AccessToken))).Expect(HttpStatusCode.OK);

        (await app.McpAsync(connection.AccessToken)).Status.Should().Be(HttpStatusCode.OK, "nothing happened to the real token");
        (await app.RefreshAsync(connection.Request.ClientId, connection.RefreshToken)).Expect(HttpStatusCode.OK);
    }

    // ---- Dynamic client registration ----

    [Fact]
    public async Task A_registration_too_large_for_the_limit_is_refused_even_without_a_content_length()
    {
        using var app = new OAuthClientApp(fixture);
        var padding = new string('a', 32 * 1024);
        var json = $"{{\"client_name\":\"{padding}\",\"redirect_uris\":[\"https://example.com/callback\"]}}";

        var response = await app.SendAsync(HttpMethod.Post, "/v1/oauth/register", new ChunkedContent(json));

        response.ExpectError("invalid_client_metadata");
    }

    // A body with no length of its own, so the client sends it chunked.
    private sealed class ChunkedContent(string text) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text)).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Theory]
    [InlineData("https://example.com/callback")]
    [InlineData("http://127.0.0.1:8123/callback")]
    [InlineData("http://localhost:3000/cb")]
    [InlineData("http://[::1]:9000/cb")]
    [InlineData("https://claude.ai/api/mcp/auth_callback")]
    public async Task Registration_accepts_an_https_a_loopback_or_the_claude_ai_redirect(string redirect)
    {
        using var app = new OAuthClientApp(fixture);

        var created = await app.RegisterAsync(new JsonObject
        {
            ["client_name"] = "Test assistant",
            ["redirect_uris"] = new JsonArray(redirect),
            ["token_endpoint_auth_method"] = "none",
            ["grant_types"] = new JsonArray("authorization_code", "refresh_token"),
            ["response_types"] = new JsonArray("code"),
        });

        created.Expect(HttpStatusCode.Created);
        created.Text("client_id").Should().NotBeNullOrWhiteSpace();
        created.Text("token_endpoint_auth_method").Should().Be("none");
        created.Text("client_name").Should().Be("Test assistant");
        created.Body.GetProperty("redirect_uris").EnumerateArray().Select(u => u.GetString()).Should().Equal(redirect);
        created.Body.TryGetProperty("client_secret", out _).Should().BeFalse("public clients get no secret");
        created.Header("Cache-Control").Should().Contain("no-store");
    }

    [Theory]
    [InlineData("http://example.com/callback")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/callback")]
    [InlineData("/relative/callback")]
    [InlineData("https://example.com/callback#fragment")]
    [InlineData("custom-scheme://callback")]
    public async Task Registration_refuses_a_redirect_that_is_not_https_or_loopback(string redirect)
    {
        using var app = new OAuthClientApp(fixture);

        var refused = await app.RegisterAsync(new JsonObject { ["client_name"] = "Test assistant", ["redirect_uris"] = new JsonArray(redirect) });

        refused.ExpectError("invalid_redirect_uri");
    }

    [Fact]
    public async Task Registration_refuses_a_missing_or_empty_or_oversized_redirect_list_and_one_bad_address_among_good_ones()
    {
        using var app = new OAuthClientApp(fixture);
        var tooMany = new JsonArray([.. Enumerable.Range(0, 11).Select(i => JsonValue.Create($"https://example.com/cb/{i}"))]);

        (await app.RegisterAsync(new JsonObject { ["client_name"] = "x" })).ExpectError("invalid_redirect_uri");
        (await app.RegisterAsync(new JsonObject { ["redirect_uris"] = new JsonArray() })).ExpectError("invalid_redirect_uri");
        (await app.RegisterAsync(new JsonObject { ["redirect_uris"] = tooMany })).ExpectError("invalid_redirect_uri");
        (await app.RegisterAsync(new JsonObject { ["redirect_uris"] = new JsonArray("https://example.com/ok", "http://example.com/bad") }))
            .ExpectError("invalid_redirect_uri");
        (await app.RegisterAsync(new JsonObject { ["redirect_uris"] = "https://example.com/not-a-list" })).ExpectError("invalid_redirect_uri");
        (await app.RegisterRawAsync("not json")).ExpectError("invalid_client_metadata");
        (await app.RegisterRawAsync("[1,2]")).ExpectError("invalid_client_metadata");
    }

    [Fact]
    public async Task Registration_refuses_a_confidential_client()
    {
        using var app = new OAuthClientApp(fixture);

        foreach (var method in new[] { "client_secret_basic", "client_secret_post", "private_key_jwt" })
        {
            var refused = await app.RegisterAsync(new JsonObject
            {
                ["redirect_uris"] = new JsonArray("https://example.com/cb"), ["token_endpoint_auth_method"] = method,
            });

            refused.ExpectError("invalid_client_metadata");
        }
    }

    [Fact]
    public async Task Registration_ignores_members_it_does_not_know_and_cuts_a_long_name()
    {
        using var app = new OAuthClientApp(fixture);
        var longName = new string('n', 500);

        var created = await app.RegisterAsync(new JsonObject
        {
            ["client_name"] = longName,
            ["redirect_uris"] = new JsonArray("https://example.com/cb"),
            ["scope"] = "read write",
            ["software_id"] = "9f1b0d6e",
            ["logo_uri"] = "https://example.com/logo.png",
            ["contacts"] = new JsonArray("ops@example.com"),
            ["something_new"] = new JsonObject { ["nested"] = 1 },
        });

        created.Expect(HttpStatusCode.Created);
        created.Text("client_name").Length.Should().BeLessThan(500).And.BeGreaterThan(0);
    }

    [Fact]
    public async Task Registration_is_limited_per_address()
    {
        using var app = new OAuthClientApp(fixture);
        var body = new JsonObject { ["redirect_uris"] = new JsonArray("https://example.com/cb") };

        for (var i = 0; i < 20; i++)
        {
            (await app.RegisterAsync(body)).Expect(HttpStatusCode.Created);
        }

        (await app.RegisterAsync(body)).Status.Should().Be(HttpStatusCode.TooManyRequests, "20 registrations an hour is the default");
    }

    // ---- The full flow ----

    [Fact]
    public async Task A_read_grant_lists_only_the_five_read_tools_even_when_the_assistant_asked_for_every_scope()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var request = await app.NewRequestAsync("Claude", state: "a b&c=d/é?");

        var info = (await owner.GetAuthorizationRequestAsync(request)).Expect(HttpStatusCode.OK);
        var approval = await owner.ApproveAsync(request, "Read");
        var tokens = (await app.ExchangeCodeAsync(request.ClientId, approval.Code, request.RedirectUri, request.Verifier, request.Resource))
            .Expect(HttpStatusCode.OK);

        info.Text("clientName").Should().Be("Claude");
        info.Text("redirectHost").Should().Be("claude.ai");
        info.At("redirectIsLoopback").GetBoolean().Should().BeFalse();
        info.Text("organizationName").Should().StartWith("Org test-");
        info.At("scopes").EnumerateArray().Select(s => (s.GetProperty("scope").GetString(), s.GetProperty("grantable").GetBoolean()))
            .Should().Equal(("Read", true), ("Write", true), ("VerificationRead", true));
        approval.State.Should().Be("a b&c=d/é?", "the state comes back exactly as sent");
        approval.Issuer.Should().Be(await app.IssuerAsync(), "RFC 9207");
        tokens.Header("Cache-Control").Should().Contain("no-store");
        tokens.Text("token_type").Should().BeEquivalentTo("Bearer");
        tokens.Body.GetProperty("expires_in").GetInt32().Should().Be(3600);
        tokens.Text("scope").Should().Be("read", "the grant is narrower than the request");
        tokens.Text("access_token").Should().StartWith($"sho_{owner.OrganizationId:N}_");
        tokens.Text("refresh_token").Should().StartWith("shr_");
        await using var mcp = await McpSupport.ConnectAsync(fixture, tokens.Text("access_token"));
        (await mcp.ToolNamesAsync()).Should().Equal(ReadTools);
        var write = () => mcp.CallToolAsync(
            "add_note", new Dictionary<string, object?> { ["applicationId"] = Guid.NewGuid(), ["note"] = "Hello" }, cancellationToken: Ct).AsTask();
        await write.Should().ThrowAsync<McpProtocolException>("the grant has no Write scope");
    }

    [Fact]
    public async Task A_write_grant_lists_the_hiring_tools_and_no_verification_tool()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);

        var connection = await owner.ConnectAsync(app, "Claude", ["Read", "Write"]);

        connection.Scope.Split(' ').Should().BeEquivalentTo("read", "write");
        await using var mcp = await McpSupport.ConnectAsync(fixture, connection.AccessToken);
        (await mcp.ToolNamesAsync()).Should().Equal(Sorted(ReadTools.Concat(WriteTools)));
    }

    [Fact]
    public async Task A_verification_read_grant_lists_the_verification_tools_and_logs_each_read_under_the_connection()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, owner);
        var bot = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);
        await VerificationSupport.AssessedAsync(owner, bot);
        var connection = await owner.ConnectAsync(app, "Auditor assistant", ["Read", "VerificationRead"]);
        await using var mcp = await McpSupport.ConnectAsync(fixture, connection.AccessToken);

        (await mcp.ToolNamesAsync()).Should().Equal(Sorted(ReadTools.Concat(VerificationTools)));
        var verification = await mcp.CallAsync("get_verification", new() { ["applicationId"] = bot });

        verification.GetProperty("authenticity").GetProperty("level").GetString().Should().Be("VerificationRecommended");
        var log = (await owner.GetAsync($"/v1/applications/{bot}/verification")).At("reviewLog").EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "ReadByKey").ToList();
        log.Should().ContainSingle("the one get_verification read is logged");
        log[0].GetProperty("apiKeyId").GetGuid().Should().Be(connection.ConnectionId);
        log[0].GetProperty("apiKeyName").GetString().Should().Be("Auditor assistant");
        log[0].GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null, "a connection acts for no person");
    }

    [Fact]
    public async Task A_loopback_redirect_may_use_another_port_than_the_one_registered_but_not_another_host()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var clientId = await app.RegisterClientAsync("Claude Code", "http://127.0.0.1:1234/callback");

        var otherPort = await app.RequestForAsync(clientId, "http://127.0.0.1:5678/callback");
        var otherHost = await app.RequestForAsync(clientId, "http://localhost:1234/callback");
        var otherPath = await app.RequestForAsync(clientId, "http://127.0.0.1:1234/elsewhere");

        var info = (await owner.GetAuthorizationRequestAsync(otherPort)).Expect(HttpStatusCode.OK);
        info.Text("redirectHost").Should().Be("127.0.0.1:5678");
        info.At("redirectIsLoopback").GetBoolean().Should().BeTrue("the page warns that this is the person's own computer");
        var approval = await owner.ApproveAsync(otherPort, "Read");
        approval.Request.RedirectUri.Should().Be("http://127.0.0.1:5678/callback");
        (await owner.GetAuthorizationRequestAsync(otherHost)).Expect(HttpStatusCode.BadRequest);
        (await owner.GetAuthorizationRequestAsync(otherPath)).Expect(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Denying_redirects_with_access_denied_state_and_issuer_and_connects_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var request = await app.NewRequestAsync("Claude", state: "keep this & that");

        var denied = (await owner.DecideAsync(request, approve: false)).Expect(HttpStatusCode.OK);

        var redirect = denied.Text("redirectUrl");
        redirect.Should().StartWith(request.RedirectUri);
        var query = OAuthSupport.QueryOf(redirect);
        query["error"].Should().Be("access_denied");
        query["state"].Should().Be("keep this & that");
        query["iss"].Should().Be(await app.IssuerAsync());
        query["code"].Should().BeNull();
        (await owner.GetAsync("/v1/organization/oauth-connections")).Expect(HttpStatusCode.OK).Count().Should().Be(0);
    }

    // ---- Who may connect and what they may grant ----

    [Fact]
    public async Task A_person_with_a_custom_role_missing_a_hiring_write_may_connect_read_but_not_write()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        // Every hiring write but Create tasks, plus Manage API keys: the role may connect, and may not hand out Write.
        string[] allButCreateTasks =
        [
            "ViewHiring", "ManageApiKeys", "EditJobs", "AddCandidates", "MoveCandidates", "AddNotesAndTags", "CompleteTasks",
            "EmailCandidates", "FillInScorecards", "ReviewVerification", "RunAutomations", "EditHiringTemplates",
        ];
        var role = (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Team lead", ["copyFrom"] = "Viewer" }))
            .Expect(HttpStatusCode.Created).Id();
        (await owner.PutAsync($"/v1/organization/roles/{role}", new JsonObject
        {
            ["name"] = "Team lead", ["permissions"] = new JsonArray([.. allButCreateTasks.Select(p => JsonValue.Create(p))]),
        })).Expect(HttpStatusCode.OK);
        using var lead = await fixture.InviteAndJoinAsync(owner, "Viewer");
        await owner.GiveRoleAsync(lead, role);
        var request = await app.NewRequestAsync("Claude");

        var info = (await lead.GetAuthorizationRequestAsync(request)).Expect(HttpStatusCode.OK);
        var refused = (await lead.DecideAsync(request, approve: true, "Read", "Write")).Expect(HttpStatusCode.Forbidden);
        (await lead.GetAsync("/v1/organization/oauth-connections")).Expect(HttpStatusCode.OK).Count().Should().Be(0, "a refused approval connects nothing");
        var verification = (await lead.DecideAsync(request, approve: true, "Read", "VerificationRead")).Expect(HttpStatusCode.Forbidden);
        var approved = await lead.ApproveAsync(request, "Read");

        var offers = info.At("scopes").EnumerateArray().ToDictionary(s => s.GetProperty("scope").GetString()!, s => s);
        offers["Read"].GetProperty("grantable").GetBoolean().Should().BeTrue();
        offers["Write"].GetProperty("grantable").GetBoolean().Should().BeFalse();
        offers["Write"].GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
        refused.Text("title").Should().Be("More access than your role");
        verification.Text("title").Should().Be("Owners only");
        approved.Code.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task An_admin_may_grant_write_but_not_verification_read()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        using var app = new OAuthClientApp(fixture);
        var request = await app.NewRequestAsync("Claude");

        var info = (await admin.GetAuthorizationRequestAsync(request)).Expect(HttpStatusCode.OK);
        var refused = (await admin.DecideAsync(request, approve: true, "Read", "VerificationRead")).Expect(HttpStatusCode.Forbidden);
        var smuggled = (await admin.DecideAsync(request, approve: true, "Read", "Write", "VerificationRead")).Expect(HttpStatusCode.Forbidden);
        var connection = await admin.ConnectAsync(app, "Claude writes", ["Read", "Write"]);

        var offers = info.At("scopes").EnumerateArray().ToDictionary(s => s.GetProperty("scope").GetString()!, s => s.GetProperty("grantable").GetBoolean());
        offers.Should().Equal(new Dictionary<string, bool> { ["Read"] = true, ["Write"] = true, ["VerificationRead"] = false });
        refused.Text("title").Should().Be("Owners only");
        smuggled.Text("title").Should().Be("Owners only");
        (await owner.GetAsync("/v1/organization/oauth-connections")).Count().Should().Be(1, "only the Read and Write approval connected");
        connection.Scope.Split(' ').Should().BeEquivalentTo("read", "write");
    }

    [Fact]
    public async Task Only_the_people_who_may_make_api_keys_may_see_or_decide_a_request_or_manage_connections()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var connection = await owner.ConnectAsync(app, "Claude", ["Read"]);
        var request = await app.NewRequestAsync("Claude again");
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        // Owners and admins always see every job; only a custom role can be limited to the jobs a person is on.
        var adminLike = (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Team lead", ["copyFrom"] = "Admin" }))
            .Expect(HttpStatusCode.Created).Id();
        using var narrow = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        await owner.GiveRoleAsync(narrow, adminLike);

        var members = (await owner.GetAsync("/v1/organization/members")).Expect(HttpStatusCode.OK);
        members.Body.EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == narrow.UserId)
            .GetProperty("jobAccess").GetString().Should().Be("HiringTeamOnly", "this admin sees only the jobs they are on");
        foreach (var (label, person) in new[] { ("recruiter", recruiter), ("viewer", viewer), ("admin-like role on only their jobs", narrow) })
        {
            (await person.GetAuthorizationRequestAsync(request)).Status.Should().Be(HttpStatusCode.Forbidden, label);
            (await person.DecideAsync(request, approve: true, "Read")).Status.Should().Be(HttpStatusCode.Forbidden, label);
            (await person.DecideAsync(request, approve: false)).Status.Should().Be(HttpStatusCode.Forbidden, label);
            (await person.GetAsync("/v1/organization/oauth-connections")).Status.Should().Be(HttpStatusCode.Forbidden, label);
            (await person.PostAsync($"/v1/organization/oauth-connections/{connection.ConnectionId}/revoke")).Status.Should().Be(HttpStatusCode.Forbidden, label);
        }

        (await app.McpAsync(connection.AccessToken)).Status.Should().Be(HttpStatusCode.OK, "nothing was revoked");
        (await owner.GetAsync("/v1/organization/oauth-connections")).Count().Should().Be(1);
    }

    [Fact]
    public async Task Someone_who_is_not_signed_in_cannot_see_or_decide_a_request()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var request = await app.NewRequestAsync("Claude");

        (await app.SendAsync(HttpMethod.Get, $"/v1/oauth/authorization-request?{request.Query()}")).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await owner.GetAsync("/v1/organization/oauth-connections")).Count().Should().Be(0);
    }

    // ---- Protocol security ----

    [Fact]
    public async Task A_code_used_twice_fails_the_second_time_and_ends_the_first_exchanges_tokens()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var connection = await owner.ConnectAsync(app, "Claude", ["Read"]);
        var request = connection.Request;
        (await app.McpAsync(connection.AccessToken)).Status.Should().Be(HttpStatusCode.OK);

        var replay = await app.ExchangeCodeAsync(request.ClientId, connection.Approval.Code, request.RedirectUri, request.Verifier, request.Resource);

        replay.ExpectError("invalid_grant");
        (await app.McpAsync(connection.AccessToken)).Status.Should().Be(HttpStatusCode.Unauthorized, "RFC 6749 4.1.2: a replayed code revokes what it issued");
        (await app.RefreshAsync(request.ClientId, connection.RefreshToken)).ExpectError("invalid_grant");
    }

    [Fact]
    public async Task A_used_code_presented_without_its_proof_ends_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var connection = await owner.ConnectAsync(app, "Claude", ["Read"]);
        var request = connection.Request;

        (await app.ExchangeCodeAsync(request.ClientId, connection.Approval.Code, request.RedirectUri, PkcePair.New().Verifier, request.Resource))
            .ExpectError("invalid_grant");

        (await app.McpAsync(connection.AccessToken)).Status.Should().Be(HttpStatusCode.OK, "only someone who could have made the exchange can show the code was replayed");
    }

    [Fact]
    public async Task A_code_is_refused_with_a_wrong_verifier_redirect_client_or_resource()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var otherClient = await app.RegisterClientAsync("Someone else", OAuthSupport.ClaudeCallback);

        var approval = await owner.ApproveAsync(await app.NewRequestAsync("Claude"), "Read");
        var request = approval.Request;

        (await app.ExchangeCodeAsync(request.ClientId, approval.Code, request.RedirectUri, PkcePair.New().Verifier, request.Resource)).ExpectError("invalid_grant");
        (await app.ExchangeCodeAsync(request.ClientId, approval.Code, request.RedirectUri, verifier: null, request.Resource)).ExpectError("invalid_grant");
        (await app.ExchangeCodeAsync(request.ClientId, approval.Code, "https://claude.ai/api/mcp/other", request.Verifier, request.Resource)).ExpectError("invalid_grant");
        (await app.ExchangeCodeAsync(otherClient, approval.Code, request.RedirectUri, request.Verifier, request.Resource)).ExpectError("invalid_grant");
        (await app.ExchangeCodeAsync(request.ClientId, approval.Code, request.RedirectUri, request.Verifier, "https://example.com/other")).ExpectError("invalid_target");

        // None of those spent the code: someone who saw it but cannot finish the exchange cannot take it from the client.
        (await app.ExchangeCodeAsync(request.ClientId, approval.Code, request.RedirectUri, request.Verifier, request.Resource)).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var request = await app.NewRequestAsync("Claude");
        var approval = await owner.ApproveAsync(request, "Read");
        await fixture.ExpireCodesAsync(owner.OrganizationId);

        var late = await app.ExchangeCodeAsync(request.ClientId, approval.Code, request.RedirectUri, request.Verifier, request.Resource);

        late.ExpectError("invalid_grant");
    }

    [Fact]
    public async Task A_made_up_or_foreign_code_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var request = await app.NewRequestAsync("Claude");
        var approval = await owner.ApproveAsync(request, "Read");
        var madeUp = $"shc_{owner.OrganizationId:N}_{new string('a', 64)}";
        var otherOrganization = approval.Code.Replace(owner.OrganizationId.ToString("N"), Guid.NewGuid().ToString("N"), StringComparison.Ordinal);

        foreach (var code in new[] { madeUp, otherOrganization, "garbage", "" })
        {
            (await app.ExchangeCodeAsync(request.ClientId, code, request.RedirectUri, request.Verifier, request.Resource)).ExpectError("invalid_grant");
        }

        (await app.ExchangeCodeAsync(request.ClientId, approval.Code, request.RedirectUri, request.Verifier, request.Resource))
            .Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_authorization_request_that_cannot_be_trusted_is_a_400_and_never_a_redirect()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var good = await app.NewRequestAsync("Claude");
        var cases = new Dictionary<string, OAuthRequest>
        {
            ["an unregistered redirect"] = good with { RedirectUri = "https://evil.example.com/callback" },
            ["a redirect that only starts like the registered one"] = good with { RedirectUri = good.RedirectUri + "/more" },
            ["no redirect"] = good with { RedirectUri = "" },
            ["an unknown client"] = good with { ClientId = Guid.NewGuid().ToString("N") },
            ["a client id that is not an id"] = good with { ClientId = "not-a-client" },
            ["no challenge"] = good with { CodeChallenge = null },
            ["no challenge method"] = good with { CodeChallengeMethod = null },
            ["the plain method"] = good with { CodeChallengeMethod = "plain", CodeChallenge = good.Verifier },
            ["a challenge that is not S256 shaped"] = good with { CodeChallenge = "short" },
            ["the token response type"] = good with { ResponseType = "token" },
            ["another resource"] = good with { Resource = "https://example.com/other" },
        };

        foreach (var (label, request) in cases)
        {
            using var response = await owner.Session.Client.GetAsync($"/v1/oauth/authorization-request?{request.Query()}", Ct);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, label);
            response.Headers.Location.Should().BeNull(label + " must not redirect");

            var decided = await owner.DecideAsync(request, approve: true, "Read");
            decided.Status.Should().Be(HttpStatusCode.BadRequest, label);
            var denied = await owner.DecideAsync(request, approve: false);
            denied.Status.Should().Be(HttpStatusCode.BadRequest, label + ": even a refusal is not sent to an address that was not registered");
        }

        (await owner.GetAsync("/v1/organization/oauth-connections")).Count().Should().Be(0);
    }

    [Fact]
    public async Task A_request_without_a_resource_or_scope_is_accepted()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var request = await app.NewRequestAsync("Claude");

        (await owner.GetAuthorizationRequestAsync(request with { Resource = null })).Expect(HttpStatusCode.OK);
        (await owner.GetAuthorizationRequestAsync(request with { Scope = null })).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_rotates_the_pair_and_the_new_access_token_works()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var connection = await owner.ConnectAsync(app, "Claude", ["Read", "Write"]);

        var refreshed = (await app.RefreshAsync(connection.Request.ClientId, connection.RefreshToken, connection.Request.Resource)).Expect(HttpStatusCode.OK);

        refreshed.Text("access_token").Should().NotBe(connection.AccessToken).And.StartWith($"sho_{owner.OrganizationId:N}_");
        refreshed.Text("refresh_token").Should().NotBe(connection.RefreshToken).And.StartWith("shr_");
        refreshed.Text("scope").Split(' ').Should().BeEquivalentTo("read", "write");
        refreshed.Header("Cache-Control").Should().Contain("no-store");
        await using var mcp = await McpSupport.ConnectAsync(fixture, refreshed.Text("access_token"));
        (await mcp.ToolNamesAsync()).Should().Equal(Sorted(ReadTools.Concat(WriteTools)));
        (await app.RefreshAsync(connection.Request.ClientId, refreshed.Text("refresh_token"))).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Presenting_a_rotated_refresh_token_again_fails_and_revokes_the_new_tokens_too()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var connection = await owner.ConnectAsync(app, "Claude", ["Read"]);
        var rotated = (await app.RefreshAsync(connection.Request.ClientId, connection.RefreshToken)).Expect(HttpStatusCode.OK);
        (await app.McpAsync(rotated.Text("access_token"))).Status.Should().Be(HttpStatusCode.OK);

        var reuse = await app.RefreshAsync(connection.Request.ClientId, connection.RefreshToken);

        reuse.ExpectError("invalid_grant");
        (await app.McpAsync(rotated.Text("access_token"))).Status.Should().Be(HttpStatusCode.Unauthorized, "a reused refresh token means it may have leaked");
        (await app.RefreshAsync(connection.Request.ClientId, rotated.Text("refresh_token"))).ExpectError("invalid_grant");
    }

    [Fact]
    public async Task A_refresh_token_is_refused_to_another_client_and_a_foreign_resource()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var first = await owner.ConnectAsync(app, "Claude", ["Read"]);
        var second = await owner.ConnectAsync(app, "Another assistant", ["Read"]);

        (await app.RefreshAsync(second.Request.ClientId, first.RefreshToken)).ExpectError("invalid_grant");
        (await app.RefreshAsync(second.Request.ClientId, second.RefreshToken, "https://example.com/other")).ExpectError("invalid_target");
        (await app.RefreshAsync(second.Request.ClientId, "shr_garbage")).ExpectError("invalid_grant");

        // A refused request spent neither token.
        (await app.RefreshAsync(first.Request.ClientId, first.RefreshToken)).Expect(HttpStatusCode.OK);
        (await app.RefreshAsync(second.Request.ClientId, second.RefreshToken)).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Removing_a_member_disconnects_the_assistants_they_connected_and_keeps_their_api_keys_and_everyone_elses_assistants()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        using var leaver = await fixture.InviteAndJoinAsync(owner, "Admin");
        var theirs = await leaver.ConnectAsync(app, "Their assistant", ["Read"]);
        var mine = await owner.ConnectAsync(app, "My assistant", ["Read"]);
        var key = (await leaver.CreateKeyAsync("Their key", ["Read"])).Id("key.id");

        (await owner.DeleteAsync($"/v1/organization/members/{await owner.MemberIdAsync(leaver.UserId)}")).Expect(HttpStatusCode.NoContent);

        (await app.McpAsync(theirs.AccessToken)).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await app.RefreshAsync(theirs.Request.ClientId, theirs.RefreshToken)).ExpectError("invalid_grant");
        (await app.McpAsync(mine.AccessToken)).Status.Should().Be(HttpStatusCode.OK);

        var connections = (await owner.GetAsync("/v1/organization/oauth-connections")).Expect(HttpStatusCode.OK).Body.EnumerateArray().ToList();
        connections.Single(c => c.GetProperty("clientName").GetString() == "Their assistant").GetProperty("revokedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        connections.Single(c => c.GetProperty("clientName").GetString() == "My assistant").GetProperty("revokedAt").ValueKind.Should().Be(JsonValueKind.Null);

        var keys = (await owner.GetAsync("/v1/organization/api-keys")).Expect(HttpStatusCode.OK).Body.EnumerateArray();
        keys.Single(k => k.GetProperty("id").GetGuid() == key).GetProperty("revokedAt").ValueKind.Should().Be(JsonValueKind.Null, "an API key is the organization's (ADR-0011)");
    }

    [Fact]
    public async Task An_expired_access_token_is_refused_until_the_client_refreshes()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var connection = await owner.ConnectAsync(app, "Claude", ["Read"]);
        await fixture.ExpireAccessTokensAsync(owner.OrganizationId);

        var expired = await app.McpAsync(connection.AccessToken);
        var refreshed = (await app.RefreshAsync(connection.Request.ClientId, connection.RefreshToken)).Expect(HttpStatusCode.OK);

        expired.Status.Should().Be(HttpStatusCode.Unauthorized);
        expired.Header("WWW-Authenticate").Should().Contain("invalid_token");
        (await app.McpAsync(refreshed.Text("access_token"))).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revoking_with_another_clients_id_leaves_the_token_alone_and_with_its_own_id_ends_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var ours = await owner.ConnectAsync(app, "Claude", ["Read"]);
        var theirs = await owner.ConnectAsync(app, "Another assistant", ["Read"]);

        // Same answer either way, so the endpoint says nothing about whose token it is.
        (await app.RevokeAsync(ours.AccessToken, theirs.Request.ClientId)).Expect(HttpStatusCode.OK);
        (await app.RevokeAsync(ours.RefreshToken, theirs.Request.ClientId)).Expect(HttpStatusCode.OK);
        (await app.McpAsync(ours.AccessToken)).Status.Should().Be(HttpStatusCode.OK, "another client cannot revoke it");
        (await app.RefreshAsync(ours.Request.ClientId, ours.RefreshToken)).Expect(HttpStatusCode.OK);

        (await app.RevokeAsync(theirs.AccessToken, theirs.Request.ClientId)).Expect(HttpStatusCode.OK);
        (await app.McpAsync(theirs.AccessToken)).Status.Should().Be(HttpStatusCode.Unauthorized, "its own client can");
    }

    [Fact]
    public async Task Revoking_a_refresh_token_ends_the_connections_tokens_and_revoking_an_access_token_ends_that_token()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var byRefresh = await owner.ConnectAsync(app, "Claude", ["Read"]);
        var byAccess = await owner.ConnectAsync(app, "Another assistant", ["Read"]);

        (await app.RevokeAsync(byRefresh.RefreshToken)).Expect(HttpStatusCode.OK);
        (await app.RevokeAsync(byAccess.AccessToken)).Expect(HttpStatusCode.OK);
        (await app.RevokeAsync("sho_not_a_token")).Expect(HttpStatusCode.OK);
        (await app.RevokeAsync($"shr_{owner.OrganizationId:N}_{new string('b', 64)}")).Expect(HttpStatusCode.OK);

        (await app.McpAsync(byRefresh.AccessToken)).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await app.RefreshAsync(byRefresh.Request.ClientId, byRefresh.RefreshToken)).ExpectError("invalid_grant");
        (await app.McpAsync(byAccess.AccessToken)).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_token_endpoint_refuses_what_it_does_not_offer()
    {
        using var app = new OAuthClientApp(fixture);

        (await app.TokenAsync([new("grant_type", "client_credentials")])).ExpectError("unsupported_grant_type");
        (await app.TokenAsync([new("grant_type", "password"), new("username", "a"), new("password", "b")])).ExpectError("unsupported_grant_type");
        (await app.TokenAsync([])).ExpectError("unsupported_grant_type");
        (await app.SendAsync(HttpMethod.Post, "/v1/oauth/token", new StringContent("{\"grant_type\":\"authorization_code\"}", System.Text.Encoding.UTF8, "application/json")))
            .ExpectError("invalid_request");
    }

    // ---- Audience and isolation ----

    [Fact]
    public async Task An_access_token_works_on_the_mcp_endpoint_only()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await owner.PublishJobAsync(job.Id());
        var connection = await owner.ConnectAsync(app, "Claude", ["Read", "Write"]);
        var token = connection.AccessToken;

        (await app.McpAsync(token)).Status.Should().Be(HttpStatusCode.OK);
        foreach (var path in new[]
        {
            "/v1/jobs", $"/v1/jobs/{job.Id()}", $"/v1/applications/{Guid.NewGuid()}", "/v1/templates", "/v1/candidates",
            "/v1/organization/api-keys", "/v1/organization/oauth-connections", "/v1/organization/members", "/v1/auth/me",
            $"/v1/oauth/authorization-request?{connection.Request.Query()}",
        })
        {
            (await app.StatusAsync(HttpMethod.Get, path, token)).Should().Be(HttpStatusCode.Unauthorized, path);
        }

        foreach (var path in new[] { "/v1/jobs", "/v1/organization/api-keys", "/v1/oauth/authorize", $"/v1/organization/oauth-connections/{connection.ConnectionId}/revoke" })
        {
            (await app.StatusAsync(HttpMethod.Post, path, token)).Should().Be(HttpStatusCode.Unauthorized, path);
        }

        (await owner.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK);
        (await owner.GetAsync("/v1/organization/oauth-connections")).Count().Should().Be(1, "none of those calls changed the connection");
    }

    [Fact]
    public async Task An_api_key_cannot_call_the_consent_or_connection_routes()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var connection = await owner.ConnectAsync(app, "Claude", ["Read"]);
        var created = await owner.CreateKeyAsync("Integration", ["Read", "Write"]);
        var forwarded = $"10.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}";
        using var key = new KeyClient(fixture, created.Text("token"), forwarded);

        (await key.GetAsync($"/v1/oauth/authorization-request?{connection.Request.Query()}")).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await key.PostAsync("/v1/oauth/authorize", connection.Request.Body(approve: true, "Read"))).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await key.GetAsync("/v1/organization/oauth-connections")).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await key.PostAsync($"/v1/organization/oauth-connections/{connection.ConnectionId}/revoke")).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await app.McpAsync(connection.AccessToken)).Status.Should().Be(HttpStatusCode.OK, "the connection is untouched");
    }

    [Fact]
    public async Task A_connection_reads_and_changes_only_its_own_organizations_data()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var theirJob = await theirs.CreateJobAsync(Samples.RemoteJob("Their Engineer"));
        await theirs.PublishJobAsync(theirJob.Id());
        var theirApplication = (await theirs.CreateApplicationAsync(theirJob.Id(), ApiSession.NewEmail("theirs"), "Robin Theirs")).Id();
        var ourJob = await ours.CreateJobAsync(Samples.RemoteJob("Our Engineer"));
        await ours.PublishJobAsync(ourJob.Id());
        var ourApplication = (await ours.CreateApplicationAsync(ourJob.Id(), ApiSession.NewEmail("ours"), "Casey Ours")).Id();
        var connection = await ours.ConnectAsync(app, "Claude", ["Read", "Write"]);
        await using var mcp = await McpSupport.ConnectAsync(fixture, connection.AccessToken);

        (await mcp.CallAsync("find_jobs", new())).GetProperty("jobs").EnumerateArray().Select(j => j.GetProperty("id").GetGuid()).Should().Equal(ourJob.Id());
        (await mcp.CallAsync("find_candidates", new() { ["text"] = "Robin" })).GetProperty("applications").GetArrayLength().Should().Be(0);
        (await mcp.CallAsync("find_candidates", new() { ["text"] = "Casey" })).GetProperty("applications").GetArrayLength().Should().Be(1);
        (await mcp.CallFailingAsync("get_application", new() { ["applicationId"] = theirApplication })).Should().Contain("No application");
        (await mcp.CallFailingAsync("add_note", new() { ["applicationId"] = theirApplication, ["note"] = "Hello" })).Should().Contain("No application");
        (await mcp.CallAsync("get_application", new() { ["applicationId"] = ourApplication })).GetProperty("id").GetGuid().Should().Be(ourApplication);
        (await theirs.GetAsync($"/v1/applications/{theirApplication}/timeline")).Body.EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).Should().NotContain("NoteAdded");
    }

    [Fact]
    public async Task A_token_cannot_be_moved_to_another_organization_by_changing_the_id_it_names()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var connection = await ours.ConnectAsync(app, "Claude", ["Read"]);
        var forged = connection.AccessToken.Replace(ours.OrganizationId.ToString("N"), theirs.OrganizationId.ToString("N"), StringComparison.Ordinal);

        var response = await app.McpAsync(forged);

        forged.Should().NotBe(connection.AccessToken);
        response.Status.Should().Be(HttpStatusCode.Unauthorized);
        (await app.RefreshAsync(connection.Request.ClientId, connection.RefreshToken.Replace(
            ours.OrganizationId.ToString("N"), theirs.OrganizationId.ToString("N"), StringComparison.Ordinal))).ExpectError("invalid_grant");
    }

    // ---- Recording ----

    [Fact]
    public async Task A_note_added_through_a_connection_shows_in_the_timeline_under_the_client_name()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await owner.PublishJobAsync(job.Id());
        var application = (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"), "Casey Morgan")).Id();
        var connection = await owner.ConnectAsync(app, "Claude (work laptop)", ["Read", "Write"]);
        await using var mcp = await McpSupport.ConnectAsync(fixture, connection.AccessToken);

        var note = await mcp.CallAsync("add_note", new() { ["applicationId"] = application, ["note"] = "Strong portfolio." });

        note.GetProperty("actorApiKeyName").GetString().Should().Be("Claude (work laptop)");
        var entry = (await owner.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Single(e => e.GetProperty("type").GetString() == "NoteAdded");
        entry.GetProperty("actorApiKeyId").GetGuid().Should().Be(connection.ConnectionId);
        entry.GetProperty("actorApiKeyName").GetString().Should().Be("Claude (work laptop)");
        entry.GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null);
        entry.GetProperty("note").GetString().Should().Be("Strong portfolio.");
    }

    // ---- Management ----

    [Fact]
    public async Task The_connections_list_shows_each_connection_and_the_api_keys_list_does_not()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var key = await owner.CreateKeyAsync("Integration", ["Read"]);
        var connection = await owner.ConnectAsync(app, "Claude", ["Read", "Write"]);

        var connections = (await owner.GetAsync("/v1/organization/oauth-connections")).Expect(HttpStatusCode.OK);
        var keys = (await owner.GetAsync("/v1/organization/api-keys")).Expect(HttpStatusCode.OK);

        var listed = connections.Body.EnumerateArray().Should().ContainSingle().Subject;
        listed.GetProperty("id").GetGuid().Should().Be(connection.ConnectionId);
        listed.GetProperty("clientName").GetString().Should().Be("Claude");
        listed.GetProperty("connectedByUserId").GetGuid().Should().Be(owner.UserId);
        listed.GetProperty("connectedByName").GetString().Should().NotBeNullOrWhiteSpace();
        listed.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()).Should().BeEquivalentTo("Read", "Write");
        listed.GetProperty("revokedAt").ValueKind.Should().Be(JsonValueKind.Null);
        listed.GetProperty("createdAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        keys.Body.EnumerateArray().Select(k => k.GetProperty("id").GetGuid()).Should().Equal([key.Id("key.id")], "a connection is not an API key");
        (await owner.PostAsync($"/v1/organization/api-keys/{connection.ConnectionId}/revoke")).Expect(HttpStatusCode.NotFound);
        (await app.McpAsync(connection.AccessToken)).Status.Should().Be(HttpStatusCode.OK, "the keys routes cannot touch a connection");
    }

    [Fact]
    public async Task Revoking_a_connection_ends_its_tokens_at_once_and_revoking_twice_is_fine()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var connection = await owner.ConnectAsync(app, "Claude", ["Read", "Write"]);
        await using var mcp = await McpSupport.ConnectAsync(fixture, connection.AccessToken);
        (await mcp.ToolNamesAsync()).Should().NotBeEmpty();

        var first = (await owner.PostAsync($"/v1/organization/oauth-connections/{connection.ConnectionId}/revoke")).Expect(HttpStatusCode.OK);
        var second = (await owner.PostAsync($"/v1/organization/oauth-connections/{connection.ConnectionId}/revoke")).Expect(HttpStatusCode.OK);

        first.At("revokedAt").ValueKind.Should().Be(JsonValueKind.String);
        second.At("revokedAt").GetDateTimeOffset().Should().Be(first.At("revokedAt").GetDateTimeOffset(), "the first revocation stands");
        var refused = await app.McpAsync(connection.AccessToken);
        refused.Status.Should().Be(HttpStatusCode.Unauthorized);
        refused.Header("WWW-Authenticate").Should().Contain("invalid_token");
        (await app.RefreshAsync(connection.Request.ClientId, connection.RefreshToken)).ExpectError("invalid_grant");
        (await owner.GetAsync("/v1/organization/oauth-connections")).Body.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("revokedAt").ValueKind.Should().Be(JsonValueKind.String, "the row stays, marked revoked");
    }

    [Fact]
    public async Task Another_organizations_connection_is_not_found()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var connection = await theirs.ConnectAsync(app, "Claude", ["Read"]);

        (await ours.PostAsync($"/v1/organization/oauth-connections/{connection.ConnectionId}/revoke")).Expect(HttpStatusCode.NotFound);
        (await ours.PostAsync($"/v1/organization/oauth-connections/{Guid.NewGuid()}/revoke")).Expect(HttpStatusCode.NotFound);
        (await ours.GetAsync("/v1/organization/oauth-connections")).Count().Should().Be(0, "their connection is not listed here");
        (await app.McpAsync(connection.AccessToken)).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Connecting_again_as_the_same_assistant_leaves_one_active_connection_for_that_person()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        using var app = new OAuthClientApp(fixture);

        // claude.ai registers a new client every time, with the same name and redirect addresses.
        var first = await owner.ConnectAsync(app, "Claude", ["Read"]);
        var second = await owner.ConnectAsync(app, "Claude", ["Read", "Write"]);
        var other = await owner.ConnectAsync(app, "Claude Code", ["Read"]);
        var colleague = await admin.ConnectAsync(app, "Claude", ["Read"]);

        var list = (await owner.GetAsync("/v1/organization/oauth-connections")).Expect(HttpStatusCode.OK).Body.EnumerateArray().ToList();
        var active = list.Where(c => c.GetProperty("revokedAt").ValueKind == JsonValueKind.Null)
            .Select(c => c.GetProperty("id").GetGuid()).ToList();
        active.Should().BeEquivalentTo([second.ConnectionId, other.ConnectionId, colleague.ConnectionId],
            "the owner's first Claude connection was replaced; another name and another person are separate connections");
        list.Should().HaveCount(4);
        first.ConnectionId.Should().NotBe(second.ConnectionId);
        (await app.McpAsync(first.AccessToken)).Status.Should().Be(HttpStatusCode.Unauthorized, "the replaced connection stops working");
        (await app.McpAsync(second.AccessToken)).Status.Should().Be(HttpStatusCode.OK);
        (await app.McpAsync(colleague.AccessToken)).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_connection_whose_redirect_addresses_differ_is_not_a_repeat()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);

        var web = await owner.ConnectAsync(app, "Claude", ["Read"]);
        var local = await owner.ConnectAsync(app, "Claude", ["Read"], redirect: "http://127.0.0.1:4455/callback");

        (await app.McpAsync(web.AccessToken)).Status.Should().Be(HttpStatusCode.OK);
        (await app.McpAsync(local.AccessToken)).Status.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync("/v1/organization/oauth-connections")).Body.EnumerateArray()
            .Count(c => c.GetProperty("revokedAt").ValueKind == JsonValueKind.Null).Should().Be(2);
    }

    // ---- CORS ----

    [Theory]
    [InlineData("OPTIONS", "/v1/oauth/token", "POST")]
    [InlineData("OPTIONS", "/v1/oauth/register", "POST")]
    [InlineData("OPTIONS", "/v1/oauth/revoke", "POST")]
    [InlineData("OPTIONS", "/.well-known/oauth-authorization-server", "GET")]
    [InlineData("OPTIONS", "/.well-known/oauth-protected-resource/v1/mcp", "GET")]
    public async Task The_protocol_endpoints_answer_a_browser_clients_preflight_without_credentials(string method, string path, string requested)
    {
        using var app = new OAuthClientApp(fixture);
        var origin = "https://inspector.example.org";

        var preflight = await app.SendAsync(
            new HttpMethod(method),
            path,
            headers: new Dictionary<string, string>
            {
                ["Origin"] = origin, ["Access-Control-Request-Method"] = requested, ["Access-Control-Request-Headers"] = "content-type",
            });

        preflight.Status.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);
        preflight.Header("Access-Control-Allow-Origin").Should().BeOneOf("*", origin);
        preflight.Header("Access-Control-Allow-Credentials").Should().BeNull("these routes never take cookies");
        preflight.Header("Access-Control-Allow-Methods").Should().Contain(requested);
        preflight.Header("Access-Control-Allow-Headers").Should().ContainEquivalentOf("content-type");
    }

    [Fact]
    public async Task A_cross_origin_call_to_the_token_and_discovery_endpoints_is_readable_and_the_consent_routes_stay_same_origin()
    {
        using var app = new OAuthClientApp(fixture);
        var origin = new Dictionary<string, string> { ["Origin"] = "https://inspector.example.org" };

        var discovery = await app.SendAsync(HttpMethod.Get, "/.well-known/oauth-authorization-server", headers: origin);
        var token = await app.SendAsync(
            HttpMethod.Post, "/v1/oauth/token", new FormUrlEncodedContent([new("grant_type", "refresh_token")]), headers: origin);
        var consent = await app.SendAsync(
            new HttpMethod("OPTIONS"),
            "/v1/oauth/authorize",
            headers: new Dictionary<string, string>
            {
                ["Origin"] = "https://inspector.example.org", ["Access-Control-Request-Method"] = "POST", ["Access-Control-Request-Headers"] = "content-type",
            });

        discovery.Header("Access-Control-Allow-Origin").Should().BeOneOf("*", "https://inspector.example.org");
        token.Header("Access-Control-Allow-Origin").Should().BeOneOf("*", "https://inspector.example.org", "an error answer is readable too");
        token.Header("Access-Control-Allow-Credentials").Should().BeNull();
        consent.Header("Access-Control-Allow-Origin").Should().BeNull("only a same-origin page may call the consent routes");
    }
}
