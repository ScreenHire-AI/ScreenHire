using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Application.Verification;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The Verification read scope (ADR-0021): only an Owner can give it to a key; without it a key is refused every
/// verification route and nothing else changes; with it the key reads an application's verification and lists
/// applications by level, and each read is in the application's review log under the key. The routes that act on a
/// verification (dismiss, explain, note) stay the session's.
/// </summary>
public sealed class VerificationReadScopeTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_an_owner_can_give_a_key_the_scope()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        var body = new JsonObject { ["name"] = "Auditor", ["scopes"] = new JsonArray("Read", "VerificationRead") };

        var refused = (await admin.PostAsync("/v1/organization/api-keys", body)).Expect(HttpStatusCode.Forbidden);
        (await admin.PostAsync("/v1/organization/api-keys", new JsonObject { ["name"] = "Export", ["scopes"] = new JsonArray("Read", "Write") }))
            .Expect(HttpStatusCode.Created);
        var created = (await owner.PostAsync("/v1/organization/api-keys", body)).Expect(HttpStatusCode.Created);

        refused.Text("title").Should().Be("Owners only");
        refused.Text("detail").Should().Contain("Verification read");
        created.At("key.scopes").EnumerateArray().Select(s => s.GetString()).Should().BeEquivalentTo(["Read", "VerificationRead"]);
        (await owner.GetAsync("/v1/organization/api-keys")).Count().Should().Be(2, "the admin's refused key was never made");
        (await owner.PostAsync("/v1/organization/api-keys", new JsonObject { ["name"] = "Alone", ["scopes"] = new JsonArray("VerificationRead") }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("scopes");
    }

    [Fact]
    public async Task A_key_without_the_scope_is_refused_the_verification_routes_and_leaves_no_trace()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, owner);
        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);
        await VerificationSupport.AssessedAsync(owner, applicationId);
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Export", ["Read", "Write"])).Text("token"));

        var responses = new[]
        {
            await key.GetAsync($"/v1/applications/{applicationId}/verification"),
            await key.GetAsync("/v1/verification/applications?authenticity=VerificationRecommended"),
        };

        responses.Should().OnlyContain(r => r.Status == HttpStatusCode.Forbidden, "the key lacks the scope, even with Write");
        (await ReadsByKeyAsync(owner, applicationId)).Should().BeEmpty("a refused read is not a read");
    }

    [Fact]
    public async Task A_key_with_the_scope_reads_the_verification_and_the_read_is_in_the_review_log()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, owner);
        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);
        var seenBySession = await VerificationSupport.AssessedAsync(owner, applicationId);
        var created = await owner.CreateKeyAsync("Auditor", ["Read", "VerificationRead"]);
        using var key = new KeyClient(fixture, created.Text("token"));

        var seenByKey = (await key.GetAsync($"/v1/applications/{applicationId}/verification")).Expect(HttpStatusCode.OK);
        (await key.GetAsync($"/v1/applications/{applicationId}/verification")).Expect(HttpStatusCode.OK);

        seenByKey.Text("authenticity.level").Should().Be(seenBySession.Text("authenticity.level"));
        seenByKey.At("authenticity.signals").GetArrayLength().Should().Be(seenBySession.At("authenticity.signals").GetArrayLength());
        seenByKey.At("authenticity.signals.0.evidence").GetArrayLength().Should().BeGreaterThan(0, "evidence comes with the level");
        var reads = await ReadsByKeyAsync(owner, applicationId);
        reads.Should().HaveCount(2, "each read is its own row");
        reads.Should().OnlyContain(r => r.GetProperty("apiKeyId").GetGuid() == created.Id("key.id"));
        reads[0].GetProperty("apiKeyName").GetString().Should().Be("Auditor");
        reads[0].GetProperty("keyCreatedByName").GetString().Should().NotBeNullOrEmpty("the log says who made the key");
        reads[0].GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null, "a key acts for no person");
        reads[0].GetProperty("occurredAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        (await owner.GetAsync($"/v1/applications/{applicationId}/verification")).Expect(HttpStatusCode.OK);
        (await ReadsByKeyAsync(owner, applicationId)).Should().HaveCount(2, "a person reading it adds no key read");
    }

    [Fact]
    public async Task A_verification_key_still_sees_no_verification_in_lists_or_the_timeline_and_cannot_act_on_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, owner);
        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);
        var verification = await VerificationSupport.AssessedAsync(owner, applicationId);
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Auditor", ["Read", "Write", "VerificationRead"])).Text("token"));
        var assessmentId = verification.Id("authenticity.id");

        var list = (await key.GetAsync($"/v1/jobs/{jobId}/applications")).Expect(HttpStatusCode.OK);
        var timeline = (await key.GetAsync($"/v1/applications/{applicationId}/timeline")).Expect(HttpStatusCode.OK);
        var acts = new[]
        {
            await key.PostAsync($"/v1/applications/{applicationId}/verification/assessments/{assessmentId}/dismiss", new JsonObject { ["reason"] = "FalsePositive" }),
            await key.PostAsync($"/v1/applications/{applicationId}/verification/notes", new JsonObject { ["note"] = "Reviewed." }),
            await key.PostAsync($"/v1/applications/{applicationId}/verification/request-verification"),
        };

        list.At("0.verification").ValueKind.Should().Be(JsonValueKind.Null, "only the verification routes carry it");
        timeline.Body.EnumerateArray().Select(e => e.GetProperty("type").GetString()).Should().NotContain("VerificationAssessed");
        acts.Should().OnlyContain(r => r.Status == HttpStatusCode.Unauthorized, "dismissing, noting and asking stay a person's");
        (await owner.GetAsync($"/v1/applications/{applicationId}/verification")).At("reviewLog").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).Should().NotContain(["Dismissed", "NoteAdded"]);
    }

    [Fact]
    public async Task Listing_by_level_finds_the_applications_at_it_logs_each_and_never_ranks_them()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, owner);
        var bot = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);
        var genuine = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.GenuineHuman);
        var botVerification = await VerificationSupport.AssessedAsync(owner, bot);
        await VerificationSupport.AssessedAsync(owner, genuine);
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Auditor", ["Read", "VerificationRead"])).Text("token"));

        var raised = (await key.GetAsync($"/v1/verification/applications?authenticity=VerificationRecommended&jobId={jobId}")).Expect(HttpStatusCode.OK);
        var calm = (await key.GetAsync("/v1/verification/applications?authenticity=NoConcerns")).Expect(HttpStatusCode.OK);

        raised.At("items").EnumerateArray().Select(i => i.GetProperty("applicationId").GetGuid()).Should().Equal(bot);
        raised.At("items.0.verification.authenticity").GetString().Should().Be("VerificationRecommended");
        raised.At("items.0.verification.authenticityReasons").GetArrayLength().Should().BeGreaterThan(0);
        raised.At("hasMore").GetBoolean().Should().BeFalse();
        calm.At("items").EnumerateArray().Select(i => i.GetProperty("applicationId").GetGuid()).Should().Equal(genuine);
        raised.Body.GetRawText().Should().NotContainEquivalentOf("score").And.NotContain("%");
        (await ReadsByKeyAsync(owner, bot)).Should().HaveCount(1, "the application it returned is logged");
        (await ReadsByKeyAsync(owner, genuine)).Should().HaveCount(1, "so is the other query's");

        // A dismissed assessment is set aside only when asked.
        var dismiss = $"/v1/applications/{bot}/verification/assessments/{botVerification.Id("authenticity.id")}/dismiss";
        (await owner.PostAsync(dismiss, new JsonObject { ["reason"] = "FalsePositive" })).Expect(HttpStatusCode.OK);
        var without = (await key.GetAsync("/v1/verification/applications?authenticity=VerificationRecommended&includeDismissed=false")).Expect(HttpStatusCode.OK);
        var with = (await key.GetAsync("/v1/verification/applications?authenticity=VerificationRecommended")).Expect(HttpStatusCode.OK);
        without.At("items").GetArrayLength().Should().Be(0);
        with.At("items.0.verification.authenticityDismissed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Listing_by_level_needs_a_level_and_a_sensible_limit()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Auditor", ["Read", "VerificationRead"])).Text("token"));

        (await key.GetAsync("/v1/verification/applications")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("authenticity");
        (await key.GetAsync("/v1/verification/applications?aiAssistance=SomeIndicators&limit=0")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("limit");
        (await key.GetAsync("/v1/verification/applications?aiAssistance=SomeIndicators&limit=51")).Expect(HttpStatusCode.BadRequest);
        (await key.GetAsync("/v1/verification/applications?aiAssistance=NotIndicated")).Expect(HttpStatusCode.OK).At("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_key_reads_only_its_own_organizations_verification()
    {
        using var a = await fixture.SignedInWithOrganizationAsync();
        using var b = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, b);
        var bApplication = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);
        await VerificationSupport.AssessedAsync(b, bApplication);
        using var aKey = new KeyClient(fixture, (await a.CreateKeyAsync("Auditor", ["Read", "VerificationRead"])).Text("token"));

        (await aKey.GetAsync($"/v1/applications/{bApplication}/verification")).Expect(HttpStatusCode.NotFound);
        (await aKey.GetAsync("/v1/verification/applications?authenticity=VerificationRecommended")).Expect(HttpStatusCode.OK)
            .At("items").GetArrayLength().Should().Be(0);
        (await aKey.GetAsync($"/v1/verification/applications?authenticity=VerificationRecommended&jobId={jobId}")).Expect(HttpStatusCode.OK)
            .At("items").GetArrayLength().Should().Be(0, "naming the other organization's job finds nothing either");
        (await ReadsByKeyAsync(b, bApplication)).Should().BeEmpty("another organization's key left no row");
    }

    [Fact]
    public async Task A_list_by_level_is_in_the_order_applications_arrived_never_ranked_by_how_concerning_they_are()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, owner);
        var bot = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);
        var genuine = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.GenuineHuman);
        var botVerification = await VerificationSupport.AssessedAsync(owner, bot);
        var genuineVerification = await VerificationSupport.AssessedAsync(owner, genuine);
        botVerification.Text("authenticity.level").Should().NotBe(genuineVerification.Text("authenticity.level"), "the two differ in the level a ranking would use");
        botVerification.Text("aiAssistance.level").Should().Be(genuineVerification.Text("aiAssistance.level"), "the other level lets one list hold both");
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Auditor", ["Read", "VerificationRead"])).Text("token"));

        var list = (await key.GetAsync($"/v1/verification/applications?aiAssistance={botVerification.Text("aiAssistance.level")}&jobId={jobId}")).Expect(HttpStatusCode.OK);

        list.At("items").EnumerateArray().Select(i => i.GetProperty("applicationId").GetGuid())
            .Should().Equal([genuine, bot], "the newest application comes first, though the older one has the more serious authenticity level");
    }

    [Fact]
    public async Task The_OpenAPI_document_declares_the_key_on_the_verification_reads_only()
    {
        using var client = fixture.CreateHttpClient("api");

        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);

        Schemes(document.At("paths./v1/applications/{applicationId}/verification.get")).Should().BeEquivalentTo(["session", "apiKey"]);
        Schemes(document.At("paths./v1/verification/applications.get")).Should().BeEquivalentTo(["session", "apiKey"]);
        Schemes(document.At("paths./v1/applications/{applicationId}/verification/notes.post")).Should().Equal("session");
        document.GetProperty("paths").EnumerateObject().Where(p => p.Name.Contains("verification", StringComparison.Ordinal))
            .SelectMany(p => p.Value.EnumerateObject().Select(o => o.Value.GetProperty("operationId").GetString()))
            .Should().NotContain(id => id!.Contains("reject", StringComparison.OrdinalIgnoreCase), "there is no reject action (ADR-0004)");
    }

    // The ReadByKey entries of an application's review log, as a person sees them.
    private static async Task<List<JsonElement>> ReadsByKeyAsync(Tenant tenant, Guid applicationId) =>
        [.. (await tenant.GetAsync($"/v1/applications/{applicationId}/verification")).Expect(HttpStatusCode.OK).At("reviewLog").EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "ReadByKey")];

    private static List<string> Schemes(JsonElement operation) =>
        [.. operation.GetProperty("security").EnumerateArray().SelectMany(r => r.EnumerateObject().Select(p => p.Name))];
}
