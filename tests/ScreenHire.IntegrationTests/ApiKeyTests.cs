using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Application.Integrity;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Organization API keys (ADR-0011): an owner creates a key in a session, and an integrator sends it as
/// <c>Authorization: Bearer</c> to the hiring API with no cookie and no antiforgery header. The key's organization is the
/// tenant, so another organization's ids are 404 exactly as in a session; a revoked key is 401, a read-only key cannot
/// write, each key has its own rate limit, and no other route accepts a key.
/// </summary>
public sealed class ApiKeyTests(AppHostFixture fixture)
{
    [Fact]
    public async Task A_key_reads_and_writes_its_own_organization_without_a_session_or_antiforgery()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await owner.PublishJobAsync(job.Id());
        var created = await CreateKeyAsync(owner, "ATS sync", ["Read", "Write"]);
        var token = created.Text("token");
        using var key = new KeyClient(fixture, token);

        var jobs = (await key.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK);
        var added = (await key.PostAsync("/v1/jobs", Samples.RemoteJob("Added by the API"))).Expect(HttpStatusCode.Created);
        var application = (await key.PostAsync($"/v1/jobs/{job.Id()}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["fullName"] = "Alex Rowe", ["email"] = ApiSession.NewEmail("candidate") },
        })).Expect(HttpStatusCode.Created);
        var moved = (await key.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "screening" }))
            .Expect(HttpStatusCode.OK);
        var timeline = (await key.GetAsync($"/v1/applications/{application.Id()}/timeline")).Expect(HttpStatusCode.OK);
        var candidates = (await key.GetAsync("/v1/candidates")).Expect(HttpStatusCode.OK);
        var templates = (await key.GetAsync("/v1/templates")).Expect(HttpStatusCode.OK);

        jobs.Body.EnumerateArray().Select(j => j.GetProperty("id").GetGuid()).Should().Equal(job.Id());
        moved.Text("stage.id").Should().Be("screening");
        timeline.At($"{timeline.Count() - 1}.actorUserId").ValueKind.Should().Be(JsonValueKind.Null, "a key acts for no person");
        timeline.Body.EnumerateArray().Select(e => e.GetProperty("actorApiKeyId").GetGuid())
            .Should().AllBeEquivalentTo(created.Id("key.id"), "the timeline records which key added and moved the application");
        candidates.Count().Should().Be(1);
        templates.Count().Should().Be(2, "the system templates");
        (await owner.GetAsync($"/v1/jobs/{added.Id()}")).Expect(HttpStatusCode.OK).Text("title").Should().Be("Added by the API");
        var listed = (await owner.GetAsync("/v1/organization/api-keys")).Expect(HttpStatusCode.OK);
        listed.Count().Should().Be(1);
        listed.Text("0.prefix").Should().Be(created.Text("key.prefix"));
        token.Should().StartWith(created.Text("key.prefix") + "_");
        listed.At("0.lastUsedAt").ValueKind.Should().Be(JsonValueKind.String, "the key has been used");
        listed.Body.GetRawText().Should().NotContain(token, "the key is shown once, when it is created");
        listed.At("0").TryGetProperty("token", out _).Should().BeFalse();
        var me = (await owner.Session.CurrentUserAsync(TestContext.Current.CancellationToken))!.Value;
        listed.Id("0.createdByUserId").Should().Be(me.GetProperty("id").GetGuid(), "removing a member offers to revoke the keys they created");
    }

    [Fact]
    public async Task A_key_never_receives_integrity_levels_which_stay_with_the_person_reviewing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await IntegritySupport.OpenJobAsync(fixture, owner);
        var applicationId = await IntegritySupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);
        await IntegritySupport.AssessedAsync(owner, applicationId);
        using var key = new KeyClient(fixture, (await CreateKeyAsync(owner, "Export", ["Read"])).Text("token"));

        var session = (await owner.GetAsync($"/v1/jobs/{jobId}/applications")).Expect(HttpStatusCode.OK);
        var lists = new Dictionary<string, ApiResult>
        {
            ["job applications"] = await key.GetAsync($"/v1/jobs/{jobId}/applications"),
            ["applications"] = await key.GetAsync("/v1/applications"),
            ["candidate"] = await key.GetAsync($"/v1/candidates/{session.Id("0.candidateId")}"),
        };
        var sessionTimeline = (await owner.GetAsync($"/v1/applications/{applicationId}/timeline")).Expect(HttpStatusCode.OK);
        var keyTimeline = (await key.GetAsync($"/v1/applications/{applicationId}/timeline")).Expect(HttpStatusCode.OK);

        session.At("0.integrity").ValueKind.Should().Be(JsonValueKind.Object, "the reviewer sees the assessment");
        Types(sessionTimeline).Should().Contain("IntegrityAssessed");
        Types(keyTimeline).Should().NotContain("IntegrityAssessed", "a key does not learn that an assessment ran").And.Contain("Submitted");
        lists["job applications"].Expect(HttpStatusCode.OK).At("0.integrity").ValueKind.Should().Be(JsonValueKind.Null);
        lists["applications"].Expect(HttpStatusCode.OK).At("0.integrity").ValueKind.Should().Be(JsonValueKind.Null);
        lists["candidate"].Expect(HttpStatusCode.OK).At("applications.0.integrity").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_key_gets_404_for_another_organizations_ids()
    {
        using var a = await fixture.SignedInWithOrganizationAsync();
        using var b = await fixture.SignedInWithOrganizationAsync();
        var bJob = await b.CreateJobAsync(Samples.RemoteJob("B engineer"));
        await b.PublishJobAsync(bJob.Id());
        var bApplication = await b.CreateApplicationAsync(bJob.Id(), ApiSession.NewEmail("b-candidate"));
        var (bTemplateId, _) = await b.CreatePublishedTemplateAsync("B pipeline");
        var bJobBefore = (await b.GetAsync($"/v1/jobs/{bJob.Id()}")).Body.GetRawText();
        using var aKey = new KeyClient(fixture, (await CreateKeyAsync(a, "A sync", ["Read", "Write"])).Text("token"));

        var responses = new Dictionary<string, ApiResult>
        {
            ["job"] = await aKey.GetAsync($"/v1/jobs/{bJob.Id()}"),
            ["update job"] = await aKey.PutAsync($"/v1/jobs/{bJob.Id()}", Samples.RemoteJob("Taken over")),
            ["close job"] = await aKey.PostAsync($"/v1/jobs/{bJob.Id()}/close"),
            ["job applications"] = await aKey.GetAsync($"/v1/jobs/{bJob.Id()}/applications"),
            ["application"] = await aKey.GetAsync($"/v1/applications/{bApplication.Id()}"),
            ["move"] = await aKey.PostAsync($"/v1/applications/{bApplication.Id()}/move", new JsonObject { ["toStageId"] = "screening" }),
            ["candidate"] = await aKey.GetAsync($"/v1/candidates/{bApplication.Id("candidate.id")}"),
            ["template"] = await aKey.GetAsync($"/v1/templates/{bTemplateId}"),
        };

        responses.Should().OnlyContain(r => r.Value.Status == HttpStatusCode.NotFound, "another tenant's row looks exactly like a missing one");
        (await aKey.GetAsync("/v1/jobs")).Count().Should().Be(0);
        (await b.GetAsync($"/v1/jobs/{bJob.Id()}")).Body.GetRawText().Should().Be(bJobBefore, "nothing of B's job changed");
        (await b.GetAsync($"/v1/applications/{bApplication.Id()}")).Text("stage.id").Should().Be("applied");
    }

    [Fact]
    public async Task A_revoked_key_a_forged_key_and_a_malformed_key_get_401()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var created = await CreateKeyAsync(owner, "Short-lived", ["Read"]);
        var token = created.Text("token");
        using var key = new KeyClient(fixture, token);
        (await key.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK);

        var revoked = (await owner.PostAsync($"/v1/organization/api-keys/{created.Id("key.id")}/revoke")).Expect(HttpStatusCode.OK);
        using var afterRevoke = await key.SendAsync(HttpMethod.Get, "/v1/jobs");

        revoked.At("revokedAt").ValueKind.Should().Be(JsonValueKind.String);
        afterRevoke.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        afterRevoke.Headers.WwwAuthenticate.Select(h => h.Scheme).Should().Contain("Bearer");
        // The same secret naming another organization finds nothing under that organization's tenant.
        var parts = token.Split('_');
        using var forged = new KeyClient(fixture, $"{parts[0]}_{parts[1]}_{other.OrganizationId:N}_{parts[3]}");
        (await forged.GetAsync("/v1/jobs")).Status.Should().Be(HttpStatusCode.Unauthorized);
        using var unknown = new KeyClient(fixture, $"shk_00000000_{owner.OrganizationId:N}_{new string('a', 64)}");
        (await unknown.GetAsync("/v1/jobs")).Status.Should().Be(HttpStatusCode.Unauthorized);
        using var malformed = new KeyClient(fixture, "not-a-key");
        (await malformed.GetAsync("/v1/jobs")).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_read_only_key_reads_but_cannot_write()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Data Engineer"));
        using var key = new KeyClient(fixture, (await CreateKeyAsync(owner, "Reporting", ["Read"])).Text("token"));

        var read = await key.GetAsync($"/v1/jobs/{job.Id()}");
        ApiResult[] writes =
        [
            await key.PostAsync("/v1/jobs", Samples.RemoteJob("Sneaky")),
            await key.PutAsync($"/v1/jobs/{job.Id()}", Samples.RemoteJob("Renamed")),
            await key.PostAsync($"/v1/jobs/{job.Id()}/publish"),
            await key.PostAsync("/v1/templates", new JsonObject { ["name"] = "Mine" }),
        ];

        read.Expect(HttpStatusCode.OK);
        writes.Select(w => w.Status).Should().AllBeEquivalentTo(HttpStatusCode.Forbidden);
        writes[0].Text("title").Should().Be("Read-only key");
        (await owner.GetAsync("/v1/jobs")).Count().Should().Be(1);
        (await owner.GetAsync($"/v1/jobs/{job.Id()}")).Text("status").Should().Be("Draft");
    }

    [Fact]
    public async Task Too_many_requests_from_one_key_get_429_without_slowing_other_keys_or_sessions()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var limited = new KeyClient(fixture, (await CreateKeyAsync(owner, "Limited", ["Read"], permitsPerMinute: 3)).Text("token"));
        using var unlimited = new KeyClient(fixture, (await CreateKeyAsync(owner, "Default", ["Read"])).Text("token"));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await limited.GetAsync("/v1/jobs")).Status);
        }

        statuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        (await unlimited.GetAsync("/v1/jobs")).Status.Should().Be(HttpStatusCode.OK, "each key has its own limit");
        (await owner.GetAsync("/v1/jobs")).Status.Should().Be(HttpStatusCode.OK, "sessions are not limited by keys");
    }

    [Fact]
    public async Task Too_many_failed_attempts_from_one_address_get_429_for_every_key_from_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var token = (await CreateKeyAsync(owner, "Good", ["Read"])).Text("token");
        // An address of its own (the API believes X-Forwarded-For from loopback), so other tests keep their budget.
        var address = $"198.51.100.{Random.Shared.Next(1, 255)}";
        using var guesser = new KeyClient(fixture, "shk_not-a-key", address);
        using var goodFromThere = new KeyClient(fixture, token, address);
        using var goodFromElsewhere = new KeyClient(fixture, token);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 30; i++)
        {
            statuses.Add((await guesser.GetAsync("/v1/jobs")).Status);
        }

        using var blocked = await guesser.SendAsync(HttpMethod.Get, "/v1/jobs");
        var good = await goodFromThere.GetAsync("/v1/jobs");

        statuses.Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Headers.RetryAfter.Should().NotBeNull();
        good.Status.Should().Be(HttpStatusCode.TooManyRequests, "the address is out of attempts for every key");
        (await goodFromElsewhere.GetAsync("/v1/jobs")).Status.Should().Be(HttpStatusCode.OK, "other addresses are not slowed");
    }

    [Fact]
    public async Task A_key_never_reaches_sign_in_onboarding_key_management_or_routes_outside_the_hiring_api()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Designer"));
        await owner.PublishJobAsync(job.Id());
        var application = await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"));
        var created = await CreateKeyAsync(owner, "Everything", ["Read", "Write"]);
        using var key = new KeyClient(fixture, created.Text("token"));

        var responses = new Dictionary<string, ApiResult>
        {
            ["me"] = await key.GetAsync("/v1/auth/me"),
            ["onboarding"] = await key.PostAsync("/v1/auth/onboarding", new JsonObject { ["organizationName"] = "Via key", ["slug"] = $"key-{Guid.NewGuid():N}" }),
            ["list keys"] = await key.GetAsync("/v1/organization/api-keys"),
            ["create key"] = await key.PostAsync("/v1/organization/api-keys", new JsonObject { ["name"] = "Child", ["scopes"] = new JsonArray("Read", "Write") }),
            ["revoke key"] = await key.PostAsync($"/v1/organization/api-keys/{created.Id("key.id")}/revoke"),
            ["branding"] = await key.PutAsync("/v1/organization/branding", new JsonObject { ["brandColor"] = "#000000" }),
            ["integrity"] = await key.GetAsync($"/v1/applications/{application.Id()}/integrity"),
            ["boards"] = await key.GetAsync("/v1/boards"),
        };

        responses.Should().OnlyContain(r => r.Value.Status == HttpStatusCode.Unauthorized, "only the hiring API accepts keys");
        (await owner.GetAsync("/v1/organization/api-keys")).Count().Should().Be(1, "the key made no other key");
        (await key.GetAsync("/v1/jobs")).Status.Should().Be(HttpStatusCode.OK, "the key was not revoked");
    }

    [Fact]
    public async Task A_session_still_needs_antiforgery_and_an_Authorization_header_replaces_the_cookie()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await fixture.SignedInWithOrganizationAsync();

        using var withoutAntiforgery = await owner.Session.Client.PostAsJsonAsync("/v1/jobs", Samples.RemoteJob("Forged"), cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/jobs");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-key");
        using var cookieAndBadKey = await owner.Session.Client.SendAsync(request, cancellationToken);

        withoutAntiforgery.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        cookieAndBadKey.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a request with a key is judged by the key alone");
        (await owner.GetAsync("/v1/jobs")).Count().Should().Be(0);
    }

    [Fact]
    public async Task Creating_a_key_is_validated_and_another_organizations_key_is_404()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var othersKey = await CreateKeyAsync(other, "Theirs", ["Read"]);

        var noRead = await owner.PostAsync("/v1/organization/api-keys", new JsonObject { ["name"] = "Write only", ["scopes"] = new JsonArray("Write") });
        var noName = await owner.PostAsync("/v1/organization/api-keys", new JsonObject { ["name"] = " ", ["scopes"] = new JsonArray("Read") });
        var tooFast = await owner.PostAsync("/v1/organization/api-keys", new JsonObject
        {
            ["name"] = "Fast", ["scopes"] = new JsonArray("Read"), ["permitsPerMinute"] = 1_000_000,
        });
        var get = await owner.GetAsync($"/v1/organization/api-keys/{othersKey.Id("key.id")}");
        var revoke = await owner.PostAsync($"/v1/organization/api-keys/{othersKey.Id("key.id")}/revoke");

        noRead.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("scopes");
        noName.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("name");
        tooFast.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("permitsPerMinute");
        get.Status.Should().Be(HttpStatusCode.NotFound);
        revoke.Status.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync("/v1/organization/api-keys")).Count().Should().Be(0);
        (await other.GetAsync($"/v1/organization/api-keys/{othersKey.Id("key.id")}")).At("revokedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_OpenAPI_document_declares_the_key_on_the_hiring_api_only()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateHttpClient("api");

        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", cancellationToken);

        document.At("components.securitySchemes.apiKey.scheme").GetString().Should().Be("bearer");
        Schemes(document.At("paths./v1/jobs.get")).Should().BeEquivalentTo(["session", "apiKey"]);
        Schemes(document.At("paths./v1/auth/onboarding.post")).Should().Equal("session");
        Schemes(document.At("paths./v1/organization/api-keys.post")).Should().Equal("session");
    }

    private static List<string?> Types(ApiResult timeline) => [.. timeline.Body.EnumerateArray().Select(e => e.GetProperty("type").GetString())];

    private static List<string> Schemes(JsonElement operation) =>
        [.. operation.GetProperty("security").EnumerateArray().SelectMany(r => r.EnumerateObject().Select(p => p.Name))];

    private static async Task<ApiResult> CreateKeyAsync(Tenant owner, string name, string[] scopes, int? permitsPerMinute = null)
    {
        var body = new JsonObject { ["name"] = name, ["scopes"] = new JsonArray([.. scopes.Select(s => JsonValue.Create(s))]) };
        if (permitsPerMinute is { } permits)
        {
            body["permitsPerMinute"] = permits;
        }

        return (await owner.PostAsync("/v1/organization/api-keys", body)).Expect(HttpStatusCode.Created);
    }

    /// <summary>An integrator's client: the key in the Authorization header, no cookies and no antiforgery header.</summary>
    private sealed class KeyClient : IDisposable
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
}
